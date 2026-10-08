using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.BillingReview;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.ServiceRequests;

namespace FieldOps.IntegrationTests.InvoiceDelivery;

/// <summary>Detail content, delivery defaults, exclusions and the draft PDF (invoice-draft-delivery AC-03, AC-04, AC-07).</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class InvoiceDeliveryDetailTests(CompanySettingsDatabaseFixture database)
{
    private static string[] Lines(JsonNode? lines) => [.. lines!.AsArray().Select(line => line!.GetValue<string>())];

    [Fact]
    public async Task Detail_PreviewDefaultsAndDraftPdf_FollowStoredData()
    {
        var world = await database.SeedWorldAsync();
        await database.ExecuteAsync(
            "UPDATE organizations SET address_line1 = '10 Main St', city = 'Austin', state_region = 'TX', postal_code = '78701', email = 'billing@acme.test' WHERE id = @o",
            ("o", world.Org));
        await database.ExecuteAsync(
            "UPDATE customers SET billing_address = CAST(@a AS jsonb) WHERE id = @c",
            ("a", """{"line1":"9 Bill Rd","line2":"Suite 4","city":"Dallas","state":"TX","postalCode":"75001"}"""),
            ("c", world.Customer));
        await using var host = RequestsHost.Create(database);
        var (owner, member) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var invoice = await database.GenerateDraftAsync(
            host,
            owner,
            world,
            member.UserId,
            new JobSpec
            {
                Title = "Fix drain",
                Discount = 20m,
                Summary = "Replaced the main drain.",
                Lines =
                [
                    new LineSpec("Labor", "service", 2m, "h", 100m),
                    new LineSpec("Pipe fitting", "product", 2.5m, "ea", 10m, 8.25m, Planned: 2.5m, Used: 2.5m),
                ],
            });
        var job = invoice.Job;
        var today = BillingSeed.Today("UTC");

        // AC-03: header, metadata, preview, totals, tax label, completion note, footer facts and checks.
        var detail = await InvoiceApi.DetailAsync(host, owner, invoice.Id);

        Assert.Equal(invoice.Id, detail["id"]!.GetValue<Guid>());
        Assert.Equal(invoice.Number, detail["number"]!.GetValue<string>());
        Assert.Equal("draft", detail["status"]!.GetValue<string>());
        Assert.Equal($"WO-{job.Number}", detail["workOrderNumber"]!.GetValue<string>());
        Assert.Equal(job.Order, detail["workOrderId"]!.GetValue<Guid>());
        Assert.Equal("Carla Customer", detail["customerName"]!.GetValue<string>());
        Assert.Equal(today, detail["issueDate"]!.GetValue<string>());
        Assert.Equal("net_15", detail["paymentTerms"]!.GetValue<string>());
        Assert.Equal(DateOnly.ParseExact(today, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddDays(15).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), detail["dueDate"]!.GetValue<string>());
        Assert.Equal("USD", detail["currency"]!.GetValue<string>());
        Assert.Equal("UTC", detail["timezone"]!.GetValue<string>());
        Assert.Equal(["10 Main St", "Austin, TX 78701"], Lines(detail["organization"]!["addressLines"]));
        Assert.Equal("billing@acme.test", detail["organization"]!["email"]!.GetValue<string>());
        Assert.Equal("+1 555 010 0100", detail["organization"]!["phone"]!.GetValue<string>());
        Assert.False(detail["organization"]!["hasLogo"]!.GetValue<bool>());
        Assert.Equal("Carla Customer", detail["billTo"]!["name"]!.GetValue<string>());
        Assert.Equal("carla@example.com", detail["billTo"]!["email"]!.GetValue<string>());
        Assert.Equal("5551234567", detail["billTo"]!["phone"]!.GetValue<string>());
        Assert.Equal(["9 Bill Rd", "Suite 4", "Dallas, TX 75001"], Lines(detail["billTo"]!["addressLines"]));
        Assert.Equal("1 Seed St, Austin", detail["serviceAddress"]!.GetValue<string>());
        Assert.Equal("Replaced the main drain.", detail["completionNote"]!.GetValue<string>());
        Assert.Equal("Sam Staff", detail["createdByName"]!.GetValue<string>());
        Assert.Null(detail["sentAt"]);
        Assert.True(detail["canAct"]!.GetValue<bool>());

        var lines = detail["lines"]!.AsArray();
        Assert.Equal(2, lines.Count);
        Assert.Equal(("Labor", "Line description", "2", "h"), (lines[0]!["description"]!.GetValue<string>(), lines[0]!["detail"]!.GetValue<string>(), lines[0]!["quantity"]!.GetValue<string>(), lines[0]!["unit"]!.GetValue<string>()));
        Assert.Equal((100m, 0m, 200m), (lines[0]!["unitPrice"]!.GetValue<decimal>(), lines[0]!["taxRate"]!.GetValue<decimal>(), lines[0]!["amount"]!.GetValue<decimal>()));
        Assert.Equal("2.5", lines[1]!["quantity"]!.GetValue<string>());
        Assert.Equal(8.25m, lines[1]!["taxRate"]!.GetValue<decimal>());
        Assert.Equal(job.Subtotal, detail["totals"]!["subtotal"]!.GetValue<decimal>());
        Assert.Equal(20m, detail["totals"]!["discountTotal"]!.GetValue<decimal>());
        Assert.Equal("Tax (8.25%)", detail["totals"]!["taxLabel"]!.GetValue<string>());
        Assert.Equal(job.Tax, detail["totals"]!["taxTotal"]!.GetValue<decimal>());
        Assert.Equal(job.Total, detail["totals"]!["total"]!.GetValue<decimal>());

        var checks = detail["checks"]!.AsArray();
        Assert.Equal(["recipient", "tax"], checks.Select(check => check!["key"]!.GetValue<string>()));
        Assert.Equal("Recipient email added", checks[0]!["label"]!.GetValue<string>());
        Assert.Equal("Tax checked (Tax (8.25%))", checks[1]!["label"]!.GetValue<string>());
        Assert.All(checks, check => Assert.True(check!["met"]!.GetValue<bool>()));

        // Never returned: unit cost, margin, internal notes, review note, variances and signature content.
        var text = detail.ToJsonString();
        foreach (var excluded in new[] { "unitCost", "margin", "variance", "signature", "Looks good", "billingReviewNote", "SECRET" })
        {
            Assert.DoesNotContain(excluded, text, StringComparison.OrdinalIgnoreCase);
        }

        // AC-04: nothing stored yet, so the defaults come from the primary contact and the template.
        Assert.False(detail["delivery"]!["saved"]!.GetValue<bool>());
        Assert.Equal("carla@example.com", detail["delivery"]!["recipientEmail"]!.GetValue<string>());
        var organization = detail["organization"]!["name"]!.GetValue<string>();
        Assert.Equal(
            $"Hi Pat,\n\nHere's your invoice {invoice.Number} for Fix drain. Thank you for choosing {organization}! If you have any questions, please don't hesitate to reach out.\n\nBest regards,\n{organization}",
            detail["delivery"]!["message"]!.GetValue<string>());

        // Without an active contact the customer values and the customer name are the fallbacks.
        await database.ExecuteAsync("UPDATE customer_contacts SET is_active = false WHERE customer_id = @c", ("c", world.Customer));
        var fallback = await InvoiceApi.DetailAsync(host, owner, invoice.Id);
        Assert.Equal("carla@example.com", fallback["delivery"]!["recipientEmail"]!.GetValue<string>());
        Assert.StartsWith("Hi Carla Customer,", fallback["delivery"]!["message"]!.GetValue<string>(), StringComparison.Ordinal);

        // Without any email the recipient is empty and its check is unmet.
        await database.ExecuteAsync("UPDATE customers SET primary_email = NULL, primary_phone = NULL WHERE id = @c", ("c", world.Customer));
        var empty = await InvoiceApi.DetailAsync(host, owner, invoice.Id);
        Assert.Equal(string.Empty, empty["delivery"]!["recipientEmail"]!.GetValue<string>());
        Assert.False(empty["checks"]![0]!["met"]!.GetValue<bool>());
        Assert.Null(empty["billTo"]!["email"]);
        Assert.Null(empty["billTo"]!["phone"]);

        // Stored delivery data wins over the defaults and marks the delivery as saved.
        var saved = await BillingSeed.ReadAsync(await host.SendAsync(
            HttpMethod.Put,
            InvoiceApi.Path(invoice.Id, "/draft"),
            owner,
            InvoiceApi.Body("net_30", "billing@customer.test", "A stored message.", empty["updatedAt"]!.GetValue<string>())));
        Assert.True(saved["delivery"]!["saved"]!.GetValue<bool>());
        var stored = await InvoiceApi.DetailAsync(host, owner, invoice.Id);
        Assert.Equal("billing@customer.test", stored["delivery"]!["recipientEmail"]!.GetValue<string>());
        Assert.Equal("A stored message.", stored["delivery"]!["message"]!.GetValue<string>());

        // AC-07: the draft PDF is generated server-side with the BR-12 headers and filename.
        var pdf = await host.SendAsync(HttpMethod.Get, InvoiceApi.Path(invoice.Id, "/pdf"), owner);
        var bytes = await pdf.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
        Assert.Equal($"attachment; filename=\"{invoice.Number}.pdf\"", pdf.Content.Headers.ContentDisposition?.ToString());
        Assert.Equal("no-store", pdf.Headers.CacheControl?.ToString());
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.DoesNotContain("billing@customer.test", System.Text.Encoding.Latin1.GetString(bytes), StringComparison.Ordinal);
    }
}
