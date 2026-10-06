using System.Net;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.ServiceRequests;

namespace FieldOps.IntegrationTests.Quotes;

/// <summary>Quote creation (quote-builder AC-03, AC-04, AC-05): numbering, idempotency, status guards and concurrent creates.</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class QuoteCreationTests(CompanySettingsDatabaseFixture database)
{
    private Task<long> NextNumberAsync(Guid org) =>
        database.ScalarAsync<long>("SELECT next_quote_number FROM organizations WHERE id = @o", ("o", org));

    private Task<long> QuoteCountAsync(Guid request) =>
        database.ScalarAsync<long>("SELECT COUNT(*) FROM quotes WHERE request_id = @r", ("r", request));

    [Fact]
    public async Task Create_NumbersIdempotentlyGuardsRequestStatusAndSerializesConcurrentCreates()
    {
        var world = await database.SeedQuoteWorldAsync();
        await using var host = RequestsHost.Create(database);
        var (owner, member) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);

        // AC-03: a ready request with a completed assessment.
        var request = await database.SeedRequestAsync(world, status: "ready_for_quote", branch: world.BranchA);
        var assessment = await database.SeedAssessmentAsync(
            world.Org, request.Id, DateTimeOffset.UtcNow.AddHours(-3), DateTimeOffset.UtcNow.AddHours(-2), member.UserId, status: "completed");
        await database.ExecuteAsync(
            "UPDATE assessments SET diagnosis = 'Worn valve', recommended_scope = 'Replace valve', completed_at = now() WHERE id = @a", ("a", assessment));

        var quote = await QuotesApi.CreateAsync(host, owner, request.Id);
        var quoteId = quote["id"]!.GetValue<Guid>();

        Assert.Equal("Q-2036", quote["displayNumber"]!.GetValue<string>());
        Assert.Equal(2036, quote["number"]!.GetValue<long>());
        Assert.Equal("draft", quote["status"]!.GetValue<string>());
        Assert.True(quote["canManage"]!.GetValue<bool>());
        Assert.Equal("Drain cleaning", quote["request"]!["title"]!.GetValue<string>());
        Assert.StartsWith("REQ-", quote["request"]!["displayNumber"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal("Carla Customer", quote["customer"]!["name"]!.GetValue<string>());
        Assert.Equal(QuotesApi.RecipientEmail, quote["recipient"]!["email"]!.GetValue<string>());
        Assert.Equal("Worn valve", quote["completedAssessment"]!["diagnosis"]!.GetValue<string>());
        Assert.Equal(8.25m, quote["organization"]!["defaultTaxRate"]!.GetValue<decimal>());
        var draft = quote["draft"]!;
        Assert.Equal(1, draft["versionNo"]!.GetValue<int>());
        Assert.Empty(draft["lines"]!.AsArray());
        Assert.Equal("due_on_completion", draft["terms"]!["preset"]!.GetValue<string>());
        Assert.Equal(QuotesApi.Today(30), draft["validUntil"]!.GetValue<string>());
        Assert.Equal(0m, draft["calculation"]!["total"]!.GetValue<decimal>());
        Assert.Null(draft["calculation"]!["margin"]!["percent"]);
        Assert.Empty(quote["sentVersions"]!.AsArray());

        Assert.Equal(0, await database.ScalarAsync<int>("SELECT current_version_no FROM quotes WHERE id = @q", ("q", quoteId)));
        Assert.Equal(world.BranchA, await database.ScalarAsync<Guid>("SELECT branch_id FROM quotes WHERE id = @q", ("q", quoteId)));
        Assert.Equal(world.Customer, await database.ScalarAsync<Guid>("SELECT customer_id FROM quotes WHERE id = @q", ("q", quoteId)));
        Assert.Equal(world.Property, await database.ScalarAsync<Guid>("SELECT property_id FROM quotes WHERE id = @q", ("q", quoteId)));
        Assert.Equal(member.UserId, await database.ScalarAsync<Guid>("SELECT created_by_user_id FROM quotes WHERE id = @q", ("q", quoteId)));
        Assert.Equal(
            "Drain cleaning|Payment due upon completion.|0.00|false",
            await database.ScalarAsync<string>(
                "SELECT scope || '|' || terms || '|' || discount_total::text || '|' || is_immutable::text FROM quote_versions WHERE quote_id = @q", ("q", quoteId)));
        Assert.Equal(2037, await NextNumberAsync(world.Org));
        Assert.Equal(1, await database.QuoteAuditCountAsync(quoteId, "quote.created"));

        var panel = await RequestsHost.ReadAsync(await host.SendAsync(HttpMethod.Get, $"/service-requests/{request.Id}", owner));
        Assert.Equal(quoteId, panel["quote"]!["id"]!.GetValue<Guid>());
        Assert.Equal("draft", panel["quote"]!["status"]!.GetValue<string>());
        Assert.True(panel["quote"]!["hasDraft"]!.GetValue<bool>());

        var repeated = await host.SendAsync(HttpMethod.Post, "/quotes", owner, new JsonObject { ["requestId"] = request.Id });
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        Assert.Equal(quoteId, (await RequestsHost.ReadAsync(repeated))["id"]!.GetValue<Guid>());
        Assert.Equal(2037, await NextNumberAsync(world.Org));
        Assert.Equal(1, await QuoteCountAsync(request.Id));
        Assert.Equal(1, await database.QuoteAuditCountAsync(quoteId, "quote.created"));

        // AC-04: only ready_for_quote requests take a quote; nothing is created or consumed otherwise.
        foreach (var status in new[] { "new", "needs_review", "assessment_scheduled", "quoted", "cancelled" })
        {
            var other = await database.SeedRequestAsync(world, status: status, branch: world.BranchA);
            var conflict = await host.SendAsync(HttpMethod.Post, "/quotes", owner, new JsonObject { ["requestId"] = other.Id });

            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
            Assert.Equal("request_changed", QuotesApi.Code(await RequestsHost.ReadAsync(conflict)));
            Assert.Equal(0, await QuoteCountAsync(other.Id));
        }

        Assert.Equal(2037, await NextNumberAsync(world.Org));
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await host.SendAsync(HttpMethod.Post, "/quotes", owner, new JsonObject { ["requestId"] = null })).StatusCode);

        var direct = await database.SeedReadyRequestAsync(world);
        var withoutAssessment = await QuotesApi.CreateAsync(host, owner, direct);
        Assert.Equal("Q-2037", withoutAssessment["displayNumber"]!.GetValue<string>());
        Assert.Null(withoutAssessment["completedAssessment"]);

        // AC-05: concurrent creates for one request yield one quote and one number; for two requests, consecutive numbers.
        var contested = await database.SeedReadyRequestAsync(world);
        var pair = await Task.WhenAll(
            host.SendAsync(HttpMethod.Post, "/quotes", owner, new JsonObject { ["requestId"] = contested }),
            host.SendAsync(HttpMethod.Post, "/quotes", owner, new JsonObject { ["requestId"] = contested }));
        Assert.All(pair, response => Assert.True(response.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK));
        Assert.Equal(1, pair.Count(response => response.StatusCode == HttpStatusCode.Created));
        var ids = (await Task.WhenAll(pair.Select(RequestsHost.ReadAsync))).Select(body => body["id"]!.GetValue<Guid>()).Distinct().ToList();
        Assert.Single(ids);
        Assert.Equal(1, await QuoteCountAsync(contested));
        Assert.Equal(2039, await NextNumberAsync(world.Org));

        var first = await database.SeedReadyRequestAsync(world);
        var second = await database.SeedReadyRequestAsync(world);
        var distinct = await Task.WhenAll(
            host.SendAsync(HttpMethod.Post, "/quotes", owner, new JsonObject { ["requestId"] = first }),
            host.SendAsync(HttpMethod.Post, "/quotes", owner, new JsonObject { ["requestId"] = second }));
        Assert.All(distinct, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
        var numbers = (await Task.WhenAll(distinct.Select(RequestsHost.ReadAsync))).Select(body => body["number"]!.GetValue<long>()).Order().ToArray();
        Assert.Equal([2039, 2040], numbers);
        Assert.Equal(2041, await NextNumberAsync(world.Org));
    }
}
