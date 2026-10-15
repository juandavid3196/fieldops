using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.Customers;
using FieldOps.IntegrationTests.Dispatch;
using FieldOps.IntegrationTests.ServiceRequests;

namespace FieldOps.IntegrationTests.Quotes;

/// <summary>The quotes workspace reads: list, summary and options (quotes-management AC-03 to AC-14).</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class QuotesWorkspaceTests(CompanySettingsDatabaseFixture database)
{
    private const short Owner = CompanySettingsDatabaseFixture.OwnerRoleId;
    private const short Operations = CompanySettingsDatabaseFixture.OperationsManagerRoleId;
    private const short Accounting = CompanySettingsDatabaseFixture.AccountingRoleId;
    private const short Viewer = CompanySettingsDatabaseFixture.ViewerRoleId;

    internal static async Task<JsonNode> GetAsync(
        RequestsHost host, string cookie, string path, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var response = await host.SendAsync(HttpMethod.Get, path, cookie);

        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());

        return await DispatchSeed.ReadAsync(response, expected);
    }

    internal static string[] Actions(JsonNode row) =>
        [.. row["actions"]!.AsArray().Select(action => action!.GetValue<string>())];

    internal static JsonNode RowOf(JsonNode page, Guid id) =>
        page["items"]!.AsArray().Single(item => item!["id"]!.GetValue<Guid>() == id)!;

    internal static int[] Counts(JsonNode page) =>
        [.. new[] { "all", "drafts", "sent", "approved", "closed" }.Select(key => page["counts"]![key]!.GetValue<int>())];

    private static Guid[] Ids(JsonNode page) => [.. page["items"]!.AsArray().Select(item => item!["id"]!.GetValue<Guid>())];

    private static long NumberOf(JsonNode row) =>
        long.Parse(row["displayNumber"]!.GetValue<string>().Split('-')[1], CultureInfo.InvariantCulture);

    private static DateOnly LocalToday(string zoneId) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(zoneId)).DateTime);

    /// <summary>The sort contract of BR-09: the key order, then the quote number descending for equal keys.</summary>
    private static void AssertOrder(JsonNode page, Func<JsonNode, string> key, bool descending)
    {
        var items = page["items"]!.AsArray().Select(item => item!).ToList();

        for (var index = 1; index < items.Count; index++)
        {
            var order = string.CompareOrdinal(key(items[index - 1]), key(items[index]));

            if (descending)
            {
                order = -order;
            }

            Assert.True(
                order < 0 || (order == 0 && NumberOf(items[index - 1]) > NumberOf(items[index])),
                $"Row {index} breaks the order.");
        }
    }

    // AC-03, AC-04, AC-05: display status (incl. end-of-day expiry in the organization zone), shown version, revision state,
    // tab membership, counts that ignore the status filter, and the out-of-tab 400.
    [Fact]
    public async Task List_EveryStoredStatus_DerivesDisplayStatusShownVersionTabsAndCounts()
    {
        const string zone = "America/New_York";
        var world = await database.SeedWorkspaceWorldAsync(zone);
        await using var host = RequestsHost.Create(database);
        var (owner, member) = await host.SignInAsync(database, world.Org, Owner);
        var user = member.UserId;
        var today = LocalToday(zone);

        var draft = await database.SeedWorkspaceQuoteAsync(world, user, "draft", currentVersion: 0, total: 80m);
        var awaiting = await database.SeedWorkspaceQuoteAsync(world, user, "sent", validUntil: today.AddDays(10));
        var lastDay = await database.SeedWorkspaceQuoteAsync(world, user, "sent", validUntil: today);
        var late = await database.SeedWorkspaceQuoteAsync(world, user, "sent", validUntil: today.AddDays(-1));
        var question = await database.SeedWorkspaceQuoteAsync(world, user, "clarification_requested", validUntil: today.AddDays(5));
        var questionLate = await database.SeedWorkspaceQuoteAsync(world, user, "clarification_requested", validUntil: today.AddDays(-3));
        var approved = await database.SeedWorkspaceQuoteAsync(world, user, "approved", response: "approved", validUntil: today.AddDays(-9));
        var rejected = await database.SeedWorkspaceQuoteAsync(world, user, "rejected", response: "rejected", validUntil: today.AddDays(4));
        var storedExpired = await database.SeedWorkspaceQuoteAsync(world, user, "expired", validUntil: today.AddDays(-30));
        var cancelled = await database.SeedWorkspaceQuoteAsync(world, user, "cancelled", currentVersion: 0);
        var revised = await database.SeedWorkspaceQuoteAsync(
            world, user, "sent", currentVersion: 2, revision: true, validUntil: today.AddDays(20), total: 150m);

        var all = await GetAsync(host, owner, "/quotes?pageSize=100");

        Assert.Equal(11, all["total"]!.GetValue<int>());
        Assert.Equal([11, 1, 4, 1, 5], Counts(all));
        Assert.Equal(
            new Dictionary<Guid, string>
            {
                [draft.Id] = "draft",
                [awaiting.Id] = "awaiting_response",
                [lastDay.Id] = "awaiting_response",
                [late.Id] = "expired",
                [question.Id] = "clarification_requested",
                [questionLate.Id] = "expired",
                [approved.Id] = "approved",
                [rejected.Id] = "rejected",
                [storedExpired.Id] = "expired",
                [cancelled.Id] = "cancelled",
                [revised.Id] = "awaiting_response",
            },
            all["items"]!.AsArray().ToDictionary(item => item!["id"]!.GetValue<Guid>(), item => item!["status"]!.GetValue<string>()));

        // BR-04: the shown version is the last sent one; a revision draft only flags revisionInProgress.
        var revisedRow = RowOf(all, revised.Id);
        Assert.Equal((2, true, 150m), (revisedRow["revisionNo"]!.GetValue<int>(), revisedRow["revisionInProgress"]!.GetValue<bool>(), revisedRow["total"]!.GetValue<decimal>()));
        Assert.Equal(today.AddDays(20).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), revisedRow["validUntil"]!.GetValue<string>());
        var draftRow = RowOf(all, draft.Id);
        Assert.Equal((1, false, 80m), (draftRow["revisionNo"]!.GetValue<int>(), draftRow["revisionInProgress"]!.GetValue<bool>(), draftRow["total"]!.GetValue<decimal>()));
        Assert.Null(draftRow["sentAt"]);

        // BR-05: tab membership; expired by date lives in Closed.
        Assert.Equal(
            new[] { late.Id, questionLate.Id, rejected.Id, storedExpired.Id, cancelled.Id }.Order(),
            Ids(await GetAsync(host, owner, "/quotes?tab=closed")).Order());
        Assert.Equal(
            new[] { awaiting.Id, lastDay.Id, question.Id, revised.Id }.Order(),
            Ids(await GetAsync(host, owner, "/quotes?tab=sent")).Order());
        Assert.Equal([draft.Id], Ids(await GetAsync(host, owner, "/quotes?tab=drafts")));
        Assert.Equal([approved.Id], Ids(await GetAsync(host, owner, "/quotes?tab=approved")));

        // BR-06: the status filter narrows the page, never the counts; an out-of-tab code is a 400.
        var byStatus = await GetAsync(host, owner, "/quotes?tab=closed&status=expired");
        Assert.Equal(3, byStatus["total"]!.GetValue<int>());
        Assert.Equal([11, 1, 4, 1, 5], Counts(byStatus));
        Assert.Equal([question.Id], Ids(await GetAsync(host, owner, "/quotes?tab=sent&status=clarification_requested")));
        Assert.Equal(1, (await GetAsync(host, owner, "/quotes?status=cancelled"))["total"]!.GetValue<int>());

        foreach (var path in new[] { "/quotes?tab=drafts&status=approved", "/quotes?tab=sent&status=expired", "/quotes?status=sent" })
        {
            var problem = await GetAsync(host, owner, path, HttpStatusCode.BadRequest);
            Assert.NotNull(problem["errors"]!["status"]);
        }
    }

    // AC-06 to AC-09: date windows, search, sorts, pagination and every validation error.
    [Fact]
    public async Task List_DateSearchSortAndPagination_FollowTheContract()
    {
        var world = await database.SeedWorkspaceWorldAsync();
        await using var host = RequestsHost.Create(database);
        var (owner, member) = await host.SignInAsync(database, world.Org, Owner);
        var user = member.UserId;
        var midnight = DateTimeOffset.UtcNow.Date;

        for (var index = 0; index < 4; index++)
        {
            await database.SeedRequestAsync(world, status: "new");
        }

        DateTimeOffset At(int daysAgo) => new DateTimeOffset(midnight, TimeSpan.Zero).AddDays(-daysAgo).AddHours(12);

        var created = new List<WorkspaceQuote>();
        var ageDays = new[] { 0, 3, 6, 7, 29, 30, 89, 90 };

        for (var index = 0; index < ageDays.Length; index++)
        {
            created.Add(await database.SeedWorkspaceQuoteAsync(
                world,
                user,
                "sent",
                total: 100m * (index + 1) % 450m,
                validUntil: DateOnly.FromDateTime(midnight).AddDays(40 - (index * 3)),
                createdAt: At(ageDays[index]),
                updatedAt: At(ageDays[index] + 100 - (index * 7))));
        }

        // Unlinked, no service: matched only by the guest name and the category.
        var guest = await database.SeedWorkspaceQuoteAsync(
            world, user, "sent", linkCustomer: false, guestName: "Zelda Guestwood", withService: false, validUntil: null, createdAt: At(0));
        await database.SeedWorkspaceQuoteAsync(world, user, "draft", currentVersion: 0, createdAt: At(0));

        for (var index = 0; index < 13; index++)
        {
            await database.SeedWorkspaceQuoteAsync(
                world, user, "sent", total: 50m, validUntil: DateOnly.FromDateTime(midnight).AddDays(30), createdAt: At(0));
        }

        var all = await GetAsync(host, owner, "/quotes?pageSize=100");
        Assert.Equal(23, all["total"]!.GetValue<int>());

        // BR-07: a window of N days covers today and the previous N - 1 local days; counts follow the window.
        foreach (var (date, expected) in new[] { ("any", 23), ("today", 16), ("7d", 18), ("30d", 20), ("90d", 22) })
        {
            var page = await GetAsync(host, owner, $"/quotes?date={date}&pageSize=100");
            Assert.Equal(expected, page["total"]!.GetValue<int>());
            Assert.Equal(expected, page["counts"]!["all"]!.GetValue<int>());
        }

        // BR-08: number forms, request number, guest name, property city, service and category, case-insensitive.
        var target = created[2];
        var number = target.Number.ToString(CultureInfo.InvariantCulture);
        var requestNumber = target.Number + 4;

        foreach (var term in new[] { $"Q-{number}", $"%23q-{number}", number, $"REQ-{requestNumber}" })
        {
            var found = await GetAsync(host, owner, $"/quotes?q={term}");
            Assert.Equal([target.Id], Ids(found));
            Assert.Equal(1, found["counts"]!["all"]!.GetValue<int>());
        }

        Assert.Equal([guest.Id], Ids(await GetAsync(host, owner, "/quotes?q=GUESTWOOD")));
        Assert.Equal(22, (await GetAsync(host, owner, "/quotes?q=austin&pageSize=100"))["total"]!.GetValue<int>());
        Assert.Equal(22, (await GetAsync(host, owner, "/quotes?q=%20DRAIN%20&pageSize=100"))["total"]!.GetValue<int>());
        Assert.Equal(23, (await GetAsync(host, owner, "/quotes?q=plumb&pageSize=100"))["total"]!.GetValue<int>());
        Assert.Equal(0, (await GetAsync(host, owner, "/quotes?q=nomatchatall"))["total"]!.GetValue<int>());

        // BR-09: each order, the nulls of `expiring` last, ties by quote number descending.
        AssertOrder(await GetAsync(host, owner, "/quotes?sort=updated&pageSize=100"), row => row["updatedAt"]!.GetValue<DateTimeOffset>().ToString("o", CultureInfo.InvariantCulture), true);
        AssertOrder(await GetAsync(host, owner, "/quotes?sort=newest&pageSize=100"), row => row["createdAt"]!.GetValue<DateTimeOffset>().ToString("o", CultureInfo.InvariantCulture), true);
        AssertOrder(await GetAsync(host, owner, "/quotes?sort=oldest&pageSize=100"), row => row["createdAt"]!.GetValue<DateTimeOffset>().ToString("o", CultureInfo.InvariantCulture), false);
        AssertOrder(await GetAsync(host, owner, "/quotes?sort=total&pageSize=100"), row => row["total"]!.GetValue<decimal>().ToString("0000000.00", CultureInfo.InvariantCulture), true);

        var expiring = await GetAsync(host, owner, "/quotes?sort=expiring&pageSize=100");
        AssertOrder(expiring, row => row["validUntil"]?.GetValue<string>() ?? "9999-12-31", false);
        Assert.Equal(2, expiring["items"]!.AsArray().TakeLast(2).Count(item => item!["validUntil"] is null));

        // BR-10: pages, the page past the last one and the total.
        var pages = new List<Guid>();

        foreach (var (page, size, expected) in new[] { (1, 10, 10), (2, 10, 10), (3, 10, 3), (4, 10, 0), (1, 25, 23), (1, 50, 23) })
        {
            var response = await GetAsync(host, owner, $"/quotes?page={page}&pageSize={size}");
            Assert.Equal(23, response["total"]!.GetValue<int>());
            Assert.Equal(expected, response["items"]!.AsArray().Count);

            if (size == 10)
            {
                pages.AddRange(Ids(response));
            }
        }

        Assert.Equal(23, pages.Distinct().Count());
        Assert.Equal(23, (await GetAsync(host, owner, "/quotes"))["items"]!.AsArray().Count);

        // 400: every field of ProblemDetails errors.<field>, a 101-character term, malformed branch.
        var problem = await GetAsync(
            host,
            owner,
            $"/quotes?tab=nope&q={new string('x', 101)}&date=yesterday&sort=best&page=0&pageSize=101&branchId=not-a-guid",
            HttpStatusCode.BadRequest);
        Assert.Equal(
            ["branchId", "date", "page", "pageSize", "q", "sort", "tab"],
            [.. problem["errors"]!.AsObject().Select(entry => entry.Key).Order(StringComparer.Ordinal)]);
        Assert.NotNull((await GetAsync(host, owner, "/quotes?pageSize=0", HttpStatusCode.BadRequest))["errors"]!["pageSize"]);
        Assert.NotNull((await GetAsync(host, owner, "/quotes/summary?branchId=zzz", HttpStatusCode.BadRequest))["errors"]!["branchId"]);

        // A 100-character term after trimming is accepted.
        Assert.Equal(0, (await GetAsync(host, owner, $"/quotes?q=%20{new string('x', 100)}%20"))["total"]!.GetValue<int>());
    }

    // AC-10, AC-11: counts, amounts, currency exclusion, month and expiry windows, branch set and ready requests.
    [Fact]
    public async Task Summary_BranchSetAndAll_AppliesMetricRulesAndReadyRequests()
    {
        var world = await database.SeedWorkspaceWorldAsync();
        var foreign = await database.SeedWorkspaceWorldAsync();
        await using var host = RequestsHost.Create(database);
        var (owner, member) = await host.SignInAsync(database, world.Org, Owner);
        var user = member.UserId;
        var (_, foreignMember) = await host.SignInAsync(database, foreign.Org, Owner);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        await database.SeedWorkspaceQuoteAsync(world, user, "draft", currentVersion: 0, total: 100m);
        await database.SeedWorkspaceQuoteAsync(world, user, "draft", currentVersion: 0, total: 50m);
        await database.SeedWorkspaceQuoteAsync(world, user, "draft", currentVersion: 0, total: 25m, requestBranch: world.BranchB);

        await database.SeedWorkspaceQuoteAsync(world, user, "sent", total: 200m, validUntil: today.AddDays(30));
        await database.SeedWorkspaceQuoteAsync(world, user, "sent", total: 300m, validUntil: today.AddDays(7));
        await database.SeedWorkspaceQuoteAsync(world, user, "sent", total: 400m, validUntil: today.AddDays(8));
        await database.SeedWorkspaceQuoteAsync(world, user, "sent", total: 1000m, currency: "EUR", validUntil: today.AddDays(30));
        await database.SeedWorkspaceQuoteAsync(world, user, "sent", total: 10m, validUntil: today);
        await database.SeedWorkspaceQuoteAsync(world, user, "clarification_requested", total: 20m, validUntil: today.AddDays(20));
        await database.SeedWorkspaceQuoteAsync(world, user, "sent", total: 7777m, validUntil: today.AddDays(-1));
        await database.SeedWorkspaceQuoteAsync(world, user, "sent", total: 5m, validUntil: today.AddDays(2), requestBranch: world.BranchB);

        await database.SeedWorkspaceQuoteAsync(
            world, user, "approved", total: 400m, response: "approved", responseTotal: 450m, respondedAt: DateTimeOffset.UtcNow);
        await database.SeedWorkspaceQuoteAsync(
            world, user, "approved", total: 999m, response: "approved", respondedAt: DateTimeOffset.UtcNow.AddDays(-45));
        await database.SeedWorkspaceQuoteAsync(world, user, "rejected", response: "rejected", total: 3m, validUntil: today.AddDays(9));

        await database.SeedWorkspaceQuoteAsync(foreign, foreignMember.UserId, "draft", currentVersion: 0, total: 9000m);

        // BR-12: ready requests in and out of scope, one with a draft quote, one without a branch, other statuses.
        await database.SeedWorkspaceQuoteAsync(world, user, "draft", currentVersion: 0, total: 1m, requestStatus: "ready_for_quote");
        await database.SeedRequestAsync(world, status: "ready_for_quote", branch: world.BranchA);
        await database.SeedRequestAsync(world, status: "ready_for_quote", branch: world.BranchB);
        await database.SeedRequestAsync(world, status: "ready_for_quote", branch: null);
        await database.SeedRequestAsync(world, status: "new", branch: world.BranchA);
        await database.SeedRequestAsync(foreign, status: "ready_for_quote", branch: foreign.BranchA);

        var summaryAll = await GetAsync(host, owner, "/quotes/summary");
        Assert.Equal("USD", summaryAll["currency"]!.GetValue<string>());
        Assert.Equal((4, 176m), Metric(summaryAll["drafts"]!));
        Assert.Equal((7, 935m), Metric(summaryAll["awaitingResponse"]!));
        Assert.Equal((1, 450m), Metric(summaryAll["approvedThisMonth"]!));
        Assert.Equal(4, summaryAll["needsAttention"]!["count"]!.GetValue<int>());
        Assert.Equal(4, summaryAll["readyRequests"]!.GetValue<int>());

        var summaryA = await GetAsync(host, owner, $"/quotes/summary?branchId={world.BranchA}");
        Assert.Equal((3, 151m), Metric(summaryA["drafts"]!));
        Assert.Equal((6, 930m), Metric(summaryA["awaitingResponse"]!));
        Assert.Equal(3, summaryA["needsAttention"]!["count"]!.GetValue<int>());
        Assert.Equal(2, summaryA["readyRequests"]!.GetValue<int>());

        var summaryB = await GetAsync(host, owner, $"/quotes/summary?branchId={world.BranchB}");
        Assert.Equal((1, 25m), Metric(summaryB["drafts"]!));
        Assert.Equal((1, 5m), Metric(summaryB["awaitingResponse"]!));
        Assert.Equal((0, 0m), Metric(summaryB["approvedThisMonth"]!));
        Assert.Equal(1, summaryB["readyRequests"]!.GetValue<int>());

        // The tab, search, status and date of the list never change the metrics.
        Assert.Equal(summaryAll.ToJsonString(), (await GetAsync(host, owner, "/quotes/summary?tab=drafts&q=x&status=draft&date=today")).ToJsonString());
    }

    private static (int Count, decimal Amount) Metric(JsonNode metric) =>
        (metric["count"]!.GetValue<int>(), metric["amount"]!.GetValue<decimal>());

    // AC-12, AC-14: row content and the actions of every state for each role.
    [Fact]
    public async Task List_RowsOfEveryState_CarryContentAndRoleSpecificActions()
    {
        var world = await database.SeedWorkspaceWorldAsync();
        await using var host = RequestsHost.Create(database);
        var (owner, member) = await host.SignInAsync(database, world.Org, Owner);
        var (operations, _) = await host.SignInAsync(database, world.Org, Operations);
        var (accounting, _) = await host.SignInAsync(database, world.Org, Accounting);
        var (viewer, _) = await host.SignInAsync(database, world.Org, Viewer);
        var user = member.UserId;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var respondedAt = DateTimeOffset.UtcNow.AddDays(-2);

        var draft = await database.SeedWorkspaceQuoteAsync(world, user, "draft", currentVersion: 0, total: 120m);
        var sent = await database.SeedWorkspaceQuoteAsync(world, user, "sent", validUntil: today.AddDays(9), total: 230.5m);
        var revising = await database.SeedWorkspaceQuoteAsync(world, user, "sent", currentVersion: 3, revision: true, validUntil: today.AddDays(9));
        var question = await database.SeedWorkspaceQuoteAsync(world, user, "clarification_requested", validUntil: today.AddDays(9));
        var late = await database.SeedWorkspaceQuoteAsync(world, user, "sent", validUntil: today.AddDays(-1));
        var rejected = await database.SeedWorkspaceQuoteAsync(world, user, "rejected", response: "rejected", respondedAt: respondedAt, validUntil: today.AddDays(9));
        var storedExpired = await database.SeedWorkspaceQuoteAsync(world, user, "expired", validUntil: today.AddDays(-9));
        var approvedBare = await database.SeedWorkspaceQuoteAsync(world, user, "approved", response: "approved", respondedAt: respondedAt, validUntil: today.AddDays(9));
        var approvedDraftOrder = await database.SeedWorkspaceQuoteAsync(world, user, "approved", response: "approved", workOrderStatus: "draft", validUntil: today.AddDays(9));
        var approvedJob = await database.SeedWorkspaceQuoteAsync(world, user, "approved", response: "approved", workOrderStatus: "scheduled", validUntil: today.AddDays(9));
        var cancelled = await database.SeedWorkspaceQuoteAsync(world, user, "cancelled", currentVersion: 0);
        var guest = await database.SeedWorkspaceQuoteAsync(
            world, user, "sent", currentVersion: 1, linkCustomer: false, guestName: "Gus Guest", withService: false,
            quoteBranch: world.BranchB, currency: "EUR", total: 75m, validUntil: today.AddDays(9));

        var page = await GetAsync(host, owner, "/quotes?pageSize=100");

        // BR-13: customer and property, request, branch, total and currency, dates and links.
        var sentRow = RowOf(page, sent.Id);
        Assert.Equal($"Q-{sent.Number}", sentRow["displayNumber"]!.GetValue<string>());
        Assert.Equal("Carla Customer", sentRow["customerName"]!.GetValue<string>());
        Assert.Equal(
            ("Primary property", "Austin"),
            (sentRow["property"]!["name"]!.GetValue<string>(), sentRow["property"]!["city"]!.GetValue<string>()));
        Assert.Null(sentRow["property"]!["postalCode"]);
        Assert.Equal(
            (sent.Request, "Drain cleaning"),
            (sentRow["request"]!["id"]!.GetValue<Guid>(), sentRow["request"]!["title"]!.GetValue<string>()));
        Assert.Equal($"REQ-{sent.Number}", sentRow["request"]!["displayNumber"]!.GetValue<string>());
        Assert.Equal("Alpha Branch", sentRow["branch"]!["name"]!.GetValue<string>());
        Assert.Equal((230.5m, "USD"), (sentRow["total"]!.GetValue<decimal>(), sentRow["currency"]!.GetValue<string>()));
        Assert.Equal(today.AddDays(9).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), sentRow["validUntil"]!.GetValue<string>());
        Assert.NotNull(sentRow["sentAt"]);
        Assert.Null(sentRow["respondedAt"]);
        Assert.Null(sentRow["workOrderId"]);
        Assert.Equal(1, sentRow["revisionNo"]!.GetValue<int>());
        Assert.Equal(3, RowOf(page, revising.Id)["revisionNo"]!.GetValue<int>());

        var guestRow = RowOf(page, guest.Id);
        Assert.Equal("Gus Guest", guestRow["customerName"]!.GetValue<string>());
        Assert.Null(guestRow["property"]);
        Assert.Equal("Plumbing", guestRow["request"]!["title"]!.GetValue<string>());
        Assert.Equal("Bravo Branch", guestRow["branch"]!["name"]!.GetValue<string>());
        Assert.Equal((75m, "EUR"), (guestRow["total"]!.GetValue<decimal>(), guestRow["currency"]!.GetValue<string>()));

        Assert.Equal(
            respondedAt.ToUniversalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            RowOf(page, rejected.Id)["respondedAt"]!.GetValue<DateTimeOffset>().UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
        Assert.NotNull(RowOf(page, approvedBare.Id)["respondedAt"]);
        Assert.Equal(approvedDraftOrder.WorkOrder, RowOf(page, approvedDraftOrder.Id)["workOrderId"]!.GetValue<Guid>());

        // BR-15: the actions of every state, in contract order, for each role.
        var ownerExpected = new Dictionary<Guid, string[]>
        {
            [draft.Id] = ["view", "continue_draft", "open_request"],
            [sent.Id] = ["view", "revise", "open_request"],
            [revising.Id] = ["view", "continue_revision", "open_request"],
            [question.Id] = ["view", "revise", "open_request"],
            [late.Id] = ["view", "revise", "open_request"],
            [rejected.Id] = ["view", "revise", "open_request"],
            [storedExpired.Id] = ["view", "revise", "open_request"],
            [approvedBare.Id] = ["view", "create_work_order", "open_request"],
            [approvedDraftOrder.Id] = ["view", "continue_work_order", "open_request"],
            [approvedJob.Id] = ["view", "view_job", "open_request"],
            [cancelled.Id] = ["view", "open_request"],
            [guest.Id] = ["view", "revise", "open_request"],
        };
        Assert.Equal(ownerExpected, ownerExpected.Keys.ToDictionary(id => id, id => Actions(RowOf(page, id))));

        var operationsPage = await GetAsync(host, operations, "/quotes?pageSize=100");
        Assert.Equal(
            ownerExpected.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.Where(action => action is not ("continue_draft" or "continue_revision" or "revise")).ToArray()),
            ownerExpected.Keys.ToDictionary(id => id, id => Actions(RowOf(operationsPage, id))));

        var viewerPage = await GetAsync(host, viewer, "/quotes?pageSize=100");
        Assert.Equal(
            ownerExpected.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.Where(action => action is "view" or "view_job" or "open_request").ToArray()),
            ownerExpected.Keys.ToDictionary(id => id, id => Actions(RowOf(viewerPage, id))));

        var accountingPage = await GetAsync(host, accounting, "/quotes?pageSize=100");
        Assert.All(ownerExpected.Keys, id => Assert.Equal(["view", "open_request"], Actions(RowOf(accountingPage, id))));
    }
}
