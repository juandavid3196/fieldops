using System.Net;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.Dispatch;
using FieldOps.IntegrationTests.ServiceRequests;

namespace FieldOps.IntegrationTests.TechnicianVisits;

/// <summary>technician-time-summary: GET /technician/time (AC-01 to AC-05).</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class TechnicianTimeTests(CompanySettingsDatabaseFixture database)
{
    private static readonly DateTimeOffset Noon = TechnicianVisitSeed.Utc("2026-06-10T12:00:00Z");

    private static DateTimeOffset At(string iso) => TechnicianVisitSeed.Utc(iso);

    private static async Task<JsonNode> ReadAsync(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK) =>
        await TechnicianVisitSeed.ReadAsync(response, expected);

    private static int[] Seconds(JsonNode payload, string field) =>
        [.. payload["days"]!.AsArray().Select(day => day![field]!.GetValue<int>())];

    [Fact]
    public async Task Time_ReturnsOnlyTheCallersEntriesNewestFirstWithAccessibilityAndIgnoresForeignIds()
    {
        var world = await database.SeedWorldAsync();
        var foreign = await database.SeedWorldAsync();
        await database.SetBranchTimezoneAsync(world.BranchA, "UTC");
        await using var host = TechnicianHost.Create(database);
        var me = await database.SeedTechnicianAsync(host, world);
        var other = await database.SeedTechnicianAsync(host, world, first: "Otto");
        var foreignTech = await database.SeedTechnicianAsync(host, foreign, first: "Fay");
        var user = me.Member.UserId;
        host.SetNow(Noon);

        var assigned = await database.SeedJobAsync(world, user, me.Profile, "in_progress", Noon, title: "Assigned job");
        var reassigned = await database.SeedJobAsync(world, user, me.Profile, "assigned", Noon, title: "Reassigned job");
        var cancelled = await database.SeedJobAsync(world, user, me.Profile, "cancelled", Noon, title: "Cancelled job");
        var unscheduled = await database.SeedJobAsync(world, user, me.Profile, "unscheduled", Noon, title: "Unscheduled job");
        await database.UnassignAsync(reassigned.Visit);

        await database.SeedEntryAsync(assigned.Visit, me.Profile, "work", At("2026-06-10T08:00:00Z"), At("2026-06-10T09:00:00Z"));
        await database.SeedEntryAsync(reassigned.Visit, me.Profile, "travel", At("2026-06-10T07:00:00Z"), At("2026-06-10T07:30:00Z"));
        await database.SeedEntryAsync(cancelled.Visit, me.Profile, "pause", At("2026-06-10T06:00:00Z"), At("2026-06-10T06:10:00Z"));
        await database.SeedEntryAsync(unscheduled.Visit, me.Profile, "work", At("2026-06-10T05:00:00Z"), At("2026-06-10T05:05:00Z"));

        // Never returned: another technician's entry on the caller's own visit, on their own visit and in another organization.
        await database.SeedEntryAsync(assigned.Visit, other.Profile, "work", At("2026-06-10T10:00:00Z"), At("2026-06-10T11:00:00Z"));
        var othersJob = await database.SeedJobAsync(world, user, other.Profile, "in_progress", Noon, title: "Secret other job");
        await database.SeedEntryAsync(othersJob.Visit, other.Profile, "work", At("2026-06-10T08:30:00Z"), At("2026-06-10T09:30:00Z"));
        var foreignJob = await database.SeedJobAsync(foreign, foreignTech.Member.UserId, foreignTech.Profile, "in_progress", Noon, title: "Secret foreign job");
        await database.SeedEntryAsync(foreignJob.Visit, foreignTech.Profile, "work", At("2026-06-10T08:45:00Z"), At("2026-06-10T09:45:00Z"));

        var response = await host.GetAsync("/technician/time", me.Cookie);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var payload = await ReadAsync(response);
        var prefix = await database.PrefixAsync(world.Org);

        Assert.Equal("UTC", payload["timezone"]!.GetValue<string>());
        Assert.Equal("2026-06-10", payload["today"]!.GetValue<string>());
        Assert.Equal("week", payload["view"]!.GetValue<string>());
        Assert.Equal("2026-06-08", payload["from"]!.GetValue<string>());
        Assert.Equal("2026-06-14", payload["to"]!.GetValue<string>());

        var entries = payload["entries"]!.AsArray();
        Assert.Equal(["work", "travel", "break", "work"], entries.Select(entry => entry!["type"]!.GetValue<string>()).ToArray());
        Assert.Equal([3600, 1800, 600, 300], entries.Select(entry => entry!["durationSeconds"]!.GetValue<int>()).ToArray());
        Assert.Equal([true, false, true, false], entries.Select(entry => entry!["visitAccessible"]!.GetValue<bool>()).ToArray());
        Assert.All(entries, entry => Assert.False(entry!["isOpen"]!.GetValue<bool>()));
        Assert.Equal(
            ["in_progress", "assigned", "cancelled", "unscheduled"],
            entries.Select(entry => entry!["visit"]!["status"]!.GetValue<string>()).ToArray());
        Assert.Equal(
            ["Assigned job", "Reassigned job", "Cancelled job", "Unscheduled job"],
            entries.Select(entry => entry!["visit"]!["title"]!.GetValue<string>()).ToArray());
        Assert.Equal(assigned.Visit, entries[0]!["visit"]!["visitId"]!.GetValue<Guid>());
        Assert.Equal($"{prefix}-{assigned.Number}", entries[0]!["visit"]!["displayNumber"]!.GetValue<string>());
        Assert.Equal(1, entries[0]!["visit"]!["visitNumber"]!.GetValue<int>());
        Assert.Equal(At("2026-06-10T08:00:00Z"), entries[0]!["startedAt"]!.GetValue<DateTimeOffset>());
        Assert.Equal(At("2026-06-10T09:00:00Z"), entries[0]!["endedAt"]!.GetValue<DateTimeOffset>());
        Assert.DoesNotContain("Secret", payload.ToJsonString());

        // A foreign technician or organization id changes nothing.
        var withForeignIds = await ReadAsync(await host.GetAsync(
            $"/technician/time?technicianId={other.Profile}&organizationId={foreign.Org}", me.Cookie));
        Assert.Equal(payload.ToJsonString(), withForeignIds.ToJsonString());

        // Another technician sees only their own two entries; the one on a visit they no longer hold is not accessible.
        var otherEntries = (await ReadAsync(await host.GetAsync("/technician/time", other.Cookie)))["entries"]!.AsArray();
        Assert.Equal(
            ["Assigned job", "Secret other job"],
            otherEntries.Select(entry => entry!["visit"]!["title"]!.GetValue<string>()).ToArray());
        Assert.Equal([false, true], otherEntries.Select(entry => entry!["visitAccessible"]!.GetValue<bool>()).ToArray());
    }

    [Fact]
    public async Task Time_UsesTheProfileZoneForDstWeeksMidnightBoundariesAndRejectsInvalidViewAndDate()
    {
        var world = await database.SeedWorldAsync();
        await database.SetBranchTimezoneAsync(world.BranchA, "America/New_York");
        await using var host = TechnicianHost.Create(database);
        var me = await database.SeedTechnicianAsync(host, world);
        var user = me.Member.UserId;

        // Wednesday in New York; the week of 2026-03-02 contains the DST change of Sunday 2026-03-08.
        host.SetNow(At("2026-03-04T17:00:00Z"));
        var job = await database.SeedJobAsync(world, user, me.Profile, "in_progress", At("2026-03-04T17:00:00Z"));

        // Monday 00:00 local, Tuesday 23:50 to Wednesday 00:20 local, Sunday 00:30 (EST) and Sunday 23:59 (EDT).
        await database.SeedEntryAsync(job.Visit, me.Profile, "work", At("2026-03-02T05:00:00Z"), At("2026-03-02T05:10:00Z"));
        await database.SeedEntryAsync(job.Visit, me.Profile, "work", At("2026-03-04T04:50:00Z"), At("2026-03-04T05:20:00Z"));
        await database.SeedEntryAsync(job.Visit, me.Profile, "pause", At("2026-03-08T05:30:00Z"), At("2026-03-08T05:32:00Z"));
        await database.SeedEntryAsync(job.Visit, me.Profile, "travel", At("2026-03-09T03:59:00Z"), At("2026-03-09T04:00:00Z"));

        // Just outside the week: Sunday 23:59 before and Monday 00:00 after.
        await database.SeedEntryAsync(job.Visit, me.Profile, "work", At("2026-03-02T04:59:00Z"), At("2026-03-02T05:00:00Z"));
        await database.SeedEntryAsync(job.Visit, me.Profile, "work", At("2026-03-09T04:00:00Z"), At("2026-03-09T04:05:00Z"));

        var week = await ReadAsync(await host.GetAsync("/technician/time?view=week&date=2026-03-05", me.Cookie));
        Assert.Equal("America/New_York", week["timezone"]!.GetValue<string>());
        Assert.Equal("2026-03-04", week["today"]!.GetValue<string>());
        Assert.Equal("2026-03-02", week["from"]!.GetValue<string>());
        Assert.Equal("2026-03-08", week["to"]!.GetValue<string>());
        Assert.Equal(
            ["2026-03-02", "2026-03-03", "2026-03-04", "2026-03-05", "2026-03-06", "2026-03-07", "2026-03-08"],
            week["days"]!.AsArray().Select(day => day!["date"]!.GetValue<string>()).ToArray());
        Assert.Equal([600, 1800, 0, 0, 0, 0, 0], Seconds(week, "workSeconds"));
        Assert.Equal([0, 0, 0, 0, 0, 0, 60], Seconds(week, "travelSeconds"));
        Assert.Equal([0, 0, 0, 0, 0, 0, 120], Seconds(week, "breakSeconds"));
        Assert.Equal([600, 1800, 0, 0, 0, 0, 180], Seconds(week, "totalSeconds"));
        Assert.Equal(4, week["entries"]!.AsArray().Count);
        Assert.Equal(2400, week["totals"]!["workSeconds"]!.GetValue<int>());
        Assert.Equal(60, week["totals"]!["travelSeconds"]!.GetValue<int>());
        Assert.Equal(120, week["totals"]!["breakSeconds"]!.GetValue<int>());
        Assert.Equal(2580, week["totals"]!["totalSeconds"]!.GetValue<int>());

        // The default is the current week; the cross-midnight entry counts wholly on its start day.
        Assert.Equal(week["entries"]!.ToJsonString(), (await ReadAsync(await host.GetAsync("/technician/time", me.Cookie)))["entries"]!.ToJsonString());

        var tuesday = await ReadAsync(await host.GetAsync("/technician/time?view=day&date=2026-03-03", me.Cookie));
        Assert.Equal("day", tuesday["view"]!.GetValue<string>());
        Assert.Equal("2026-03-03", tuesday["from"]!.GetValue<string>());
        Assert.Equal("2026-03-03", tuesday["to"]!.GetValue<string>());
        Assert.Single(tuesday["days"]!.AsArray());
        Assert.Equal(1800, tuesday["totals"]!["totalSeconds"]!.GetValue<int>());
        Assert.Single(tuesday["entries"]!.AsArray());

        var wednesday = await ReadAsync(await host.GetAsync("/technician/time?view=day&date=2026-03-04", me.Cookie));
        Assert.Empty(wednesday["entries"]!.AsArray());
        Assert.Equal(0, wednesday["totals"]!["totalSeconds"]!.GetValue<int>());

        foreach (var edge in new[] { "2000-01-01", "2099-12-31" })
        {
            Assert.Equal(7, (await ReadAsync(await host.GetAsync($"/technician/time?date={edge}", me.Cookie)))["days"]!.AsArray().Count);
        }

        foreach (var (query, field) in new[]
        {
            ("view=month", "view"),
            ("date=2026-02-30", "date"),
            ("date=garbage", "date"),
            ("date=1999-12-31", "date"),
            ("date=2100-01-01", "date"),
            ("date=2026-3-4", "date"),
        })
        {
            var problem = await ReadAsync(await host.GetAsync($"/technician/time?{query}", me.Cookie), HttpStatusCode.BadRequest);

            Assert.Equal("Choose a valid date.", problem["errors"]![field]![0]!.GetValue<string>());
        }

        // A branch without a zone falls back to the organization zone.
        var auckland = await database.SeedWorldAsync();
        await database.SetBranchTimezoneAsync(auckland.BranchA, null);
        await database.ExecuteAsync("UPDATE organizations SET timezone = 'Pacific/Auckland' WHERE id = @o", ("o", auckland.Org));
        var aki = await database.SeedTechnicianAsync(host, auckland, first: "Aki");
        var fallback = await ReadAsync(await host.GetAsync("/technician/time", aki.Cookie));
        Assert.Equal("Pacific/Auckland", fallback["timezone"]!.GetValue<string>());
        Assert.Equal("2026-03-05", fallback["today"]!.GetValue<string>());
    }

    [Fact]
    public async Task Time_CountsTheOpenEntryToNowWithoutPauseSecondsAndWritesNothing()
    {
        var world = await database.SeedWorldAsync();
        await database.SetBranchTimezoneAsync(world.BranchA, "UTC");
        await using var host = TechnicianHost.Create(database);
        var me = await database.SeedTechnicianAsync(host, world);
        host.SetNow(Noon);

        var job = await database.SeedJobAsync(world, me.Member.UserId, me.Profile, "in_progress", Noon);
        await database.ExecuteAsync("UPDATE visits SET pause_seconds = 777 WHERE id = @v", ("v", job.Visit));
        await database.SeedEntryAsync(job.Visit, me.Profile, "work", At("2026-06-08T09:00:00Z"), At("2026-06-08T10:00:00Z"));
        await database.SeedEntryAsync(job.Visit, me.Profile, "travel", At("2026-06-08T08:00:00Z"), At("2026-06-08T08:15:00Z"));
        await database.SeedEntryAsync(job.Visit, me.Profile, "pause", At("2026-06-09T09:00:00Z"), At("2026-06-09T09:05:00Z"));
        await database.SeedEntryAsync(job.Visit, me.Profile, "work", At("2026-06-10T11:00:00Z"));

        const string counts = "SELECT (SELECT COUNT(*) FROM visit_time_entries) || '|' || (SELECT COUNT(*) FROM audit_logs) || '|' || (SELECT updated_at::text FROM visits WHERE id = @v)";
        var before = await database.ScalarAsync<string>(counts, ("v", job.Visit));

        var payload = await ReadAsync(await host.GetAsync("/technician/time", me.Cookie));

        Assert.Equal(7200, payload["totals"]!["workSeconds"]!.GetValue<int>());
        Assert.Equal(900, payload["totals"]!["travelSeconds"]!.GetValue<int>());
        Assert.Equal(300, payload["totals"]!["breakSeconds"]!.GetValue<int>());
        Assert.Equal(8400, payload["totals"]!["totalSeconds"]!.GetValue<int>());
        Assert.Equal([4500, 300, 3600, 0, 0, 0, 0], Seconds(payload, "totalSeconds"));

        var open = payload["entries"]![0]!;
        Assert.True(open["isOpen"]!.GetValue<bool>());
        Assert.Null(open["endedAt"]);
        Assert.Equal(3600, open["durationSeconds"]!.GetValue<int>());
        Assert.Equal(before, await database.ScalarAsync<string>(counts, ("v", job.Visit)));
    }

    [Fact]
    public async Task Time_DeniesOtherRolesUnlinkedAndInactiveTechnicians()
    {
        var world = await database.SeedWorldAsync();
        await using var host = TechnicianHost.Create(database);
        var inactive = await database.SeedTechnicianAsync(host, world, "inactive", "Ina");
        var job = await database.SeedJobAsync(world, inactive.Member.UserId, inactive.Profile, "assigned", Noon, title: "Secret blocked job");
        await database.SeedEntryAsync(job.Visit, inactive.Profile, "work", At("2026-06-10T08:00:00Z"), At("2026-06-10T09:00:00Z"));
        host.SetNow(Noon);

        foreach (var role in new[] { CompanySettingsDatabaseFixture.DispatcherRoleId, CompanySettingsDatabaseFixture.OwnerRoleId })
        {
            var (cookie, _) = await host.SignInAsync(database, world.Org, role, "Role", "Holder");

            Assert.Equal(HttpStatusCode.Forbidden, (await host.GetAsync("/technician/time", cookie)).StatusCode);
        }

        var (unlinked, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.TechnicianRoleId, "Una", "Linked");
        var notLinked = await ReadAsync(await host.GetAsync("/technician/time", unlinked), HttpStatusCode.NotFound);
        Assert.Equal("technician_profile_not_linked", notLinked["code"]!.GetValue<string>());

        var blocked = await host.GetAsync("/technician/time", inactive.Cookie);
        var problem = await ReadAsync(blocked, HttpStatusCode.Forbidden);
        Assert.Equal("technician_inactive", problem["code"]!.GetValue<string>());
        Assert.DoesNotContain("Secret", problem.ToJsonString());

        Assert.Equal(HttpStatusCode.Unauthorized, (await host.GetAsync("/technician/time", null)).StatusCode);
    }
}
