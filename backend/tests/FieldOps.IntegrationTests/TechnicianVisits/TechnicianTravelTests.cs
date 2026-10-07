using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.Dispatch;
using FieldOps.IntegrationTests.ServiceRequests;
using FieldOps.IntegrationTests.Team;
using Microsoft.Extensions.Logging;

namespace FieldOps.IntegrationTests.TechnicianVisits;

/// <summary>Start travel and arrive: effect, guards, authorization, concurrency and the customer email (mobile-job-details AC-01 to AC-13).</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class TechnicianTravelTests(CompanySettingsDatabaseFixture database)
{
    private const string InvalidStateTitle = "Travel cannot be managed for this job in its current state.";

    private static readonly DateTimeOffset Noon = TechnicianVisitSeed.Utc("2026-06-10T12:00:00Z");

    private static string StartPath(Guid visit) => $"/technician/visits/{visit}/start-travel";

    private static string ArrivePath(Guid visit) => $"/technician/visits/{visit}/arrive";

    private static DateTimeOffset At(JsonNode? value) => DateTimeOffset.Parse(value!.GetValue<string>(), CultureInfo.InvariantCulture);

    [Fact]
    public async Task StartTravelAndArrive_WriteOncePerTransitionAndEmailOnlyTheEffectiveStart()
    {
        var world = await database.SeedWorldAsync();
        await using var host = TechnicianHost.Create(database);
        var me = await database.SeedTechnicianAsync(host, world);
        host.SetNow(Noon);
        var user = me.Member.UserId;
        var job = await database.SeedJobAsync(world, user, me.Profile, "assigned", Noon.AddHours(1), 60, "Fix drain");
        await database.ExecuteAsync(
            "UPDATE visits SET dispatch_note = 'Gate code 4321', arrival_window_start = scheduled_start - interval '30 minutes', arrival_window_end = scheduled_start + interval '30 minutes' WHERE id = @v",
            ("v", job.Visit));

        // AC-07: the effective start.
        var response = await host.PostAsync(StartPath(job.Visit), me.Cookie);
        var started = await TechnicianVisitSeed.ReadAsync(response);

        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.True(started["changed"]!.GetValue<bool>());
        Assert.Equal("on_the_way", started["visit"]!["status"]!.GetValue<string>());
        Assert.True(started["visit"]!["isPrimary"]!.GetValue<bool>());
        Assert.Equal(Noon, At(started["visit"]!["travel"]!["startedAt"]));
        Assert.Null(started["visit"]!["travel"]!["arrivedAt"]);
        Assert.Null(started["visit"]!["travel"]!["durationMinutes"]);

        Assert.StartsWith("on_the_way|h=1|t=1|c=0|a=1|", await database.TravelStateAsync(job.Visit));
        Assert.Equal(
            1,
            await database.ScalarAsync<long>(
                "SELECT COUNT(*) FROM visit_status_history WHERE visit_id = @v AND from_status = 'assigned' AND to_status = 'on_the_way' AND changed_by_user_id = @u AND reason IS NULL AND changed_at = @n",
                ("v", job.Visit),
                ("u", user),
                ("n", Noon)));
        Assert.Equal(
            1,
            await database.ScalarAsync<long>(
                "SELECT COUNT(*) FROM visit_time_entries WHERE visit_id = @v AND technician_id = @t AND entry_type = 'travel' AND started_at = @n AND ended_at IS NULL",
                ("v", job.Visit),
                ("t", me.Profile),
                ("n", Noon)));
        Assert.Equal(
            1,
            await database.ScalarAsync<long>(
                """
                SELECT COUNT(*) FROM audit_logs WHERE entity_id = @v AND action = 'visit.travel_started' AND entity_type = 'visit'
                    AND organization_id = @o AND actor_user_id = @u AND branch_id = @b
                    AND before_data->>'status' = 'assigned' AND after_data->>'status' = 'on_the_way'
                    AND metadata->>'workOrderId' = @w AND metadata->>'visitNumber' = '1' AND metadata->>'notified' = 'email'
                """,
                ("v", job.Visit),
                ("o", world.Org),
                ("u", user),
                ("b", world.BranchA),
                ("w", job.Order.ToString())));
        Assert.Equal("scheduled", await database.ScalarAsync<string>("SELECT status::text FROM work_orders WHERE id = @w", ("w", job.Order)));

        var audit = await DispatchSeed.AuditTextAsync(database, job.Visit);
        Assert.DoesNotContain("Carla", audit);
        Assert.DoesNotContain("carla@example.com", audit);
        Assert.DoesNotContain("1 Seed St", audit);
        Assert.DoesNotContain("Gate code", audit);

        // AC-13: one email after the commit, with the BR-11 content and nothing sensitive.
        var prefix = await database.PrefixAsync(world.Org);
        var organization = await database.ScalarAsync<string>("SELECT name FROM organizations WHERE id = @o", ("o", world.Org));
        var message = Assert.Single(host.Sender.Messages);
        Assert.Equal("carla@example.com", message.To);
        Assert.Equal($"{organization}: your technician is on the way for {prefix}-{job.Number}", message.Subject);
        Assert.Contains("Hi Pat,", message.TextBody);
        Assert.Contains("Your technician is on the way for Fix drain at 1 Seed St.", message.TextBody);
        Assert.Contains("Scheduled arrival window: 12:30 PM – 1:30 PM.", message.TextBody);
        Assert.Contains("Your technician: Tess Tech.", message.TextBody);
        Assert.Contains("Questions? Call us at +1 555 010 0100.", message.TextBody);
        Assert.DoesNotContain("Gate code", message.TextBody + message.HtmlBody);

        // AC-10: arrival closes only that entry, keeps the status and writes no history or email.
        host.Time.Advance(TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(45));
        var arrived = await TechnicianVisitSeed.ReadAsync(await host.PostAsync(ArrivePath(job.Visit), me.Cookie));

        Assert.True(arrived["changed"]!.GetValue<bool>());
        Assert.Equal("on_the_way", arrived["visit"]!["status"]!.GetValue<string>());
        Assert.Equal(Noon.AddSeconds(645), At(arrived["visit"]!["travel"]!["arrivedAt"]));
        Assert.Equal(10, arrived["visit"]!["travel"]!["durationMinutes"]!.GetValue<int>());

        var afterArrival = await database.TravelStateAsync(job.Visit);
        Assert.StartsWith("on_the_way|h=1|t=1|c=1|a=2|", afterArrival);
        Assert.Equal(
            1,
            await database.ScalarAsync<long>(
                "SELECT COUNT(*) FROM audit_logs WHERE entity_id = @v AND action = 'visit.arrived' AND metadata->>'travelMinutes' = '10' AND metadata->>'workOrderId' = @w",
                ("v", job.Visit),
                ("w", job.Order.ToString())));
        Assert.Equal(1, host.Sender.Attempts);

        // AC-08, AC-10: repeats change nothing, also on a later date.
        var repeatedArrival = await TechnicianVisitSeed.ReadAsync(await host.PostAsync(ArrivePath(job.Visit), me.Cookie));
        host.Time.Advance(TimeSpan.FromDays(2));
        var repeatedStart = await TechnicianVisitSeed.ReadAsync(await host.PostAsync(StartPath(job.Visit), me.Cookie));

        Assert.False(repeatedArrival["changed"]!.GetValue<bool>());
        Assert.False(repeatedStart["changed"]!.GetValue<bool>());
        Assert.Equal(10, repeatedStart["visit"]!["travel"]!["durationMinutes"]!.GetValue<int>());
        Assert.Equal(afterArrival, await database.TravelStateAsync(job.Visit));
        Assert.Equal(1, host.Sender.Attempts);
    }

    [Fact]
    public async Task TravelEndpoints_DenyUnavailableVisitsNonPrimaryAndUnusableProfilesAndRefuseInvalidStatesWithoutWriting()
    {
        var world = await database.SeedWorldAsync();
        var foreign = await database.SeedWorldAsync();
        await using var host = TechnicianHost.Create(database);
        var me = await database.SeedTechnicianAsync(host, world);
        var other = await database.SeedTechnicianAsync(host, world, first: "Otto");
        var walker = await database.SeedTechnicianAsync(host, world, first: "Walt");
        var foreignTech = await database.SeedTechnicianAsync(host, foreign, first: "Fay");
        host.SetNow(Noon);
        var user = me.Member.UserId;

        // AC-01: another technician's, released, unscheduled, cancelled, other-organization and random visits.
        var hidden = new List<Guid>();
        var hiddenSeeded = new List<Guid>();

        foreach (var (title, technician, status) in new[]
        {
            ("Secret other job", other.Profile, "assigned"),
            ("Secret released job", me.Profile, "assigned"),
            ("Secret unscheduled job", me.Profile, "unscheduled"),
            ("Secret cancelled job", me.Profile, "cancelled"),
        })
        {
            var seeded = await database.SeedJobAsync(world, user, technician, status, Noon, title: title);

            if (title.Contains("released", StringComparison.Ordinal))
            {
                await database.UnassignAsync(seeded.Visit);
            }

            hidden.Add(seeded.Visit);
            hiddenSeeded.Add(seeded.Visit);
        }

        var foreignJob = await database.SeedJobAsync(foreign, foreignTech.Member.UserId, foreignTech.Profile, "assigned", Noon, title: "Secret foreign job");
        hidden.Add(foreignJob.Visit);
        hiddenSeeded.Add(foreignJob.Visit);
        hidden.Add(Guid.NewGuid());

        var before = new List<string>();

        foreach (var id in hiddenSeeded)
        {
            before.Add(await database.TravelStateAsync(id));
        }

        var bodies = new HashSet<string>();

        foreach (var id in hidden)
        {
            foreach (var response in new[]
            {
                await host.GetAsync($"/technician/visits/{id}", me.Cookie),
                await host.PostAsync(StartPath(id), me.Cookie),
                await host.PostAsync(ArrivePath(id), me.Cookie),
                await host.GetAsync($"/technician/visits/{id}/assessment-photos/{Guid.NewGuid()}", me.Cookie),
            })
            {
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
                Assert.DoesNotContain("Secret", await response.Content.ReadAsStringAsync());
                bodies.Add(await TechnicianVisitSeed.WithoutTraceAsync(response));
            }
        }

        Assert.Single(bodies);
        Assert.DoesNotContain("\"code\"", bodies.Single());

        for (var index = 0; index < hiddenSeeded.Count; index++)
        {
            Assert.Equal(before[index], await database.TravelStateAsync(hiddenSeeded[index]));
        }

        // AC-03: an actively assigned non-primary technician reads but cannot manage travel.
        var shared = await database.SeedJobAsync(world, user, other.Profile, "assigned", Noon.AddHours(1), title: "Shared job");
        await database.AssignAsync(shared.Visit, me.Profile, user, primary: false);
        var sharedBefore = await database.TravelStateAsync(shared.Visit);

        foreach (var path in new[] { StartPath(shared.Visit), ArrivePath(shared.Visit) })
        {
            var forbidden = await host.PostAsync(path, me.Cookie);
            var problem = await TechnicianVisitSeed.ReadAsync(forbidden, HttpStatusCode.Forbidden);

            Assert.Equal("not_primary_technician", problem["code"]!.GetValue<string>());
            Assert.Equal("The primary technician manages travel for this job.", problem["title"]!.GetValue<string>());
        }

        var sharedDetail = await TechnicianVisitSeed.ReadAsync(await host.GetAsync($"/technician/visits/{shared.Visit}", me.Cookie));
        var sharedToday = await TechnicianVisitSeed.ReadAsync(await host.GetAsync("/technician/today", me.Cookie));
        var primaryDetail = await TechnicianVisitSeed.ReadAsync(await host.GetAsync($"/technician/visits/{shared.Visit}", other.Cookie));

        Assert.False(sharedDetail["isPrimary"]!.GetValue<bool>());
        Assert.False(sharedToday["visits"]!.AsArray().Single(visit => visit!["visitId"]!.GetValue<Guid>() == shared.Visit)!["isPrimary"]!.GetValue<bool>());
        Assert.True(primaryDetail["isPrimary"]!.GetValue<bool>());
        Assert.Equal(sharedBefore, await database.TravelStateAsync(shared.Visit));

        // AC-02: other roles, an unlinked account and inactive or suspended profiles.
        foreach (var role in new[] { CompanySettingsDatabaseFixture.DispatcherRoleId, CompanySettingsDatabaseFixture.OwnerRoleId })
        {
            var (cookie, _) = await host.SignInAsync(database, world.Org, role, "Role", "Holder");

            foreach (var path in new[] { StartPath(shared.Visit), ArrivePath(shared.Visit) })
            {
                Assert.Equal(HttpStatusCode.Forbidden, (await host.PostAsync(path, cookie)).StatusCode);
            }
        }

        var (unlinked, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.TechnicianRoleId, "Una", "Linked");

        foreach (var path in new[] { StartPath(shared.Visit), ArrivePath(shared.Visit) })
        {
            var problem = await TechnicianVisitSeed.ReadAsync(await host.PostAsync(path, unlinked), HttpStatusCode.NotFound);
            Assert.Equal("technician_profile_not_linked", problem["code"]!.GetValue<string>());
        }

        foreach (var status in new[] { "inactive", "suspended" })
        {
            var blocked = await database.SeedTechnicianAsync(host, world, status, first: status);
            var job = await database.SeedJobAsync(world, blocked.Member.UserId, blocked.Profile, "assigned", Noon.AddHours(1), title: "Secret blocked job");
            var blockedBefore = await database.TravelStateAsync(job.Visit);

            foreach (var path in new[] { StartPath(job.Visit), ArrivePath(job.Visit) })
            {
                var problem = await TechnicianVisitSeed.ReadAsync(await host.PostAsync(path, blocked.Cookie), HttpStatusCode.Forbidden);
                Assert.Equal("technician_inactive", problem["code"]!.GetValue<string>());
            }

            Assert.Equal(blockedBefore, await database.TravelStateAsync(job.Visit));
        }

        // AC-09: statuses, other days and another active visit, each without a write.
        var startCases = new (string Status, DateTimeOffset Start, string Code, string Title)[]
        {
            ("scheduled", Noon.AddHours(1), "visit_status_invalid", InvalidStateTitle),
            ("in_progress", Noon.AddHours(1), "visit_status_invalid", InvalidStateTitle),
            ("paused", Noon.AddHours(1), "visit_status_invalid", InvalidStateTitle),
            ("completed", Noon.AddHours(1), "visit_status_invalid", InvalidStateTitle),
            ("needs_correction", Noon.AddHours(1), "visit_status_invalid", InvalidStateTitle),
            ("approved", Noon.AddHours(1), "visit_status_invalid", InvalidStateTitle),
            ("assigned", Noon.AddDays(-1), "visit_not_today", "Travel can only be started on the day of the visit."),
            ("assigned", Noon.AddDays(1), "visit_not_today", "Travel can only be started on the day of the visit."),

            // Placed after the in_progress case above: the caller already works on another job.
            ("assigned", Noon.AddHours(2), "another_visit_active", "You're already traveling to or working on another job."),
        };

        foreach (var (status, start, code, title) in startCases)
        {
            var job = await database.SeedJobAsync(world, user, me.Profile, status, start);
            var jobBefore = await database.TravelStateAsync(job.Visit);
            var problem = await TechnicianVisitSeed.ReadAsync(await host.PostAsync(StartPath(job.Visit), me.Cookie), HttpStatusCode.Conflict);

            Assert.Equal(code, problem["code"]!.GetValue<string>());
            Assert.Equal(title, problem["title"]!.GetValue<string>());
            Assert.Equal(jobBefore, await database.TravelStateAsync(job.Visit));
        }

        var traveling = await database.SeedJobAsync(world, walker.Member.UserId, walker.Profile, "on_the_way", Noon.AddHours(1));
        await database.SeedTravelEntryAsync(traveling.Visit, walker.Profile, Noon.AddMinutes(-5));
        var next = await database.SeedJobAsync(world, walker.Member.UserId, walker.Profile, "assigned", Noon.AddHours(3));
        var nextBefore = await database.TravelStateAsync(next.Visit);
        var busy = await TechnicianVisitSeed.ReadAsync(await host.PostAsync(StartPath(next.Visit), walker.Cookie), HttpStatusCode.Conflict);

        Assert.Equal("another_visit_active", busy["code"]!.GetValue<string>());
        Assert.Equal(nextBefore, await database.TravelStateAsync(next.Visit));

        // AC-11: arrive needs an on_the_way visit with the caller's travel entry.
        var arrivalCases = new List<Guid>
        {
            (await database.SeedJobAsync(world, other.Member.UserId, other.Profile, "assigned", Noon.AddHours(1))).Visit,
            (await database.SeedJobAsync(world, other.Member.UserId, other.Profile, "in_progress", Noon.AddHours(2))).Visit,
            (await database.SeedJobAsync(world, other.Member.UserId, other.Profile, "on_the_way", Noon.AddHours(3))).Visit,
        };
        var someoneElses = await database.SeedJobAsync(world, other.Member.UserId, other.Profile, "on_the_way", Noon.AddHours(4));
        await database.SeedTravelEntryAsync(someoneElses.Visit, me.Profile, Noon.AddMinutes(-5));
        arrivalCases.Add(someoneElses.Visit);

        foreach (var visit in arrivalCases)
        {
            var arrivalBefore = await database.TravelStateAsync(visit);
            var problem = await TechnicianVisitSeed.ReadAsync(await host.PostAsync(ArrivePath(visit), other.Cookie), HttpStatusCode.Conflict);

            Assert.Equal("visit_status_invalid", problem["code"]!.GetValue<string>());
            Assert.Equal(InvalidStateTitle, problem["title"]!.GetValue<string>());
            Assert.Equal(arrivalBefore, await database.TravelStateAsync(visit));
        }
    }

    [Fact]
    public async Task ConcurrentTravelRequests_ProduceOneTransitionAndSerializeOnTheTechnician()
    {
        var world = await database.SeedWorldAsync();
        await using var host = TechnicianHost.Create(database);
        var me = await database.SeedTechnicianAsync(host, world);
        var pair = await database.SeedTechnicianAsync(host, world, first: "Pia");
        host.SetNow(Noon);

        // AC-12: three identical starts, then three identical arrivals.
        var job = await database.SeedJobAsync(world, me.Member.UserId, me.Profile, "assigned", Noon.AddHours(1));
        var starts = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => host.PostAsync(StartPath(job.Visit), me.Cookie)));
        var startChanges = new List<bool>();

        foreach (var response in starts)
        {
            startChanges.Add((await TechnicianVisitSeed.ReadAsync(response))["changed"]!.GetValue<bool>());
        }

        Assert.Equal(1, startChanges.Count(changed => changed));
        Assert.StartsWith("on_the_way|h=1|t=1|c=0|a=1|", await database.TravelStateAsync(job.Visit));
        Assert.Equal(1, host.Sender.Attempts);

        host.Time.Advance(TimeSpan.FromMinutes(3));
        var arrivals = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => host.PostAsync(ArrivePath(job.Visit), me.Cookie)));
        var arrivalChanges = new List<bool>();

        foreach (var response in arrivals)
        {
            arrivalChanges.Add((await TechnicianVisitSeed.ReadAsync(response))["changed"]!.GetValue<bool>());
        }

        Assert.Equal(1, arrivalChanges.Count(changed => changed));
        Assert.StartsWith("on_the_way|h=1|t=1|c=1|a=2|", await database.TravelStateAsync(job.Visit));
        Assert.Equal(1, await database.AuditCountAsync(job.Visit, "visit.arrived"));
        Assert.Equal(1, host.Sender.Attempts);

        // Two assigned visits of one technician: the profile lock lets one win and refuses the other.
        var first = await database.SeedJobAsync(world, pair.Member.UserId, pair.Profile, "assigned", Noon.AddHours(2));
        var second = await database.SeedJobAsync(world, pair.Member.UserId, pair.Profile, "assigned", Noon.AddHours(4));
        var race = await Task.WhenAll(
            host.PostAsync(StartPath(first.Visit), pair.Cookie),
            host.PostAsync(StartPath(second.Visit), pair.Cookie));

        Assert.Equal(1, race.Count(response => response.StatusCode == HttpStatusCode.OK));
        var loser = Assert.Single(race, response => response.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal("another_visit_active", (await TechnicianVisitSeed.ReadAsync(loser, HttpStatusCode.Conflict))["code"]!.GetValue<string>());
        Assert.Equal(
            1,
            await database.ScalarAsync<long>(
                "SELECT COUNT(*) FROM visits WHERE id IN (@a, @b) AND status = 'on_the_way'", ("a", first.Visit), ("b", second.Visit)));
        Assert.Equal(
            1,
            await database.ScalarAsync<long>(
                "SELECT COUNT(*) FROM visit_time_entries WHERE visit_id IN (@a, @b)", ("a", first.Visit), ("b", second.Visit)));
    }

    [Theory]
    [InlineData(true, true, true, "range", false)]
    [InlineData(true, true, false, "point", false)]
    [InlineData(true, true, true, "none", false)]
    [InlineData(false, true, true, "range", false)]
    [InlineData(true, false, true, "range", false)]
    [InlineData(true, true, true, "range", true)]
    public async Task StartTravel_SendsTheCustomerEmailOnlyWhenConfiguredAndNeverFailsTheRequest(
        bool reminder, bool hasEmail, bool technicianDetails, string window, bool providerFails)
    {
        var world = await database.SeedWorldAsync();
        await using var host = TechnicianHost.Create(database);
        var me = await database.SeedTechnicianAsync(host, world);
        host.SetNow(Noon);
        var start = Noon.AddHours(1);
        var job = await database.SeedJobAsync(world, me.Member.UserId, me.Profile, "assigned", start, title: "Fix <b>drain</b>");
        await database.SetSendFlagsAsync(job.Order, reminder, technicianDetails);
        await database.ExecuteAsync("UPDATE visits SET dispatch_note = 'Gate SECRETNOTE' WHERE id = @v", ("v", job.Visit));
        await database.ExecuteAsync("UPDATE properties SET access_instructions = 'Key SECRETACCESS' WHERE id = @p", ("p", world.Property));
        await database.ExecuteAsync("UPDATE technician_profiles SET phone = '555-SECRETPHONE' WHERE id = @t", ("t", me.Profile));

        if (!hasEmail)
        {
            await database.ExecuteAsync("UPDATE customer_contacts SET email = NULL WHERE customer_id = @c", ("c", world.Customer));
        }

        if (window == "range")
        {
            await database.ExecuteAsync(
                "UPDATE visits SET arrival_window_start = scheduled_start - interval '30 minutes', arrival_window_end = scheduled_start + interval '30 minutes' WHERE id = @v",
                ("v", job.Visit));
        }
        else if (window == "point")
        {
            await database.ExecuteAsync(
                "UPDATE visits SET arrival_window_start = scheduled_start, arrival_window_end = scheduled_start WHERE id = @v", ("v", job.Visit));
        }

        host.Sender.Fail = providerFails;
        var started = await TechnicianVisitSeed.ReadAsync(await host.PostAsync(StartPath(job.Visit), me.Cookie));

        Assert.True(started["changed"]!.GetValue<bool>());
        Assert.Equal("on_the_way", started["visit"]!["status"]!.GetValue<string>());
        Assert.StartsWith("on_the_way|h=1|t=1|c=0|a=1|", await database.TravelStateAsync(job.Visit));

        var expected = reminder && hasEmail;
        Assert.Equal(expected ? 1 : 0, host.Sender.Attempts);
        Assert.Equal(
            expected ? "email" : "none",
            await database.ScalarAsync<string>("SELECT metadata->>'notified' FROM audit_logs WHERE entity_id = @v AND action = 'visit.travel_started'", ("v", job.Visit)));

        if (expected && !providerFails)
        {
            var message = Assert.Single(host.Sender.Messages);
            var prefix = await database.PrefixAsync(world.Org);
            var organization = await database.ScalarAsync<string>("SELECT name FROM organizations WHERE id = @o", ("o", world.Org));
            var text = message.TextBody;

            Assert.Equal($"{organization}: your technician is on the way for {prefix}-{job.Number}", message.Subject);
            Assert.Contains("Your technician is on the way for Fix <b>drain</b> at 1 Seed St.", text);
            Assert.Contains("Fix &lt;b&gt;drain&lt;/b&gt;", message.HtmlBody);
            Assert.DoesNotContain("<b>drain</b>", message.HtmlBody);
            Assert.Equal(window == "range", text.Contains("Scheduled arrival window: 12:30 PM – 1:30 PM.", StringComparison.Ordinal));
            Assert.Equal(window == "point", text.Contains("Scheduled arrival: 1:00 PM.", StringComparison.Ordinal));
            Assert.Equal(window == "none", !text.Contains("Scheduled arrival", StringComparison.Ordinal));
            Assert.Equal(technicianDetails, text.Contains("Your technician: Tess Tech.", StringComparison.Ordinal));
            Assert.DoesNotContain("SECRET", text + message.HtmlBody);
            Assert.DoesNotContain("ETA", text + message.HtmlBody);
        }

        if (providerFails)
        {
            // A send failure keeps the 200 and logs only the visit id and the failure category.
            var failure = Assert.Single(host.Logs.Entries, entry => entry.Level == LogLevel.Warning && entry.Category.Contains("TravelNotifier", StringComparison.Ordinal));

            Assert.Equal(job.Visit.ToString(), failure.Properties["VisitId"]?.ToString());
            Assert.Equal(nameof(InvalidOperationException), failure.Properties["Category"]?.ToString());
            Assert.Null(failure.Exception);
            Assert.DoesNotContain(host.Logs.Entries, entry =>
                entry.Message.Contains("carla@example.com", StringComparison.Ordinal)
                || entry.Message.Contains("1 Seed St", StringComparison.Ordinal)
                || entry.Message.Contains("SECRET", StringComparison.Ordinal));
        }

        // No email on a repeat or on arrival.
        var attempts = host.Sender.Attempts;
        host.Sender.Fail = false;
        await host.PostAsync(StartPath(job.Visit), me.Cookie);
        host.Time.Advance(TimeSpan.FromMinutes(5));
        Assert.True((await TechnicianVisitSeed.ReadAsync(await host.PostAsync(ArrivePath(job.Visit), me.Cookie)))["changed"]!.GetValue<bool>());
        Assert.Equal(attempts, host.Sender.Attempts);
    }

    [Fact]
    public async Task Detail_ReturnsTheRichAndTheMinimalContentAndServesOnlyTheCurrentAssessmentPhotos()
    {
        var world = await database.SeedWorldAsync();
        var bare = await database.SeedWorldAsync(phone: null);
        var foreign = await database.SeedWorldAsync();
        await using var host = TechnicianHost.Create(database);
        var me = await database.SeedTechnicianAsync(host, world);
        var other = await database.SeedTechnicianAsync(host, world, first: "Otto");
        var minimalTech = await database.SeedTechnicianAsync(host, bare);
        host.SetNow(Noon);
        var user = me.Member.UserId;

        // The originating request, quote and version of the work order.
        var request = await database.SeedRequestAsync(world);
        await database.ExecuteAsync("UPDATE service_requests SET has_active_damage = true WHERE id = @r", ("r", request.Id));
        var job = await database.SeedJobAsync(world, user, me.Profile, "assigned", Noon.AddHours(1), title: "Rich job");
        await AttachQuoteAsync(world.Org, user, request.Id, job.Order);
        await database.AssignAsync(job.Visit, other.Profile, user, primary: false);
        await database.ExecuteAsync(
            "UPDATE work_orders SET internal_instructions = 'Call on arrival', scope_snapshot = E'- Replace valve\\n* Test pressure\\n\\u2022 Clean up' WHERE id = @w",
            ("w", job.Order));
        await database.ExecuteAsync("UPDATE visits SET dispatch_note = 'Ring twice' WHERE id = @v", ("v", job.Visit));
        await database.ExecuteAsync("UPDATE customers SET type = 'company' WHERE id = @c", ("c", world.Customer));
        await database.ExecuteAsync("UPDATE properties SET access_instructions = 'Gate code 1234' WHERE id = @p", ("p", world.Property));
        await database.ExecuteAsync(
            "UPDATE customer_contacts SET prefers_email = true, prefers_sms = true WHERE customer_id = @c", ("c", world.Customer));

        foreach (var name in new[] { "Zeta", "Alpha" })
        {
            await database.ExecuteAsync(
                "INSERT INTO work_order_required_skills (work_order_id, skill_id) VALUES (@w, @s)",
                ("w", job.Order),
                ("s", await database.SeedSkillAsync(world.Org, name)));
        }

        await database.ExecuteAsync(
            "INSERT INTO work_order_planned_materials (organization_id, work_order_id, description, quantity, unit, source, sort_order) VALUES (@o, @w, 'Valve', 2.5, 'ea', 'warehouse', 2), (@o, @w, 'Pipe', 3, 'm', 'truck_stock', 1), (@o, @w, 'Sealant', 1, 'tube', 'to_purchase', 3)",
            ("o", world.Org),
            ("w", job.Order));
        await database.ExecuteAsync(
            "INSERT INTO visit_checklist_items (visit_id, label, is_required, is_completed, sort_order) VALUES (@v, 'Clean up', true, false, 2), (@v, 'Shut off water', true, true, 0), (@v, 'Photograph work area', false, false, 1)",
            ("v", job.Visit));

        var oldAssessment = await SeedCompletedAssessmentAsync(world.Org, request.Id, user, Noon.AddDays(-10), "Old diagnosis", "Old scope", "old-notes");
        var currentAssessment = await SeedCompletedAssessmentAsync(world.Org, request.Id, user, Noon.AddDays(-2), "Corroded valve", "Replace the valve", "INTERNALNOTES");
        var scheduledAssessment = await database.SeedAssessmentAsync(world.Org, request.Id, Noon.AddDays(3), Noon.AddDays(3).AddHours(1), user);
        var png = await SeedPhotoAsync(world.Org, currentAssessment, "image/png", [1, 2, 3, 4], Noon.AddDays(-2));
        var jpeg = await SeedPhotoAsync(world.Org, currentAssessment, "image/jpeg", [9, 8, 7], Noon.AddDays(-2).AddMinutes(1));
        var oldPhoto = await SeedPhotoAsync(world.Org, oldAssessment, "image/png", [5, 5], Noon.AddDays(-10));
        var scheduledPhoto = await SeedPhotoAsync(world.Org, scheduledAssessment, "image/png", [6, 6], Noon.AddDays(3));
        var foreignRequest = await database.SeedRequestAsync(foreign);
        var foreignAssessment = await SeedCompletedAssessmentAsync(foreign.Org, foreignRequest.Id, user, Noon.AddDays(-1), "Foreign", "Foreign", null);
        var foreignPhoto = await SeedPhotoAsync(foreign.Org, foreignAssessment, "image/png", [4, 4], Noon.AddDays(-1));

        // AC-04
        var detailResponse = await host.GetAsync($"/technician/visits/{job.Visit}", me.Cookie);
        var text = await detailResponse.Content.ReadAsStringAsync();
        var detail = await TechnicianVisitSeed.ReadAsync(detailResponse);

        Assert.True(detail["isPrimary"]!.GetValue<bool>());
        Assert.Equal("company", detail["customerType"]!.GetValue<string>());
        Assert.Equal("Gate code 1234", detail["access"]!["instructions"]!.GetValue<string>());
        Assert.Equal("email_or_sms", detail["access"]!["contactPreference"]!.GetValue<string>());
        Assert.True(detail["access"]!["activeDamage"]!.GetValue<bool>());
        Assert.Equal("Call on arrival", detail["instructions"]!.GetValue<string>());
        Assert.Equal("Ring twice", detail["dispatchNote"]!.GetValue<string>());
        Assert.Equal("- Replace valve\n* Test pressure\n• Clean up", detail["scope"]!.GetValue<string>());
        Assert.Equal(new[] { "Alpha", "Zeta" }, detail["requiredSkills"]!.AsArray().Select(item => item!.GetValue<string>()));
        Assert.Equal("+1 555 010 0100", detail["officePhone"]!.GetValue<string>());
        Assert.Null(detail["travel"]!["startedAt"]);
        Assert.Equal(new[] { "Pipe", "Valve", "Sealant" }, detail["plannedMaterials"]!.AsArray().Select(item => item!["description"]!.GetValue<string>()));
        Assert.Equal(new[] { "truck_stock", "warehouse", "to_purchase" }, detail["plannedMaterials"]!.AsArray().Select(item => item!["source"]!.GetValue<string>()));
        Assert.Equal(2.5m, detail["plannedMaterials"]![1]!["quantity"]!.GetValue<decimal>());
        Assert.Equal(new[] { "Shut off water", "Photograph work area", "Clean up" }, detail["tasks"]!.AsArray().Select(item => item!["label"]!.GetValue<string>()));
        Assert.Equal(new[] { true, false, true }, detail["tasks"]!.AsArray().Select(item => item!["isRequired"]!.GetValue<bool>()));
        Assert.Equal(new[] { true, false, false }, detail["tasks"]!.AsArray().Select(item => item!["isCompleted"]!.GetValue<bool>()));
        Assert.Equal("Corroded valve", detail["assessment"]!["diagnosis"]!.GetValue<string>());
        Assert.Equal("Replace the valve", detail["assessment"]!["recommendedScope"]!.GetValue<string>());
        Assert.Equal(Noon.AddDays(-2), At(detail["assessment"]!["completedAt"]));
        Assert.Equal(new[] { png, jpeg }, detail["assessment"]!["photos"]!.AsArray().Select(item => item!["id"]!.GetValue<Guid>()));
        Assert.DoesNotContain("INTERNALNOTES", text);
        Assert.DoesNotContain("internalNotes", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(oldPhoto.ToString(), text);
        Assert.DoesNotContain(scheduledPhoto.ToString(), text);

        // BR-03: one channel is that channel (the schema requires at least one; no primary contact is covered below).
        foreach (var (email, sms, expected) in new[] { (true, false, "email"), (false, true, "sms") })
        {
            await database.ExecuteAsync(
                "UPDATE customer_contacts SET prefers_email = @e, prefers_sms = @s WHERE customer_id = @c", ("e", email), ("s", sms), ("c", world.Customer));
            var variant = await TechnicianVisitSeed.ReadAsync(await host.GetAsync($"/technician/visits/{job.Visit}", me.Cookie));

            Assert.Equal(expected, variant["access"]!["contactPreference"]?.GetValue<string>());
        }

        // AC-06: only a photo of the shown assessment is served, with the BR-06 headers; a non-primary may read it too.
        var image = await host.GetAsync($"/technician/visits/{job.Visit}/assessment-photos/{png}", me.Cookie);

        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.Equal("image/png", image.Content.Headers.ContentType?.MediaType);
        Assert.Equal("inline", image.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("nosniff", image.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("no-store", image.Headers.CacheControl?.ToString());
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, await image.Content.ReadAsByteArrayAsync());

        var jpegResponse = await host.GetAsync($"/technician/visits/{job.Visit}/assessment-photos/{jpeg}", other.Cookie);
        Assert.Equal("image/jpeg", jpegResponse.Content.Headers.ContentType?.MediaType);
        Assert.False((await TechnicianVisitSeed.ReadAsync(await host.GetAsync($"/technician/visits/{job.Visit}", other.Cookie)))["isPrimary"]!.GetValue<bool>());

        var rejected = new HashSet<string>();

        foreach (var photo in new[] { oldPhoto, scheduledPhoto, foreignPhoto, Guid.NewGuid() })
        {
            var response = await host.GetAsync($"/technician/visits/{job.Visit}/assessment-photos/{photo}", me.Cookie);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Null(response.Content.Headers.ContentDisposition);
            rejected.Add(await TechnicianVisitSeed.WithoutTraceAsync(response));
        }

        Assert.Single(rejected);

        // AC-05: a minimal visit.
        await database.ExecuteAsync("UPDATE customer_contacts SET is_primary = false WHERE customer_id = @c", ("c", bare.Customer));
        var minimal = await database.SeedJobAsync(bare, minimalTech.Member.UserId, minimalTech.Profile, "assigned", Noon.AddHours(1));
        var minimalResponse = await TechnicianVisitSeed.ReadAsync(await host.GetAsync($"/technician/visits/{minimal.Visit}", minimalTech.Cookie));

        Assert.Equal("person", minimalResponse["customerType"]!.GetValue<string>());
        Assert.Null(minimalResponse["access"]!["instructions"]);
        Assert.Null(minimalResponse["access"]!["contactPreference"]);
        Assert.False(minimalResponse["access"]!["activeDamage"]!.GetValue<bool>());
        Assert.Null(minimalResponse["instructions"]);
        Assert.Null(minimalResponse["dispatchNote"]);
        Assert.Equal("Scope", minimalResponse["scope"]!.GetValue<string>());
        Assert.Empty(minimalResponse["requiredSkills"]!.AsArray());
        Assert.Null(minimalResponse["officePhone"]);
        Assert.Null(minimalResponse["travel"]!["startedAt"]);
        Assert.Null(minimalResponse["travel"]!["arrivedAt"]);
        Assert.Null(minimalResponse["travel"]!["durationMinutes"]);
        Assert.Empty(minimalResponse["plannedMaterials"]!.AsArray());
        Assert.Empty(minimalResponse["tasks"]!.AsArray());
        Assert.Null(minimalResponse["assessment"]);
    }

    private async Task AttachQuoteAsync(Guid org, Guid user, Guid request, Guid order)
    {
        var quote = Guid.NewGuid();
        var version = Guid.NewGuid();

        await database.ExecuteAsync(
            """
            INSERT INTO quotes (id, organization_id, request_id, quote_number, status, created_by_user_id)
            VALUES (@q, @o, @r, 1, 'draft', @u);
            INSERT INTO quote_versions (id, organization_id, quote_id, version_no, scope, subtotal, tax_total, total, currency, created_by_user_id)
            VALUES (@v, @o, @q, 1, 'Scope', 0, 0, 0, 'USD', @u);
            UPDATE work_orders SET quote_version_id = @v WHERE id = @w;
            """,
            ("q", quote),
            ("v", version),
            ("o", org),
            ("r", request),
            ("u", user),
            ("w", order));
    }

    private async Task<Guid> SeedCompletedAssessmentAsync(
        Guid org, Guid request, Guid user, DateTimeOffset completedAt, string diagnosis, string scope, string? internalNotes)
    {
        var id = await database.SeedAssessmentAsync(org, request, completedAt.AddHours(-2), completedAt.AddHours(-1), user);

        await database.ExecuteAsync(
            "UPDATE assessments SET status = 'completed', completed_at = @c, diagnosis = @d, recommended_scope = @s, internal_notes = @n WHERE id = @a",
            ("c", completedAt),
            ("d", diagnosis),
            ("s", scope),
            ("n", internalNotes),
            ("a", id));

        return id;
    }

    private async Task<Guid> SeedPhotoAsync(Guid org, Guid assessment, string mime, byte[] content, DateTimeOffset createdAt)
    {
        var id = Guid.NewGuid();

        await database.ExecuteAsync(
            "INSERT INTO assessment_attachments (id, organization_id, assessment_id, file_name, content, mime_type, size_bytes, created_at) VALUES (@id, @o, @a, 'photo', @c, @m, @n, @t)",
            ("id", id),
            ("o", org),
            ("a", assessment),
            ("c", content),
            ("m", mime),
            ("n", (long)content.Length),
            ("t", createdAt));

        return id;
    }
}
