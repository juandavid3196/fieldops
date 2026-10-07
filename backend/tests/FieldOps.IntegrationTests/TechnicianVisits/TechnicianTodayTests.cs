using System.Net;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.Dispatch;
using FieldOps.IntegrationTests.ServiceRequests;

namespace FieldOps.IntegrationTests.TechnicianVisits;

/// <summary>GET /technician/today: selection, time zone, order, metrics and next visit (technician-todays-jobs AC-01, AC-04 to AC-08).</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class TechnicianTodayTests(CompanySettingsDatabaseFixture database)
{
    private static readonly DateTimeOffset Noon = TechnicianVisitSeed.Utc("2026-06-10T12:00:00Z");

    public static TheoryData<string[], int, int, int, int> MetricsCases => new()
    {
        { [], 0, 0, 0, -1 },
        { ["assigned", "in_progress"], 2, 180, 0, 1 },
        { ["scheduled", "assigned"], 2, 180, 0, 0 },
        { ["completed", "approved"], 2, 180, 2, -1 },
        { ["completed", "needs_correction"], 2, 180, 1, -1 },
        { ["completed", "scheduled", "paused", "assigned", "on_the_way"], 5, 450, 1, 2 },
    };

    [Fact]
    public async Task Today_SelectionIgnoresForeignVisitsAndClientIdentifiers()
    {
        var world = await database.SeedWorldAsync();
        var foreign = await database.SeedWorldAsync();
        await using var host = TechnicianHost.Create(database);
        var me = await database.SeedTechnicianAsync(host, world);
        var other = await database.SeedTechnicianAsync(host, world, first: "Otto");
        var foreignTech = await database.SeedTechnicianAsync(host, foreign, first: "Fay");
        host.SetNow(Noon);

        var mine = await database.SeedJobAsync(world, me.Member.UserId, me.Profile, "assigned", Noon.AddHours(1), 90, "Mine", priority: 1);
        await database.SeedJobAsync(world, me.Member.UserId, other.Profile, "assigned", Noon.AddHours(2), title: "Other technician");
        var released = await database.SeedJobAsync(world, me.Member.UserId, me.Profile, "assigned", Noon.AddHours(3), title: "Released");
        await database.UnassignAsync(released.Visit);
        await database.SeedJobAsync(world, me.Member.UserId, me.Profile, "unscheduled", Noon.AddHours(4), title: "Unscheduled");
        await database.SeedJobAsync(world, me.Member.UserId, me.Profile, "cancelled", Noon.AddHours(5), title: "Cancelled");
        await database.SeedJobAsync(world, me.Member.UserId, me.Profile, "assigned", Noon.AddDays(1), title: "Tomorrow");
        await database.SeedJobAsync(foreign, foreignTech.Member.UserId, foreignTech.Profile, "assigned", Noon.AddHours(1), title: "Foreign org");
        await database.ExecuteAsync(
            "UPDATE visits SET dispatch_note = 'Gate code 1234', arrival_window_start = scheduled_start, arrival_window_end = scheduled_start + interval '2 hours' WHERE id = @v",
            ("v", mine.Visit));
        await database.ExecuteAsync(
            "INSERT INTO work_order_planned_materials (organization_id, work_order_id, description, quantity, unit, source) VALUES (@o, @w, 'Pipe', 1, 'ea', 'truck_stock'), (@o, @w, 'Glue', 2, 'ea', 'warehouse')",
            ("o", world.Org),
            ("w", mine.Order));

        var response = await host.GetAsync($"/technician/today?technicianId={other.Profile}&organizationId={foreign.Org}", me.Cookie);
        var today = await TechnicianVisitSeed.ReadAsync(response);

        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal(new[] { mine.Visit }, TechnicianVisitSeed.VisitIds(today));
        Assert.Equal("2026-06-10", today["date"]!.GetValue<string>());
        Assert.Equal("UTC", today["timezone"]!.GetValue<string>());
        Assert.Equal("Tess", today["technician"]!["firstName"]!.GetValue<string>());
        Assert.Equal("TT", today["technician"]!["initials"]!.GetValue<string>());
        Assert.Equal(mine.Visit, today["nextVisitId"]!.GetValue<Guid>());
        Assert.DoesNotContain("Gate code", today.ToJsonString());

        var visit = today["visits"]![0]!;
        Assert.Equal($"{await database.PrefixAsync(world.Org)}-{mine.Number}", visit["displayNumber"]!.GetValue<string>());
        Assert.Equal("assigned", visit["status"]!.GetValue<string>());
        Assert.Equal(1, visit["priority"]!.GetValue<int>());
        Assert.Equal("Plumbing", visit["serviceCategory"]!.GetValue<string>());
        Assert.Equal("Carla Customer", visit["customerName"]!.GetValue<string>());
        Assert.Equal(2, visit["plannedMaterialsCount"]!.GetValue<int>());
        Assert.Equal(
            await database.ScalarAsync<string>("SELECT phone FROM customer_contacts WHERE id = @c", ("c", world.Contact)),
            visit["phone"]!.GetValue<string>());
        Assert.Equal(Noon.AddHours(1), TechnicianVisitSeed.Utc(visit["start"]!.GetValue<string>()));
        Assert.Equal(Noon.AddHours(3), TechnicianVisitSeed.Utc(visit["arrivalWindowEnd"]!.GetValue<string>()));
        Assert.NotNull(visit["address"]!["line1"]);

        // The other organization's technician sees only the own organization's visit.
        var foreignToday = await TechnicianVisitSeed.ReadAsync(await host.GetAsync("/technician/today", foreignTech.Cookie));
        Assert.Single(foreignToday["visits"]!.AsArray());
        Assert.DoesNotContain("Mine", foreignToday.ToJsonString());
    }

    [Fact]
    public async Task Today_UsesBranchZoneThenOrganizationZoneAndLocalDayBoundaries()
    {
        var world = await database.SeedWorldAsync("America/New_York");
        await using var host = TechnicianHost.Create(database);
        var me = await database.SeedTechnicianAsync(host, world);
        var user = me.Member.UserId;
        await database.SetBranchTimezoneAsync(world.BranchA, "Asia/Tokyo");
        host.SetNow(TechnicianVisitSeed.Utc("2026-06-10T20:00:00Z"));

        var previousDay = await database.SeedJobAsync(world, user, me.Profile, "assigned", TechnicianVisitSeed.Utc("2026-06-10T14:30:00Z"));
        var midnight = await database.SeedJobAsync(world, user, me.Profile, "assigned", TechnicianVisitSeed.Utc("2026-06-10T15:00:00Z"));
        var lastMinute = await database.SeedJobAsync(world, user, me.Profile, "assigned", TechnicianVisitSeed.Utc("2026-06-11T14:59:00Z"));
        var nextDay = await database.SeedJobAsync(world, user, me.Profile, "assigned", TechnicianVisitSeed.Utc("2026-06-11T15:15:00Z"));

        // Branch zone (Tokyo): the local day is June 11, 00:00 to 24:00 at +09:00.
        var tokyo = await TechnicianVisitSeed.ReadAsync(await host.GetAsync("/technician/today", me.Cookie));
        Assert.Equal(new[] { midnight.Visit, lastMinute.Visit }, TechnicianVisitSeed.VisitIds(tokyo));
        Assert.Equal("2026-06-11", tokyo["date"]!.GetValue<string>());
        Assert.Equal("Asia/Tokyo", tokyo["timezone"]!.GetValue<string>());
        Assert.EndsWith("+09:00", tokyo["visits"]![0]!["start"]!.GetValue<string>());

        // No branch zone: the organization zone applies and the same instants select a different set.
        await database.SetBranchTimezoneAsync(world.BranchA, null);
        var newYork = await TechnicianVisitSeed.ReadAsync(await host.GetAsync("/technician/today", me.Cookie));
        Assert.Equal(new[] { previousDay.Visit, midnight.Visit }, TechnicianVisitSeed.VisitIds(newYork));
        Assert.Equal("2026-06-10", newYork["date"]!.GetValue<string>());
        Assert.Equal("America/New_York", newYork["timezone"]!.GetValue<string>());
        Assert.EndsWith("-04:00", newYork["visits"]![0]!["start"]!.GetValue<string>());
        Assert.DoesNotContain(nextDay.Visit, TechnicianVisitSeed.VisitIds(newYork));
    }

    [Fact]
    public async Task Today_DstTransitionDayCoversTheTwentyThreeHourLocalDay()
    {
        var world = await database.SeedWorldAsync("America/New_York");
        await using var host = TechnicianHost.Create(database);
        var me = await database.SeedTechnicianAsync(host, world);
        var user = me.Member.UserId;
        host.SetNow(TechnicianVisitSeed.Utc("2026-03-08T18:00:00Z"));

        // March 8 local runs from 05:00Z (EST) to 04:00Z the next day (EDT).
        await database.SeedJobAsync(world, user, me.Profile, "assigned", TechnicianVisitSeed.Utc("2026-03-08T04:59:00Z"));
        var first = await database.SeedJobAsync(world, user, me.Profile, "assigned", TechnicianVisitSeed.Utc("2026-03-08T05:00:00Z"));
        var afterJump = await database.SeedJobAsync(world, user, me.Profile, "assigned", TechnicianVisitSeed.Utc("2026-03-08T07:30:00Z"));
        var last = await database.SeedJobAsync(world, user, me.Profile, "assigned", TechnicianVisitSeed.Utc("2026-03-09T03:59:00Z"));
        await database.SeedJobAsync(world, user, me.Profile, "assigned", TechnicianVisitSeed.Utc("2026-03-09T04:00:00Z"));

        var today = await TechnicianVisitSeed.ReadAsync(await host.GetAsync("/technician/today", me.Cookie));

        Assert.Equal("2026-03-08", today["date"]!.GetValue<string>());
        Assert.Equal(new[] { first.Visit, afterJump.Visit, last.Visit }, TechnicianVisitSeed.VisitIds(today));
        Assert.EndsWith("-05:00", today["visits"]![0]!["start"]!.GetValue<string>());
        Assert.EndsWith("-04:00", today["visits"]![2]!["start"]!.GetValue<string>());
    }

    [Fact]
    public async Task Today_OrdersByStartThenWorkOrderNumberThenVisitNumber()
    {
        var world = await database.SeedWorldAsync();
        await using var host = TechnicianHost.Create(database);
        var me = await database.SeedTechnicianAsync(host, world);
        var user = me.Member.UserId;
        host.SetNow(Noon);

        var late = await database.SeedJobAsync(world, user, me.Profile, "assigned", Noon.AddHours(4));
        var secondOrder = await database.SeedJobAsync(world, user, me.Profile, "assigned", Noon.AddHours(2));
        var firstOrder = await database.SeedJobAsync(world, user, me.Profile, "assigned", Noon.AddHours(2));
        var firstOrderVisit2 = await database.SeedVisitAsync(
            world.Org, firstOrder.Order, 2, "assigned", Noon.AddHours(2), Noon.AddHours(3), me.Profile, user);
        var early = await database.SeedJobAsync(world, user, me.Profile, "assigned", Noon.AddHours(1));

        var today = await TechnicianVisitSeed.ReadAsync(await host.GetAsync("/technician/today", me.Cookie));

        Assert.True(secondOrder.Number < firstOrder.Number);
        Assert.Equal(
            new[] { early.Visit, secondOrder.Visit, firstOrder.Visit, firstOrderVisit2, late.Visit },
            TechnicianVisitSeed.VisitIds(today));
    }

    [Theory]
    [MemberData(nameof(MetricsCases))]
    public async Task Today_MetricsAndNextVisitFollowStatuses(string[] statuses, int jobs, int minutes, int completed, int nextIndex)
    {
        var world = await database.SeedWorldAsync();
        await using var host = TechnicianHost.Create(database);
        var me = await database.SeedTechnicianAsync(host, world);
        host.SetNow(Noon);
        var visits = new List<Guid>();

        for (var index = 0; index < statuses.Length; index++)
        {
            var job = await database.SeedJobAsync(
                world, me.Member.UserId, me.Profile, statuses[index], Noon.AddHours(index - 3), 90);
            visits.Add(job.Visit);
        }

        var today = await TechnicianVisitSeed.ReadAsync(await host.GetAsync("/technician/today", me.Cookie));
        var metrics = today["metrics"]!;

        Assert.Equal(visits, TechnicianVisitSeed.VisitIds(today));
        Assert.Equal(jobs, metrics["jobs"]!.GetValue<int>());
        Assert.Equal(minutes, metrics["scheduledMinutes"]!.GetValue<int>());
        Assert.Equal(completed, metrics["completed"]!.GetValue<int>());
        Assert.Equal(jobs - completed, metrics["remaining"]!.GetValue<int>());
        Assert.Equal(
            nextIndex < 0 ? (Guid?)null : visits[nextIndex],
            today["nextVisitId"] is { } next ? (Guid?)next.GetValue<Guid>() : null);
    }
}
