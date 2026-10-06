using System.Net;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.Quotes;
using FieldOps.IntegrationTests.ServiceRequests;
using FieldOps.IntegrationTests.Users;

namespace FieldOps.IntegrationTests.QuoteLinks;

/// <summary>Approve, decline and question, idempotency, concurrency, audit and leaks (AC-10 to AC-15, AC-19).</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class QuoteLinkResponseTests(CompanySettingsDatabaseFixture database)
{
    private const string UserAgent = "FieldOpsTest/1.0 (CustomerPhone)";

    private static JsonObject Approve(string token, params Guid[] selected) =>
        QuoteLinkApi.Body(token, ("selectedOptionalLineIds", QuoteLinkApi.Ids(selected)), ("acceptTerms", true));

    private static async Task AssertAlreadyAnsweredAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await RequestsHost.ReadAsync(response);
        Assert.Equal("quote_already_answered", problem["code"]!.GetValue<string>());
        Assert.Equal("This quote has already been answered.", problem["title"]!.GetValue<string>());
    }

    private Task<long> NonResponseRowsAsync(Guid org) =>
        database.ScalarAsync<long>(
            """
            SELECT (SELECT COUNT(*) FROM work_orders WHERE organization_id = @o)
                 + (SELECT COUNT(*) FROM visits WHERE organization_id = @o)
                 + (SELECT COUNT(*) FROM invoices WHERE organization_id = @o)
                 + (SELECT COUNT(*) FROM payments WHERE organization_id = @o)
                 + (SELECT COUNT(*) FROM notifications WHERE organization_id = @o)
                 + (SELECT COUNT(*) FROM assessments WHERE organization_id = @o)
            """,
            ("o", org));

    // AC-10, AC-11, AC-14 (approval), AC-19: approve with a selection, then every repeat and different action.
    [Fact]
    public async Task Approve_StoresServerTotalsSelectionAndAuditOnce_AndRepeatsAreIdempotent()
    {
        var world = await database.SeedQuoteWorldAsync();
        await using var host = RequestsHost.Create(database);
        var (owner, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var sent = await QuoteLinkApi.SendAsync(database, host, world, owner);
        var optional = QuoteLinkApi.OptionalIds(await QuoteLinkApi.ViewAsync(host, sent.Token));
        var downstream = await NonResponseRowsAsync(world.Org);
        var updatedAt = await database.QuoteUpdatedAtAsync(sent.QuoteId);

        // The checkbox is required on the server too.
        foreach (var body in new[]
        {
            QuoteLinkApi.Body(sent.Token, ("selectedOptionalLineIds", QuoteLinkApi.Ids()), ("acceptTerms", false)),
            QuoteLinkApi.Body(sent.Token, ("selectedOptionalLineIds", QuoteLinkApi.Ids())),
        })
        {
            var unchecked_ = await QuoteLinkApi.PostAsync(host, "approve", body);
            Assert.Equal(HttpStatusCode.BadRequest, unchecked_.StatusCode);
            Assert.Equal(
                "Confirm that you approve the scope of work and agree to the terms.",
                QuotesApi.Error(await RequestsHost.ReadAsync(unchecked_), "acceptTerms"));
        }

        Assert.Equal(0, await database.ResponseCountAsync(sent.QuoteId));

        var approved = await QuoteLinkApi.PostAsync(host, "approve", Approve(sent.Token, optional[0]), "203.0.113.7", UserAgent);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        var view = await RequestsHost.ReadAsync(approved);
        Assert.Equal("approved", view["quote"]!["status"]!.GetValue<string>());
        Assert.Equal("approved", view["response"]!["type"]!.GetValue<string>());
        Assert.Equal(QuotesApi.Today(), view["response"]!["respondedOn"]!.GetValue<string>());
        Assert.Equal([optional[0].ToString()], view["response"]!["selectedOptionalLineIds"]!.AsArray().Select(id => id!.GetValue<string>()).ToArray());
        var totals = view["response"]!["totals"]!;
        Assert.Equal((310m, 10m, 24.75m, 324.75m, "Tax (8.25%)"), (totals["subtotal"]!.GetValue<decimal>(), totals["discountTotal"]!.GetValue<decimal>(), totals["taxTotal"]!.GetValue<decimal>(), totals["total"]!.GetValue<decimal>(), totals["taxLabel"]!.GetValue<string>()));

        Assert.Equal(
            $"approved|{world.Org}|{sent.VersionId}|Pat Contact|{world.Contact}|203.0.113.7|310.00|10.00|24.75|324.75|",
            await database.ScalarAsync<string>(
                "SELECT response::text || '|' || organization_id || '|' || quote_version_id || '|' || responder_name || '|' || responder_contact_id || '|' || host(ip_address) || '|' || subtotal || '|' || discount_total || '|' || tax_total || '|' || total || '|' || COALESCE(comment, '') FROM quote_responses WHERE quote_version_id = @v",
                ("v", sent.VersionId)));
        Assert.Equal(
            $"{optional[0]}",
            await database.ScalarAsync<string>(
                "SELECT quote_line_id::text FROM quote_response_optional_lines WHERE organization_id = @o AND quote_version_id = @v",
                ("o", world.Org),
                ("v", sent.VersionId)));
        Assert.Equal("approved", await database.QuoteStatusAsync(sent.QuoteId));
        Assert.Equal(
            sent.VersionId,
            await database.ScalarAsync<Guid>("SELECT approved_version_id FROM quotes WHERE id = @q", ("q", sent.QuoteId)));
        var approvedAt = await database.QuoteUpdatedAtAsync(sent.QuoteId);
        Assert.True(approvedAt > updatedAt);
        Assert.Equal("quoted", await database.StatusOfAsync(sent.RequestId));

        // BR-22: one audit row with the version, the server total and the accepted terms, and no personal data.
        Assert.Equal(1, await database.QuoteAuditCountAsync(sent.QuoteId, "quote.approved"));
        var audit = await database.ScalarAsync<string>(
            "SELECT before_data::text || ' ' || after_data::text || ' ' || metadata::text || ' ' || COALESCE(actor_user_id::text, 'no-actor') || ' ' || host(ip_address) || ' ' || (branch_id = @b)::text FROM audit_logs WHERE entity_id = @q AND action = 'quote.approved'",
            ("q", sent.QuoteId),
            ("b", world.BranchA));
        var compact = audit.Replace(" ", string.Empty, StringComparison.Ordinal);
        Assert.Contains("{\"status\":\"sent\"}", compact, StringComparison.Ordinal);
        Assert.Contains("{\"status\":\"approved\"}", compact, StringComparison.Ordinal);
        Assert.Contains("\"versionNo\":1", compact, StringComparison.Ordinal);
        Assert.Contains("\"total\":324.75", compact, StringComparison.Ordinal);
        Assert.Contains("\"currency\":\"USD\"", compact, StringComparison.Ordinal);
        Assert.Contains($"\"selectedOptionalLineIds\":[\"{optional[0]}\"]", compact, StringComparison.Ordinal);
        Assert.Contains("\"termsAccepted\":true", compact, StringComparison.Ordinal);
        Assert.Contains("\"userAgent\":\"FieldOpsTest/1.0(CustomerPhone)\"", compact, StringComparison.Ordinal);
        Assert.EndsWith("no-actor203.0.113.7true", compact, StringComparison.Ordinal);

        // AC-11: nothing else is created.
        Assert.Equal(downstream, await NonResponseRowsAsync(world.Org));

        // AC-14: the same action returns the original with a different selection and writes nothing; others are 409.
        var repeat = await QuoteLinkApi.PostAsync(host, "approve", Approve(sent.Token, optional[1]));
        Assert.Equal(HttpStatusCode.OK, repeat.StatusCode);
        var repeated = await RequestsHost.ReadAsync(repeat);
        Assert.Equal(view.ToJsonString(), repeated.ToJsonString());
        await AssertAlreadyAnsweredAsync(await QuoteLinkApi.PostAsync(host, "decline", QuoteLinkApi.Body(sent.Token, ("reason", "SECRET REASON"))));
        await AssertAlreadyAnsweredAsync(await QuoteLinkApi.PostAsync(host, "clarification", QuoteLinkApi.Body(sent.Token, ("message", "SECRET QUESTION"))));
        Assert.Equal(1, await database.ResponseCountAsync(sent.QuoteId));
        Assert.Equal(1, await database.ScalarAsync<long>("SELECT COUNT(*) FROM quote_response_optional_lines WHERE organization_id = @o", ("o", world.Org)));
        Assert.Equal(approvedAt, await database.QuoteUpdatedAtAsync(sent.QuoteId));
        Assert.Equal(1, await database.QuoteAuditCountAsync(sent.QuoteId, "quote.approved"));
        Assert.Equal(1, await database.ScalarAsync<long>(
            "SELECT COUNT(*) FROM audit_logs WHERE entity_id = @q AND action IN ('quote.approved', 'quote.rejected', 'quote.clarification_requested', 'quote.expired')",
            ("q", sent.QuoteId)));

        // The final state stays readable; the link text, hash and personal data never reach logs or audit rows (AC-19).
        Assert.Equal("approved", (await QuoteLinkApi.ViewAsync(host, sent.Token))["quote"]!["status"]!.GetValue<string>());
        var logs = new CapturingLogs(host.Logs).AllText();
        Assert.Contains(sent.QuoteId.ToString(), logs, StringComparison.Ordinal);
        Assert.Contains("approved", logs, StringComparison.Ordinal);

        foreach (var secret in new[] { sent.Token, sent.Hash, "Pat Contact", "carla@example.com", "SECRET", UserAgent, "203.0.113.7" })
        {
            Assert.DoesNotContain(secret, logs, StringComparison.Ordinal);
        }

        var auditText = await database.QuoteAuditTextAsync(sent.QuoteId, sent.RequestId);

        foreach (var secret in new[] { sent.Token, sent.Hash, "Pat Contact", "carla@example.com", "SECRET" })
        {
            Assert.DoesNotContain(secret, auditText, StringComparison.Ordinal);
        }
    }

    // AC-12, AC-14 (decline), AC-19: a required reason, a new concurrency value for staff and idempotent repeats.
    [Fact]
    public async Task Decline_StoresTheReasonOnlyInTheResponse_AndInvalidatesStaffWritesAndRepeats()
    {
        var world = await database.SeedQuoteWorldAsync();
        await using var host = RequestsHost.Create(database);
        var (owner, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);

        // A request without a linked customer answers as its guest (BR-13).
        var sent = await QuoteLinkApi.SendAsync(database, host, world, owner, linkCustomer: false);
        var before = await QuotesApi.GetAsync(host, owner, sent.QuoteId);
        var updatedAt = await database.QuoteUpdatedAtAsync(sent.QuoteId);

        foreach (var (reason, message) in new[]
        {
            ("   ", "Tell us why you're declining."),
            (string.Empty, "Tell us why you're declining."),
            (new string('r', 1001), "Reason must be 1,000 characters or fewer."),
        })
        {
            var invalid = await QuoteLinkApi.PostAsync(host, "decline", QuoteLinkApi.Body(sent.Token, ("reason", reason)));
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.Equal(message, QuotesApi.Error(await RequestsHost.ReadAsync(invalid), "reason"));
        }

        Assert.Equal(0, await database.ResponseCountAsync(sent.QuoteId));
        Assert.Equal(updatedAt, await database.QuoteUpdatedAtAsync(sent.QuoteId));

        var declined = await QuoteLinkApi.PostAsync(
            host, "decline", QuoteLinkApi.Body(sent.Token, ("reason", "  SECRET REASON too expensive  ")), "203.0.113.8", UserAgent);
        Assert.Equal(HttpStatusCode.OK, declined.StatusCode);
        var view = await RequestsHost.ReadAsync(declined);
        Assert.Equal("rejected", view["quote"]!["status"]!.GetValue<string>());
        Assert.Equal("rejected", view["response"]!["type"]!.GetValue<string>());
        Assert.Null(view["response"]!["totals"]);
        Assert.Empty(view["response"]!["selectedOptionalLineIds"]!.AsArray());
        Assert.DoesNotContain("SECRET", view.ToJsonString(), StringComparison.Ordinal);

        Assert.Equal(
            "rejected|Guest Person||SECRET REASON too expensive|true",
            await database.ScalarAsync<string>(
                "SELECT response::text || '|' || responder_name || '|' || COALESCE(responder_contact_id::text, '') || '|' || comment || '|' || (subtotal IS NULL AND discount_total IS NULL AND tax_total IS NULL AND total IS NULL)::text FROM quote_responses WHERE quote_version_id = @v",
                ("v", sent.VersionId)));
        Assert.Equal("rejected", await database.QuoteStatusAsync(sent.QuoteId));
        Assert.True(await database.ScalarAsync<bool>("SELECT approved_version_id IS NULL FROM quotes WHERE id = @q", ("q", sent.QuoteId)));
        var rejectedAt = await database.QuoteUpdatedAtAsync(sent.QuoteId);
        Assert.True(rejectedAt > updatedAt);
        Assert.Equal("quoted", await database.StatusOfAsync(sent.RequestId));

        Assert.Equal(1, await database.QuoteAuditCountAsync(sent.QuoteId, "quote.rejected"));
        var (_, _, metadata) = await database.GetLatestAuditAsync(world.Org, "quote.rejected");
        Assert.Equal(1, JsonNode.Parse(metadata!)!["versionNo"]!.GetValue<int>());
        var auditText = await database.QuoteAuditTextAsync(sent.QuoteId, sent.RequestId);

        foreach (var secret in new[] { "SECRET", "Guest Person", sent.Token, sent.Hash })
        {
            Assert.DoesNotContain(secret, auditText, StringComparison.Ordinal);
        }

        // The staff write that read the quote before the decline is stale.
        var stale = await host.SendAsync(HttpMethod.Post, $"/quotes/{sent.QuoteId}/revise", owner, QuotesApi.UpdatedAt(before));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("quote_changed", QuotesApi.Code(await RequestsHost.ReadAsync(stale)));

        // Repeats: the same action returns the original, a different action is 409; nothing changes.
        var repeat = await QuoteLinkApi.PostAsync(host, "decline", QuoteLinkApi.Body(sent.Token, ("reason", "Another reason")));
        Assert.Equal(HttpStatusCode.OK, repeat.StatusCode);
        Assert.Equal(view.ToJsonString(), (await RequestsHost.ReadAsync(repeat)).ToJsonString());
        await AssertAlreadyAnsweredAsync(await QuoteLinkApi.PostAsync(host, "approve", Approve(sent.Token)));
        await AssertAlreadyAnsweredAsync(await QuoteLinkApi.PostAsync(host, "clarification", QuoteLinkApi.Body(sent.Token, ("message", "A question"))));
        Assert.Equal(1, await database.ResponseCountAsync(sent.QuoteId));
        Assert.Equal("SECRET REASON too expensive", await database.ScalarAsync<string>("SELECT comment FROM quote_responses WHERE quote_version_id = @v", ("v", sent.VersionId)));
        Assert.Equal(rejectedAt, await database.QuoteUpdatedAtAsync(sent.QuoteId));

        var logs = new CapturingLogs(host.Logs).AllText();

        foreach (var secret in new[] { sent.Token, sent.Hash, "SECRET", "Guest Person", "Another reason", UserAgent })
        {
            Assert.DoesNotContain(secret, logs, StringComparison.Ordinal);
        }
    }

    // AC-13: one question per version, repeats store nothing, and approval stays available.
    [Fact]
    public async Task Clarification_StoresOneQuestionPerVersion_AndApprovalStaysAvailable()
    {
        var world = await database.SeedQuoteWorldAsync();
        await using var host = RequestsHost.Create(database);
        var (owner, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var sent = await QuoteLinkApi.SendAsync(database, host, world, owner);
        var updatedAt = await database.QuoteUpdatedAtAsync(sent.QuoteId);

        foreach (var (message, error) in new[]
        {
            ("  ", "Enter your question."),
            (new string('q', 1001), "Question must be 1,000 characters or fewer."),
        })
        {
            var invalid = await QuoteLinkApi.PostAsync(host, "clarification", QuoteLinkApi.Body(sent.Token, ("message", message)));
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.Equal(error, QuotesApi.Error(await RequestsHost.ReadAsync(invalid), "message"));
        }

        Assert.Equal(0, await database.ResponseCountAsync(sent.QuoteId));

        var first = await QuoteLinkApi.PostAsync(host, "clarification", QuoteLinkApi.Body(sent.Token, ("message", "SECRET QUESTION about parking")));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var view = await RequestsHost.ReadAsync(first);
        Assert.Equal("clarification_requested", view["quote"]!["status"]!.GetValue<string>());
        Assert.Equal(QuotesApi.Today(), view["clarification"]!["askedOn"]!.GetValue<string>());
        Assert.Null(view["response"]);
        Assert.DoesNotContain("SECRET", view.ToJsonString(), StringComparison.Ordinal);
        Assert.Equal("clarification_requested", await database.QuoteStatusAsync(sent.QuoteId));
        var askedAt = await database.QuoteUpdatedAtAsync(sent.QuoteId);
        Assert.True(askedAt > updatedAt);
        Assert.Equal(1, await database.QuoteAuditCountAsync(sent.QuoteId, "quote.clarification_requested"));
        Assert.DoesNotContain("SECRET", await database.QuoteAuditTextAsync(sent.QuoteId, sent.RequestId), StringComparison.Ordinal);

        // A later question returns the existing clarification and stores nothing.
        var second = await QuoteLinkApi.PostAsync(host, "clarification", QuoteLinkApi.Body(sent.Token, ("message", "Another question")));
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(view.ToJsonString(), (await RequestsHost.ReadAsync(second)).ToJsonString());
        Assert.Equal(1, await database.ResponseCountAsync(sent.QuoteId, "clarification_requested"));
        Assert.Equal("SECRET QUESTION about parking", await database.ScalarAsync<string>("SELECT comment FROM quote_responses WHERE quote_version_id = @v", ("v", sent.VersionId)));
        Assert.Equal(askedAt, await database.QuoteUpdatedAtAsync(sent.QuoteId));
        Assert.Equal(1, await database.QuoteAuditCountAsync(sent.QuoteId, "quote.clarification_requested"));

        // Approving from clarification_requested works; afterwards a question is 409 and both responses are kept.
        var approved = await QuoteLinkApi.PostAsync(host, "approve", Approve(sent.Token));
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        var final = await RequestsHost.ReadAsync(approved);
        Assert.Equal("approved", final["quote"]!["status"]!.GetValue<string>());
        Assert.NotNull(final["clarification"]);
        await AssertAlreadyAnsweredAsync(await QuoteLinkApi.PostAsync(host, "clarification", QuoteLinkApi.Body(sent.Token, ("message", "Too late"))));
        Assert.Equal(2, await database.ResponseCountAsync(sent.QuoteId));
        Assert.Equal(1, await database.QuoteAuditCountAsync(sent.QuoteId, "quote.approved"));
    }

    // AC-15: identical pairs store one response and both succeed; mixed pairs have exactly one winner.
    [Fact]
    public async Task ConcurrentResponses_StoreOneFinalResponseAndMatchTheQuoteStatus()
    {
        var world = await database.SeedQuoteWorldAsync();
        await using var host = RequestsHost.Create(database);
        var (owner, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);

        var twoApprovals = await QuoteLinkApi.SendAsync(database, host, world, owner);
        var optional = QuoteLinkApi.OptionalIds(await QuoteLinkApi.ViewAsync(host, twoApprovals.Token));
        var approvals = await Task.WhenAll(
            QuoteLinkApi.PostAsync(host, "approve", Approve(twoApprovals.Token, optional[0])),
            QuoteLinkApi.PostAsync(host, "approve", Approve(twoApprovals.Token, optional[0])));
        Assert.All(approvals, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        Assert.Equal(1, await database.ResponseCountAsync(twoApprovals.QuoteId, "approved"));
        Assert.Equal(1, await database.QuoteAuditCountAsync(twoApprovals.QuoteId, "quote.approved"));
        Assert.Equal("approved", await database.QuoteStatusAsync(twoApprovals.QuoteId));

        var twoDeclines = await QuoteLinkApi.SendAsync(database, host, world, owner);
        var declines = await Task.WhenAll(
            QuoteLinkApi.PostAsync(host, "decline", QuoteLinkApi.Body(twoDeclines.Token, ("reason", "No"))),
            QuoteLinkApi.PostAsync(host, "decline", QuoteLinkApi.Body(twoDeclines.Token, ("reason", "No"))));
        Assert.All(declines, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        Assert.Equal(1, await database.ResponseCountAsync(twoDeclines.QuoteId, "rejected"));
        Assert.Equal(1, await database.QuoteAuditCountAsync(twoDeclines.QuoteId, "quote.rejected"));

        for (var round = 0; round < 3; round++)
        {
            var mixed = await QuoteLinkApi.SendAsync(database, host, world, owner);
            var results = await Task.WhenAll(
                QuoteLinkApi.PostAsync(host, "approve", Approve(mixed.Token)),
                QuoteLinkApi.PostAsync(host, "decline", QuoteLinkApi.Body(mixed.Token, ("reason", "No"))));

            Assert.Equal(1, results.Count(response => response.StatusCode == HttpStatusCode.OK));
            await AssertAlreadyAnsweredAsync(results.Single(response => response.StatusCode != HttpStatusCode.OK));
            Assert.Equal(1, await database.ScalarAsync<long>(
                "SELECT COUNT(*) FROM quote_responses r JOIN quote_versions v ON v.id = r.quote_version_id WHERE v.quote_id = @q AND r.response IN ('approved', 'rejected')",
                ("q", mixed.QuoteId)));
            var stored = await database.ScalarAsync<string>(
                "SELECT r.response::text FROM quote_responses r JOIN quote_versions v ON v.id = r.quote_version_id WHERE v.quote_id = @q", ("q", mixed.QuoteId));
            Assert.Equal(stored, await database.QuoteStatusAsync(mixed.QuoteId));
        }
    }

    // AC-21: staff read the responses of the selected version, newest first, and a Viewer sees them too.
    [Fact]
    public async Task QuoteDetail_ShowsResponsesNewestFirstToReadRolesOnly()
    {
        var world = await database.SeedQuoteWorldAsync();
        var foreign = await database.SeedQuoteWorldAsync();
        await using var host = RequestsHost.Create(database);
        var (owner, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var (viewer, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.ViewerRoleId);
        var (otherBranch, _) = await host.SignInAsync(
            database, world.Org, CompanySettingsDatabaseFixture.DispatcherRoleId, "Bea", "Bravo", world.BranchB);
        var (foreignOwner, _) = await host.SignInAsync(database, foreign.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var sent = await QuoteLinkApi.SendAsync(database, host, world, owner);
        var optional = QuoteLinkApi.OptionalIds(await QuoteLinkApi.ViewAsync(host, sent.Token));

        Assert.Empty((await QuotesApi.GetAsync(host, viewer, sent.QuoteId))["responses"]!.AsArray());
        Assert.Equal("sent", (await QuotesApi.GetAsync(host, viewer, sent.QuoteId))["displayStatus"]!.GetValue<string>());

        await QuoteLinkApi.PostAsync(host, "clarification", QuoteLinkApi.Body(sent.Token, ("message", "Is parking included?")));
        await QuoteLinkApi.PostAsync(host, "approve", Approve(sent.Token, optional[0]));

        foreach (var cookie in new[] { owner, viewer })
        {
            var detail = await QuotesApi.GetAsync(host, cookie, sent.QuoteId);
            var responses = detail["responses"]!.AsArray();
            Assert.Equal(["approved", "clarification_requested"], responses.Select(item => item!["type"]!.GetValue<string>()).ToArray());
            Assert.Equal("approved", detail["displayStatus"]!.GetValue<string>());

            var approval = responses[0]!;
            Assert.Equal((1, "Pat Contact"), (approval["versionNo"]!.GetValue<int>(), approval["responderName"]!.GetValue<string>()));
            Assert.Null(approval["comment"]);
            Assert.Equal([QuoteLinkApi.OptionalTaxable], approval["selectedOptionalLines"]!.AsArray().Select(line => line!["name"]!.GetValue<string>()).ToArray());
            Assert.Equal(120m, approval["selectedOptionalLines"]![0]!["lineSubtotal"]!.GetValue<decimal>());
            Assert.Equal(324.75m, approval["totals"]!["total"]!.GetValue<decimal>());
            Assert.Equal("Is parking included?", responses[1]!["comment"]!.GetValue<string>());
            Assert.Null(responses[1]!["totals"]);
            Assert.Empty(responses[1]!["selectedOptionalLines"]!.AsArray());
            Assert.False(string.IsNullOrEmpty(approval["respondedAt"]!.GetValue<string>()));
        }

        // A dispatcher limited to another branch and an owner of another organization get the plain 404.
        foreach (var cookie in new[] { otherBranch, foreignOwner })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Get, $"/quotes/{sent.QuoteId}", cookie)).StatusCode);
        }
    }
}
