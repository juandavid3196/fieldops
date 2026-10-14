using System.Data.Common;
using System.Net;
using System.Text.Json.Nodes;
using FieldOps.Infrastructure.Persistence;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.Customers;
using FieldOps.IntegrationTests.Dispatch;
using FieldOps.IntegrationTests.ServiceRequests;
using FieldOps.IntegrationTests.Sessions;
using FieldOps.IntegrationTests.Team;
using FieldOps.IntegrationTests.Users;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace FieldOps.IntegrationTests.WorkOrders;

/// <summary>The jobs workspace reads: list, summary and options (jobs-management AC-01 to AC-10, AC-21).</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class JobsManagementTests(CompanySettingsDatabaseFixture database)
{
    private const short Owner = CompanySettingsDatabaseFixture.OwnerRoleId;
    private const short Dispatcher = CompanySettingsDatabaseFixture.DispatcherRoleId;
    private const short Technician = CompanySettingsDatabaseFixture.TechnicianRoleId;
    private const short Accounting = CompanySettingsDatabaseFixture.AccountingRoleId;
    private const short Operations = CompanySettingsDatabaseFixture.OperationsManagerRoleId;
    private const short Viewer = CompanySettingsDatabaseFixture.ViewerRoleId;

    private static async Task<JsonNode> GetAsync(
        RequestsHost host, string cookie, string path, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var response = await host.SendAsync(HttpMethod.Get, path, cookie);

        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());

        return await DispatchSeed.ReadAsync(response, expected);
    }

    /// <summary>The problem body without its per-request trace id.</summary>
    private static async Task<string> ProblemBodyAsync(HttpResponseMessage response)
    {
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        body.Remove("traceId");

        return body.ToJsonString();
    }

    private static string[] Titles(JsonNode page) =>
        [.. page["items"]!.AsArray().Select(item => item!["title"]!.GetValue<string>())];

    private static JsonNode Row(JsonNode page, string title) =>
        page["items"]!.AsArray().Single(item => item!["title"]!.GetValue<string>() == title)!;

    private static string[] Actions(JsonNode row) =>
        [.. row["actions"]!.AsArray().Select(action => action!.GetValue<string>())];

    private static int[] Counts(JsonNode page) =>
        [.. new[] { "all", "active", "unscheduled", "completed" }.Select(key => page["counts"]![key]!.GetValue<int>())];

    // AC-03, AC-04, AC-05: derived status and current visit for every shape, tab membership, counts ignoring the status filter.
    [Fact]
    public async Task List_EveryStatusAndVisitShape_DerivesStatusCurrentVisitTabsAndCounts()
    {
        var world = await database.SeedWorldAsync();
        await using var host = RequestsHost.Create(database);
        var (owner, member) = await host.SignInAsync(database, world.Org, Owner);
        var user = member.UserId;
        var start = DateTimeOffset.UtcNow.AddDays(1);
        var end = start.AddHours(1);

        Task<SeededOrder> Seed(string title, string status, string visit) =>
            database.SeedOrderAsync(
                world, user, status: status, title: title, visitStatus: visit,
                start: visit == "unscheduled" ? null : start, end: visit == "unscheduled" ? null : end);

        var ready = await Seed("ready", "ready_to_schedule", "unscheduled");
        await Seed("scheduled", "scheduled", "scheduled");
        var assigned = await Seed("assigned", "scheduled", "assigned");
        var onTheWay = await Seed("on the way", "scheduled", "on_the_way");
        await Seed("running", "in_progress", "in_progress");
        await Seed("paused", "in_progress", "paused");
        var recurring = await Seed("recurring", "scheduled", "completed");
        var next = await DispatchSeed.SeedVisitAsync(database, world.Org, recurring.Order, 2, "unscheduled");
        var multi = await Seed("multi", "scheduled", "assigned");
        var lowest = await DispatchSeed.SeedVisitAsync(database, world.Org, multi.Order, 2, "unscheduled");
        await Seed("done", "completed", "completed");
        var closed = await Seed("closed", "completed", "completed");
        await DispatchSeed.SeedVisitAsync(database, world.Org, closed.Order, 2, "cancelled", start, end);
        await Seed("fix", "completed", "needs_correction");
        await Seed("billed", "approved_for_billing", "approved");
        var cancelled = await Seed("cancelled", "cancelled", "cancelled");
        var bare = await Seed("bare", "ready_to_schedule", "unscheduled");
        await database.ExecuteAsync("DELETE FROM visits WHERE id = @v", ("v", bare.Visit));
        await Seed("draft", "draft", "unscheduled");

        var ann = await database.SeedTechAsync(world.Org, world.BranchA, "Ann", "Alpha");
        await database.ExecuteAsync("UPDATE technician_profiles SET color_hex = '#112233' WHERE id = @t", ("t", ann));
        await database.AssignAsync(assigned.Visit, ann, user);
        await database.ExecuteAsync(
            """
            INSERT INTO visit_assignments (visit_id, technician_id, assigned_by_user_id, is_primary, assigned_at, unassigned_at)
            VALUES (@v, @t, @u, true, now() - interval '2 hours', now() - interval '1 hour')
            """,
            ("v", onTheWay.Visit),
            ("t", ann),
            ("u", user));

        var all = await GetAsync(host, owner, "/work-orders?pageSize=100");

        Assert.Equal(14, all["total"]!.GetValue<int>());
        Assert.DoesNotContain("draft", Titles(all));
        Assert.Equal([14, 7, 2, 4], Counts(all));
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["ready"] = "ready_to_schedule",
                ["scheduled"] = "scheduled",
                ["assigned"] = "assigned",
                ["on the way"] = "on_the_way",
                ["running"] = "in_progress",
                ["paused"] = "paused",
                ["recurring"] = "scheduled",
                ["multi"] = "assigned",
                ["done"] = "completed",
                ["closed"] = "completed",
                ["fix"] = "needs_correction",
                ["billed"] = "approved_for_billing",
                ["cancelled"] = "cancelled",
                ["bare"] = "ready_to_schedule",
            },
            all["items"]!.AsArray()
                .Where(item => item!["title"]!.GetValue<string>() != "draft")
                .ToDictionary(item => item!["title"]!.GetValue<string>(), item => item!["status"]!.GetValue<string>()));

        // BR-04: lowest open visit; else highest non-cancelled; else highest; none without visits.
        Assert.Equal(next, Row(all, "recurring")["currentVisit"]!["id"]!.GetValue<Guid>());
        Assert.Equal(multi.Visit, Row(all, "multi")["currentVisit"]!["id"]!.GetValue<Guid>());
        Assert.NotEqual(lowest, Row(all, "multi")["currentVisit"]!["id"]!.GetValue<Guid>());
        Assert.Equal(closed.Visit, Row(all, "closed")["currentVisit"]!["id"]!.GetValue<Guid>());
        Assert.Equal(cancelled.Visit, Row(all, "cancelled")["currentVisit"]!["id"]!.GetValue<Guid>());
        Assert.Null(Row(all, "bare")["currentVisit"]);
        Assert.Null(Row(all, "ready")["currentVisit"]!["scheduledStart"]);
        Assert.Equal(ready.Visit, Row(all, "ready")["currentVisit"]!["id"]!.GetValue<Guid>());

        // BR-12: the active primary assignment only; a removed one shows no technician.
        var technician = Row(all, "assigned")["technician"]!;
        Assert.Equal(("Ann Alpha", "AA", "#112233"), (technician["fullName"]!.GetValue<string>(), technician["initials"]!.GetValue<string>(), technician["colorHex"]!.GetValue<string>()));
        Assert.Null(Row(all, "on the way")["technician"]);
        Assert.Equal(("Alpha Branch", "Carla Customer", "UTC"), (
            Row(all, "ready")["branch"]!["name"]!.GetValue<string>(),
            Row(all, "ready")["customerName"]!.GetValue<string>(),
            Row(all, "ready")["timezone"]!.GetValue<string>()));
        Assert.Equal(["view", "schedule_visit"], Actions(Row(all, "ready")));
        Assert.Equal(["view"], Actions(Row(all, "bare")));
        Assert.Equal(["view", "open_in_schedule"], Actions(Row(all, "assigned")));
        Assert.Equal(["view", "open_in_schedule", "review_completion"], Actions(Row(all, "fix")));
        Assert.Equal(["view"], Actions(Row(all, "cancelled")));

        // BR-03, BR-06: tabs by work order status; the status filter changes the items and total, never the counts.
        Assert.Equal(7, (await GetAsync(host, owner, "/work-orders?tab=active"))["total"]!.GetValue<int>());
        Assert.Equal(["ready", "bare"], [.. Titles(await GetAsync(host, owner, "/work-orders?tab=unscheduled&sort=oldest"))]);
        var completed = await GetAsync(host, owner, "/work-orders?tab=completed&status=needs_correction");
        Assert.Equal(["fix"], Titles(completed));
        Assert.Equal(1, completed["total"]!.GetValue<int>());
        Assert.Equal([14, 7, 2, 4], Counts(completed));
        Assert.Equal(["paused"], Titles(await GetAsync(host, owner, "/work-orders?status=paused")));
        Assert.Equal(["cancelled"], Titles(await GetAsync(host, owner, "/work-orders?status=cancelled")));
        Assert.Equal(["assigned", "multi"], [.. Titles(await GetAsync(host, owner, "/work-orders?tab=active&status=assigned&sort=oldest"))]);

        // AC-05: a code outside the tab, or an unknown one, is a 400 on the status key.
        foreach (var path in new[] { "?tab=active&status=ready_to_schedule", "?tab=completed&status=assigned", "?status=bogus", "?tab=unscheduled&status=cancelled" })
        {
            var problem = await GetAsync(host, owner, $"/work-orders{path}", HttpStatusCode.BadRequest);
            Assert.NotNull(problem["errors"]!["status"]);
        }
    }

    // AC-06 to AC-09: search, priority, sorts with nulls last and the tie-break, pagination and validation errors.
    [Fact]
    public async Task List_SearchPrioritySortAndPagination_FollowTheRules()
    {
        var world = await database.SeedWorldAsync();
        await using var host = RequestsHost.Create(database);
        var (owner, member) = await host.SignInAsync(database, world.Org, Owner);
        var user = member.UserId;
        var now = DateTimeOffset.UtcNow;
        var zed = await database.SeedCustomerAsync(world.Org, world.BranchA, "Zed Zimmer");
        var zedProperty = (await database.QueryGuidsAsync("SELECT id FROM properties WHERE customer_id = @c", ("c", zed))).Single();
        await database.ExecuteAsync(
            "UPDATE properties SET name = 'Harbor House', address_line1 = '9 Pier Rd', city = 'Boston', postal_code = '02110' WHERE id = @p",
            ("p", zedProperty));

        // (title, priority, created offset days, schedule offset days or null, customer)
        var specs = new (string Title, short Priority, int Created, int? Schedule, bool Zed)[]
        {
            ("Alpha pump", 1, -5, 3, false),
            ("Beta valve", 2, -4, null, false),
            ("Gamma heater", 3, -3, 1, true),
            ("Delta filter", 4, -2, null, false),
            ("Epsilon fan", 5, -1, 2, false),
        };
        var visits = new Dictionary<string, Guid>();

        foreach (var spec in specs)
        {
            var created = now.AddDays(spec.Created);
            var order = await database.SeedOrderAsync(
                world, user, status: spec.Schedule is null ? "ready_to_schedule" : "scheduled", title: spec.Title, priority: spec.Priority,
                visitStatus: spec.Schedule is null ? "unscheduled" : "scheduled",
                start: spec.Schedule is { } days ? now.AddDays(days) : null,
                end: spec.Schedule is { } later ? now.AddDays(later).AddHours(1) : null,
                customer: spec.Zed ? zed : null,
                property: spec.Zed ? zedProperty : null);
            visits[spec.Title] = order.Visit;
            await database.ExecuteAsync(
                "UPDATE work_orders SET created_at = @c, updated_at = @c WHERE id = @o; UPDATE visits SET updated_at = @c WHERE work_order_id = @o",
                ("c", created),
                ("o", order.Order));
        }

        // Recently updated: a visit touched after its order wins over every order update.
        await database.ExecuteAsync("UPDATE visits SET updated_at = @u WHERE id = @v", ("u", now), ("v", visits["Alpha pump"]));
        await database.ExecuteAsync("UPDATE work_orders SET updated_at = @u WHERE title = 'Beta valve' AND organization_id = @o", ("u", now.AddHours(-1)), ("o", world.Org));

        async Task<string[]> Ordered(string query) => Titles(await GetAsync(host, owner, $"/work-orders?{query}"));

        Assert.Equal(["Epsilon fan", "Delta filter", "Gamma heater", "Beta valve", "Alpha pump"], await Ordered("sort=newest"));
        Assert.Equal(["Alpha pump", "Beta valve", "Gamma heater", "Delta filter", "Epsilon fan"], await Ordered("sort=oldest"));
        Assert.Equal(["Alpha pump", "Beta valve", "Gamma heater", "Delta filter", "Epsilon fan"], await Ordered("sort=priority"));
        Assert.Equal(["Alpha pump", "Beta valve", "Epsilon fan", "Delta filter", "Gamma heater"], await Ordered("sort=updated"));
        Assert.Equal(["Gamma heater", "Epsilon fan", "Alpha pump", "Delta filter", "Beta valve"], await Ordered("sort=schedule"));

        // BR-08: number with or without prefix and '#', any case, and text contains; counts follow the search.
        foreach (var term in new[] { "WO-3", "%23wo-3", "3", "zimmer", "BOST", "02110", "harbor", "9%20pier", "heater" })
        {
            Assert.Equal(["Gamma heater"], await Ordered($"q={term}"));
        }

        var searched = await GetAsync(host, owner, "/work-orders?q=zimmer&tab=completed");
        Assert.Empty(searched["items"]!.AsArray());
        Assert.Equal([1, 1, 0, 0], Counts(searched));
        Assert.Equal(["Epsilon fan", "Delta filter", "Beta valve", "Alpha pump"], await Ordered("q=carla"));
        Assert.Equal(5, (await GetAsync(host, owner, "/work-orders?q=%20%20"))["total"]!.GetValue<int>());

        // BR-07: priority ranges; low is 4 - 5; counts follow the priority.
        var low = await GetAsync(host, owner, "/work-orders?priority=low");
        Assert.Equal(["Epsilon fan", "Delta filter"], Titles(low));
        Assert.Equal(["low", "low"], [.. low["items"]!.AsArray().Select(item => item!["priority"]!.GetValue<string>())]);
        Assert.Equal([2, 1, 1, 0], Counts(low));
        Assert.Equal(["Alpha pump"], await Ordered("priority=urgent"));

        // BR-10: pages of 2, the last one partial, and a page past the last is empty with the real total.
        Assert.Equal(new[] { 2, 2, 1, 0 }, await Task.WhenAll(new[] { 1, 2, 3, 4 }.Select(async page =>
            (await GetAsync(host, owner, $"/work-orders?pageSize=2&page={page}"))["items"]!.AsArray().Count)));
        var past = await GetAsync(host, owner, "/work-orders?pageSize=2&page=4");
        Assert.Equal(5, past["total"]!.GetValue<int>());
        Assert.Equal(5, (await GetAsync(host, owner, "/work-orders"))["items"]!.AsArray().Count);

        var invalid = await GetAsync(
            host,
            owner,
            $"/work-orders?tab=x&q={new string('a', 101)}&priority=x&sort=x&page=0&pageSize=101&branchId=nope",
            HttpStatusCode.BadRequest);
        Assert.Equal(
            ["branchId", "page", "pageSize", "priority", "q", "sort", "tab"],
            [.. invalid["errors"]!.AsObject().Select(pair => pair.Key).Order(StringComparer.Ordinal)]);
        Assert.NotNull((await GetAsync(host, owner, "/work-orders?pageSize=0", HttpStatusCode.BadRequest))["errors"]!["pageSize"]);
        Assert.NotNull((await GetAsync(host, owner, "/work-orders/summary?branchId=nope", HttpStatusCode.BadRequest))["errors"]!["branchId"]);
    }

    // AC-10: the summary in the branch time zone, the local midnight boundaries, delayed and unassigned visits, a second branch.
    [Fact]
    public async Task Summary_BranchTimeZone_CountsTodayDelayedAndUnassignedJobsOnce()
    {
        var world = await database.SeedWorldAsync();
        var now = new DateTimeOffset(2026, 10, 20, 3, 30, 0, TimeSpan.Zero);
        await using var session = SessionTestHost.Create(database.ConnectionString, new MutableTimeProvider(now));
        var member = await database.SeedMemberAsync(world.Org, Owner, "Sam", "Staff");
        var cookie = await CompanySettingsApi.SignInCookieAsync(session.Client, member.Email);
        var user = member.UserId;

        // Branch A is New York (EDT): local today is Oct 19, [04:00Z, 04:00Z next day). Branch B has no zone, so the organization's UTC applies.
        await database.ExecuteAsync("UPDATE branches SET timezone = 'America/New_York' WHERE id = @b", ("b", world.BranchA));
        var day = new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero);

        Task<SeededOrder> Seed(string order, string visit, DateTimeOffset from, TimeSpan length, Guid? branch = null) =>
            database.SeedOrderAsync(world, user, branch: branch, status: order, visitStatus: visit, start: visit == "unscheduled" ? null : from, end: visit == "unscheduled" ? null : from + length);

        await Seed("completed", "completed", day.AddHours(-20), TimeSpan.FromHours(1));      // 04:00Z Oct 19: local midnight, in
        await Seed("scheduled", "assigned", day.AddHours(-20).AddSeconds(-1), TimeSpan.FromHours(1)); // one second before: out
        await Seed("scheduled", "assigned", day.AddHours(4), TimeSpan.FromHours(1));          // 04:00Z Oct 20: next local midnight, out (in UTC)
        var delayed = await Seed("scheduled", "assigned", day.AddHours(3), TimeSpan.FromMinutes(20));   // 03:00Z: delayed
        await DispatchSeed.SeedVisitAsync(database, world.Org, delayed.Order, 2, "assigned", day.AddHours(2), day.AddHours(2).AddMinutes(20));
        await Seed("scheduled", "scheduled", day.AddHours(3).AddMinutes(45), TimeSpan.FromHours(1));    // not assigned yet: attention
        await Seed("in_progress", "in_progress", day.AddHours(2), TimeSpan.FromMinutes(89));            // ends 03:29:00, exactly 60 s ago: delayed
        await Seed("in_progress", "in_progress", day.AddHours(2), TimeSpan.FromMinutes(89).Add(TimeSpan.FromSeconds(1))); // 59 s: on time
        await Seed("scheduled", "on_the_way", day.AddHours(3).AddMinutes(45).AddSeconds(30), TimeSpan.FromHours(1)); // starts in the future
        await Seed("scheduled", "cancelled", day.AddHours(3), TimeSpan.FromHours(1));                   // cancelled visit: never counts
        await Seed("cancelled", "assigned", day.AddHours(3), TimeSpan.FromHours(1));                    // cancelled job: never counts
        await Seed("draft", "assigned", day.AddHours(3), TimeSpan.FromHours(1));                        // draft job: never counts
        await Seed("ready_to_schedule", "unscheduled", day, TimeSpan.Zero);
        await Seed("ready_to_schedule", "unscheduled", day, TimeSpan.Zero);
        await Seed("scheduled", "assigned", day.AddHours(1), TimeSpan.FromHours(1), world.BranchB);     // 01:00Z Oct 20: today in UTC only, delayed

        async Task<JsonNode> Summary(string query = "") =>
            await DispatchSeed.ReadAsync(
                await CompanySettingsApi.SendRawAsync(session.Client, HttpMethod.Get, $"/work-orders/summary{query}", null, cookie));

        static int[] Flatten(JsonNode summary) =>
        [
            summary["activeJobs"]!["total"]!.GetValue<int>(),
            summary["activeJobs"]!["inProgress"]!.GetValue<int>(),
            summary["readyToSchedule"]!.GetValue<int>(),
            summary["today"]!["total"]!.GetValue<int>(),
            summary["today"]!["assigned"]!.GetValue<int>(),
            summary["today"]!["completed"]!.GetValue<int>(),
            summary["needsAttention"]!.GetValue<int>(),
        ];

        var branchA = await Summary($"?branchId={world.BranchA}");
        Assert.Equal("America/New_York", branchA["timezone"]!.GetValue<string>());
        Assert.Equal([8, 2, 2, 7, 5, 1, 3], Flatten(branchA));

        var branchB = await Summary($"?branchId={world.BranchB}");
        Assert.Equal("UTC", branchB["timezone"]!.GetValue<string>());
        Assert.Equal([1, 0, 0, 1, 1, 0, 1], Flatten(branchB));

        var everything = await Summary();
        Assert.Equal("UTC", everything["timezone"]!.GetValue<string>());
        Assert.Equal([9, 2, 2, 8, 7, 0, 4], Flatten(everything));
    }

    // AC-01, AC-02: role 403s and canManage, a Dispatcher's scope over items, counts and metrics, and identical 404s for a branch filter.
    [Fact]
    public async Task Endpoints_RolesScopeAndBranchFilter_AuthorizeAndIsolateTenants()
    {
        var world = await database.SeedWorldAsync();
        var foreign = await database.SeedWorldAsync();
        await using var host = RequestsHost.Create(database);
        var (owner, member) = await host.SignInAsync(database, world.Org, Owner);
        var user = member.UserId;
        var (operations, _) = await host.SignInAsync(database, world.Org, Operations);
        var (viewer, _) = await host.SignInAsync(database, world.Org, Viewer);
        var (dispatcherA, _) = await host.SignInAsync(database, world.Org, Dispatcher, "Dee", "Alpha", world.BranchA);
        var (foreignOwner, foreignMember) = await host.SignInAsync(database, foreign.Org, Owner);
        var start = DateTimeOffset.UtcNow.AddDays(1);
        var inactive = await database.SeedBranchAsync(world.Org, "Closed Branch", isActive: false);

        await database.SeedOrderAsync(world, user, status: "ready_to_schedule", title: "A ready");
        await database.SeedOrderAsync(world, user, status: "completed", title: "A done", visitStatus: "completed", start: start, end: start.AddHours(1));
        await database.SeedOrderAsync(world, user, branch: world.BranchB, status: "scheduled", title: "B booked", visitStatus: "assigned", start: start, end: start.AddHours(1));
        await database.SeedOrderAsync(foreign, foreignMember.UserId, status: "ready_to_schedule", title: "Foreign ready");

        // AC-01: Read roles get 200; Accounting and Technician get 403 on all three endpoints.
        foreach (var role in new[] { Accounting, Technician })
        {
            var (denied, _) = await host.SignInAsync(database, world.Org, role);

            foreach (var path in new[] { "/work-orders", "/work-orders/summary", "/work-orders/options" })
            {
                Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(HttpMethod.Get, path, denied)).StatusCode);
            }
        }

        foreach (var (cookie, canManage) in new[] { (owner, true), (operations, true), (dispatcherA, true), (viewer, false) })
        {
            Assert.Equal(canManage, (await GetAsync(host, cookie, "/work-orders/options"))["canManage"]!.GetValue<bool>());
            await GetAsync(host, cookie, "/work-orders/summary");
        }

        // BR-14: the Viewer never gets schedule_visit; Manage roles do.
        Assert.Equal(["view"], Actions(Row(await GetAsync(host, viewer, "/work-orders"), "A ready")));
        Assert.Equal(["view", "schedule_visit"], Actions(Row(await GetAsync(host, owner, "/work-orders"), "A ready")));

        // Options: active in-scope branches by name, the organization zone and canManage.
        var options = await GetAsync(host, owner, "/work-orders/options");
        Assert.Equal(["Alpha Branch", "Bravo Branch"], [.. options["branches"]!.AsArray().Select(branch => branch!["name"]!.GetValue<string>())]);
        Assert.Equal("UTC", options["organizationTimezone"]!.GetValue<string>());
        Assert.Equal(
            ["Alpha Branch"],
            [.. (await GetAsync(host, dispatcherA, "/work-orders/options"))["branches"]!.AsArray().Select(branch => branch!["name"]!.GetValue<string>())]);
        Assert.Equal(2, (await GetAsync(host, foreignOwner, "/work-orders/options"))["branches"]!.AsArray().Count);

        // AC-02: the Dispatcher sees only branch A in items, counts and metrics; the Owner sees both; the foreign organization only its own.
        var scoped = await GetAsync(host, dispatcherA, "/work-orders");
        Assert.Equal(["A done", "A ready"], [.. Titles(scoped).Order(StringComparer.Ordinal)]);
        Assert.Equal([2, 0, 1, 1], Counts(scoped));
        Assert.Equal(3, (await GetAsync(host, owner, "/work-orders"))["total"]!.GetValue<int>());
        Assert.Equal(["Foreign ready"], Titles(await GetAsync(host, foreignOwner, "/work-orders")));
        var summary = await GetAsync(host, dispatcherA, "/work-orders/summary");
        Assert.Equal((0, 1), (summary["activeJobs"]!["total"]!.GetValue<int>(), summary["readyToSchedule"]!.GetValue<int>()));
        Assert.Equal(["B booked"], Titles(await GetAsync(host, owner, $"/work-orders?branchId={world.BranchB}")));
        Assert.Equal(2, (await GetAsync(host, dispatcherA, $"/work-orders?branchId={world.BranchA}"))["total"]!.GetValue<int>());

        // A foreign, inactive, out-of-scope and unknown branch are one identical 404, for the list and the summary.
        var unknown = await host.SendAsync(HttpMethod.Get, $"/work-orders?branchId={Guid.NewGuid()}", dispatcherA);
        var unknownBody = await ProblemBodyAsync(unknown);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        foreach (var (cookie, branch) in new[]
        {
            (dispatcherA, world.BranchB),
            (dispatcherA, foreign.BranchA),
            (owner, foreign.BranchA),
            (owner, inactive.Id),
        })
        {
            foreach (var path in new[] { "/work-orders", "/work-orders/summary" })
            {
                var response = await host.SendAsync(HttpMethod.Get, $"{path}?branchId={branch}", cookie);
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
                Assert.Equal(unknownBody, await ProblemBodyAsync(response));
                Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
            }
        }
    }

    // AC-21: the same number of commands for a page of 10 and a page of 50 over 220 jobs with several visits and assignments.
    [Fact]
    public async Task ListAndSummary_PageSizeAndRowCount_DoNotChangeTheCommandCount()
    {
        var world = await database.SeedWorldAsync();
        var counter = new CommandCounter();
        await using var session = SessionTestHost.Create(database.ConnectionString);
        using var client = SessionApi.CreateClient(session.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.ConfigureDbContext<FieldOpsDbContext>(options => options.AddInterceptors(counter)))));
        var member = await database.SeedMemberAsync(world.Org, Owner, "Sam", "Staff");
        var cookie = await CompanySettingsApi.SignInCookieAsync(client, member.Email);
        var tech = await database.SeedTechAsync(world.Org, world.BranchA, "Ann", "Alpha");

        await database.ExecuteAsync(
            """
            SET session_replication_role = replica;
            INSERT INTO work_orders (id, organization_id, branch_id, work_order_number, quote_version_id, customer_id, property_id, title, job_type,
                service_category_id, estimated_duration_minutes, notify_customer_when_scheduled, send_technician_details, status, priority, scope_snapshot, created_by_user_id)
            SELECT gen_random_uuid(), @org, @branch, 1000 + g, gen_random_uuid(), @customer, @property, 'Bulk ' || g, 'one_time',
                @category, 60, true, true, CAST('scheduled' AS work_order_status), 3, 'Scope', @user
            FROM generate_series(1, 220) g;
            INSERT INTO visits (id, organization_id, work_order_id, visit_number, status, scheduled_start, scheduled_end)
            SELECT gen_random_uuid(), w.organization_id, w.id, n,
                CAST(CASE WHEN n = 1 THEN 'completed' ELSE 'assigned' END AS visit_status), now() + interval '1 day', now() + interval '1 day 1 hour'
            FROM work_orders w CROSS JOIN generate_series(1, 2) n WHERE w.organization_id = @org;
            INSERT INTO visit_assignments (visit_id, technician_id, assigned_by_user_id, is_primary)
            SELECT id, @tech, @user, true FROM visits WHERE organization_id = @org AND visit_number = 2;
            """,
            ("org", world.Org),
            ("branch", world.BranchA),
            ("customer", world.Customer),
            ("property", world.Property),
            ("category", world.Category),
            ("user", member.UserId),
            ("tech", tech));

        async Task<(int Commands, JsonNode Body)> Measure(string path)
        {
            counter.Reset();
            var response = await CompanySettingsApi.SendRawAsync(client, HttpMethod.Get, path, null, cookie);
            var body = await DispatchSeed.ReadAsync(response);

            return (counter.Count, body);
        }

        var (small, smallPage) = await Measure("/work-orders?pageSize=10");
        var (large, largePage) = await Measure("/work-orders?pageSize=50");
        var (single, _) = await Measure("/work-orders?pageSize=1");
        var (sorted, _) = await Measure("/work-orders?pageSize=50&sort=updated");
        var (sortedSmall, _) = await Measure("/work-orders?pageSize=10&sort=updated");
        var (summaryCount, _) = await Measure("/work-orders/summary");

        Assert.Equal(10, smallPage["items"]!.AsArray().Count);
        Assert.Equal(50, largePage["items"]!.AsArray().Count);
        Assert.Equal(220, largePage["total"]!.GetValue<int>());
        Assert.Equal("Ann Alpha", largePage["items"]![0]!["technician"]!["fullName"]!.GetValue<string>());
        Assert.Equal(small, large);
        Assert.Equal(small, single);
        Assert.Equal(sorted, sortedSmall);
        Assert.InRange(large, 1, 12);
        Assert.InRange(summaryCount, 1, 12);
    }

    private sealed class CommandCounter : DbCommandInterceptor
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public void Reset() => Volatile.Write(ref _count, 0);

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Interlocked.Increment(ref _count);

            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _count);

            return ValueTask.FromResult(result);
        }
    }
}
