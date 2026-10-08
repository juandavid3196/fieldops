using System.Net;
using System.Text.Json.Nodes;
using FieldOps.Application.Features.Email;
using FieldOps.Domain.Invoices;
using FieldOps.Infrastructure.Persistence;
using FieldOps.IntegrationTests.BillingReview;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.PasswordResets;
using FieldOps.IntegrationTests.ServiceRequests;
using FieldOps.IntegrationTests.Sessions;
using FieldOps.IntegrationTests.Team;
using FieldOps.IntegrationTests.Users;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FieldOps.IntegrationTests.InvoiceDelivery;

/// <summary>Send rollback, email failure after the commit and the token rotation of resend (invoice-draft-delivery AC-10, AC-11, AC-15).</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class InvoiceDeliveryFailureTests(CompanySettingsDatabaseFixture database)
{
    private const string Recipient = "billing.contact@customer.test";

    private const string Note = "Private invoice note for the customer";

    [Fact]
    public async Task Send_FailureInsideTheTransactionRollsBackAndEmailFailureNeverUndoesIt_ThenResendRotatesTheToken()
    {
        var world = await database.SeedWorldAsync();
        var interceptor = new FailOnTokenInterceptor();
        var sender = new RecordingEmailSender();
        await using var session = SessionTestHost.Create(database.ConnectionString, captureLogs: true);
        using var client = SessionApi.CreateClient(session.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(sender);
            services.ConfigureDbContext<FieldOpsDbContext>(options => options.AddInterceptors(interceptor));
        })));
        var member = await database.SeedMemberAsync(world.Org, CompanySettingsDatabaseFixture.OwnerRoleId, "Sam", "Staff");
        var cookie = await CompanySettingsApi.SignInCookieAsync(client, member.Email);

        Task<HttpResponseMessage> Send(HttpMethod method, string path, JsonObject? body) =>
            CompanySettingsApi.SendRawAsync(client, method, path, body?.ToJsonString(), cookie);

        async Task<JsonNode> Detail(Guid id) => await BillingSeed.ReadAsync(await Send(HttpMethod.Get, InvoiceApi.Path(id), null));

        var invoice = await database.GenerateDraftAsync(Send, world, member.UserId);
        var token = (await Detail(invoice.Id))["updatedAt"]!.GetValue<string>();
        var path = InvoiceApi.Path(invoice.Id, "/send");
        var body = InvoiceApi.Body("net_30", Recipient, Note, token);

        // AC-10: a failure after the status update, while the token and audit rows are written, leaves no trace.
        var untouched = await database.StateAsync(invoice.Id);
        interceptor.Armed = true;
        var failed = await Send(HttpMethod.Post, path, body);
        interceptor.Armed = false;

        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        Assert.Equal(untouched, await database.StateAsync(invoice.Id));
        Assert.Equal("draft", await database.ScalarAsync<string>("SELECT status::text FROM invoices WHERE id = @i", ("i", invoice.Id)));
        Assert.Equal(0, await database.CountAsync("invoice_access_tokens", "invoice_id = @i", ("i", invoice.Id)));
        Assert.Empty(await database.AuditActionsAsync(invoice.Id));
        Assert.Equal(0, sender.Attempts);
        Assert.DoesNotContain(Recipient, await failed.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        // AC-11: the email provider throws; the send is still committed, with its token, and answers emailStatus = failed.
        sender.Fail = true;
        var sent = await BillingSeed.ReadAsync(await Send(HttpMethod.Post, path, body));

        Assert.True(sent["changed"]!.GetValue<bool>());
        Assert.Equal("failed", sent["emailStatus"]!.GetValue<string>());
        Assert.Equal("sent", sent["invoice"]!["status"]!.GetValue<string>());
        Assert.Equal(1, sender.Attempts);
        Assert.Empty(sender.Messages);
        Assert.Equal(1, await database.ActiveTokensAsync(invoice.Id));
        Assert.Equal(["invoice.sent"], await database.AuditActionsAsync(invoice.Id));

        var warning = Assert.Single(session.Logs!.Entries, entry => entry.Message.StartsWith("Invoice email failed.", StringComparison.Ordinal));
        Assert.Equal(invoice.Id, Guid.Parse(warning.Properties["InvoiceId"]!.ToString()!));
        Assert.Equal("InvalidOperationException", warning.Properties["Category"]);
        Assert.Null(warning.Exception);

        // Resend: the earlier token is revoked, a new one is created, sent_at, amounts and the snapshot stay, and the email goes out.
        var sentDetail = sent["invoice"]!;
        var oldHash = await database.ScalarAsync<string>("SELECT token_hash FROM invoice_access_tokens WHERE invoice_id = @i AND revoked_at IS NULL", ("i", invoice.Id));
        var sentAt = await database.ScalarAsync<string>("SELECT sent_at::text FROM invoices WHERE id = @i", ("i", invoice.Id));
        var snapshot = await database.ScalarAsync<string>("SELECT customer_snapshot::text FROM invoices WHERE id = @i", ("i", invoice.Id));
        var resendPath = InvoiceApi.Path(invoice.Id, "/resend-email");
        var resendToken = sentDetail["updatedAt"]!.GetValue<string>();

        sender.Fail = false;
        await BillingSeed.ProblemAsync(await Send(HttpMethod.Post, resendPath, InvoiceApi.Resend(token)), HttpStatusCode.Conflict, "invoice_changed");
        Assert.Equal(1, sender.Attempts);
        Assert.Equal(1, await database.ActiveTokensAsync(invoice.Id));

        var resent = await BillingSeed.ReadAsync(await Send(HttpMethod.Post, resendPath, InvoiceApi.Resend(resendToken)));

        Assert.Equal("sent", resent["emailStatus"]!.GetValue<string>());
        Assert.Equal(sentDetail["sentAt"]!.GetValue<string>(), resent["invoice"]!["sentAt"]!.GetValue<string>());
        Assert.Equal(sentDetail["totals"]!.ToJsonString(), resent["invoice"]!["totals"]!.ToJsonString());
        Assert.Equal(sentDetail["delivery"]!.ToJsonString(), resent["invoice"]!["delivery"]!.ToJsonString());
        Assert.Equal(sentDetail["dueDate"]!.GetValue<string>(), resent["invoice"]!["dueDate"]!.GetValue<string>());
        Assert.NotEqual(resendToken, resent["invoice"]!["updatedAt"]!.GetValue<string>());
        Assert.Equal(sentAt, await database.ScalarAsync<string>("SELECT sent_at::text FROM invoices WHERE id = @i", ("i", invoice.Id)));
        Assert.Equal(snapshot, await database.ScalarAsync<string>("SELECT customer_snapshot::text FROM invoices WHERE id = @i", ("i", invoice.Id)));
        Assert.Equal(2, await database.CountAsync("invoice_access_tokens", "invoice_id = @i", ("i", invoice.Id)));
        Assert.Equal(1, await database.CountAsync("invoice_access_tokens", "invoice_id = @i AND token_hash = @h AND revoked_at IS NOT NULL", ("i", invoice.Id), ("h", oldHash)));
        Assert.Equal(["invoice.sent", "invoice.email_resent"], await database.AuditActionsAsync(invoice.Id));
        Assert.Equal(
            invoice.Number,
            await database.ScalarAsync<string>("SELECT metadata ->> 'invoiceNumber' FROM audit_logs WHERE entity_id = @i AND action = 'invoice.email_resent'", ("i", invoice.Id)));

        var email = Assert.Single(sender.Messages);
        var raw = InvoiceApi.TokenOf(email);
        Assert.Equal(Recipient, email.To);
        Assert.Contains(Note, email.TextBody, StringComparison.Ordinal);
        Assert.Equal(1, await database.CountAsync("invoice_access_tokens", "invoice_id = @i AND token_hash = @h AND revoked_at IS NULL", ("i", invoice.Id), ("h", InvoiceApi.Hash(raw))));
        Assert.NotEqual(oldHash, InvoiceApi.Hash(raw));

        // Resend on a draft is invoice_not_sent with no write.
        var draft = await database.GenerateDraftAsync(Send, world, member.UserId, new JobSpec { Title = "Draft job" });
        var draftState = await database.StateAsync(draft.Id);
        await BillingSeed.ProblemAsync(
            await Send(HttpMethod.Post, InvoiceApi.Path(draft.Id, "/resend-email"), InvoiceApi.Resend((await Detail(draft.Id))["updatedAt"]!.GetValue<string>())),
            HttpStatusCode.Conflict,
            "invoice_not_sent");
        Assert.Equal(draftState, await database.StateAsync(draft.Id));
        Assert.Single(sender.Messages);

        // AC-15: logs and audit rows contain no recipient, message, token or customer data.
        var logs = string.Join('\n', session.Logs.Entries.Select(entry => $"{entry.Message} {entry.Exception} {string.Join(' ', entry.Properties.Values)}"));
        var audit = await database.AuditTextAsync(invoice.Id);

        foreach (var secret in new[] { Recipient, Note, raw, "Carla Customer", "Pat Contact", "1 Seed St", "5551234567" })
        {
            Assert.DoesNotContain(secret, logs, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(secret, audit, StringComparison.OrdinalIgnoreCase);
        }
    }

    // Fails the save that writes the access token, which happens after the status update of the same transaction.
    private sealed class FailOnTokenInterceptor : SaveChangesInterceptor
    {
        public volatile bool Armed;

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            Check(eventData);

            return result;
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Check(eventData);

            return ValueTask.FromResult(result);
        }

        private void Check(DbContextEventData eventData)
        {
            if (Armed && eventData.Context!.ChangeTracker.Entries<InvoiceAccessToken>().Any(entry => entry.State == EntityState.Added))
            {
                throw new InvalidOperationException("Injected failure while saving the access token.");
            }
        }
    }
}
