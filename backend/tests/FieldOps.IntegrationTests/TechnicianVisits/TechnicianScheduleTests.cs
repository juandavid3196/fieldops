using System.Net;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.Dispatch;
using FieldOps.IntegrationTests.ServiceRequests;

namespace FieldOps.IntegrationTests.TechnicianVisits;

/// <summary>
/// Technician side of technician-schedule-active-visits: the week schedule (AC-01 to AC-03), the active-visit guard and the
/// unresolved visits (AC-04, AC-05, AC-07, AC-08) and the read-only cancelled visit (AC-09).
/// </summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class TechnicianScheduleTests(CompanySettingsDatabaseFixture database)
{
    private const string AnotherActive = "You're already traveling to or working on another job.";

    private static readonly DateTimeOffset Noon = TechnicianVisitSeed.Utc("2026-06-10T12:00:00Z");

    private static DateTimeOffset At(string iso) => TechnicianVisitSeed.Utc(iso);

    private static string[] Titles(JsonNode week) =>
        [.. week["visits"]!.AsArray().Select(visit => visit!["title"]!.GetValue<string>())];

    private static async Task<JsonNode> ReadAsync(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK) =>
        await TechnicianVisitSeed.ReadAsync(response, expected);

    [Fact]
    public async Task Schedule_ReturnsTheCallersWeekInTheProfileZoneWithTotalsAndIgnoresForeignIds()
    {
        var world = await database.SeedWorldAsync();
        var foreign = await database.SeedWorldAsync();
        await database.SetBranchTimezoneAsync(world.BranchA, "America/New_York");
        await using var host = TechnicianHost.Create(database);
        var me = await database.SeedTechnicianAsync(host, world);
        var other = await database.SeedTechnicianAsync(host, world, first: "Otto");
        var foreignTech = await database.SeedTechnicianAsync(host, foreign, first: "Fay");
        var user = me.Member.UserId;

        // Wednesday noon in New York; the week of 2026-03-02 contains the DST change of Sunday 2026-03-08 (167 hours).
        host.SetNow(At("2026-03-04T17:00:00Z"));

        var monday = await database.SeedJobAsync(world, user, me.Profile, "assigned", At("2026-03-02T05:00:00Z"), 60, "Mon midnight");
        var tuesdayA = await database.SeedJobAsync(world, user, me.Profile, "assigned", At("2026-03-03T14:00:00Z"), 60, "Tue A");
        await database.SeedJobAsync(world, user, me.Profile, "assigned", At("2026-03-03T14:00:00Z"), 90, "Tue B");
        await database.SeedJobAsync(world, user, me.Profile, "cancelled", At("2026-03-03T16:00:00Z"), 30, "Tue cancelled");
        await database.SeedJobAsync(world, user, me.Profile, "assigned", At("2026-03-04T20:00:00Z"), 45, "Wed today");
        await database.SeedJobAsync(world, user, me.Profile, "assigned", At("2026-03-09T03:59:00Z"), 15, "Sun last");
        await database.SeedJobAsync(world, user, me.Profile, "assigned", At("2026-03-02T04:59:00Z"), 15, "Before the week");
        await database.SeedJobAsync(world, user, me.Profile, "assigned", At("2026-03-09T04:00:00Z"), 15, "After the week");

        // Never returned: unscheduled, another technician's, released and other-organization visits.
        await database.SeedJobAsync(world, user, me.Profile, "unscheduled", At("2026-03-04T18:00:00Z"), 30, "Secret unscheduled");
        await database.SeedJobAsync(world, user, other.Profile, "assigned", At("2026-03-04T18:00:00Z"), 30, "Secret other");
        var released = await database.SeedJobAsync(world, user, me.Profile, "assigned", At("2026-03-04T18:00:00Z"), 30, "Secret released");
        await database.UnassignAsync(released.Visit);
        await database.SeedJobAsync(foreign, foreignTech.Member.UserId, foreignTech.Profile, "assigned", At("2026-03-04T18:00:00Z"), 30, "Secret foreign");

        var plain = await host.GetAsync("/technician/schedule", me.Cookie);
        Assert.Equal("no-store", plain.Headers.CacheControl?.ToString());
        var week = await ReadAsync(plain);

        Assert.Equal("America/New_York", week["timezone"]!.GetValue<string>());
        Assert.Equal("2026-03-04", week["today"]!.GetValue<string>());
        Assert.Equal("2026-03-02", week["weekStart"]!.GetValue<string>());
        Assert.Equal(["Mon midnight", "Tue A", "Tue B", "Tue cancelled", "Wed today", "Sun last"], Titles(week));
        Assert.Equal(
            ["2026-03-02", "2026-03-03", "2026-03-04", "2026-03-05", "2026-03-06", "2026-03-07", "2026-03-08"],
            week["days"]!.AsArray().Select(day => day!["date"]!.GetValue<string>()).ToArray());
        Assert.Equal([1, 2, 1, 0, 0, 0, 1], week["days"]!.AsArray().Select(day => day!["visitCount"]!.GetValue<int>()).ToArray());
        Assert.Equal([60, 150, 45, 0, 0, 0, 15], week["days"]!.AsArray().Select(day => day!["scheduledMinutes"]!.GetValue<int>()).ToArray());
        Assert.Equal(
            [true, true, true, false, false, false],
            week["visits"]!.AsArray().Select(visit => visit!["needsReschedule"]!.GetValue<bool>()).ToArray());
        Assert.Empty(week["unresolvedVisits"]!.AsArray());
        Assert.DoesNotContain("Secret", week.ToJsonString());

        var first = week["visits"]![0]!;
        var prefix = await database.PrefixAsync(world.Org);
        Assert.Equal(monday.Visit, first["visitId"]!.GetValue<Guid>());
        Assert.Equal($"{prefix}-{monday.Number}", first["displayNumber"]!.GetValue<string>());
        Assert.Equal("assigned", first["status"]!.GetValue<string>());
        Assert.Equal(60, first["durationMinutes"]!.GetValue<int>());
        Assert.Equal("Carla Customer", first["customerName"]!.GetValue<string>());
        Assert.Equal("person", first["customerType"]!.GetValue<string>());
        Assert.Equal("1 Seed St", first["address"]!["line1"]!.GetValue<string>());
        Assert.Equal("Austin", first["address"]!["city"]!.GetValue<string>());
        Assert.Equal(DateTimeOffset.Parse("2026-03-02T00:00:00-05:00"), first["start"]!.GetValue<DateTimeOffset>());
        Assert.Equal(tuesdayA.Visit, week["visits"]![1]!["visitId"]!.GetValue<Guid>());

        // The same week by weekStart, with a foreign technician and organization id: identical, nothing foreign.
        var explicitWeek = await ReadAsync(await host.GetAsync("/technician/schedule?weekStart=2026-03-02", me.Cookie));
        var withForeignIds = await ReadAsync(await host.GetAsync(
            $"/technician/schedule?weekStart=2026-03-02&technicianId={other.Profile}&organizationId={foreign.Org}", me.Cookie));
        Assert.Equal(week.ToJsonString(), explicitWeek.ToJsonString());
        Assert.Equal(week.ToJsonString(), withForeignIds.ToJsonString());

        // The visits just outside the DST week belong to the neighbouring weeks.
        Assert.Equal(["After the week"], Titles(await ReadAsync(await host.GetAsync("/technician/schedule?weekStart=2026-03-09", me.Cookie))));
        Assert.Equal(["Before the week"], Titles(await ReadAsync(await host.GetAsync("/technician/schedule?weekStart=2026-02-23", me.Cookie))));

        // weekStart: a Monday between 2000-01-03 and 2099-12-28, otherwise errors.weekStart.
        foreach (var valid in new[] { "2000-01-03", "2099-12-28" })
        {
            var edge = await ReadAsync(await host.GetAsync($"/technician/schedule?weekStart={valid}", me.Cookie));
            Assert.Equal(valid, edge["weekStart"]!.GetValue<string>());
            Assert.Equal(7, edge["days"]!.AsArray().Count);
        }

        foreach (var invalid in new[] { "2026-03-03", "garbage", "2026-02-30", "1999-12-27", "2100-01-04" })
        {
            var problem = await ReadAsync(await host.GetAsync($"/technician/schedule?weekStart={invalid}", me.Cookie), HttpStatusCode.BadRequest);
            Assert.Equal("Choose a valid week.", problem["errors"]!["weekStart"]![0]!.GetValue<string>());
        }

        // A branch without a zone falls back to the organization zone: Thursday morning already in Auckland.
        var other2 = await database.SeedWorldAsync();
        await database.SetBranchTimezoneAsync(other2.BranchA, null);
        await database.ExecuteAsync("UPDATE organizations SET timezone = 'Pacific/Auckland' WHERE id = @o", ("o", other2.Org));
        var auckland = await database.SeedTechnicianAsync(host, other2, first: "Aki");
        var fallback = await ReadAsync(await host.GetAsync("/technician/schedule", auckland.Cookie));
        Assert.Equal("Pacific/Auckland", fallback["timezone"]!.GetValue<string>());
        Assert.Equal("2026-03-05", fallback["today"]!.GetValue<string>());
        Assert.Equal("2026-03-02", fallback["weekStart"]!.GetValue<string>());
    }

    [Fact]
    public async Task ActiveVisits_BlockStartTravelByStatusAndOpenEntriesAndListPastActiveVisitsAsUnresolved()
    {
        var world = await database.SeedWorldAsync();
        await using var host = TechnicianHost.Create(database);
        host.SetNow(Noon);

        // AC-05: another visit of the caller blocks only when active; the scheduled date never decides.
        var prefix = await database.PrefixAsync(world.Org);

        foreach (var (name, status, start, openEntry, released, blocks) in new[]
        {
            ("on_the_way past", "on_the_way", Noon.AddDays(-1), null, false, true),
            ("in_progress today", "in_progress", Noon.AddHours(-3), null, false, true),
            ("paused", "paused", Noon.AddDays(-2), null, false, true),
            ("assigned with open work entry", "assigned", Noon.AddHours(-4), "work", false, true),
            ("cancelled with open entry", "cancelled", Noon.AddHours(-4), "work", false, false),
            ("released with open entry", "in_progress", Noon.AddHours(-4), "work", true, false),
        })
        {
            var me = await database.SeedTechnicianAsync(host, world, first: name.Split(' ')[0]);
            var user = me.Member.UserId;
            var blocker = await database.SeedJobAsync(world, user, me.Profile, status, start, 60, $"Blocker {name}");

            if (openEntry is not null)
            {
                await database.SeedEntryAsync(blocker.Visit, me.Profile, openEntry, Noon.AddHours(-1));
            }

            if (released)
            {
                await database.UnassignAsync(blocker.Visit);
            }

            var target = await database.SeedJobAsync(world, user, me.Profile, "assigned", Noon.AddHours(2), 60, "Target");
            var before = await database.TravelStateAsync(target.Visit);
            var response = await host.PostAsync($"/technician/visits/{target.Visit}/start-travel", me.Cookie);

            if (!blocks)
            {
                var started = await ReadAsync(response);
                Assert.True(started["changed"]!.GetValue<bool>(), name);
                Assert.Equal("on_the_way", started["visit"]!["status"]!.GetValue<string>());

                continue;
            }

            var problem = await ReadAsync(response, HttpStatusCode.Conflict);
            Assert.Equal("another_visit_active", problem["code"]!.GetValue<string>());
            Assert.Equal(AnotherActive, problem["title"]!.GetValue<string>());
            Assert.Equal(blocker.Visit, problem["activeVisit"]!["visitId"]!.GetValue<Guid>());
            Assert.Equal($"{prefix}-{blocker.Number}", problem["activeVisit"]!["displayNumber"]!.GetValue<string>());
            Assert.Equal($"Blocker {name}", problem["activeVisit"]!["title"]!.GetValue<string>());
            Assert.Equal(status, problem["activeVisit"]!["status"]!.GetValue<string>());
            Assert.Equal(start, problem["activeVisit"]!["scheduledStart"]!.GetValue<DateTimeOffset>());
            Assert.Equal(before, await database.TravelStateAsync(target.Visit));
        }

        // AC-07 and AC-05 (earliest wins): two past active visits, one of today, one past assigned that never counts.
        var unresolved = await database.SeedTechnicianAsync(host, world, first: "Uma");
        var uUser = unresolved.Member.UserId;
        var paused = await database.SeedJobAsync(world, uUser, unresolved.Profile, "paused", Noon.AddDays(-1), 60, "Past paused");
        var onTheWay = await database.SeedJobAsync(world, uUser, unresolved.Profile, "on_the_way", Noon.AddDays(-2), 60, "Past on the way");
        await database.SeedJobAsync(world, uUser, unresolved.Profile, "in_progress", Noon.AddHours(-3), 60, "Today in progress");
        await database.SeedJobAsync(world, uUser, unresolved.Profile, "assigned", Noon.AddDays(-1).AddHours(1), 60, "Past assigned");
        var next = await database.SeedJobAsync(world, uUser, unresolved.Profile, "assigned", Noon.AddHours(3), 60, "Next today");

        // BR-08: statusSince is the latest history row into the current status; earlier rows and other statuses are ignored.
        var pausedSince = Noon.AddDays(-1).AddHours(2);
        foreach (var (toStatus, changedAt) in new[]
        {
            ("paused", pausedSince.AddHours(-3)),
            ("paused", pausedSince),
            ("in_progress", pausedSince.AddHours(1)),
        })
        {
            await database.ExecuteAsync(
                "INSERT INTO visit_status_history (visit_id, to_status, changed_at) VALUES (@v, CAST(@s AS visit_status), @t)",
                ("v", paused.Visit), ("s", toStatus), ("t", changedAt));
        }

        var today = await ReadAsync(await host.GetAsync("/technician/today", unresolved.Cookie));
        var week = await ReadAsync(await host.GetAsync("/technician/schedule", unresolved.Cookie));

        foreach (var payload in new[] { today, week })
        {
            var rows = payload["unresolvedVisits"]!.AsArray();
            Assert.Equal("Carla Customer", rows[0]!["customerName"]!.GetValue<string>());
            Assert.Equal("person", rows[0]!["customerType"]!.GetValue<string>());
            Assert.Null(rows[0]!["statusSince"]);
            Assert.Equal(pausedSince, rows[1]!["statusSince"]!.GetValue<DateTimeOffset>());

            var list = payload["unresolvedVisits"]!.AsArray();
            Assert.Equal([onTheWay.Visit, paused.Visit], list.Select(item => item!["visitId"]!.GetValue<Guid>()).ToArray());
            Assert.Equal(["on_the_way", "paused"], list.Select(item => item!["status"]!.GetValue<string>()).ToArray());
            Assert.Equal($"{prefix}-{onTheWay.Number}", list[0]!["displayNumber"]!.GetValue<string>());
            Assert.Equal(Noon.AddDays(-2), list[0]!["scheduledStart"]!.GetValue<DateTimeOffset>());
        }

        var blockedNext = await ReadAsync(
            await host.PostAsync($"/technician/visits/{next.Visit}/start-travel", unresolved.Cookie), HttpStatusCode.Conflict);
        Assert.Equal(onTheWay.Visit, blockedNext["activeVisit"]!["visitId"]!.GetValue<Guid>());

        // AC-04: past scheduled and assigned visits are never active, show needsReschedule and cannot start travel.
        var pat = await database.SeedTechnicianAsync(host, world, first: "Pat");
        var pUser = pat.Member.UserId;
        var pastAssigned = await database.SeedJobAsync(world, pUser, pat.Profile, "assigned", Noon.AddDays(-1), 60, "Old assigned");
        var pastScheduled = await database.SeedJobAsync(world, pUser, pat.Profile, "scheduled", Noon.AddDays(-1).AddHours(2), 60, "Old scheduled");
        var todayJob = await database.SeedJobAsync(world, pUser, pat.Profile, "assigned", Noon.AddHours(1), 60, "Today assigned");

        Assert.Empty((await ReadAsync(await host.GetAsync("/technician/today", pat.Cookie)))["unresolvedVisits"]!.AsArray());

        foreach (var old in new[] { pastAssigned, pastScheduled })
        {
            var detail = await ReadAsync(await host.GetAsync($"/technician/visits/{old.Visit}", pat.Cookie));
            Assert.True(detail["needsReschedule"]!.GetValue<bool>());
            Assert.Null(detail["cancellation"]);
        }

        var oldBefore = await database.TravelStateAsync(pastAssigned.Visit);
        var notToday = await ReadAsync(
            await host.PostAsync($"/technician/visits/{pastAssigned.Visit}/start-travel", pat.Cookie), HttpStatusCode.Conflict);
        Assert.Equal("visit_not_today", notToday["code"]!.GetValue<string>());
        Assert.Equal(oldBefore, await database.TravelStateAsync(pastAssigned.Visit));
        Assert.Equal(
            HttpStatusCode.Conflict,
            (await host.PostAsync($"/technician/visits/{pastScheduled.Visit}/start-travel", pat.Cookie)).StatusCode);

        var todayDetail = await ReadAsync(await host.GetAsync($"/technician/visits/{todayJob.Visit}", pat.Cookie));
        Assert.False(todayDetail["needsReschedule"]!.GetValue<bool>());

        var started2 = await ReadAsync(await host.PostAsync($"/technician/visits/{todayJob.Visit}/start-travel", pat.Cookie));
        Assert.True(started2["changed"]!.GetValue<bool>());

        // AC-08: a past in-progress visit pauses, resumes and completes without a date error and leaves the unresolved list.
        var pam = await database.SeedTechnicianAsync(host, world, first: "Pam");
        var pamUser = pam.Member.UserId;
        var past = await database.SeedJobAsync(world, pamUser, pam.Profile, "in_progress", Noon.AddDays(-1), 60, "Past in progress");
        await database.SeedEntryAsync(past.Visit, pam.Profile, "work", Noon.AddDays(-1));
        await database.ExecuteAsync(
            "INSERT INTO visit_evidence (visit_id, file_name, content, mime_type, size_bytes, evidence_type, uploaded_by_user_id) VALUES (@v, 'b.png', @c, 'image/png', 12, 'before', @u), (@v, 'a.png', @c, 'image/png', 12, 'after', @u)",
            ("v", past.Visit),
            ("c", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4 }),
            ("u", pamUser));

        Assert.Equal(
            [past.Visit],
            (await ReadAsync(await host.GetAsync("/technician/today", pam.Cookie)))["unresolvedVisits"]!.AsArray()
                .Select(item => item!["visitId"]!.GetValue<Guid>())
                .ToArray());

        foreach (var action in new[] { "pause", "resume" })
        {
            var done = await ReadAsync(await host.PostAsync($"/technician/visits/{past.Visit}/{action}", pam.Cookie));
            Assert.True(done["changed"]!.GetValue<bool>(), action);
        }

        using var form = new MultipartFormDataContent { { new StringContent("customer_absent"), "method" }, { new StringContent("Nobody home"), "comment" } };
        var completed = await ReadAsync(await host.PostFormAsync($"/technician/visits/{past.Visit}/complete", pam.Cookie, form));
        Assert.True(completed["changed"]!.GetValue<bool>());
        Assert.Equal("completed", completed["visit"]!["status"]!.GetValue<string>());
        Assert.Empty((await ReadAsync(await host.GetAsync("/technician/today", pam.Cookie)))["unresolvedVisits"]!.AsArray());
    }

    [Fact]
    public async Task CancelledVisit_IsServedReadOnlyToItsTechnicianAndEveryMutationIsA409WithoutWriting()
    {
        var world = await database.SeedWorldAsync();
        await using var host = TechnicianHost.Create(database);
        var me = await database.SeedTechnicianAsync(host, world);
        var other = await database.SeedTechnicianAsync(host, world, first: "Otto");
        host.SetNow(Noon);
        var user = me.Member.UserId;

        var mine = await database.SeedJobAsync(world, user, me.Profile, "cancelled", Noon, title: "Cancelled mine");
        await database.ExecuteAsync(
            "INSERT INTO visit_status_history (visit_id, from_status, to_status, changed_by_user_id, reason, changed_at) VALUES (@v, 'assigned', 'cancelled', @u, 'Old reason', @old), (@v, 'assigned', 'cancelled', @u, 'Customer moved away', @new)",
            ("v", mine.Visit),
            ("u", user),
            ("old", Noon.AddHours(-3)),
            ("new", Noon.AddHours(-2)));
        await database.ExecuteAsync(
            "INSERT INTO visit_checklist_items (visit_id, label, is_required, sort_order) VALUES (@v, 'Task', true, 0)", ("v", mine.Visit));
        var task = await database.ScalarAsync<Guid>("SELECT id FROM visit_checklist_items WHERE visit_id = @v", ("v", mine.Visit));
        var others = await database.SeedJobAsync(world, user, other.Profile, "cancelled", Noon, title: "Secret cancelled other");
        var unscheduled = await database.SeedJobAsync(world, user, me.Profile, "unscheduled", Noon, title: "Secret unscheduled");

        var detail = await ReadAsync(await host.GetAsync($"/technician/visits/{mine.Visit}", me.Cookie));
        Assert.Equal("cancelled", detail["status"]!.GetValue<string>());
        Assert.Equal("Customer moved away", detail["cancellation"]!["reason"]!.GetValue<string>());
        Assert.Equal(Noon.AddHours(-2), detail["cancellation"]!["cancelledAt"]!.GetValue<DateTimeOffset>());
        Assert.False(detail["needsReschedule"]!.GetValue<bool>());

        foreach (var id in new[] { others.Visit, unscheduled.Visit })
        {
            var response = await host.GetAsync($"/technician/visits/{id}", me.Cookie);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.DoesNotContain("Secret", await response.Content.ReadAsStringAsync());
        }

        // Every mutation goes through the locked transaction or the status precheck: 409, never 404, nothing written.
        var before = await database.ProgressStateAsync(mine.Visit);
        var path = $"/technician/visits/{mine.Visit}";

        foreach (var response in new[]
        {
            await host.PostAsync($"{path}/start-travel", me.Cookie),
            await host.PostAsync($"{path}/arrive", me.Cookie),
            await host.PostAsync($"{path}/start-job", me.Cookie),
            await host.PostAsync($"{path}/pause", me.Cookie),
            await host.PostAsync($"{path}/resume", me.Cookie),
            await host.SendAsync(HttpMethod.Patch, $"{path}/tasks/{task}", me.Cookie, new JsonObject { ["isCompleted"] = true }),
            await host.SendAsync(HttpMethod.Post, $"{path}/tasks", me.Cookie, new JsonObject { ["label"] = "More" }),
            await host.SendAsync(HttpMethod.Put, $"{path}/notes", me.Cookie, new JsonObject { ["notes"] = "Anything" }),
            await host.PostFormAsync(
                $"{path}/complete",
                me.Cookie,
                new MultipartFormDataContent { { new StringContent("customer_absent"), "method" }, { new StringContent("Nobody"), "comment" } }),
        })
        {
            var problem = await ReadAsync(response, HttpStatusCode.Conflict);
            Assert.Equal("visit_status_invalid", problem["code"]!.GetValue<string>());
        }

        Assert.Equal(before, await database.ProgressStateAsync(mine.Visit));
    }
}
