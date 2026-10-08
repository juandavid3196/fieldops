using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.BillingReview;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.ServiceRequests;

namespace FieldOps.IntegrationTests.InvoiceDelivery;

/// <summary>Send: validation order, effects, idempotency, concurrency, email after commit and audit/log content (AC-08, AC-09, AC-10, AC-15).</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class InvoiceDeliverySendTests(CompanySettingsDatabaseFixture database)
{
    private const string Recipient = "Carla.Billing@Example.com";

    private const string Note = "Please pay <b>soon</b> & thank you";

    [Fact]
    public async Task Send_ReadyDraft_FreezesTransitionsTokenizesAndEmailsExactlyOnce()
    {
        var world = await database.SeedWorldAsync();
        await using var host = RequestsHost.Create(database);
        var (owner, member) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var invoice = await database.GenerateDraftAsync(host, owner, world, member.UserId);
        var path = InvoiceApi.Path(invoice.Id, "/send");
        var before = await InvoiceApi.DetailAsync(host, owner, invoice.Id);
        var token = before["updatedAt"]!.GetValue<string>();
        var organization = before["organization"]!["name"]!.GetValue<string>();
        var issue = DateOnly.ParseExact(before["issueDate"]!.GetValue<string>(), "yyyy-MM-dd", CultureInfo.InvariantCulture);

        // AC-10: no recipient and invalid fields are a 400 with the field errors and no write or email.
        var untouched = await database.StateAsync(invoice.Id);
        var rejected = await BillingSeed.ProblemAsync(
            await host.SendAsync(HttpMethod.Post, path, owner, InvoiceApi.Body("net_9", "  ", "  ", token)),
            HttpStatusCode.BadRequest,
            errorKey: "recipientEmail");
        Assert.Equal("Enter the customer's email address.", rejected["errors"]!["recipientEmail"]![0]!.GetValue<string>());
        Assert.Equal("Select payment terms.", rejected["errors"]!["paymentTerms"]![0]!.GetValue<string>());
        Assert.Equal("Enter a message.", rejected["errors"]!["message"]![0]!.GetValue<string>());
        Assert.Equal(untouched, await database.StateAsync(invoice.Id));
        Assert.Empty(host.Sender.Messages);

        // AC-08: delivery data, status, sent_at, snapshot, one hashed token, audit and the email after the commit.
        var sent = await BillingSeed.ReadAsync(await host.SendAsync(
            HttpMethod.Post, path, owner, InvoiceApi.Body("net_30", $"  {Recipient}  ", Note, token)));

        Assert.True(sent["changed"]!.GetValue<bool>());
        Assert.Equal("sent", sent["emailStatus"]!.GetValue<string>());
        var after = sent["invoice"]!;
        Assert.Equal("sent", after["status"]!.GetValue<string>());
        Assert.NotNull(after["sentAt"]);
        Assert.Equal(Recipient, after["delivery"]!["recipientEmail"]!.GetValue<string>());
        Assert.Equal(Note, after["delivery"]!["message"]!.GetValue<string>());
        Assert.Equal("net_30", after["paymentTerms"]!.GetValue<string>());
        Assert.Equal(before["issueDate"]!.GetValue<string>(), after["issueDate"]!.GetValue<string>());
        Assert.Equal(issue.AddDays(30).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), after["dueDate"]!.GetValue<string>());
        Assert.Equal(before["totals"]!.ToJsonString(), after["totals"]!.ToJsonString());
        Assert.Equal(before["lines"]!.ToJsonString(), after["lines"]!.ToJsonString());
        Assert.NotEqual(token, after["updatedAt"]!.GetValue<string>());

        var email = Assert.Single(host.Sender.Messages);
        var raw = InvoiceApi.TokenOf(email);
        var total = after["totals"]!["total"]!.GetValue<decimal>().ToString("N2", CultureInfo.InvariantCulture);
        var due = issue.AddDays(30).ToString("MMM d, yyyy", CultureInfo.InvariantCulture);

        Assert.Equal(Recipient, email.To);
        Assert.Equal($"{organization}: invoice {invoice.Number}", email.Subject);
        Assert.Contains(Note, email.TextBody, StringComparison.Ordinal);
        Assert.Contains($"Invoice {invoice.Number} · Total due {total} USD · Due {due}", email.TextBody, StringComparison.Ordinal);
        Assert.Contains($"View your invoice: {FieldOps.IntegrationTests.Api.FieldOpsApiFactory.AllowedOrigin}/invoices/view#token={raw}", email.TextBody, StringComparison.Ordinal);
        Assert.Contains("Questions? Call us at +1 555 010 0100.", email.TextBody, StringComparison.Ordinal);
        Assert.Contains("Please pay &lt;b&gt;soon&lt;/b&gt; &amp; thank you", email.HtmlBody, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>", email.HtmlBody, StringComparison.Ordinal);

        Assert.Equal("sent", await database.ScalarAsync<string>("SELECT status::text FROM invoices WHERE id = @i", ("i", invoice.Id)));
        Assert.Equal(Recipient, await database.ScalarAsync<string>("SELECT recipient_email FROM invoices WHERE id = @i", ("i", invoice.Id)));
        Assert.Equal(1, await database.CountAsync("invoices", "id = @i AND sent_at IS NOT NULL AND updated_at >= sent_at", ("i", invoice.Id)));
        Assert.Equal("Carla Customer", await database.ScalarAsync<string>("SELECT customer_snapshot -> 'billTo' ->> 'name' FROM invoices WHERE id = @i", ("i", invoice.Id)));
        Assert.Equal(1, await database.ActiveTokensAsync(invoice.Id));
        Assert.Equal(1, await database.CountAsync("invoice_access_tokens", "invoice_id = @i AND token_hash = @h", ("i", invoice.Id), ("h", InvoiceApi.Hash(raw))));
        Assert.Equal(1, await database.CountAsync("invoice_access_tokens", "invoice_id = @i AND length(token_hash) = 64 AND token_hash <> @raw", ("i", invoice.Id), ("raw", raw)));
        Assert.Equal(
            1,
            await database.CountAsync(
                "invoice_access_tokens",
                "invoice_id = @i AND expires_at - created_at >= interval '365 days' AND expires_at - created_at < interval '365 days 1 second' AND organization_id = @o",
                ("i", invoice.Id),
                ("o", world.Org)));
        Assert.Equal(["invoice.sent"], await database.AuditActionsAsync(invoice.Id));
        var audit = await database.TextsAsync(
            "SELECT (before_data ->> 'status') || '|' || (after_data ->> 'status') || '|' || (metadata ->> 'invoiceNumber') || '|' || (metadata ->> 'currency') || '|' || (metadata ->> 'notified') || '|' || (metadata ->> 'total') || '|' || branch_id::text FROM audit_logs WHERE entity_id = @i",
            ("i", invoice.Id));
        var totalValue = after["totals"]!["total"]!.GetValue<decimal>();
        Assert.Equal(
            $"draft|sent|{invoice.Number}|USD|email|{totalValue.ToString("0.00", CultureInfo.InvariantCulture)}|{world.BranchA}",
            Assert.Single(audit));

        // AC-09: a repeated send is changed = false without a write, even with an invalid body and a stale token.
        var stable = await database.StateAsync(invoice.Id);
        foreach (var repeated in new[] { InvoiceApi.Body("net_30", Recipient, Note, token), InvoiceApi.Body(null, "", "", null) })
        {
            var again = await BillingSeed.ReadAsync(await host.SendAsync(HttpMethod.Post, path, owner, repeated));

            Assert.False(again["changed"]!.GetValue<bool>());
            Assert.Equal("not_sent", again["emailStatus"]!.GetValue<string>());
            Assert.Equal("sent", again["invoice"]!["status"]!.GetValue<string>());
        }

        Assert.Equal(stable, await database.StateAsync(invoice.Id));
        Assert.Single(host.Sender.Messages);

        // AC-09: concurrent sends of another draft produce one transition, one token, one audit row and one email.
        var second = await database.GenerateDraftAsync(host, owner, world, member.UserId, new JobSpec { Title = "Second job" });
        var secondToken = (await InvoiceApi.DetailAsync(host, owner, second.Id))["updatedAt"]!.GetValue<string>();
        var secondPath = InvoiceApi.Path(second.Id, "/send");
        var responses = await Task.WhenAll(
            Enumerable.Range(0, 6).Select(_ => host.SendAsync(HttpMethod.Post, secondPath, owner, InvoiceApi.Body("net_15", Recipient, Note, secondToken))));
        var bodies = new List<JsonNode>();

        foreach (var response in responses)
        {
            bodies.Add(await BillingSeed.ReadAsync(response));
        }

        Assert.Equal(1, bodies.Count(body => body["changed"]!.GetValue<bool>()));
        Assert.All(bodies.Where(body => !body["changed"]!.GetValue<bool>()), body => Assert.Equal("not_sent", body["emailStatus"]!.GetValue<string>()));
        Assert.Equal(1, await database.ActiveTokensAsync(second.Id));
        Assert.Equal(["invoice.sent"], await database.AuditActionsAsync(second.Id));
        Assert.Equal(2, host.Sender.Messages.Count);
        Assert.Equal(1, await database.CountAsync("invoices", "id = @i AND status = 'sent' AND sent_at IS NOT NULL", ("i", second.Id)));

        // AC-15: audit rows and logs carry no recipient, message, names, addresses, phones or tokens.
        var secondRaw = InvoiceApi.TokenOf(host.Sender.Messages[1]);
        var texts = (await database.AuditTextAsync(invoice.Id)) + (await database.AuditTextAsync(second.Id));
        var logs = string.Join('\n', host.Logs.Entries.Select(entry => $"{entry.Message} {entry.Exception} {string.Join(' ', entry.Properties.Values)}"));

        foreach (var secret in new[] { Recipient, "thank you", "Carla Customer", "Pat Contact", "1 Seed St", "5551234567", raw, secondRaw })
        {
            Assert.DoesNotContain(secret, texts, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(secret, logs, StringComparison.OrdinalIgnoreCase);
        }
    }
}
