using System.Net;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.Quotes;
using FieldOps.IntegrationTests.ServiceRequests;

namespace FieldOps.IntegrationTests.QuoteLinks;

/// <summary>Token resolution table, lazy expiry and final states (customer-quote-approval AC-02, AC-03, AC-16).</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class QuoteLinkResolutionTests(CompanySettingsDatabaseFixture database)
{
    private const string UnavailableTitle = "This link isn't available.";

    private static async Task<string> UnavailableBodyAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await RequestsHost.ReadAsync(response);
        Assert.Equal("quote_link_unavailable", body["code"]!.GetValue<string>());
        Assert.Equal(UnavailableTitle, body["title"]!.GetValue<string>());
        body.AsObject().Remove("traceId");

        return body.ToJsonString();
    }

    // AC-02: every unusable token is the same 404 and the sent quote of an expired link becomes expired exactly once.
    [Fact]
    public async Task View_WithAnyUnusableToken_ReturnsOneIdentical404AndExpiresASentQuoteOnce()
    {
        var world = await database.SeedQuoteWorldAsync();
        await using var host = RequestsHost.Create(database);
        var (owner, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);

        var revoked = await QuoteLinkApi.SendAsync(database, host, world, owner);
        var resend = await host.SendAsync(
            HttpMethod.Post,
            $"/quotes/{revoked.QuoteId}/resend-email",
            owner,
            QuotesApi.UpdatedAt(await QuotesApi.GetAsync(host, owner, revoked.QuoteId)).With("emailMessage", "Hi again"));
        Assert.Equal(HttpStatusCode.OK, resend.StatusCode);
        var replacement = QuotesApi.TokenOf(host.Sender.Messages[^1].TextBody);

        var cancelled = await QuoteLinkApi.SendAsync(database, host, world, owner);
        await database.ExecuteAsync("UPDATE quotes SET status = 'cancelled' WHERE id = @q", ("q", cancelled.QuoteId));

        var expiredSent = await QuoteLinkApi.SendAsync(database, host, world, owner);
        var expiredAsked = await QuoteLinkApi.SendAsync(database, host, world, owner);
        var question = await QuoteLinkApi.PostAsync(
            host, "clarification", QuoteLinkApi.Body(expiredAsked.Token, ("message", "Is parking included?")));
        Assert.Equal(HttpStatusCode.OK, question.StatusCode);
        await database.ExpireTokenAsync(expiredSent.Token);
        await database.ExpireTokenAsync(expiredAsked.Token);

        var unusable = new (string Name, JsonObject Body)[]
        {
            ("missing", new JsonObject()),
            ("null", new JsonObject { ["token"] = null }),
            ("malformed", QuoteLinkApi.Body("not-a-token")),
            ("padded", QuoteLinkApi.Body(new string('A', 42) + "=")),
            ("unknown", QuoteLinkApi.Body(new string('B', 43))),
            ("revoked", QuoteLinkApi.Body(revoked.Token)),
            ("cancelled", QuoteLinkApi.Body(cancelled.Token)),
            ("expired-sent", QuoteLinkApi.Body(expiredSent.Token)),
            ("expired-question", QuoteLinkApi.Body(expiredAsked.Token)),
        };

        // Every operation of the feature answers the same; the expired quotes change only on the first touch.
        var seen = new HashSet<string>();

        foreach (var action in new[] { "view", "pdf", "logo" })
        {
            foreach (var (name, body) in unusable)
            {
                var response = await QuoteLinkApi.PostAsync(host, action, body);
                seen.Add(await UnavailableBodyAsync(response));
                Assert.True(seen.Count == 1, $"{action}/{name} differs from the generic body");
            }
        }

        foreach (var (action, extra) in new (string, (string, JsonNode?)[])[]
        {
            ("calculate", [("selectedOptionalLineIds", new JsonArray())]),
            ("approve", [("selectedOptionalLineIds", new JsonArray()), ("acceptTerms", true)]),
            ("decline", [("reason", "No thanks")]),
            ("clarification", [("message", "A question")]),
        })
        {
            foreach (var (name, body) in unusable)
            {
                var withExtra = QuoteLinkApi.Body((string?)body["token"] ?? string.Empty, extra);

                if (!body.ContainsKey("token") || body["token"] is null)
                {
                    withExtra.Remove("token");
                }

                seen.Add(await UnavailableBodyAsync(await QuoteLinkApi.PostAsync(host, action, withExtra)));
                Assert.True(seen.Count == 1, $"{action}/{name} differs from the generic body");
            }
        }

        var photo = await QuoteLinkApi.PostAsync(host, $"photos/{Guid.NewGuid()}", QuoteLinkApi.Body(new string('B', 43)));
        seen.Add(await UnavailableBodyAsync(photo));
        Assert.Single(seen);

        // The replacement link of the resend works; the revoked one never did.
        Assert.Equal("sent", (await QuoteLinkApi.ViewAsync(host, replacement))["quote"]!["status"]!.GetValue<string>());
        Assert.Equal("sent", await database.QuoteStatusAsync(revoked.QuoteId));
        Assert.Equal("cancelled", await database.QuoteStatusAsync(cancelled.QuoteId));

        // BR-18: the expired sent and clarification-requested quotes became expired, with one audit row each, once.
        Assert.Equal("expired", await database.QuoteStatusAsync(expiredSent.QuoteId));
        Assert.Equal("expired", await database.QuoteStatusAsync(expiredAsked.QuoteId));

        foreach (var (quote, before) in new[] { (expiredSent, "sent"), (expiredAsked, "clarification_requested") })
        {
            Assert.Equal(1, await database.QuoteAuditCountAsync(quote.QuoteId, "quote.expired"));
            var row = await database.ScalarAsync<string>(
                "SELECT before_data::text || ' ' || after_data::text || ' ' || metadata::text || ' ' || COALESCE(actor_user_id::text, 'no-actor') FROM audit_logs WHERE entity_id = @q AND action = 'quote.expired'",
                ("q", quote.QuoteId));
            var compact = row.Replace(" ", string.Empty, StringComparison.Ordinal);
            Assert.Contains($"{{\"status\":\"{before}\"}}", compact, StringComparison.Ordinal);
            Assert.Contains("{\"status\":\"expired\"}", compact, StringComparison.Ordinal);
            Assert.Contains("{\"versionNo\":1}", compact, StringComparison.Ordinal);
            Assert.EndsWith("no-actor", compact, StringComparison.Ordinal);
        }

        Assert.Equal(0, await database.QuoteAuditCountAsync(revoked.QuoteId, "quote.expired"));
        Assert.Equal(0, await database.QuoteAuditCountAsync(cancelled.QuoteId, "quote.expired"));

        // Staff see the stored status expired, and the link of an expired quote never answers again.
        Assert.Equal("expired", (await QuotesApi.GetAsync(host, owner, expiredSent.QuoteId))["status"]!.GetValue<string>());
    }

    // AC-03, AC-16: an older version is gone with no data; approved and declined quotes show their final state until the
    // link expires and never become expired.
    [Fact]
    public async Task Resolution_WithAnOlderVersionIsGone_AndFinalQuotesNeverExpire()
    {
        var world = await database.SeedQuoteWorldAsync();
        await using var host = RequestsHost.Create(database);
        var (owner, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);

        // Version 1 is sent, revised and version 2 is sent: the version-1 link is superseded whatever it was.
        var first = await QuoteLinkApi.SendAsync(database, host, world, owner);
        var detail = await QuotesApi.GetAsync(host, owner, first.QuoteId);
        var revised = await host.SendAsync(HttpMethod.Post, $"/quotes/{first.QuoteId}/revise", owner, QuotesApi.UpdatedAt(detail));
        Assert.Equal(HttpStatusCode.OK, revised.StatusCode);
        var draft = await RequestsHost.ReadAsync(revised);
        var second = await QuotesApi.SendAsync(
            host, owner, draft, QuotesApi.Draft([QuotesApi.Line(name: "Revised work", unitPrice: 60m)]));
        Assert.Equal("sent", second["quote"]!["status"]!.GetValue<string>());
        var secondToken = QuotesApi.TokenOf(host.Sender.Messages[^1].TextBody);
        var updatedAt = await database.QuoteUpdatedAtAsync(first.QuoteId);

        await database.ExpireTokenAsync(first.Token);

        foreach (var (action, body) in new (string, JsonObject)[]
        {
            ("view", QuoteLinkApi.Body(first.Token)),
            ("pdf", QuoteLinkApi.Body(first.Token)),
            ("calculate", QuoteLinkApi.Body(first.Token, ("selectedOptionalLineIds", new JsonArray()))),
            ("approve", QuoteLinkApi.Body(first.Token, ("selectedOptionalLineIds", new JsonArray()), ("acceptTerms", true))),
            ("decline", QuoteLinkApi.Body(first.Token, ("reason", "No"))),
            ("clarification", QuoteLinkApi.Body(first.Token, ("message", "Question"))),
        })
        {
            var response = await QuoteLinkApi.PostAsync(host, action, body);
            Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
            var problem = await RequestsHost.ReadAsync(response);
            Assert.Equal("quote_superseded", problem["code"]!.GetValue<string>());
            Assert.Equal("A newer version of this quote is available.", problem["title"]!.GetValue<string>());
            Assert.DoesNotContain("Drain", problem.ToJsonString(), StringComparison.Ordinal);
        }

        Assert.Equal(0, await database.ResponseCountAsync(first.QuoteId));
        Assert.Equal("sent", await database.QuoteStatusAsync(first.QuoteId));
        Assert.Equal(updatedAt, await database.QuoteUpdatedAtAsync(first.QuoteId));
        Assert.Equal(2, (await QuoteLinkApi.ViewAsync(host, secondToken))["quote"]!["versionNo"]!.GetValue<int>());

        // AC-16: an approved and a declined quote show their final state, then the generic 404 once the link expired.
        var approved = await QuoteLinkApi.SendAsync(database, host, world, owner);
        var declined = await QuoteLinkApi.SendAsync(database, host, world, owner);
        var approval = await QuoteLinkApi.PostAsync(
            host, "approve", QuoteLinkApi.Body(approved.Token, ("selectedOptionalLineIds", new JsonArray()), ("acceptTerms", true)));
        var decline = await QuoteLinkApi.PostAsync(host, "decline", QuoteLinkApi.Body(declined.Token, ("reason", "Too expensive")));
        Assert.Equal(HttpStatusCode.OK, approval.StatusCode);
        Assert.Equal(HttpStatusCode.OK, decline.StatusCode);

        Assert.Equal("approved", (await QuoteLinkApi.ViewAsync(host, approved.Token))["quote"]!["status"]!.GetValue<string>());
        Assert.Equal("rejected", (await QuoteLinkApi.ViewAsync(host, declined.Token))["quote"]!["status"]!.GetValue<string>());

        await database.ExpireTokenAsync(approved.Token);
        await database.ExpireTokenAsync(declined.Token);

        foreach (var (token, quoteId, status) in new[] { (approved.Token, approved.QuoteId, "approved"), (declined.Token, declined.QuoteId, "rejected") })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await QuoteLinkApi.PostAsync(host, "view", QuoteLinkApi.Body(token))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await QuoteLinkApi.PostAsync(host, "pdf", QuoteLinkApi.Body(token))).StatusCode);
            Assert.Equal(status, await database.QuoteStatusAsync(quoteId));
            Assert.Equal(0, await database.QuoteAuditCountAsync(quoteId, "quote.expired"));
        }
    }
}
