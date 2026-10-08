using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.BillingReview;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.ServiceRequests;

namespace FieldOps.IntegrationTests.InvoiceDelivery;

/// <summary>Draft save: validation, due date, audit and the three conflicts (invoice-draft-delivery AC-05, AC-06).</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class InvoiceDeliverySaveTests(CompanySettingsDatabaseFixture database)
{
    private static string Token(JsonNode detail) => detail["updatedAt"]!.GetValue<string>();

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    [Fact]
    public async Task Save_ValidatesStoresAuditsAndConflictsWithoutLosingTheFirstWriter()
    {
        var world = await database.SeedWorldAsync();
        await using var host = RequestsHost.Create(database);
        var (owner, member) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var invoice = await database.GenerateDraftAsync(host, owner, world, member.UserId);
        var path = InvoiceApi.Path(invoice.Id, "/draft");
        var issue = DateOnly.ParseExact(BillingSeed.Today("UTC"), "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var detail = await InvoiceApi.DetailAsync(host, owner, invoice.Id);
        var total = detail["totals"]!["total"]!.GetValue<decimal>();

        // AC-05: each terms value recomputes the due date from the unchanged issue date; client amounts, dates and status are ignored.
        foreach (var (terms, days) in new[] { ("due_upon_receipt", 0), ("net_30", 30), ("net_15", 15) })
        {
            var body = InvoiceApi.Body(terms, "carla@example.com", InvoiceApi.Message, Token(detail));
            body["total"] = 1m;
            body["status"] = "paid";
            body["issueDate"] = "2000-01-01";
            body["dueDate"] = "2000-01-02";
            body["organizationId"] = Guid.NewGuid();

            var before = Token(detail);
            var saved = await BillingSeed.ReadAsync(await host.SendAsync(HttpMethod.Put, path, owner, body));

            Assert.Equal(terms, saved["paymentTerms"]!.GetValue<string>());
            Assert.Equal(Iso(issue.AddDays(days)), saved["dueDate"]!.GetValue<string>());
            Assert.Equal(Iso(issue), saved["issueDate"]!.GetValue<string>());
            Assert.Equal(total, saved["totals"]!["total"]!.GetValue<decimal>());
            Assert.Equal("draft", saved["status"]!.GetValue<string>());
            Assert.True(saved["delivery"]!["saved"]!.GetValue<bool>());
            Assert.True(DateTimeOffset.Parse(Token(saved), CultureInfo.InvariantCulture) > DateTimeOffset.Parse(before, CultureInfo.InvariantCulture));
            detail = saved;
        }

        Assert.Equal(Iso(issue.AddDays(15)), await database.ScalarAsync<string>("SELECT due_date::text FROM invoices WHERE id = @i", ("i", invoice.Id)));
        Assert.Equal(Iso(issue), await database.ScalarAsync<string>("SELECT issue_date::text FROM invoices WHERE id = @i", ("i", invoice.Id)));

        // Audit: one row per change with field names only, never the values.
        Assert.Equal(["invoice.draft_saved", "invoice.draft_saved", "invoice.draft_saved"], await database.AuditActionsAsync(invoice.Id));
        var changed = await database.TextsAsync(
            "SELECT (metadata -> 'changedFields')::text FROM audit_logs WHERE entity_id = @i AND action = 'invoice.draft_saved' ORDER BY id", ("i", invoice.Id));
        Assert.Equal(
            [["paymentTerms", "recipientEmail", "message"], ["paymentTerms"], ["paymentTerms"]],
            changed.Select(text => JsonNode.Parse(text)!.AsArray().Select(name => name!.GetValue<string>()).ToArray()));
        var audit = await database.AuditTextAsync(invoice.Id);
        Assert.DoesNotContain("carla@example.com", audit, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Thanks for choosing us", audit, StringComparison.Ordinal);

        // Identical values still succeed, move nothing and write no audit.
        var stable = await database.StateAsync(invoice.Id);
        var same = await BillingSeed.ReadAsync(await host.SendAsync(
            HttpMethod.Put, path, owner, InvoiceApi.Body("net_15", "carla@example.com", InvoiceApi.Message, Token(detail))));
        Assert.Equal(Token(detail), Token(same));
        Assert.Equal(stable, await database.StateAsync(invoice.Id));

        // An empty recipient is stored as null on save and the detail falls back to the default.
        var cleared = await BillingSeed.ReadAsync(await host.SendAsync(
            HttpMethod.Put, path, owner, InvoiceApi.Body("net_15", "   ", InvoiceApi.Message, Token(detail))));
        Assert.Equal(1, await database.CountAsync("invoices", "id = @i AND recipient_email IS NULL", ("i", invoice.Id)));
        Assert.Equal("carla@example.com", cleared["delivery"]!["recipientEmail"]!.GetValue<string>());
        detail = cleared;

        // Invalid fields: 400 with the field message and no write.
        var tooLong = new string('x', 501);
        var longEmail = new string('a', 250) + "@x.co";
        var invalid = new (JsonObject Body, string Key, string Message)[]
        {
            (InvoiceApi.Body("net_14", "carla@example.com", InvoiceApi.Message, Token(detail)), "paymentTerms", "Select payment terms."),
            (InvoiceApi.Body(null, "carla@example.com", InvoiceApi.Message, Token(detail)), "paymentTerms", "Select payment terms."),
            (InvoiceApi.Body("net_15", "not-an-email", InvoiceApi.Message, Token(detail)), "recipientEmail", "Enter a valid email address."),
            (InvoiceApi.Body("net_15", longEmail, InvoiceApi.Message, Token(detail)), "recipientEmail", "Enter a valid email address."),
            (InvoiceApi.Body("net_15", "carla@example.com", "   ", Token(detail)), "message", "Enter a message."),
            (InvoiceApi.Body("net_15", "carla@example.com", tooLong, Token(detail)), "message", "Message must be 500 characters or fewer."),
        };
        var beforeInvalid = await database.StateAsync(invoice.Id);

        foreach (var (body, key, message) in invalid)
        {
            var problem = await BillingSeed.ProblemAsync(await host.SendAsync(HttpMethod.Put, path, owner, body), HttpStatusCode.BadRequest, errorKey: key);
            Assert.Equal(message, problem["errors"]![key]![0]!.GetValue<string>());
        }

        Assert.Equal(beforeInvalid, await database.StateAsync(invoice.Id));

        // AC-06: a stale or unreadable updatedAt is invoice_changed with no write.
        var stale = Token(detail);
        await BillingSeed.ReadAsync(await host.SendAsync(HttpMethod.Put, path, owner, InvoiceApi.Body("net_30", "carla@example.com", InvoiceApi.Message, stale)));
        var afterWinner = await database.StateAsync(invoice.Id);
        foreach (var token in new[] { stale, "not-a-date", null })
        {
            await BillingSeed.ProblemAsync(
                await host.SendAsync(HttpMethod.Put, path, owner, InvoiceApi.Body("net_15", "other@example.com", "Lost write", token)),
                HttpStatusCode.Conflict,
                "invoice_changed");
        }

        Assert.Equal(afterWinner, await database.StateAsync(invoice.Id));
        detail = await InvoiceApi.DetailAsync(host, owner, invoice.Id);
        Assert.Equal("net_30", detail["paymentTerms"]!.GetValue<string>());

        // A save and a send with the same updatedAt: exactly one of them succeeds.
        var sameToken = Token(detail);
        var save = host.SendAsync(HttpMethod.Put, path, owner, InvoiceApi.Body("net_15", "carla@example.com", "Raced message", sameToken));
        var send = host.SendAsync(HttpMethod.Post, InvoiceApi.Path(invoice.Id, "/send"), owner, InvoiceApi.Body("net_30", "carla@example.com", InvoiceApi.Message, sameToken));
        var results = await Task.WhenAll(save, send);
        var saveWon = results[0].StatusCode == HttpStatusCode.OK;
        var sendWon = results[1].StatusCode == HttpStatusCode.OK && (await BillingSeed.ReadAsync(results[1]))["changed"]!.GetValue<bool>();

        Assert.True(saveWon ^ sendWon, "Exactly one of the concurrent save and send must succeed.");
        Assert.Equal(HttpStatusCode.Conflict, saveWon ? results[1].StatusCode : results[0].StatusCode);

        // Once sent, the draft cannot be saved: invoice_not_draft comes before the updatedAt check.
        detail = await InvoiceApi.DetailAsync(host, owner, invoice.Id);

        if (detail["status"]!.GetValue<string>() == "draft")
        {
            await BillingSeed.ReadAsync(await host.SendAsync(
                HttpMethod.Post, InvoiceApi.Path(invoice.Id, "/send"), owner, InvoiceApi.Body("net_30", "carla@example.com", InvoiceApi.Message, Token(detail))));
            detail = await InvoiceApi.DetailAsync(host, owner, invoice.Id);
        }

        Assert.Equal("sent", detail["status"]!.GetValue<string>());
        var sentState = await database.StateAsync(invoice.Id);

        foreach (var token in new[] { Token(detail), stale, null })
        {
            await BillingSeed.ProblemAsync(
                await host.SendAsync(HttpMethod.Put, path, owner, InvoiceApi.Body("net_15", "x@example.com", "Too late", token)),
                HttpStatusCode.Conflict,
                "invoice_not_draft");
        }

        Assert.Equal(sentState, await database.StateAsync(invoice.Id));
    }
}
