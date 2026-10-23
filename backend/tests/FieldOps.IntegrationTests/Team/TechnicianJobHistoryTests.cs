using System.Net;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.Customers;

namespace FieldOps.IntegrationTests.Team;

internal static class JobHistorySeed
{
    public static async Task<Guid> SeedPropertyAsync(
        this CompanySettingsDatabaseFixture db, Guid organizationId, string name = "Maple Plaza", string city = "Springfield")
    {
        var id = Guid.NewGuid();

        await db.ExecuteAsync(
            """
            SET session_replication_role = replica;
            INSERT INTO properties (id, organization_id, customer_id, name, address_line1, city, country_code)
            VALUES (@id, @org, gen_random_uuid(), @name, '1 Main St', @city, 'US');
            """,
            ("id", id),
            ("org", organizationId),
            ("name", name),
            ("city", city));

        return id;
    }

    /// <summary>A work order and its first visit; foreign keys to unrelated parents are bypassed (throwaway container).</summary>
    public static async Task<(Guid WorkOrderId, Guid VisitId)> SeedHistoryVisitAsync(
        this CompanySettingsDatabaseFixture db,
        Guid organizationId,
        Guid userId,
        Guid branchId,
        Guid propertyId,
        string status,
        string title,
        long number,
        DateTimeOffset? scheduledStart = null,
        DateTimeOffset? startedAt = null,
        DateTimeOffset? completedAt = null,
        int pauseSeconds = 0,
        Guid? workOrderId = null,
        int visitNumber = 1)
    {
        var visitId = Guid.NewGuid();
        var order = workOrderId ?? Guid.NewGuid();

        // status is a test-controlled constant, never user input.
        await db.ExecuteAsync(
            $"""
            SET session_replication_role = replica;
            INSERT INTO work_orders (id, organization_id, branch_id, work_order_number, quote_version_id, customer_id, property_id, title, status, scope_snapshot, created_by_user_id)
            SELECT @wo, @org, @branch, @number, gen_random_uuid(), gen_random_uuid(), @property, @title, 'scheduled', 'Scope', @user
            WHERE NOT EXISTS (SELECT 1 FROM work_orders WHERE id = @wo);
            INSERT INTO visits (id, organization_id, work_order_id, visit_number, status, scheduled_start, scheduled_end, actual_started_at, actual_completed_at, pause_seconds)
            VALUES (@visit, @org, @wo, @vn, '{status}', @start, @end, @started, @completed, @pause);
            """,
            ("wo", order),
            ("visit", visitId),
            ("org", organizationId),
            ("branch", branchId),
            ("number", number),
            ("property", propertyId),
            ("title", title),
            ("user", userId),
            ("vn", visitNumber),
            ("start", scheduledStart),
            ("end", scheduledStart?.AddHours(1)),
            ("started", startedAt),
            ("completed", completedAt),
            ("pause", pauseSeconds));

        return (order, visitId);
    }

    public static Task AssignHistoryAsync(
        this CompanySettingsDatabaseFixture db,
        Guid visitId,
        Guid technicianId,
        Guid userId,
        DateTimeOffset? assignedAt = null,
        DateTimeOffset? unassignedAt = null) =>
        db.ExecuteAsync(
            """
            SET session_replication_role = replica;
            INSERT INTO visit_assignments (id, visit_id, technician_id, assigned_by_user_id, assigned_at, unassigned_at)
            VALUES (gen_random_uuid(), @visit, @tech, @user, COALESCE(@assigned, now()), @unassigned);
            """,
            ("visit", visitId),
            ("tech", technicianId),
            ("user", userId),
            ("assigned", assignedAt),
            ("unassigned", unassignedAt));

    public static Task LogHistoryAsync(
        this CompanySettingsDatabaseFixture db,
        Guid visitId,
        Guid technicianId,
        string type,
        DateTimeOffset startedAt,
        DateTimeOffset? endedAt) =>
        db.ExecuteAsync(
            """
            SET session_replication_role = replica;
            INSERT INTO visit_time_entries (id, visit_id, technician_id, started_at, ended_at, entry_type)
            VALUES (gen_random_uuid(), @visit, @tech, @started, @ended, @type);
            """,
            ("visit", visitId),
            ("tech", technicianId),
            ("started", startedAt),
            ("ended", endedAt),
            ("type", type));
}

/// <summary>technician-profile-overview-history backend evidence: AC-04, AC-09, AC-11 to AC-18.</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class TechnicianJobHistoryTests(CompanySettingsDatabaseFixture database)
{
    private const short Owner = CompanySettingsDatabaseFixture.OwnerRoleId;
    private const short Dispatcher = CompanySettingsDatabaseFixture.DispatcherRoleId;
    private const short Technician = CompanySettingsDatabaseFixture.TechnicianRoleId;
    private const short Accounting = CompanySettingsDatabaseFixture.AccountingRoleId;
    private const short Viewer = CompanySettingsDatabaseFixture.ViewerRoleId;

    private static string[] Titles(JsonNode page) =>
        [.. page["items"]!.AsArray().Select(item => item!["title"]!.GetValue<string>())];

    // AC-11, AC-12, AC-14, AC-16: one row per visit, outcome precedence, reassignment kept, time logged (work + travel,
    // open entry, own entries only) and period metrics independent of search and status.
    [Fact]
    public async Task JobHistory_ClassifiesParticipationLogsTimeAndKeepsMetricsIndependentOfFilters()
    {
        var org = await database.SeedOrganizationAsync();
        var branch = await database.SeedBranchAsync(org, "Alpha", isMain: true);
        var property = await database.SeedPropertyAsync(org);
        var other = await database.SeedPropertyAsync(org, "Oak Tower", "Rivertown");

        await using var host = CustomerHost.Create(database);
        var owner = await database.ActorAsync(host, org, Owner);
        var user = owner.Member.UserId;
        var tech = await database.SeedTechAsync(org, branch.Id, "Tia", "Tech");
        var peer = await database.SeedTechAsync(org, branch.Id, "Pete", "Peer");
        var now = DateTimeOffset.UtcNow;

        // Completed, on time, with own work + travel + a pause entry and a peer entry on the same visit.
        var start1 = now.AddDays(-5);
        var (order1, v1) = await database.SeedHistoryVisitAsync(
            org, user, branch.Id, property, "completed", "Alpha install", 101, start1, start1.AddMinutes(10), start1.AddMinutes(110), 600);
        await database.AssignHistoryAsync(v1, tech, user);
        await database.LogHistoryAsync(v1, tech, "work", start1, start1.AddMinutes(60));
        await database.LogHistoryAsync(v1, tech, "travel", start1.AddMinutes(-40), start1.AddMinutes(-10));
        await database.LogHistoryAsync(v1, tech, "pause", start1.AddMinutes(20), start1.AddMinutes(30));
        await database.LogHistoryAsync(v1, peer, "work", start1, start1.AddMinutes(120));

        // Second visit of the same work order, cancelled.
        var (_, v1b) = await database.SeedHistoryVisitAsync(
            org, user, branch.Id, property, "cancelled", "Alpha install", 101, now.AddDays(-4), workOrderId: order1, visitNumber: 2);
        await database.AssignHistoryAsync(v1b, tech, user);

        // Reassigned: unassigned and replaced after the technician logged time.
        var (_, v2) = await database.SeedHistoryVisitAsync(
            org, user, branch.Id, other, "completed", "Beta repair", 102, now.AddDays(-3), now.AddDays(-3), now.AddDays(-3).AddHours(1));
        await database.AssignHistoryAsync(v2, tech, user, now.AddDays(-9), now.AddDays(-3));
        await database.AssignHistoryAsync(v2, peer, user);
        await database.LogHistoryAsync(v2, tech, "work", now.AddDays(-3), now.AddDays(-3).AddMinutes(45));

        // Assigned twice (one closed, one active): a single Completed row; started late.
        var start3 = now.AddDays(-2);
        var (_, v3) = await database.SeedHistoryVisitAsync(
            org, user, branch.Id, property, "approved", "Gamma service", 103, start3, start3.AddMinutes(30), start3.AddMinutes(90));
        await database.AssignHistoryAsync(v3, tech, user, now.AddDays(-8), now.AddDays(-7));
        await database.AssignHistoryAsync(v3, tech, user, now.AddDays(-6));

        // Time entry only, no assignment row: Reassigned.
        var (_, v4) = await database.SeedHistoryVisitAsync(
            org, user, branch.Id, property, "needs_correction", "Delta fix", 104, now.AddDays(-1));
        await database.LogHistoryAsync(v4, tech, "work", now.AddDays(-1), now.AddDays(-1).AddMinutes(20));

        // Still assigned and unfinished: excluded, with or without logged time.
        var (_, v5) = await database.SeedHistoryVisitAsync(org, user, branch.Id, property, "scheduled", "Open one", 105, now.AddDays(1));
        await database.AssignHistoryAsync(v5, tech, user);
        var (_, v6) = await database.SeedHistoryVisitAsync(org, user, branch.Id, property, "in_progress", "Open two", 106, now.AddHours(-1));
        await database.AssignHistoryAsync(v6, tech, user);
        await database.LogHistoryAsync(v6, tech, "work", now.AddMinutes(-30), null);

        // Completed without actual start, with an open work entry counted to now.
        var (_, v7) = await database.SeedHistoryVisitAsync(org, user, branch.Id, property, "completed", "Eta open", 107, now.AddHours(-3));
        await database.AssignHistoryAsync(v7, tech, user);
        await database.LogHistoryAsync(v7, tech, "work", now.AddMinutes(-30), null);

        var page = await CustomerHost.ReadAsync(
            await host.SendAsync(HttpMethod.Get, $"/team/technicians/{tech}/job-history?period=all", owner.Cookie));

        Assert.Equal("UTC", page["timezone"]!.GetValue<string>());
        Assert.Equal(6, page["total"]!.GetValue<int>());
        Assert.Equal(
            ["Eta open", "Delta fix", "Gamma service", "Beta repair", "Alpha install", "Alpha install"],
            Titles(page));
        Assert.Null(page["period"]!["from"]);

        var rows = page["items"]!.AsArray().ToDictionary(
            item => (item!["title"]!.GetValue<string>(), item["visitNumber"]!.GetValue<int>()), item => item!);

        Assert.Equal(6, rows.Count);
        Assert.Equal("completed", rows[("Alpha install", 1)]["outcome"]!.GetValue<string>());
        Assert.Equal(90, rows[("Alpha install", 1)]["loggedMinutes"]!.GetValue<int>());
        Assert.Equal(2, rows[("Alpha install", 1)]["visitCount"]!.GetValue<int>());
        Assert.Equal("cancelled", rows[("Alpha install", 2)]["outcome"]!.GetValue<string>());
        Assert.Equal("reassigned", rows[("Beta repair", 1)]["outcome"]!.GetValue<string>());
        Assert.Equal(45, rows[("Beta repair", 1)]["loggedMinutes"]!.GetValue<int>());
        Assert.Equal("Oak Tower", rows[("Beta repair", 1)]["property"]!["name"]!.GetValue<string>());
        Assert.Equal("Rivertown", rows[("Beta repair", 1)]["property"]!["city"]!.GetValue<string>());
        Assert.Equal("completed", rows[("Gamma service", 1)]["outcome"]!.GetValue<string>());
        Assert.Equal("reassigned", rows[("Delta fix", 1)]["outcome"]!.GetValue<string>());
        Assert.Equal(20, rows[("Delta fix", 1)]["loggedMinutes"]!.GetValue<int>());
        Assert.Equal(30, rows[("Eta open", 1)]["loggedMinutes"]!.GetValue<int>());
        Assert.Equal("WO-107", rows[("Eta open", 1)]["displayNumber"]!.GetValue<string>());
        Assert.True(rows[("Eta open", 1)]["canOpen"]!.GetValue<bool>());
        Assert.DoesNotContain("Open one", Titles(page));
        Assert.DoesNotContain("Open two", Titles(page));

        // BR-16: completed rows are v1, v3, v7. On time: v1 yes, v3 late (v7 has no start) -> 50 %.
        // Hours logged sum every row (90 + 45 + 20 + 30). Average duration: (90 + 60) / 2.
        async Task<JsonNode> Summary(string query) =>
            (await CustomerHost.ReadAsync(
                await host.SendAsync(HttpMethod.Get, $"/team/technicians/{tech}/job-history?period=all&{query}", owner.Cookie)))["summary"]!;

        var summary = page["summary"]!;
        Assert.Equal(3, summary["completedJobs"]!.GetValue<int>());
        Assert.Equal(50, summary["onTimePercent"]!.GetValue<int>());
        Assert.Equal(185, summary["loggedMinutes"]!.GetValue<int>());
        Assert.Equal(75, summary["avgDurationMinutes"]!.GetValue<int>());

        var filtered = await CustomerHost.ReadAsync(await host.SendAsync(
            HttpMethod.Get, $"/team/technicians/{tech}/job-history?period=all&status=reassigned&search=beta", owner.Cookie));
        Assert.Equal(["Beta repair"], Titles(filtered));
        Assert.Equal(1, filtered["total"]!.GetValue<int>());
        Assert.Equal(summary.ToJsonString(), filtered["summary"]!.ToJsonString());
        Assert.Equal(summary.ToJsonString(), (await Summary("status=cancelled&sort=oldest&page=9")).ToJsonString());

        // A period with no completed rows reports zero and no percentage or average.
        var idle = await database.SeedTechAsync(org, branch.Id, "Ida", "Idle");
        var empty = await CustomerHost.ReadAsync(await host.SendAsync(
            HttpMethod.Get, $"/team/technicians/{idle}/job-history?period=30d", owner.Cookie));
        Assert.Equal(0, empty["total"]!.GetValue<int>());
        Assert.Equal(0, empty["summary"]!["completedJobs"]!.GetValue<int>());
        Assert.Null(empty["summary"]!["onTimePercent"]);
        Assert.Null(empty["summary"]!["avgDurationMinutes"]);
    }

    // AC-15: search, status, sort and page sizes combine; stable tie-breakers; a page past the last; invalid queries.
    [Fact]
    public async Task JobHistory_FiltersSortsAndPagesInStableOrderAndRejectsInvalidQueries()
    {
        var org = await database.SeedOrganizationAsync();
        var branch = await database.SeedBranchAsync(org, isMain: true);
        var maple = await database.SeedPropertyAsync(org, "Maple Plaza", "Springfield");
        var oak = await database.SeedPropertyAsync(org, "Oak Tower", "Rivertown");

        await using var host = CustomerHost.Create(database);
        var owner = await database.ActorAsync(host, org, Owner);
        var tech = await database.SeedTechAsync(org, branch.Id, "Tia", "Tech");
        var baseTime = DateTimeOffset.UtcNow.AddDays(-40);

        for (var i = 1; i <= 27; i++)
        {
            // Pairs share the same scheduled start so the visit id decides the order.
            var (_, visit) = await database.SeedHistoryVisitAsync(
                org,
                owner.Member.UserId,
                branch.Id,
                i % 3 == 0 ? oak : maple,
                i % 9 == 0 ? "cancelled" : "completed",
                $"Bulk {i:00}",
                2000 + i,
                baseTime.AddDays(i / 2));
            await database.AssignHistoryAsync(visit, tech, owner.Member.UserId);
        }

        async Task<JsonNode> Get(string query) =>
            await CustomerHost.ReadAsync(await host.SendAsync(HttpMethod.Get, $"/team/technicians/{tech}/job-history?{query}", owner.Cookie));

        var all = await Get("pageSize=50");
        Assert.Equal((27, 1, 50), (all["total"]!.GetValue<int>(), all["page"]!.GetValue<int>(), all["pageSize"]!.GetValue<int>()));
        Assert.Equal(27, Titles(all).Distinct().Count());

        // Default is 10 per page, newest first; paging reproduces the single page without gaps or repeats.
        var paged = new List<string>();

        for (var number = 1; number <= 3; number++)
        {
            var slice = await Get($"page={number}");
            Assert.Equal(10, slice["pageSize"]!.GetValue<int>());
            paged.AddRange(Titles(slice));
        }

        Assert.Equal(Titles(all), paged);
        Assert.Equal(Titles(all).Reverse(), Titles(await Get("sort=oldest&pageSize=50")));
        Assert.Equal(2, (await Get("pageSize=25&page=2"))["items"]!.AsArray().Count);

        var beyond = await Get("page=4");
        Assert.Empty(beyond["items"]!.AsArray());
        Assert.Equal(27, beyond["total"]!.GetValue<int>());
        Assert.Empty((await Get("page=2000000000&pageSize=50"))["items"]!.AsArray());

        // Search: display number, title, property name and city, any case; wildcards are literal.
        Assert.Equal(["Bulk 05"], Titles(await Get("search=wo-2005")));
        Assert.Equal(["Bulk 12"], Titles(await Get("search=BULK%2012")));
        Assert.Equal(9, (await Get("search=oak%20TOWER"))["total"]!.GetValue<int>());
        Assert.Equal(18, (await Get("search=springfield"))["total"]!.GetValue<int>());
        Assert.Equal(0, (await Get("search=%25"))["total"]!.GetValue<int>());
        Assert.Equal(3, (await Get("status=cancelled"))["total"]!.GetValue<int>());
        Assert.Equal(3, (await Get("status=cancelled&search=oak"))["total"]!.GetValue<int>());
        Assert.Equal(24, (await Get("status=completed"))["total"]!.GetValue<int>());

        foreach (var (query, field) in new[]
        {
            ("status=bogus", "status"),
            ("period=1y", "period"),
            ("sort=up", "sort"),
            ("page=0", "page"),
            ("page=abc", "page"),
            ("pageSize=20", "pageSize"),
            ("pageSize=abc", "pageSize"),
            ($"search={new string('a', 101)}", "search"),
        })
        {
            var response = await host.SendAsync(HttpMethod.Get, $"/team/technicians/{tech}/job-history?{query}", owner.Cookie);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.NotNull((await CustomerHost.ReadAsync(response))["errors"]![field]);
        }
    }

    // AC-13: participation date fallbacks and inclusive local-day period bounds in a non-UTC home-branch zone.
    [Fact]
    public async Task JobHistory_AppliesParticipationDateFallbacksAndLocalDayPeriodBounds()
    {
        var org = await database.SeedOrganizationAsync();
        var branch = await database.SeedBranchAsync(org, isMain: true);
        await database.ExecuteAsync("UPDATE branches SET timezone = 'America/Chicago' WHERE id = @b", ("b", branch.Id));
        var property = await database.SeedPropertyAsync(org);

        await using var host = CustomerHost.Create(database);
        var owner = await database.ActorAsync(host, org, Owner);
        var user = owner.Member.UserId;
        var tech = await database.SeedTechAsync(org, branch.Id, "Tia", "Tech");

        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).DateTime);
        var twelveMonths = today.AddMonths(-12).AddDays(1);

        DateTimeOffset Local(DateOnly date, int hour, int minute = 0)
        {
            var local = date.ToDateTime(new TimeOnly(hour, minute), DateTimeKind.Unspecified);

            return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
        }

        var number = 3000L;

        async Task<Guid> Add(string title, DateTimeOffset? scheduled, DateTimeOffset? started = null)
        {
            var (_, visit) = await database.SeedHistoryVisitAsync(
                org, user, branch.Id, property, "completed", title, number++, scheduled, started, started?.AddHours(1));
            await database.AssignHistoryAsync(visit, tech, user, DateTimeOffset.UtcNow.AddYears(-3));

            return visit;
        }

        await Add("d-29 early", Local(today.AddDays(-29), 0, 30));
        await Add("d-30 late", Local(today.AddDays(-30), 23, 30));
        await Add("d-89", Local(today.AddDays(-89), 12));
        await Add("d-90", Local(today.AddDays(-90), 12));
        await Add("m12 first day", Local(twelveMonths, 12));
        await Add("m12 minus one", Local(twelveMonths.AddDays(-1), 12));
        await Add("tomorrow", Local(today.AddDays(1), 8));
        await Add("today", Local(today, 1));

        // Fallbacks: actual start, earliest own time entry, latest assignment.
        await Add("fallback started", null, Local(today.AddDays(-10), 9));
        var viaEntry = await Add("fallback entry", null);
        await database.LogHistoryAsync(viaEntry, tech, "work", Local(today.AddDays(-20), 22, 30), Local(today.AddDays(-20), 23));
        var (_, viaAssignment) = await database.SeedHistoryVisitAsync(org, user, branch.Id, property, "completed", "fallback assigned", number++);
        await database.AssignHistoryAsync(viaAssignment, tech, user, Local(today.AddDays(-50), 10));
        await database.AssignHistoryAsync(viaAssignment, tech, user, Local(today.AddDays(-45), 10), Local(today.AddDays(-44), 10));

        async Task<JsonNode> Get(string period) =>
            await CustomerHost.ReadAsync(await host.SendAsync(HttpMethod.Get, $"/team/technicians/{tech}/job-history?period={period}&pageSize=50", owner.Cookie));

        var days30 = await Get("30d");
        Assert.Equal(
            ["today", "fallback started", "fallback entry", "d-29 early"],
            Titles(days30));
        Assert.Equal(today.AddDays(-29).ToString("yyyy-MM-dd"), days30["period"]!["from"]!.GetValue<string>());
        Assert.Equal(today.ToString("yyyy-MM-dd"), days30["period"]!["to"]!.GetValue<string>());
        Assert.Equal(today.ToString("yyyy-MM-dd"), days30["items"]![0]!["participationDate"]!.GetValue<string>());

        var entryRow = days30["items"]!.AsArray().Single(item => item!["title"]!.GetValue<string>() == "fallback entry")!;
        Assert.Equal(today.AddDays(-20).ToString("yyyy-MM-dd"), entryRow["participationDate"]!.GetValue<string>());

        Assert.Equal(
            ["today", "fallback started", "fallback entry", "d-29 early", "d-30 late", "fallback assigned", "d-89"],
            Titles(await Get("90d")));
        Assert.Equal(
            ["today", "fallback started", "fallback entry", "d-29 early", "d-30 late", "fallback assigned", "d-89", "d-90", "m12 first day"],
            Titles(await Get("12m")));

        var everything = await Get("all");
        Assert.Equal(10, everything["total"]!.GetValue<int>());
        Assert.DoesNotContain("tomorrow", Titles(everything));
        Assert.Null(everything["period"]!["from"]);
        Assert.Contains("m12 minus one", Titles(everything));
    }

    // AC-04, AC-09: additive detail fields keep the existing ones and select the earliest qualifying time off.
    [Fact]
    public async Task Detail_ReturnsBranchHoursFlagVisitStatusAndEarliestUpcomingTimeOff()
    {
        var org = await database.SeedOrganizationAsync();
        var branch = await database.SeedBranchAsync(org, isMain: true);

        await using var host = CustomerHost.Create(database);
        var owner = await database.ActorAsync(host, org, Owner);
        var tech = await database.SeedTechAsync(org, branch.Id, "Tia", "Tech");
        var now = DateTimeOffset.UtcNow;

        async Task<JsonNode> Detail() =>
            await CustomerHost.ReadAsync(await host.SendAsync(HttpMethod.Get, $"/team/technicians/{tech}", owner.Cookie));

        var first = await Detail();
        Assert.True(first["usesBranchHours"]!.GetValue<bool>());
        Assert.Null(first["upcomingTimeOff"]);
        Assert.Null(first["today"]!["currentJob"]);
        Assert.Equal("Tia", first["firstName"]!.GetValue<string>());

        await database.SeedSlotAsync(tech, 1, "09:00", "17:00");
        Assert.False((await Detail())["usesBranchHours"]!.GetValue<bool>());

        var visit = await database.SeedAssignmentAsync(
            org, owner.Member.UserId, branch.Id, tech, "on_the_way", now.AddMinutes(-5), now.AddMinutes(55));
        Assert.Equal("on_the_way", (await Detail())["today"]!["currentJob"]!["visitStatus"]!.GetValue<string>());
        Assert.NotNull((await Detail())["today"]!["currentJob"]!["label"]);

        await database.ExecuteAsync("UPDATE visits SET status = 'paused' WHERE id = @v", ("v", visit));
        Assert.Equal("paused", (await Detail())["today"]!["currentJob"]!["visitStatus"]!.GetValue<string>());

        Task Exception(DateTimeOffset start, DateTimeOffset end, string reason, bool available = false, string status = "active") =>
            database.ExecuteAsync(
                "INSERT INTO technician_exceptions (technician_id, starts_at, ends_at, is_available, reason, status) VALUES (@t, @s, @e, @a, @r, @st)",
                ("t", tech), ("s", start), ("e", end), ("a", available), ("r", reason), ("st", status));

        var midnight = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        await Exception(now.AddDays(-5), now.AddDays(-4), "Past");
        await Exception(now.AddDays(1), now.AddDays(1).AddHours(2), "Cancelled", status: "cancelled");
        await Exception(now.AddDays(1), now.AddDays(1).AddHours(3), "Extended", available: true);
        await Exception(midnight.AddDays(3), midnight.AddDays(4), "Vacation day");
        await Exception(now.AddHours(-1), now.AddHours(2), "Dentist");

        var partial = (await Detail())["upcomingTimeOff"]!;
        Assert.Equal("Dentist", partial["reason"]!.GetValue<string>());
        Assert.False(partial["allDay"]!.GetValue<bool>());
        Assert.Equal(now.AddHours(-1).UtcDateTime.ToString("yyyy-MM-dd"), partial["date"]!.GetValue<string>());
        Assert.Equal(now.AddHours(-1).UtcDateTime.ToString("HH:mm"), partial["start"]!.GetValue<string>());
        Assert.Equal(now.AddHours(2).UtcDateTime.ToString("HH:mm"), partial["end"]!.GetValue<string>());

        await database.ExecuteAsync(
            "UPDATE technician_exceptions SET starts_at = @s, ends_at = @e WHERE reason = 'Dentist'",
            ("s", now.AddHours(-4)), ("e", now.AddMinutes(-1)));

        var allDay = (await Detail())["upcomingTimeOff"]!;
        Assert.Equal("Vacation day", allDay["reason"]!.GetValue<string>());
        Assert.True(allDay["allDay"]!.GetValue<bool>());
        Assert.Equal(midnight.AddDays(3).UtcDateTime.ToString("yyyy-MM-dd"), allDay["date"]!.GetValue<string>());
        Assert.Null(allDay["start"]);
        Assert.Null(allDay["end"]);

        await database.ExecuteAsync("UPDATE technician_exceptions SET status = 'cancelled' WHERE reason = 'Vacation day'");
        Assert.Null((await Detail())["upcomingTimeOff"]);
    }

    // AC-16, AC-17, AC-18: branch scope on rows and metrics, identical 404s and 403 before any read.
    [Fact]
    public async Task JobHistory_EnforcesTenantBranchScopeAndTeamViewPermission()
    {
        var org = await database.SeedOrganizationAsync();
        var foreignOrg = await database.SeedOrganizationAsync();
        var branchA = await database.SeedBranchAsync(org, "Alpha", isMain: true);
        var branchB = await database.SeedBranchAsync(org, "Bravo");
        var foreignBranch = await database.SeedBranchAsync(foreignOrg, isMain: true);
        var property = await database.SeedPropertyAsync(org);
        var foreignProperty = await database.SeedPropertyAsync(foreignOrg);

        await using var host = CustomerHost.Create(database);
        var owner = await database.ActorAsync(host, org, Owner);
        var dispatcher = await database.ActorAsync(host, org, Dispatcher, branchA.Id);
        var user = owner.Member.UserId;
        var techA = await database.SeedTechAsync(org, branchA.Id, "Ann", "Alpha");
        var techB = await database.SeedTechAsync(org, branchB.Id, "Ben", "Bravo");
        var foreignTech = await database.SeedTechAsync(foreignOrg, foreignBranch.Id, "Far", "Away");
        var now = DateTimeOffset.UtcNow;

        async Task Work(Guid orgId, Guid branchId, Guid propertyId, Guid technicianId, string title, long number)
        {
            var (_, visit) = await database.SeedHistoryVisitAsync(
                orgId, user, branchId, propertyId, "completed", title, number, now.AddDays(-2), now.AddDays(-2), now.AddDays(-2).AddHours(1));
            await database.AssignHistoryAsync(visit, technicianId, user);
            await database.LogHistoryAsync(visit, technicianId, "work", now.AddDays(-2), now.AddDays(-2).AddMinutes(30));
        }

        await Work(org, branchA.Id, property, techA, "Branch A job", 4001);
        await Work(org, branchB.Id, property, techA, "Branch B job", 4002);
        await Work(foreignOrg, foreignBranch.Id, foreignProperty, techA, "Foreign org job", 4003);
        await Work(foreignOrg, foreignBranch.Id, foreignProperty, foreignTech, "Far job", 4004);

        var ownerView = await host.SendAsync(HttpMethod.Get, $"/team/technicians/{techA}/job-history?period=all", owner.Cookie);
        Assert.True(ownerView.Headers.CacheControl?.NoStore);
        var ownerPage = await CustomerHost.ReadAsync(ownerView);
        Assert.Equal(["Branch A job", "Branch B job"], Titles(ownerPage).Order().ToArray());
        Assert.Equal(60, ownerPage["summary"]!["loggedMinutes"]!.GetValue<int>());
        Assert.Equal(2, ownerPage["summary"]!["completedJobs"]!.GetValue<int>());

        var scoped = await CustomerHost.ReadAsync(
            await host.SendAsync(HttpMethod.Get, $"/team/technicians/{techA}/job-history?period=all", dispatcher.Cookie));
        Assert.Equal(["Branch A job"], Titles(scoped));
        Assert.Equal(1, scoped["total"]!.GetValue<int>());
        Assert.Equal(1, scoped["summary"]!["completedJobs"]!.GetValue<int>());
        Assert.Equal(30, scoped["summary"]!["loggedMinutes"]!.GetValue<int>());
        Assert.True(scoped["items"]![0]!["canOpen"]!.GetValue<bool>());

        // Identical 404 for a branch-B technician (out of scope), a foreign technician and an unknown id, on both endpoints.
        var hidden = new List<(string Cookie, Guid Id)> { (dispatcher.Cookie, techB), (owner.Cookie, foreignTech), (owner.Cookie, Guid.NewGuid()) };

        foreach (var (cookie, id) in hidden)
        {
            foreach (var path in new[] { $"/team/technicians/{id}", $"/team/technicians/{id}/job-history" })
            {
                var response = await host.SendAsync(HttpMethod.Get, path, cookie);
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
                var problem = await CustomerHost.ReadAsync(response);
                Assert.Equal("This technician profile isn't available.", problem["title"]!.GetValue<string>());
                Assert.DoesNotContain("job", problem.ToJsonString(), StringComparison.OrdinalIgnoreCase);
            }
        }

        // No TeamView: 403 on both endpoints.
        foreach (var role in new[] { Technician, Accounting, Viewer })
        {
            var actor = await database.ActorAsync(host, org, role);

            foreach (var path in new[] { $"/team/technicians/{techA}", $"/team/technicians/{techA}/job-history" })
            {
                Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(HttpMethod.Get, path, actor.Cookie)).StatusCode);
            }
        }
    }
}
