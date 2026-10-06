using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.ServiceRequests;
using FieldOps.IntegrationTests.Users;

namespace FieldOps.IntegrationTests.Quotes;

/// <summary>Email failure, resend and what logs may contain (quote-builder AC-18, AC-24, AC-27).</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class QuoteDeliveryTests(CompanySettingsDatabaseFixture database)
{
    private static string Hash(string raw) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));

    [Fact]
    public async Task EmailFailureKeepsTheSendAndResendReplacesTheTokenWithoutLeakingSecrets()
    {
        var world = await database.SeedQuoteWorldAsync();
        await using var host = RequestsHost.Create(database);
        var (owner, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var request = await database.SeedReadyRequestAsync(world);
        var quote = await QuotesApi.CreateAsync(host, owner, request);
        var quoteId = quote["id"]!.GetValue<Guid>();
        var path = $"/quotes/{quoteId}";

        // Resend is only for a sent quote.
        var early = await host.SendAsync(
            HttpMethod.Post, $"{path}/resend-email", owner, QuotesApi.UpdatedAt(quote).With("emailMessage", "Hi"));
        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);
        Assert.Equal("quote_changed", QuotesApi.Code(await RequestsHost.ReadAsync(early)));

        // AC-18: the provider fails after the commit; the send stays and the response says so.
        host.Sender.Fail = true;
        var sent = await QuotesApi.SendAsync(host, owner, quote, QuotesApi.Draft([QuotesApi.Line(unitPrice: 50m)]));

        Assert.Equal("failed", sent["emailStatus"]!.GetValue<string>());
        Assert.Equal("sent", sent["quote"]!["status"]!.GetValue<string>());
        Assert.Equal("quoted", await database.StatusOfAsync(request));
        Assert.Equal(1, await database.ScalarAsync<long>(
            "SELECT COUNT(*) FROM quote_access_tokens t JOIN quote_versions v ON v.id = t.quote_version_id WHERE v.quote_id = @q AND t.revoked_at IS NULL AND v.is_immutable",
            ("q", quoteId)));
        Assert.Equal(1, host.Sender.Attempts);
        var logs = new CapturingLogs(host.Logs).AllText();
        Assert.Contains(quoteId.ToString(), logs, StringComparison.Ordinal);
        Assert.Contains("InvalidOperationException", logs, StringComparison.Ordinal);
        foreach (var secret in new[] { QuotesApi.RecipientEmail, "quotes/view", "token=", "Hi Pat", "Carla" })
        {
            Assert.DoesNotContain(secret, logs, StringComparison.Ordinal);
        }

        var firstHash = await database.ScalarAsync<string>("SELECT token_hash FROM quote_access_tokens WHERE revoked_at IS NULL AND organization_id = @o", ("o", world.Org));

        // Validation of the resend body and its recipient.
        var empty = await host.SendAsync(
            HttpMethod.Post, $"{path}/resend-email", owner, QuotesApi.UpdatedAt(sent["quote"]!).With("emailMessage", " "));
        Assert.Equal("Enter a message.", QuotesApi.Error(await RequestsHost.ReadAsync(empty), "emailMessage"));
        var tooLong = await host.SendAsync(
            HttpMethod.Post, $"{path}/resend-email", owner, QuotesApi.UpdatedAt(sent["quote"]!).With("emailMessage", new string('m', 321)));
        Assert.Equal("Message must be 320 characters or fewer.", QuotesApi.Error(await RequestsHost.ReadAsync(tooLong), "emailMessage"));

        // The resend revokes the earlier token, creates a new one and sends the same email.
        host.Sender.Fail = false;
        var stale = await host.SendAsync(
            HttpMethod.Post, $"{path}/resend-email", owner, QuotesApi.UpdatedAt(quote).With("emailMessage", "Hi again"));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("quote_changed", QuotesApi.Code(await RequestsHost.ReadAsync(stale)));
        Assert.Empty(host.Sender.Messages);

        var resent = await host.SendAsync(
            HttpMethod.Post, $"{path}/resend-email", owner, QuotesApi.UpdatedAt(sent["quote"]!).With("emailMessage", "Hi again"));
        Assert.Equal(HttpStatusCode.OK, resent.StatusCode);
        var resentBody = await RequestsHost.ReadAsync(resent);
        Assert.Equal("sent", resentBody["emailStatus"]!.GetValue<string>());
        Assert.NotEqual(sent["quote"]!["updatedAt"]!.GetValue<string>(), resentBody["quote"]!["updatedAt"]!.GetValue<string>());

        var message = Assert.Single(host.Sender.Messages);
        Assert.Contains("Hi again", message.TextBody, StringComparison.Ordinal);
        var newHash = Hash(QuotesApi.TokenOf(message.TextBody));
        Assert.NotEqual(firstHash, newHash);
        Assert.Equal(2, await database.ScalarAsync<long>("SELECT COUNT(*) FROM quote_access_tokens WHERE organization_id = @o", ("o", world.Org)));
        Assert.Equal(newHash, await database.ScalarAsync<string>("SELECT token_hash FROM quote_access_tokens WHERE revoked_at IS NULL AND organization_id = @o", ("o", world.Org)));
        Assert.True(await database.ScalarAsync<bool>("SELECT revoked_at IS NOT NULL FROM quote_access_tokens WHERE token_hash = @h", ("h", firstHash)));
        Assert.Equal(1, await database.QuoteAuditCountAsync(quoteId, "quote.email_resent"));
        var (_, _, resentMetadata) = await database.GetLatestAuditAsync(world.Org, "quote.email_resent");
        Assert.Equal(1, JsonNode.Parse(resentMetadata!)!["versionNo"]!.GetValue<int>());
        var audit = await database.QuoteAuditTextAsync(quoteId, request);
        Assert.DoesNotContain(QuotesApi.RecipientEmail, audit, StringComparison.Ordinal);
        Assert.DoesNotContain("Hi again", audit, StringComparison.Ordinal);

        // A resend without a recipient is a 400 and changes nothing.
        await database.ExecuteAsync("UPDATE customer_contacts SET email = NULL WHERE id = @c", ("c", world.Contact));
        await database.ExecuteAsync("UPDATE service_requests SET guest_email = NULL WHERE id = @r", ("r", request));
        var noRecipient = await host.SendAsync(
            HttpMethod.Post, $"{path}/resend-email", owner, QuotesApi.UpdatedAt(resentBody["quote"]!).With("emailMessage", "Hi"));
        Assert.Equal(HttpStatusCode.BadRequest, noRecipient.StatusCode);
        Assert.Equal("This customer has no email address.", QuotesApi.Error(await RequestsHost.ReadAsync(noRecipient), "recipient"));
        Assert.Equal(2, await database.ScalarAsync<long>("SELECT COUNT(*) FROM quote_access_tokens WHERE organization_id = @o", ("o", world.Org)));
        Assert.Single(host.Sender.Messages);
    }
}
