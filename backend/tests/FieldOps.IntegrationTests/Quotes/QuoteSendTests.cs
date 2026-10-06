using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.Api;
using FieldOps.IntegrationTests.Catalog;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.ServiceRequests;

namespace FieldOps.IntegrationTests.Quotes;

/// <summary>Send, immutability and audit content (quote-builder AC-11, AC-17, AC-19, AC-20, AC-24, AC-27).</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class QuoteSendTests(CompanySettingsDatabaseFixture database)
{
    [Fact]
    public async Task Send_FreezesTheVersionCreatesTheTokenAndNeverTouchesItAfterSettingChanges()
    {
        var world = await database.SeedQuoteWorldAsync();
        await using var host = RequestsHost.Create(database);
        var (owner, member) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var item = await database.SeedCatalogItemAsync(world.Org, "service", "Catalog item", 95m, 40m);

        // AC-11: an optional-only draft cannot be sent; AC-19: neither can a draft without a recipient.
        var request = await database.SeedReadyRequestAsync(world);
        var quote = await QuotesApi.CreateAsync(host, owner, request);
        var quoteId = quote["id"]!.GetValue<Guid>();
        var optionalOnly = await host.SendAsync(
            HttpMethod.Post,
            $"/quotes/{quoteId}/send",
            owner,
            QuotesApi.Draft([QuotesApi.Line(optional: true)]).WithToken(quote));
        Assert.Equal(HttpStatusCode.BadRequest, optionalOnly.StatusCode);
        Assert.Equal("Add at least one line that isn't optional.", QuotesApi.Error(await RequestsHost.ReadAsync(optionalOnly), "lines"));
        var emptyMessage = await host.SendAsync(
            HttpMethod.Post, $"/quotes/{quoteId}/send", owner, QuotesApi.Draft([QuotesApi.Line()]).WithToken(quote, "  "));
        Assert.Equal("Enter a message.", QuotesApi.Error(await RequestsHost.ReadAsync(emptyMessage), "emailMessage"));

        var guestless = await database.SeedReadyRequestAsync(world, linkCustomer: false, guestEmail: null);
        var guestlessQuote = await QuotesApi.CreateAsync(host, owner, guestless);
        var noRecipient = await host.SendAsync(
            HttpMethod.Post,
            $"/quotes/{guestlessQuote["id"]}/send",
            owner,
            QuotesApi.Draft([QuotesApi.Line()]).WithToken(guestlessQuote));
        Assert.Equal(HttpStatusCode.BadRequest, noRecipient.StatusCode);
        Assert.Equal("This customer has no email address.", QuotesApi.Error(await RequestsHost.ReadAsync(noRecipient), "recipient"));
        Assert.Null(guestlessQuote["recipient"]!["email"]);

        Assert.Equal("draft", await database.ScalarAsync<string>("SELECT status::text FROM quotes WHERE id = @q", ("q", quoteId)));
        Assert.Equal("ready_for_quote", await database.StatusOfAsync(request));
        Assert.Empty(host.Sender.Messages);

        // AC-17: one transaction freezes the version, moves the request and writes the token and the audit rows.
        var draft = QuotesApi.Draft(
            [
                QuotesApi.Line(name: "SECRET LINE ONE", description: "SECRET DESCRIPTION", quantity: 2m, unitPrice: 95m, unitCost: 40m, catalogItemId: item),
                QuotesApi.Line(name: "SECRET OPTIONAL", unitPrice: 25m, optional: true),
            ],
            discount: 10m,
            message: "SECRET CUSTOMER MESSAGE",
            note: "SECRET INTERNAL NOTE",
            preset: "custom",
            customText: "SECRET TERMS TEXT",
            validDays: 40);
        var sent = await QuotesApi.SendAsync(host, owner, quote, draft);

        Assert.Equal("sent", sent["emailStatus"]!.GetValue<string>());
        var detail = sent["quote"]!;
        Assert.Equal("sent", detail["status"]!.GetValue<string>());
        Assert.Null(detail["draft"]);
        Assert.Equal(1, detail["sentVersions"]![0]!["versionNo"]!.GetValue<int>());
        Assert.True(detail["sentVersions"]![0]!["isCurrent"]!.GetValue<bool>());
        Assert.Equal(194.85m, detail["sentVersions"]![0]!["total"]!.GetValue<decimal>());

        Assert.Equal(
            "true|true|USD|10.00|190.00|14.85|194.85|SECRET TERMS TEXT",
            await database.ScalarAsync<string>(
                "SELECT is_immutable::text || '|' || (sent_at IS NOT NULL)::text || '|' || currency || '|' || discount_total || '|' || subtotal || '|' || tax_total || '|' || total || '|' || terms FROM quote_versions WHERE quote_id = @q",
                ("q", quoteId)));
        Assert.Equal(QuotesApi.Today(40), await database.ScalarAsync<string>("SELECT valid_until::text FROM quote_versions WHERE quote_id = @q", ("q", quoteId)));
        Assert.Equal(1, await database.ScalarAsync<int>("SELECT current_version_no FROM quotes WHERE id = @q", ("q", quoteId)));
        Assert.Equal("quoted", await database.StatusOfAsync(request));
        Assert.Equal(1, await database.ScalarAsync<long>(
            "SELECT COUNT(*) FROM request_status_history WHERE request_id = @r AND to_status = 'quoted' AND changed_by_user_id = @u",
            ("r", request),
            ("u", member.UserId)));
        Assert.Equal(1, await database.QuoteAuditCountAsync(quoteId, "quote.sent"));
        Assert.Equal(1, await database.AuditCountAsync(request, "service_request.quoted"));
        var (_, _, sentMetadata) = await database.GetLatestAuditAsync(world.Org, "quote.sent");
        Assert.Equal(1, JsonNode.Parse(sentMetadata!)!["versionNo"]!.GetValue<int>());
        Assert.Equal("email", JsonNode.Parse(sentMetadata!)!["notified"]!.GetValue<string>());
        Assert.Equal(194.85m, JsonNode.Parse(sentMetadata!)!["total"]!.GetValue<decimal>());

        // The email after the commit: the raw token only there, the database holds its SHA-256 hash and the end of the day.
        var message = Assert.Single(host.Sender.Messages);
        Assert.Equal(QuotesApi.RecipientEmail, message.To);
        var organizationName = await database.ScalarAsync<string>("SELECT name FROM organizations WHERE id = @o", ("o", world.Org));
        Assert.Equal($"{organizationName}: quote Q-2036 for Drain cleaning", message.Subject);
        Assert.Contains("Hi Pat, here is your quote.", message.TextBody, StringComparison.Ordinal);
        Assert.Contains("Quote Q-2036 · Total 194.85 USD · Valid until", message.TextBody, StringComparison.Ordinal);
        Assert.Contains("Questions? Call us at +1 555 010 0100.", message.TextBody, StringComparison.Ordinal);
        Assert.Contains($"Review your quote: {FieldOpsApiFactory.AllowedOrigin}/quotes/view#token=", message.TextBody, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET", message.TextBody + message.HtmlBody, StringComparison.Ordinal);
        var raw = QuotesApi.TokenOf(message.TextBody);
        Assert.Equal(43, raw.Length);
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
        Assert.Equal(
            1,
            await database.ScalarAsync<long>(
                "SELECT COUNT(*) FROM quote_access_tokens WHERE token_hash = @h AND revoked_at IS NULL AND organization_id = @o AND created_by_user_id = @u",
                ("h", hash),
                ("o", world.Org),
                ("u", member.UserId)));
        var expires = await database.ScalarAsync<DateTimeOffset>("SELECT expires_at FROM quote_access_tokens WHERE token_hash = @h", ("h", hash));
        Assert.Equal(DateTimeOffset.Parse($"{QuotesApi.Today(41)}T00:00:00+00:00"), expires);
        Assert.Equal(0, await database.ScalarAsync<long>(
            "SELECT COUNT(*) FROM quote_access_tokens WHERE token_hash = @r", ("r", raw)));

        // AC-24, AC-27: audit rows carry ids, codes and numbers only.
        var audit = await database.QuoteAuditTextAsync(quoteId, request);
        foreach (var secret in new[] { "SECRET", "Carla", QuotesApi.RecipientEmail, "5551234567", "1 Seed St", raw, hash, "Burst", "Hi Pat" })
        {
            Assert.DoesNotContain(secret, audit, StringComparison.Ordinal);
        }

        // AC-20: later catalog and settings changes never alter the sent version; edits are 409 no_draft.
        var before = await RequestsHost.ReadAsync(await host.SendAsync(HttpMethod.Get, $"/quotes/{quoteId}/versions/1", owner));
        await database.ExecuteAsync("UPDATE catalog_items SET unit_price = 500, unit_cost = 300, name = 'Renamed' WHERE id = @i", ("i", item));
        await database.ExecuteAsync("UPDATE organizations SET default_tax_rate = 20, currency = 'EUR' WHERE id = @o", ("o", world.Org));
        var after = await RequestsHost.ReadAsync(await host.SendAsync(HttpMethod.Get, $"/quotes/{quoteId}/versions/1", owner));
        Assert.Equal(before.ToJsonString(), after.ToJsonString());
        Assert.Equal("USD", after["currency"]!.GetValue<string>());
        Assert.Equal("Tax (8.25%)", after["taxLabel"]!.GetValue<string>());
        Assert.Equal(55.6m, after["margin"]!["percent"]!.GetValue<decimal>());
        Assert.Equal("SECRET INTERNAL NOTE", after["internalNote"]!.GetValue<string>());
        Assert.Equal("SECRET LINE ONE", after["lines"]![0]!["name"]!.GetValue<string>());
        Assert.Equal(40m, after["lines"]![0]!["unitCost"]!.GetValue<decimal>());
        Assert.True(after["lines"]![1]!["isOptional"]!.GetValue<bool>());

        var current = await QuotesApi.GetAsync(host, owner, quoteId);
        var updatedAt = await database.QuoteUpdatedAtAsync(quoteId);
        foreach (var (method, suffix) in new[] { (HttpMethod.Put, "draft"), (HttpMethod.Post, "calculate"), (HttpMethod.Post, "send") })
        {
            var response = await host.SendAsync(
                method, $"/quotes/{quoteId}/{suffix}", owner, QuotesApi.Draft([QuotesApi.Line()]).WithToken(current));
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            var problem = await RequestsHost.ReadAsync(response);
            Assert.Equal("no_draft", QuotesApi.Code(problem));
            Assert.Equal("This quote has no draft. Revise it to make changes.", problem["title"]!.GetValue<string>());
        }

        Assert.Equal(updatedAt, await database.QuoteUpdatedAtAsync(quoteId));
        Assert.Equal(2, await database.LineCountAsync(quoteId));
        Assert.Single(host.Sender.Messages);
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Get, $"/quotes/{quoteId}/versions/2", owner)).StatusCode);
    }
}
