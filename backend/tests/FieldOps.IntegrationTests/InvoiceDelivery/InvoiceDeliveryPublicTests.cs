using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FieldOps.IntegrationTests.BillingReview;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.ServiceRequests;

namespace FieldOps.IntegrationTests.InvoiceDelivery;

/// <summary>Snapshot freezing and the public link: content, headers, token states and no writes (invoice-draft-delivery AC-12, AC-13).</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class InvoiceDeliveryPublicTests(CompanySettingsDatabaseFixture database)
{
    private const string Recipient = "recipient.only@customer.test";

    private const string Note = "Internal wording for the customer email";

    private static string[] Lines(JsonNode? lines) => [.. lines!.AsArray().Select(line => line!.GetValue<string>())];

    [Fact]
    public async Task Public_SentInvoiceStaysFrozenAndOnlyAValidTokenOpensIt()
    {
        var world = await database.SeedWorldAsync();
        await database.ExecuteAsync(
            "INSERT INTO organization_logos (organization_id, content_type, content, size_bytes) VALUES (@o, 'image/png', @c, @s)",
            ("o", world.Org),
            ("c", BillingSeed.Png),
            ("s", BillingSeed.Png.Length));
        await database.ExecuteAsync(
            "UPDATE customers SET billing_address = CAST(@a AS jsonb) WHERE id = @c",
            ("a", """{"line1":"9 Bill Rd","city":"Dallas","state":"TX","postalCode":"75001"}"""),
            ("c", world.Customer));
        await using var host = RequestsHost.Create(database);
        var (owner, member) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var invoice = await database.GenerateDraftAsync(host, owner, world, member.UserId);
        var draft = await database.GenerateDraftAsync(host, owner, world, member.UserId, new JobSpec { Title = "Draft job" });
        var voided = await database.GenerateDraftAsync(host, owner, world, member.UserId, new JobSpec { Title = "Void job" });
        var token = (await InvoiceApi.DetailAsync(host, owner, invoice.Id))["updatedAt"]!.GetValue<string>();

        var sent = await BillingSeed.ReadAsync(await host.SendAsync(
            HttpMethod.Post, InvoiceApi.Path(invoice.Id, "/send"), owner, InvoiceApi.Body("net_30", Recipient, Note, token)));
        Assert.Equal("sent", sent["emailStatus"]!.GetValue<string>());
        var raw = InvoiceApi.TokenOf(Assert.Single(host.Sender.Messages));

        // AC-12: the customer, the contact, the property and the visit change afterwards; nothing customer-facing moves.
        await database.ExecuteAsync(
            "UPDATE customers SET display_name = 'Renamed Corp', primary_email = 'renamed@example.com', primary_phone = '1112223333', billing_address = CAST(@a AS jsonb) WHERE id = @c",
            ("a", """{"line1":"Changed Rd"}"""),
            ("c", world.Customer));
        await database.ExecuteAsync(
            "UPDATE customer_contacts SET first_name = 'Zed', email = 'changed@example.com', phone = '9998887777' WHERE customer_id = @c", ("c", world.Customer));
        await database.ExecuteAsync("UPDATE properties SET address_line1 = 'Moved Ave' WHERE customer_id = @c", ("c", world.Customer));
        await database.ExecuteAsync("UPDATE visits SET completion_summary = 'Changed summary' WHERE id = @v", ("v", invoice.Job.Visit));

        var internalDetail = await InvoiceApi.DetailAsync(host, owner, invoice.Id);
        Assert.Equal("Carla Customer", internalDetail["customerName"]!.GetValue<string>());
        Assert.Equal("Carla Customer", internalDetail["billTo"]!["name"]!.GetValue<string>());
        Assert.Equal("carla@example.com", internalDetail["billTo"]!["email"]!.GetValue<string>());
        Assert.Equal("5551234567", internalDetail["billTo"]!["phone"]!.GetValue<string>());
        Assert.Equal(["9 Bill Rd", "Dallas, TX 75001"], Lines(internalDetail["billTo"]!["addressLines"]));
        Assert.Equal("1 Seed St, Austin", internalDetail["serviceAddress"]!.GetValue<string>());
        Assert.Equal("All done", internalDetail["completionNote"]!.GetValue<string>());
        Assert.Equal(Recipient, internalDetail["delivery"]!["recipientEmail"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(HttpMethod.Get, InvoiceApi.Path(invoice.Id, "/pdf"), owner)).StatusCode);

        // AC-13: the valid token returns the frozen PublicInvoice, PDF and logo with the public headers and no internal data.
        var audits = await database.CountAsync("audit_logs", "organization_id = @o", ("o", world.Org));
        var view = await InvoiceApi.PublicAsync(host.Client, "view", raw);
        var publicJson = await BillingSeed.ReadAsync(view);
        var text = publicJson.ToJsonString();

        Assert.Equal("no-store", view.Headers.CacheControl?.ToString());
        Assert.Equal("no-referrer", view.Headers.GetValues("Referrer-Policy").Single());
        Assert.Equal("nosniff", view.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("sent", publicJson["status"]!.GetValue<string>());
        Assert.Equal(invoice.Number, publicJson["number"]!.GetValue<string>());
        Assert.Equal("Carla Customer", publicJson["billTo"]!["name"]!.GetValue<string>());
        Assert.Equal(["9 Bill Rd", "Dallas, TX 75001"], Lines(publicJson["billTo"]!["addressLines"]));
        Assert.Equal("1 Seed St, Austin", publicJson["serviceAddress"]!.GetValue<string>());
        Assert.Equal("All done", publicJson["completionNote"]!.GetValue<string>());
        Assert.Equal("net_30", publicJson["paymentTerms"]!.GetValue<string>());
        Assert.True(publicJson["organization"]!["hasLogo"]!.GetValue<bool>());
        Assert.Equal(internalDetail["lines"]!.ToJsonString(), publicJson["lines"]!.ToJsonString());
        Assert.Equal(internalDetail["totals"]!.ToJsonString(), publicJson["totals"]!.ToJsonString());

        foreach (var key in new[] { "id", "workOrderId", "delivery", "customerName", "createdAt", "createdByName", "sentAt", "canAct", "updatedAt", "checks" })
        {
            Assert.Null(publicJson[key]);
        }

        foreach (var internalValue in new[] { Recipient, Note, "Sam Staff", "Renamed Corp", "Changed Rd", "Moved Ave", "Changed summary", "changed@example.com", "unitCost", "variance" })
        {
            Assert.DoesNotContain(internalValue, text, StringComparison.OrdinalIgnoreCase);
        }

        Assert.False(Regex.IsMatch(text, "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-"), "The public invoice must not contain any id.");

        var pdf = await InvoiceApi.PublicAsync(host.Client, "pdf", raw);
        var bytes = await pdf.Content.ReadAsByteArrayAsync();
        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
        Assert.Equal($"attachment; filename=\"{invoice.Number}.pdf\"", pdf.Content.Headers.ContentDisposition?.ToString());
        Assert.True(pdf.Headers.CacheControl?.NoStore);
        Assert.True(pdf.Headers.CacheControl?.Private);
        Assert.Equal("no-referrer", pdf.Headers.GetValues("Referrer-Policy").Single());
        Assert.Equal("nosniff", pdf.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));

        var logo = await InvoiceApi.PublicAsync(host.Client, "logo", raw);
        Assert.Equal(HttpStatusCode.OK, logo.StatusCode);
        Assert.Equal("image/png", logo.Content.Headers.ContentType?.MediaType);
        Assert.True(logo.Headers.CacheControl?.NoStore);
        Assert.True(logo.Headers.CacheControl?.Private);
        Assert.Equal(BillingSeed.Png, await logo.Content.ReadAsByteArrayAsync());

        // Every other token is the same 404 for view, PDF and logo: malformed, unknown, expired, draft and void.
        var expired = InvoiceApi.NewToken();
        var ofDraft = InvoiceApi.NewToken();
        var ofVoid = InvoiceApi.NewToken();
        await database.InsertTokenAsync(world.Org, invoice.Id, member.UserId, expired, expired: true);
        await database.InsertTokenAsync(world.Org, draft.Id, member.UserId, ofDraft);
        await database.InsertTokenAsync(world.Org, voided.Id, member.UserId, ofVoid);
        await database.ExecuteAsync("UPDATE invoices SET status = 'void' WHERE id = @i", ("i", voided.Id));
        var unavailable = new List<string>();
        var stable = await database.StateAsync(invoice.Id);

        foreach (var bad in new string?[] { null, string.Empty, "short", new string('!', 43), new string('A', 44), InvoiceApi.NewToken(), expired, ofDraft, ofVoid })
        {
            foreach (var action in new[] { "view", "pdf", "logo" })
            {
                var response = await InvoiceApi.PublicAsync(host.Client, action, bad);
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
                Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
                unavailable.Add(await InvoiceApi.WithoutTraceAsync(response));
            }
        }

        var problem = JsonNode.Parse(unavailable[0])!;
        Assert.Equal("invoice_link_unavailable", problem["code"]!.GetValue<string>());
        Assert.Equal("This link isn't available.", problem["title"]!.GetValue<string>());
        Assert.Single(unavailable.Distinct());
        Assert.DoesNotContain(invoice.Number, unavailable[0], StringComparison.Ordinal);

        // Public reads write nothing: no status change, no audit, no view tracking.
        Assert.Equal(stable, await database.StateAsync(invoice.Id));
        Assert.Equal(audits, await database.CountAsync("audit_logs", "organization_id = @o", ("o", world.Org)));
        Assert.Equal(0, await database.CountAsync("invoice_access_tokens", "invoice_id IN (@d, @v) AND revoked_at IS NOT NULL", ("d", draft.Id), ("v", voided.Id)));

        // A resend revokes the earlier link: the old token is the same 404 and the new one opens the invoice.
        var resend = await BillingSeed.ReadAsync(await host.SendAsync(
            HttpMethod.Post, InvoiceApi.Path(invoice.Id, "/resend-email"), owner, InvoiceApi.Resend(internalDetail["updatedAt"]!.GetValue<string>())));
        Assert.Equal("sent", resend["emailStatus"]!.GetValue<string>());
        var fresh = InvoiceApi.TokenOf(host.Sender.Messages[1]);
        Assert.NotEqual(raw, fresh);

        var afterResend = await database.StateAsync(invoice.Id);
        foreach (var action in new[] { "view", "pdf", "logo" })
        {
            var old = await InvoiceApi.PublicAsync(host.Client, action, raw);
            Assert.Equal(HttpStatusCode.NotFound, old.StatusCode);
            Assert.Equal(unavailable[0], await InvoiceApi.WithoutTraceAsync(old));
            Assert.Equal(HttpStatusCode.OK, (await InvoiceApi.PublicAsync(host.Client, action, fresh)).StatusCode);
        }

        Assert.Equal(afterResend, await database.StateAsync(invoice.Id));
    }
}
