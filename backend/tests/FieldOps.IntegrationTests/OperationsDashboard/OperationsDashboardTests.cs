using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.Dispatch;
using FieldOps.IntegrationTests.PublicRequests;
using FieldOps.IntegrationTests.ServiceRequests;
using FieldOps.IntegrationTests.Team;

namespace FieldOps.IntegrationTests.OperationsDashboard;

/// <summary>Operations dashboard reads: data, filters, time zones, performance and authorization (operations-dashboard AC-02 to AC-15).</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class OperationsDashboardTests(CompanySettingsDatabaseFixture database)
{
    private static DateOnly Today(int offset = 0) => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(offset);

    private static DateTimeOffset At(DateOnly day, int hour, int minute = 0) =>
        new(day.Year, day.Month, day.Day, hour, minute, 0, TimeSpan.Zero);

    private static string Iso(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static async Task<JsonNode> GetAsync(
        RequestsHost host, string cookie, string path, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var response = await host.SendAsync(HttpMethod.Get, path, cookie);

        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());

        return await DispatchSeed.ReadAsync(response, expected);
    }

    private static string[] Titles(JsonNode dashboard) =>
        [.. dashboard["visits"]!.AsArray().Select(item => item!["title"]!.GetValue<string>())];

    private static string[] Ids(JsonNode array, string key = "id") =>
        [.. array.AsArray().Select(item => item![key]!.GetValue<Guid>().ToString())];

    private Task SetWindowAsync(Guid visit, DateTimeOffset end) =>
        database.ExecuteAsync(
            "UPDATE visits SET arrival_window_start = scheduled_start, arrival_window_end = @w WHERE id = @v", ("w", end), ("v", visit));

    private Task SetStartedAsync(Guid visit, DateTimeOffset started, int pauseSeconds = 0) =>
        database.ExecuteAsync(
            "UPDATE visits SET actual_started_at = @s, pause_seconds = @p WHERE id = @v", ("s", started), ("p", pauseSeconds), ("v", visit));

    private Task SetCompletedAsync(Guid visit, DateTimeOffset completed) =>
        database.ExecuteAsync("UPDATE visits SET actual_completed_at = @c WHERE id = @v", ("c", completed), ("v", visit));

    private Task AssignAtAsync(Guid visit, Guid technician, Guid user, DateTimeOffset assignedAt, DateTimeOffset? unassignedAt = null) =>
        database.ExecuteAsync(
            """
            INSERT INTO visit_assignments (visit_id, technician_id, assigned_by_user_id, is_primary, assigned_at, unassigned_at)
            VALUES (@v, @t, @u, true, @a, @x)
            """,
            ("v", visit),
            ("t", technician),
            ("u", user),
            ("a", assignedAt),
            ("x", unassignedAt));

    private Task HistoryAsync(Guid visit, string? from, string to, DateTimeOffset at) =>
        database.ExecuteAsync(
            // from and to are test-controlled constants, never user input.
            $"INSERT INTO visit_status_history (visit_id, from_status, to_status, changed_at) VALUES (@v, {(from is null ? "NULL" : $"CAST('{from}' AS visit_status)")}, CAST('{to}' AS visit_status), @at)",
            ("v", visit),
            ("at", at));

    private Task ReviewAsync(Guid org, Guid order, short rating, DateTimeOffset createdAt, Guid? technician = null) =>
        database.ExecuteAsync(
            """
            SET session_replication_role = replica;
            INSERT INTO invoice_reviews (organization_id, work_order_id, invoice_id, technician_id, rating, created_at)
            VALUES (@o, @w, gen_random_uuid(), @t, @r, @c);
            """,
            ("o", org),
            ("w", order),
            ("t", technician),
            ("r", rating),
            ("c", createdAt));

    // AC-04, AC-06 to AC-08, AC-11, AC-13, AC-15: one grouped past day with every card of the dashboard.
    [Fact]
    public async Task Dashboard_PastDay_ReturnsKpisVisitsAttentionTechniciansDurationsAndActivity()
    {
        var world = await database.SeedWorldAsync();
        await using var host = RequestsHost.Create(database);
        var (owner, ownerMember) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var user = ownerMember.UserId;
        var day = Today(-2);
        var plumbing = await database.SeedSkillAsync(world.Org, "Plumbing");
        var drywall = await database.SeedSkillAsync(world.Org, "Drywall");
        var welding = await database.SeedSkillAsync(world.Org, "Welding");
        var ann = await database.SeedAvailableTechAsync(world.Org, world.BranchA, "Ann", "Alpha", "active", true, plumbing, welding, drywall);
        var bob = await database.SeedTechAsync(world.Org, world.BranchB, "Bob", "Bravo");
        await database.SeedSlotAsync(bob, (short)day.DayOfWeek, "08:00", "10:00");

        var done = await database.SeedOrderAsync(world, user, status: "completed", title: "Done job", visitStatus: "completed", start: At(day, 9), end: At(day, 10), minutes: 120);
        var late = await database.SeedOrderAsync(world, user, status: "scheduled", title: "Late job", visitStatus: "assigned", start: At(day, 9), end: At(day, 10));
        var open = await database.SeedOrderAsync(world, user, status: "scheduled", title: "Unassigned job", visitStatus: "scheduled", start: At(day, 14), end: At(day, 15));
        var running = await database.SeedOrderAsync(world, user, status: "in_progress", title: "Long job", visitStatus: "in_progress", start: At(day, 13), end: At(day, 14), minutes: 60);
        var travelling = await database.SeedOrderAsync(world, user, status: "in_progress", title: "Travel job", visitStatus: "on_the_way", start: At(day, 11), end: At(day, 12));
        var bravo = await database.SeedOrderAsync(world, user, branch: world.BranchB, status: "scheduled", title: "Bravo job", visitStatus: "assigned", start: At(day, 8), end: At(day, 11));
        await database.SeedOrderAsync(world, user, status: "scheduled", title: "Cancelled job", visitStatus: "cancelled", start: At(day, 10), end: At(day, 11));
        await database.SeedOrderAsync(world, user, status: "draft", title: "Draft job", visitStatus: "scheduled", start: At(day, 10), end: At(day, 11));
        await database.SeedOrderAsync(world, user, status: "ready_to_schedule", title: "Waiting job");

        await SetStartedAsync(done.Visit, At(day, 9, 5), 300);
        await SetCompletedAsync(done.Visit, At(day, 10, 10));
        await SetWindowAsync(late.Visit, At(day, 9, 30));
        await SetStartedAsync(running.Visit, At(day, 13));
        await AssignAtAsync(done.Visit, ann, user, At(day, 7));
        await AssignAtAsync(late.Visit, bob, user, At(day, 6), At(day, 7, 30));
        await AssignAtAsync(late.Visit, ann, user, At(day, 7, 30));
        await database.AssignAsync(running.Visit, ann, user);
        await database.AssignAsync(travelling.Visit, ann, user);
        await database.AssignAsync(bravo.Visit, bob, user);
        var request = await database.SeedRequestAsync(world, status: "assessment_scheduled");
        await database.SeedAssessmentAsync(world.Org, request.Id, At(day, 15), At(day, 16, 30), user, ann);
        await HistoryAsync(done.Visit, "assigned", "on_the_way", At(day, 8, 50));
        await HistoryAsync(done.Visit, "on_the_way", "in_progress", At(day, 9, 5));
        await HistoryAsync(done.Visit, "in_progress", "completed", At(day, 10, 10));
        await HistoryAsync(running.Visit, "paused", "in_progress", At(day, 13));

        var dashboard = await GetAsync(host, owner, $"/operations-dashboard?date={Iso(day)}");

        Assert.Equal("UTC", dashboard["timezone"]!.GetValue<string>());
        Assert.Equal(Iso(day), dashboard["date"]!.GetValue<string>());
        Assert.Equal(At(day.AddDays(1), 0), dashboard["reference"]!.GetValue<DateTimeOffset>());

        // BR-06 to BR-09: cancelled, draft and unscheduled visits never count.
        var kpis = dashboard["kpis"]!;
        Assert.Equal([6, 1, 2, 3], new[] { "total", "completed", "active", "scheduled" }.Select(key => kpis["jobs"]![key]!.GetValue<int>()).ToArray());
        Assert.Equal(1, kpis["unassigned"]!.GetValue<int>());
        Assert.Equal(5, kpis["atRiskOrDelayed"]!.GetValue<int>());
        Assert.Equal(510, kpis["utilization"]!["scheduledMinutes"]!.GetValue<int>());
        Assert.Equal(660, kpis["utilization"]!["availableMinutes"]!.GetValue<int>());
        Assert.Equal(77, kpis["utilization"]!["percent"]!.GetValue<int>());
        Assert.Equal(6, kpis["onTimeArrival"]!["eligible"]!.GetValue<int>());
        Assert.Equal(33, kpis["onTimeArrival"]!["percent"]!.GetValue<int>());
        Assert.Null(kpis["onTimeArrival"]!["previousPercent"]);

        Assert.Equal(["Bravo job", "Done job", "Late job", "Travel job", "Long job", "Unassigned job"], Titles(dashboard));
        var visits = dashboard["visits"]!.AsArray().ToDictionary(item => item!["title"]!.GetValue<string>(), item => item!);
        Assert.Equal("completed", visits["Done job"]["statusGroup"]!.GetValue<string>());
        Assert.Equal(At(day, 10, 10), visits["Done job"]["actualCompletedAt"]!.GetValue<DateTimeOffset>());
        Assert.Equal(870, visits["Late job"]["delayMinutes"]!.GetValue<int>());
        Assert.Equal("Ann Alpha", visits["Late job"]["technician"]!["fullName"]!.GetValue<string>());
        Assert.Null(visits["Unassigned job"]["technician"]);
        Assert.Equal("5551234567", visits["Travel job"]["customerPhone"]!.GetValue<string>());
        Assert.Null(visits["Late job"]["customerPhone"]);
        Assert.Equal(At(day, 14, 0), visits["Long job"]["projectedEnd"]!.GetValue<DateTimeOffset>());

        // BR-11 order: delayed by delay descending, over estimate, over capacity.
        var attention = dashboard["attention"]!.AsArray();
        Assert.Equal(
            ["delayed", "delayed", "delayed", "delayed", "delayed", "over_estimate", "over_capacity"],
            attention.Select(item => item!["type"]!.GetValue<string>()).ToArray());
        Assert.Equal([960, 870, 780, 600, 600], attention.Take(5).Select(item => item!["minutes"]!.GetValue<int>()).ToArray());
        Assert.Equal("Long job", attention[3]!["title"]!.GetValue<string>());
        Assert.Equal(600, attention[5]!["minutes"]!.GetValue<int>());
        Assert.Equal(150, attention[6]!["technician"]!["percent"]!.GetValue<int>());

        // BR-12: workload descending, skills primary first, no live status on another day.
        var technicians = dashboard["technicians"]!.AsArray();
        Assert.Equal(["Bob Bravo", "Ann Alpha"], technicians.Select(item => item!["fullName"]!.GetValue<string>()).ToArray());
        Assert.Equal(61, technicians[1]!["percent"]!.GetValue<int>());
        Assert.Equal(["Plumbing", "Drywall"], technicians[1]!["skills"]!.AsArray().Select(item => item!.GetValue<string>()).ToArray());
        Assert.Equal(1, technicians[1]!["extraSkillCount"]!.GetValue<int>());
        Assert.Equal("AA", technicians[1]!["initials"]!.GetValue<string>());
        Assert.All(technicians, item =>
        {
            Assert.Null(item!["currentStatus"]);
            Assert.Null(item["nextAvailable"]);
        });

        // BR-13: actual = completed - started - pauses.
        var durations = dashboard["durations"]!;
        Assert.Equal(1, durations["visitCount"]!.GetValue<int>());
        Assert.Equal([120, 60], new[] { "estimatedMinutes", "actualMinutes" }.Select(key => durations["categories"]![0]![key]!.GetValue<int>()).ToArray());

        // BR-15: newest first, assigned vs reassigned, technician at read time.
        var activity = dashboard["activity"]!.AsArray();
        Assert.Equal(
            ["completed", "started", "on_the_way", "reassigned", "assigned", "assigned"],
            activity.Select(item => item!["type"]!.GetValue<string>()).ToArray());
        Assert.Equal("Ann Alpha", activity[0]!["technicianName"]!.GetValue<string>());
        Assert.Equal("Bob Bravo", activity[5]!["technicianName"]!.GetValue<string>());
        Assert.EndsWith("-" + done.Number, activity[0]!["displayNumber"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    // AC-04: the day is the local day of the branch zone (organization zone for All) and a null branch zone falls back to the organization.
    [Fact]
    public async Task Dashboard_TimeZoneDayBoundary_UsesBranchZoneOrOrganizationZone()
    {
        var world = await database.SeedWorldAsync();
        await using var host = RequestsHost.Create(database);
        var (owner, ownerMember) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        await database.ExecuteAsync("UPDATE branches SET timezone = 'Pacific/Auckland' WHERE id = @b", ("b", world.BranchA));
        var day = Today(-3);

        // 13:00 UTC of the previous day is 01:00 or 02:00 of `day` in Auckland.
        await database.SeedOrderAsync(
            world, ownerMember.UserId, status: "scheduled", title: "Edge job", visitStatus: "scheduled", start: At(day.AddDays(-1), 13), end: At(day.AddDays(-1), 14));

        var options = await GetAsync(host, owner, "/operations-dashboard/options");
        Assert.Equal("UTC", options["organizationTimezone"]!.GetValue<string>());
        Assert.Equal(
            new string?[] { "Pacific/Auckland", null },
            options["branches"]!.AsArray().Select(item => item!["timezone"]?.GetValue<string>()).ToArray());

        var branch = await GetAsync(host, owner, $"/operations-dashboard?branchId={world.BranchA}&date={Iso(day)}");
        Assert.Equal("Pacific/Auckland", branch["timezone"]!.GetValue<string>());
        Assert.Equal(["Edge job"], Titles(branch));

        var all = await GetAsync(host, owner, $"/operations-dashboard?date={Iso(day)}");
        Assert.Equal("UTC", all["timezone"]!.GetValue<string>());
        Assert.Empty(Titles(all));
        Assert.Equal(["Edge job"], Titles(await GetAsync(host, owner, $"/operations-dashboard?date={Iso(day.AddDays(-1))}")));

        var fallback = await GetAsync(host, owner, $"/operations-dashboard?branchId={world.BranchB}&date={Iso(day)}");
        Assert.Equal("UTC", fallback["timezone"]!.GetValue<string>());
    }

    // AC-08 and validation: technician filter limits visits and utilization, category filter limits visits only; invalid input is a 400/404.
    [Fact]
    public async Task Dashboard_FiltersAndValidation_LimitVisitsNotUtilizationAndRejectInvalidInput()
    {
        var world = await database.SeedWorldAsync();
        var foreign = await database.SeedWorldAsync();
        await using var host = RequestsHost.Create(database);
        var (owner, ownerMember) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var user = ownerMember.UserId;
        var day = Today(-1);
        var electrical = await database.SeedCategoryAsync(world.Org, "Electrical");
        var retired = await database.SeedCategoryAsync(world.Org, "Retired", active: false);
        var foreignCategory = await database.SeedCategoryAsync(foreign.Org, "Foreign");
        var ann = await database.SeedAvailableTechAsync(world.Org, world.BranchA, "Ann", "Alpha");
        var bob = await database.SeedAvailableTechAsync(world.Org, world.BranchA, "Bob", "Bravo");
        var dormant = await database.SeedAvailableTechAsync(world.Org, world.BranchA, "Dora", "Dormant", "inactive");
        var other = await database.SeedAvailableTechAsync(world.Org, world.BranchB, "Cy", "Charlie");

        var first = await database.SeedOrderAsync(world, user, status: "scheduled", title: "Plumbing one", visitStatus: "assigned", start: At(day, 9), end: At(day, 10));
        var second = await database.SeedOrderAsync(world, user, status: "scheduled", title: "Plumbing two", visitStatus: "assigned", start: At(day, 10), end: At(day, 11));
        var wiring = await database.SeedOrderAsync(world, user, status: "scheduled", title: "Wiring", visitStatus: "assigned", start: At(day, 13), end: At(day, 14));
        await database.ExecuteAsync("UPDATE work_orders SET service_category_id = @c WHERE id = @w", ("c", electrical), ("w", wiring.Order));
        await database.AssignAsync(first.Visit, ann, user);
        await database.AssignAsync(second.Visit, bob, user);
        await database.AssignAsync(wiring.Visit, ann, user);

        var baseUrl = $"/operations-dashboard?date={Iso(day)}";
        var unfiltered = await GetAsync(host, owner, baseUrl);
        Assert.Equal(3, Titles(unfiltered).Length);
        Assert.Equal(3, unfiltered["technicians"]!.AsArray().Count);
        Assert.Equal(180, unfiltered["kpis"]!["utilization"]!["scheduledMinutes"]!.GetValue<int>());
        Assert.Equal(1620, unfiltered["kpis"]!["utilization"]!["availableMinutes"]!.GetValue<int>());

        // Category: visits only; utilization and technician rows are unchanged.
        var byCategory = await GetAsync(host, owner, $"{baseUrl}&categoryId={electrical}");
        Assert.Equal(["Wiring"], Titles(byCategory));
        Assert.Equal(3, byCategory["technicians"]!.AsArray().Count);
        Assert.Equal(unfiltered["kpis"]!["utilization"]!.ToJsonString(), byCategory["kpis"]!["utilization"]!.ToJsonString());

        // Technician: visits with an active assignment, one technician row and that technician's utilization.
        var byTechnician = await GetAsync(host, owner, $"{baseUrl}&technicianId={ann}");
        Assert.Equal(["Plumbing one", "Wiring"], Titles(byTechnician));
        Assert.Equal([ann.ToString()], Ids(byTechnician["technicians"]!));
        Assert.Equal(120, byTechnician["kpis"]!["utilization"]!["scheduledMinutes"]!.GetValue<int>());
        Assert.Equal(["Wiring"], Titles(await GetAsync(host, owner, $"{baseUrl}&technicianId={ann}&categoryId={electrical}")));

        // 400: date, category and malformed ids; several errors are reported together.
        foreach (var (query, key) in new[]
        {
            ("", "date"),
            ("date=2026-13-45", "date"),
            ($"date={Iso(Today(366))}", "date"),
            ($"date={Iso(Today(-366))}", "date"),
            ($"date={Iso(day)}&categoryId=abc", "categoryId"),
            ($"date={Iso(day)}&categoryId={foreignCategory}", "categoryId"),
            ($"date={Iso(day)}&categoryId={retired}", "categoryId"),
            ($"date={Iso(day)}&categoryId={Guid.NewGuid()}", "categoryId"),
            ($"date={Iso(day)}&branchId=abc", "branchId"),
            ($"date={Iso(day)}&technicianId=abc", "technicianId"),
        })
        {
            await DispatchSeed.ProblemAsync(
                await host.SendAsync(HttpMethod.Get, $"/operations-dashboard?{query}", owner), HttpStatusCode.BadRequest, errorKey: key);
        }

        var both = await DispatchSeed.ReadAsync(
            await host.SendAsync(HttpMethod.Get, $"/operations-dashboard?date=nope&categoryId=abc", owner), HttpStatusCode.BadRequest);
        Assert.NotNull(both["errors"]!["date"]);
        Assert.NotNull(both["errors"]!["categoryId"]);
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(HttpMethod.Get, $"/operations-dashboard?date={Iso(Today(365))}", owner)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(HttpMethod.Get, $"/operations-dashboard?date={Iso(Today(-365))}", owner)).StatusCode);

        // 404: unknown, inactive, and (for a branch selection) out-of-branch technicians.
        foreach (var technician in new[] { Guid.NewGuid(), dormant })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Get, $"{baseUrl}&technicianId={technician}", owner)).StatusCode);
        }

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await host.SendAsync(HttpMethod.Get, $"{baseUrl}&branchId={world.BranchA}&technicianId={other}", owner)).StatusCode);

        // Performance: the period is 7 or 30.
        foreach (var query in new[] { "", "&period=14", "&period=abc" })
        {
            await DispatchSeed.ProblemAsync(
                await host.SendAsync(HttpMethod.Get, $"/operations-dashboard/performance?date={Iso(day)}{query}", owner),
                HttpStatusCode.BadRequest,
                errorKey: "period");
        }
    }

    // AC-14: jobs completed, first-time fix rate, rating and the daily series for 7 and 30 days, with category and technician filters.
    [Fact]
    public async Task Performance_SevenAndThirtyDays_ReturnsCompletionFixRateRatingAndDaily()
    {
        var world = await database.SeedWorldAsync();
        await using var host = RequestsHost.Create(database);
        var (owner, ownerMember) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var user = ownerMember.UserId;
        var day = Today(-1);
        var electrical = await database.SeedCategoryAsync(world.Org, "Electrical");
        var ann = await database.SeedAvailableTechAsync(world.Org, world.BranchA, "Ann", "Alpha");

        async Task<SeededOrder> CompletedAsync(
            string title, string orderStatus, DateOnly on, string jobType = "one_time", string visitStatus = "completed", int hour = 9)
        {
            var seeded = await database.SeedOrderAsync(
                world,
                user,
                status: orderStatus,
                title: title,
                jobType: jobType,
                frequency: jobType == "recurring" ? "weekly" : null,
                count: jobType == "recurring" ? (short)4 : null,
                visitStatus: visitStatus,
                start: At(on, hour),
                end: At(on, hour + 1));

            if (visitStatus == "completed")
            {
                await SetStartedAsync(seeded.Visit, At(on, hour));
                await SetCompletedAsync(seeded.Visit, At(on, hour + 1));
            }

            return seeded;
        }

        var fixedOnce = await CompletedAsync("Fixed once", "completed", day);
        var twice = await CompletedAsync("Two visits", "completed", day.AddDays(-2));
        var secondVisit = await database.SeedVisitAsync(world.Org, twice.Order, 2, "completed", At(day.AddDays(-1), 9), At(day.AddDays(-1), 10));
        await SetCompletedAsync(secondVisit, At(day.AddDays(-1), 10));
        var corrected = await CompletedAsync("Corrected", "approved_for_billing", day.AddDays(-3));
        await HistoryAsync(corrected.Visit, "in_progress", "needs_correction", At(day.AddDays(-3), 11));
        await CompletedAsync("Recurring", "completed", day, jobType: "recurring", hour: 11);
        var old = await CompletedAsync("Old job", "completed", day.AddDays(-15));
        await database.ExecuteAsync("UPDATE work_orders SET service_category_id = @c WHERE id = @w", ("c", electrical), ("w", old.Order));
        await CompletedAsync("Pending job", "in_progress", day, visitStatus: "scheduled", hour: 14);
        await database.AssignAsync(fixedOnce.Visit, ann, user);
        await ReviewAsync(world.Org, fixedOnce.Order, 5, At(day, 12), ann);
        await ReviewAsync(world.Org, twice.Order, 4, At(day.AddDays(-2), 12));
        await ReviewAsync(world.Org, old.Order, 3, At(day.AddDays(-15), 12));
        await ReviewAsync(world.Org, corrected.Order, 1, At(day.AddDays(-40), 12));

        var week = await GetAsync(host, owner, $"/operations-dashboard/performance?date={Iso(day)}&period=7");
        Assert.Equal(Iso(day.AddDays(-6)), week["from"]!.GetValue<string>());
        Assert.Equal(Iso(day), week["to"]!.GetValue<string>());
        Assert.Equal([5, 6], new[] { "completed", "total" }.Select(key => week["jobsCompleted"]![key]!.GetValue<int>()).ToArray());
        Assert.Equal([1, 3], new[] { "fixed", "total" }.Select(key => week["firstTimeFix"]![key]!.GetValue<int>()).ToArray());
        Assert.Equal(4.5m, week["rating"]!["average"]!.GetValue<decimal>());
        Assert.Equal(2, week["rating"]!["count"]!.GetValue<int>());
        Assert.Equal([0, 0, 0, 1, 1, 1, 2], week["daily"]!.AsArray().Select(item => item!["completed"]!.GetValue<int>()).ToArray());
        Assert.Equal(Iso(day.AddDays(-6)), week["daily"]![0]!["date"]!.GetValue<string>());

        var month = await GetAsync(host, owner, $"/operations-dashboard/performance?date={Iso(day)}&period=30");
        Assert.Equal([6, 7], new[] { "completed", "total" }.Select(key => month["jobsCompleted"]![key]!.GetValue<int>()).ToArray());
        Assert.Equal([2, 4], new[] { "fixed", "total" }.Select(key => month["firstTimeFix"]![key]!.GetValue<int>()).ToArray());
        Assert.Equal(4.0m, month["rating"]!["average"]!.GetValue<decimal>());
        Assert.Equal(3, month["rating"]!["count"]!.GetValue<int>());
        Assert.Equal(30, month["daily"]!.AsArray().Count);

        var byTechnician = await GetAsync(host, owner, $"/operations-dashboard/performance?date={Iso(day)}&period=30&technicianId={ann}");
        Assert.Equal([1, 1], new[] { "completed", "total" }.Select(key => byTechnician["jobsCompleted"]![key]!.GetValue<int>()).ToArray());
        Assert.Equal([1, 1], new[] { "fixed", "total" }.Select(key => byTechnician["firstTimeFix"]![key]!.GetValue<int>()).ToArray());
        Assert.Equal(5.0m, byTechnician["rating"]!["average"]!.GetValue<decimal>());

        var byCategory = await GetAsync(host, owner, $"/operations-dashboard/performance?date={Iso(day)}&period=30&categoryId={electrical}");
        Assert.Equal([1, 1], new[] { "completed", "total" }.Select(key => byCategory["jobsCompleted"]![key]!.GetValue<int>()).ToArray());
        Assert.Equal(3.0m, byCategory["rating"]!["average"]!.GetValue<decimal>());

        var empty = await GetAsync(host, owner, $"/operations-dashboard/performance?date={Iso(day.AddDays(200))}&period=7");
        Assert.Null(empty["rating"]!["average"]);
        Assert.Equal(0, empty["firstTimeFix"]!["total"]!.GetValue<int>());
    }

    // AC-07, AC-10, AC-12: today uses now as the reference and shows live status; a future day has no delays or attention.
    [Fact]
    public async Task Dashboard_TodayAndFutureDay_UseTheReferenceAndLiveStatus()
    {
        var world = await database.SeedWorldAsync();
        await using var host = RequestsHost.Create(database);
        var (owner, ownerMember) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var user = ownerMember.UserId;
        var ann = await database.SeedAvailableTechAsync(world.Org, world.BranchA, "Ann", "Alpha");
        var today = Today();
        var future = Today(3);

        var travelling = await database.SeedOrderAsync(world, user, status: "in_progress", title: "Travel job", visitStatus: "on_the_way", start: At(today, 0, 30), end: At(today, 1, 30));
        await database.AssignAsync(travelling.Visit, ann, user);
        await database.SeedOrderAsync(world, user, status: "scheduled", title: "Future job", visitStatus: "scheduled", start: At(future, 9), end: At(future, 10));

        var now = await GetAsync(host, owner, $"/operations-dashboard?date={Iso(today)}");
        Assert.InRange((DateTimeOffset.UtcNow - now["reference"]!.GetValue<DateTimeOffset>()).Duration(), TimeSpan.Zero, TimeSpan.FromMinutes(2));
        Assert.Equal("5551234567", now["visits"]![0]!["customerPhone"]!.GetValue<string>());
        Assert.Equal("on_job", now["technicians"]![0]!["currentStatus"]!.GetValue<string>());

        var next = await GetAsync(host, owner, $"/operations-dashboard?date={Iso(future)}");
        Assert.Equal(At(future, 0), next["reference"]!.GetValue<DateTimeOffset>());
        Assert.Equal(["Future job"], Titles(next));
        Assert.False(next["visits"]![0]!["isDelayed"]!.GetValue<bool>());
        Assert.False(next["visits"]![0]!["isAtRisk"]!.GetValue<bool>());
        Assert.Empty(next["attention"]!.AsArray());
        Assert.Equal(0, next["kpis"]!["atRiskOrDelayed"]!.GetValue<int>());
        Assert.Null(next["technicians"]![0]!["currentStatus"]);
        Assert.Equal(540, next["technicians"]![0]!["availableMinutes"]!.GetValue<int>());
    }

    // AC-02, AC-03: 403 for the other roles, Dispatcher branch scope, cross-organization 404 identical to unknown, no foreign rows.
    [Fact]
    public async Task Access_RolesScopeAndTenantIsolation_Hold()
    {
        var world = await database.SeedWorldAsync();
        var foreign = await database.SeedWorldAsync();
        await using var host = RequestsHost.Create(database);
        var (owner, ownerMember) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var (manager, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OperationsManagerRoleId);
        var (dispatcher, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.DispatcherRoleId, "Dee", "Spatch", world.BranchA);
        var (foreignOwner, foreignMember) = await host.SignInAsync(database, foreign.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var day = Today(-1);

        var techA = await database.SeedAvailableTechAsync(world.Org, world.BranchA, "Ann", "Alpha");
        var techB = await database.SeedAvailableTechAsync(world.Org, world.BranchB, "Bob", "Bravo");
        var foreignTech = await database.SeedAvailableTechAsync(foreign.Org, foreign.BranchA, "Fay", "Foreign");
        var orderA = await database.SeedOrderAsync(world, ownerMember.UserId, status: "scheduled", title: "Alpha job", visitStatus: "assigned", start: At(day, 9), end: At(day, 10));
        var orderB = await database.SeedOrderAsync(world, ownerMember.UserId, branch: world.BranchB, status: "scheduled", title: "Bravo job", visitStatus: "assigned", start: At(day, 9), end: At(day, 10));
        await database.SeedOrderAsync(foreign, foreignMember.UserId, status: "scheduled", title: "Foreign job", visitStatus: "assigned", start: At(day, 9), end: At(day, 10));
        await database.AssignAsync(orderA.Visit, techA, ownerMember.UserId);
        await database.AssignAsync(orderB.Visit, techB, ownerMember.UserId);

        string[] paths =
        [
            "/operations-dashboard/options",
            $"/operations-dashboard?date={Iso(day)}",
            $"/operations-dashboard/performance?date={Iso(day)}&period=7",
        ];

        // Viewer, Technician and Accounting are forbidden; Owner and Operations Manager read.
        foreach (var role in new[]
        {
            CompanySettingsDatabaseFixture.ViewerRoleId,
            CompanySettingsDatabaseFixture.TechnicianRoleId,
            CompanySettingsDatabaseFixture.AccountingRoleId,
        })
        {
            var (cookie, _) = await host.SignInAsync(database, world.Org, role);

            foreach (var path in paths)
            {
                Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(HttpMethod.Get, path, cookie)).StatusCode);
            }
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await host.SendAsync(HttpMethod.Get, paths[1], null)).StatusCode);

        foreach (var path in paths)
        {
            Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(HttpMethod.Get, path, manager)).StatusCode);
        }

        // Dispatcher: All is limited to branch A everywhere; branch B and its technician are a 404.
        var options = await GetAsync(host, dispatcher, paths[0]);
        Assert.Equal([world.BranchA.ToString()], Ids(options["branches"]!));
        Assert.Equal([techA.ToString()], Ids(options["technicians"]!));

        var scoped = await GetAsync(host, dispatcher, paths[1]);
        Assert.Equal(["Alpha job"], Titles(scoped));
        Assert.Equal([techA.ToString()], Ids(scoped["technicians"]!));
        Assert.Equal(
            ["Alpha job", "Bravo job"],
            Titles(await GetAsync(host, owner, paths[1])).Order().ToArray());

        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Get, $"{paths[1]}&branchId={world.BranchB}", dispatcher)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Get, $"{paths[1]}&technicianId={techB}", dispatcher)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Get, $"{paths[2]}&branchId={world.BranchB}", dispatcher)).StatusCode);

        // Another organization: the same 404 as an unknown id, and nothing of the foreign organization is returned.
        var missing = await host.SendAsync(HttpMethod.Get, $"{paths[1]}&branchId={Guid.NewGuid()}", owner);
        var crossBranch = await host.SendAsync(HttpMethod.Get, $"{paths[1]}&branchId={foreign.BranchA}", owner);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, crossBranch.StatusCode);
        Assert.Equal(await WithoutTraceAsync(missing), await WithoutTraceAsync(crossBranch));
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Get, $"{paths[1]}&technicianId={foreignTech}", owner)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Get, $"{paths[2]}&branchId={foreign.BranchA}", owner)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Get, $"{paths[1]}&branchId={world.BranchA}", foreignOwner)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Get, $"{paths[1]}&technicianId={techA}", foreignOwner)).StatusCode);
        await DispatchSeed.ProblemAsync(
            await host.SendAsync(HttpMethod.Get, $"{paths[1]}&categoryId={foreign.Category}", owner), HttpStatusCode.BadRequest, errorKey: "categoryId");

        var foreignView = await GetAsync(host, foreignOwner, paths[1]);
        Assert.Equal(["Foreign job"], Titles(foreignView));
        Assert.Equal([foreignTech.ToString()], Ids(foreignView["technicians"]!));
        Assert.DoesNotContain("Alpha job", foreignView.ToJsonString());
        Assert.DoesNotContain("Foreign job", (await GetAsync(host, owner, paths[1])).ToJsonString());
        Assert.DoesNotContain(foreign.BranchA.ToString(), (await GetAsync(host, owner, paths[0])).ToJsonString());
    }

    private static async Task<string> WithoutTraceAsync(HttpResponseMessage response)
    {
        var problem = (JsonObject)await RequestsHost.ReadAsync(response);
        problem.Remove("traceId");

        return problem.ToJsonString();
    }
}
