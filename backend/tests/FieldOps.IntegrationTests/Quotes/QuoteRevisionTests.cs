using System.Net;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.ServiceRequests;

namespace FieldOps.IntegrationTests.Quotes;

/// <summary>Revision, discard and request guards (quote-builder AC-05, AC-21, AC-22, AC-23).</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class QuoteRevisionTests(CompanySettingsDatabaseFixture database)
{
    private Task<long> TokenCountAsync(Guid quoteId, bool onlyActive, int? versionNo = null) =>
        database.ScalarAsync<long>(
            """
            SELECT COUNT(*) FROM quote_access_tokens t JOIN quote_versions v ON v.id = t.quote_version_id
            WHERE v.quote_id = @q AND (@a = false OR t.revoked_at IS NULL) AND (@n::int IS NULL OR v.version_no = @n)
            """,
            ("q", quoteId),
            ("a", onlyActive),
            ("n", versionNo));

    [Fact]
    public async Task ReviseSupersedesTokensDiscardDeletesDraftsAndRequestTransitionsRespectDrafts()
    {
        var world = await database.SeedQuoteWorldAsync();
        await using var host = RequestsHost.Create(database);
        var (owner, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var request = await database.SeedReadyRequestAsync(world);
        var quote = await QuotesApi.CreateAsync(host, owner, request);
        var quoteId = quote["id"]!.GetValue<Guid>();
        var path = $"/quotes/{quoteId}";

        var v1Lines = new[] { QuotesApi.Line(name: "Pipe repair", quantity: 2m, unitPrice: 100m, unitCost: 30m), QuotesApi.Line(name: "Extra", unitPrice: 10m, optional: true) };
        var sent = await QuotesApi.SendAsync(host, owner, quote, QuotesApi.Draft(v1Lines, discount: 20m, message: "First version", validDays: 10));
        var current = sent["quote"]!;
        var v1Tax = await database.ScalarAsync<decimal>("SELECT tax_total FROM quote_versions WHERE quote_id = @q AND version_no = 1", ("q", quoteId));
        Assert.Equal(14.85m, v1Tax);

        // AC-21: the revision copies the last sent version and recalculates with the current organization rate.
        await database.ExecuteAsync("UPDATE organizations SET default_tax_rate = 10 WHERE id = @o", ("o", world.Org));
        var stale = await host.SendAsync(HttpMethod.Post, $"{path}/revise", owner, QuotesApi.UpdatedAt(quote));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("quote_changed", QuotesApi.Code(await RequestsHost.ReadAsync(stale)));

        var revised = await host.SendAsync(HttpMethod.Post, $"{path}/revise", owner, QuotesApi.UpdatedAt(current));
        Assert.Equal(HttpStatusCode.OK, revised.StatusCode);
        var revision = await RequestsHost.ReadAsync(revised);
        var draft = revision["draft"]!;
        Assert.Equal(2, draft["versionNo"]!.GetValue<int>());
        Assert.Equal("sent", revision["status"]!.GetValue<string>());
        Assert.Equal(["Pipe repair", "Extra"], draft["lines"]!.AsArray().Select(line => line!["name"]!.GetValue<string>()));
        Assert.Equal("First version", draft["customerMessage"]!.GetValue<string>());
        Assert.Equal(18.00m, draft["calculation"]!["taxTotal"]!.GetValue<decimal>());
        Assert.Equal(QuotesApi.Today(10), draft["validUntil"]!.GetValue<string>());
        Assert.Equal("quoted", await database.StatusOfAsync(request));
        Assert.Equal(1, await TokenCountAsync(quoteId, onlyActive: true, versionNo: 1));
        Assert.Equal(1, await database.QuoteAuditCountAsync(quoteId, "quote.revised"));
        Assert.Equal(v1Tax, await database.ScalarAsync<decimal>("SELECT tax_total FROM quote_versions WHERE quote_id = @q AND version_no = 1", ("q", quoteId)));

        var again = await host.SendAsync(HttpMethod.Post, $"{path}/revise", owner, QuotesApi.UpdatedAt(revision));
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("quote_changed", QuotesApi.Code(await RequestsHost.ReadAsync(again)));

        // Resend stays available while a revision exists and keeps the revision untouched.
        var resend = await host.SendAsync(
            HttpMethod.Post, $"{path}/resend-email", owner, QuotesApi.UpdatedAt(revision).With("emailMessage", "Again"));
        Assert.Equal(HttpStatusCode.OK, resend.StatusCode);
        var afterResend = (await RequestsHost.ReadAsync(resend))["quote"]!;
        Assert.Equal(2, afterResend["draft"]!["versionNo"]!.GetValue<int>());
        Assert.Equal(1, await TokenCountAsync(quoteId, onlyActive: true, versionNo: 1));

        // Sending version 2 supersedes version 1 and its token; version 1 stays as it was.
        var v2 = QuotesApi.Draft(
            [QuotesApi.Line(name: "Pipe repair", quantity: 2m, unitPrice: 150m, unitCost: 30m)], message: "Second version", validDays: 20);
        var secondSend = await QuotesApi.SendAsync(host, owner, afterResend, v2);
        var final = secondSend["quote"]!;
        Assert.Equal(1, await TokenCountAsync(quoteId, onlyActive: true));
        Assert.Equal(0, await TokenCountAsync(quoteId, onlyActive: true, versionNo: 1));
        Assert.Equal(1, await TokenCountAsync(quoteId, onlyActive: true, versionNo: 2));
        Assert.Equal(2, await database.ScalarAsync<int>("SELECT current_version_no FROM quotes WHERE id = @q", ("q", quoteId)));
        Assert.Equal(v1Tax, await database.ScalarAsync<decimal>("SELECT tax_total FROM quote_versions WHERE quote_id = @q AND version_no = 1", ("q", quoteId)));
        Assert.Equal(
            [(1, false), (2, true)],
            final["sentVersions"]!.AsArray().Select(version => (version!["versionNo"]!.GetValue<int>(), version["isCurrent"]!.GetValue<bool>())));
        Assert.Equal("quoted", await database.StatusOfAsync(request));
        Assert.Equal(1, await database.ScalarAsync<long>(
            "SELECT COUNT(*) FROM request_status_history WHERE request_id = @r AND to_status = 'quoted'", ("r", request)));
        Assert.Equal(1, await database.AuditCountAsync(request, "service_request.quoted"));
        Assert.Equal(2, await database.QuoteAuditCountAsync(quoteId, "quote.sent"));

        // AC-22: discarding a revision deletes its version and lines and leaves the sent versions and tokens alone.
        var third = await RequestsHost.ReadAsync(await host.SendAsync(HttpMethod.Post, $"{path}/revise", owner, QuotesApi.UpdatedAt(final)));
        Assert.Equal(3, third["draft"]!["versionNo"]!.GetValue<int>());
        var discarded = await host.SendAsync(HttpMethod.Post, $"{path}/discard-draft", owner, QuotesApi.UpdatedAt(third));
        Assert.Equal(HttpStatusCode.OK, discarded.StatusCode);
        var discardedBody = await RequestsHost.ReadAsync(discarded);
        Assert.Equal(quoteId, discardedBody["quoteId"]!.GetValue<Guid>());
        Assert.Equal("sent", discardedBody["status"]!.GetValue<string>());
        Assert.Equal(request, discardedBody["requestId"]!.GetValue<Guid>());
        Assert.Equal(2, await database.ScalarAsync<long>("SELECT COUNT(*) FROM quote_versions WHERE quote_id = @q", ("q", quoteId)));
        Assert.Equal(3, await database.LineCountAsync(quoteId));
        Assert.Equal(1, await TokenCountAsync(quoteId, onlyActive: true, versionNo: 2));
        Assert.Equal(1, await database.QuoteAuditCountAsync(quoteId, "quote.draft_discarded"));
        var noDraft = await host.SendAsync(
            HttpMethod.Post, $"{path}/discard-draft", owner, new JsonObject { ["updatedAt"] = (await QuotesApi.GetAsync(host, owner, quoteId))["updatedAt"]!.GetValue<string>() });
        Assert.Equal(HttpStatusCode.Conflict, noDraft.StatusCode);
        Assert.Equal("no_draft", QuotesApi.Code(await RequestsHost.ReadAsync(noDraft)));

        // AC-22, AC-05: discarding version 1 cancels the quote, keeps the draft rows and never reuses the number.
        var fresh = await database.SeedReadyRequestAsync(world);
        var draftQuote = await QuotesApi.CreateAsync(host, owner, fresh);
        var draftId = draftQuote["id"]!.GetValue<Guid>();
        var number = draftQuote["number"]!.GetValue<long>();
        var cancelled = await host.SendAsync(HttpMethod.Post, $"/quotes/{draftId}/discard-draft", owner, QuotesApi.UpdatedAt(draftQuote));
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        Assert.Equal("cancelled", (await RequestsHost.ReadAsync(cancelled))["status"]!.GetValue<string>());
        Assert.Equal("cancelled", await database.ScalarAsync<string>("SELECT status::text FROM quotes WHERE id = @q", ("q", draftId)));
        Assert.Equal(1, await database.ScalarAsync<long>("SELECT COUNT(*) FROM quote_versions WHERE quote_id = @q", ("q", draftId)));
        Assert.Equal("ready_for_quote", await database.StatusOfAsync(fresh));
        var (cancelBefore, cancelAfter, cancelMetadata) = await database.GetLatestAuditAsync(world.Org, "quote.cancelled");
        Assert.Contains("draft", cancelBefore!, StringComparison.Ordinal);
        Assert.Contains("cancelled", cancelAfter!, StringComparison.Ordinal);
        Assert.Equal("discarded", JsonNode.Parse(cancelMetadata!)!["reason"]!.GetValue<string>());
        var panel = await RequestsHost.ReadAsync(await host.SendAsync(HttpMethod.Get, $"/service-requests/{fresh}", owner));
        Assert.Null(panel["quote"]);
        var staleSave = await host.SendAsync(
            HttpMethod.Put, $"/quotes/{draftId}/draft", owner, QuotesApi.Draft([QuotesApi.Line()]).With("updatedAt", (await QuotesApi.GetAsync(host, owner, draftId))["updatedAt"]!.GetValue<string>()));
        Assert.Equal(HttpStatusCode.Conflict, staleSave.StatusCode);
        Assert.Equal("quote_changed", QuotesApi.Code(await RequestsHost.ReadAsync(staleSave)));

        var replacement = await QuotesApi.CreateAsync(host, owner, fresh);
        Assert.Equal(number + 1, replacement["number"]!.GetValue<long>());

        // AC-23: a draft quote blocks moving back to review; cancelling the request cancels the draft with it.
        var move = await host.SendAsync(HttpMethod.Post, $"/service-requests/{fresh}/move-to-review", owner);
        Assert.Equal(HttpStatusCode.Conflict, move.StatusCode);
        var moveProblem = await RequestsHost.ReadAsync(move);
        Assert.Equal("quote_draft_exists", QuotesApi.Code(moveProblem));
        Assert.Equal("This request has a draft quote. Discard it first.", moveProblem["title"]!.GetValue<string>());
        Assert.Equal("ready_for_quote", await database.StatusOfAsync(fresh));

        var cancelRequest = await host.SendAsync(
            HttpMethod.Post, $"/service-requests/{fresh}/cancel", owner, new JsonObject { ["reason"] = "Customer left" });
        Assert.Equal(HttpStatusCode.OK, cancelRequest.StatusCode);
        Assert.Equal("cancelled", await database.StatusOfAsync(fresh));
        var replacementId = replacement["id"]!.GetValue<Guid>();
        Assert.Equal("cancelled", await database.ScalarAsync<string>("SELECT status::text FROM quotes WHERE id = @q", ("q", replacementId)));
        Assert.Equal(
            "request_cancelled",
            JsonNode.Parse(await database.ScalarAsync<string>(
                "SELECT metadata::text FROM audit_logs WHERE entity_id = @q AND action = 'quote.cancelled'", ("q", replacementId)))!["reason"]!.GetValue<string>());
        Assert.DoesNotContain("Customer left", await database.QuoteAuditTextAsync(replacementId, fresh), StringComparison.Ordinal);
    }
}
