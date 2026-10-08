using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.Catalog;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.Dispatch;
using FieldOps.IntegrationTests.ServiceRequests;
using FieldOps.IntegrationTests.Team;
using Microsoft.Extensions.Logging;

namespace FieldOps.IntegrationTests.TechnicianVisits;

/// <summary>Job progress: start job, pause and resume, tasks, materials, photos and notes (mobile-job-progress AC-01 to AC-15).</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class TechnicianProgressTests(CompanySettingsDatabaseFixture database)
{
    private const string NotAvailable = "This job isn't available.";

    private const string EditInvalid = "This job can't be updated in its current state.";

    private const string PauseResumeInvalid = "This job can't be paused or resumed in its current state.";

    private const string PhotoMessage = "Upload a JPEG or PNG image up to 10 MB.";

    private static readonly DateTimeOffset Noon = TechnicianVisitSeed.Utc("2026-06-10T12:00:00Z");

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];

    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 9, 8, 7];

    private static string Path(Guid visit, string suffix = "") => $"/technician/visits/{visit}{suffix}";

    private static DateTimeOffset At(JsonNode? value) => DateTimeOffset.Parse(value!.GetValue<string>(), CultureInfo.InvariantCulture);

    private static JsonNode TaskOf(JsonNode detail, Guid id) =>
        detail["tasks"]!.AsArray().Single(task => task!["id"]!.GetValue<Guid>() == id)!;

    private static async Task<JsonNode> ProblemAsync(HttpResponseMessage response, HttpStatusCode expected) =>
        await TechnicianVisitSeed.ReadAsync(response, expected);

    private sealed record Children(Guid Task, Guid Optional, Guid Planned, Guid Material, Guid Evidence);

    private sealed record Call(string Name, bool Mutation, bool UsesNestedIds, Func<string?, Task<HttpResponseMessage>> Send);

    /// <summary>Two tasks (required first), one planned material, one additional material and one photo for a visit.</summary>
    private async Task<Children> SeedChildrenAsync(RequestWorld world, SeededOrder job, Guid user)
    {
        var children = new Children(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        await database.ExecuteAsync(
            "INSERT INTO visit_checklist_items (id, visit_id, label, is_required, sort_order) VALUES (@a, @v, 'Shut off water', true, 0), (@b, @v, 'Photograph', false, 1)",
            ("a", children.Task),
            ("b", children.Optional),
            ("v", job.Visit));
        await database.ExecuteAsync(
            "INSERT INTO work_order_planned_materials (id, organization_id, work_order_id, description, quantity, unit, source, sort_order) VALUES (@p, @o, @w, 'Valve', 2.5, 'ea', 'warehouse', 0)",
            ("p", children.Planned),
            ("o", world.Org),
            ("w", job.Order));
        await database.ExecuteAsync(
            "INSERT INTO visit_materials (id, visit_id, description, quantity, unit) VALUES (@m, @v, 'Tape', 1, 'roll')",
            ("m", children.Material),
            ("v", job.Visit));
        await database.ExecuteAsync(
            "INSERT INTO visit_evidence (id, visit_id, file_name, content, mime_type, size_bytes, evidence_type, uploaded_by_user_id) VALUES (@e, @v, 'seed.png', @c, 'image/png', @n, 'before', @u)",
            ("e", children.Evidence),
            ("v", job.Visit),
            ("c", Png),
            ("n", (long)Png.Length),
            ("u", user));

        return children;
    }

    /// <summary>Every endpoint of the feature against one visit; the bodies are valid so only the authorization can refuse them.</summary>
    private static List<Call> Calls(TechnicianHost host, Guid visit, Children c, Guid catalogItem) =>
    [
        new("detail", false, false, cookie => host.GetAsync(Path(visit), cookie)),
        new("start-job", true, false, cookie => host.PostAsync(Path(visit, "/start-job"), cookie)),
        new("pause", true, false, cookie => host.PostAsync(Path(visit, "/pause"), cookie)),
        new("resume", true, false, cookie => host.PostAsync(Path(visit, "/resume"), cookie)),
        new("task", true, true, cookie => host.SendAsync(HttpMethod.Patch, Path(visit, $"/tasks/{c.Task}"), cookie, new JsonObject { ["isCompleted"] = true })),
        new("add-task", true, false, cookie => host.SendAsync(HttpMethod.Post, Path(visit, "/tasks"), cookie, new JsonObject { ["label"] = "Extra" })),
        new("planned", true, true, cookie => host.SendAsync(HttpMethod.Put, Path(visit, $"/planned-materials/{c.Planned}"), cookie, new JsonObject { ["usedQuantity"] = 1 })),
        new("add-material", true, true, cookie => host.SendAsync(HttpMethod.Post, Path(visit, "/materials"), cookie, new JsonObject { ["quantity"] = 1, ["catalogItemId"] = catalogItem })),
        new("material", true, true, cookie => host.SendAsync(HttpMethod.Put, Path(visit, $"/materials/{c.Material}"), cookie, new JsonObject { ["quantity"] = 2 })),
        new("catalog", false, false, cookie => host.GetAsync(Path(visit, "/material-catalog?search=pipe"), cookie)),
        new("upload", true, false, cookie => host.UploadAsync(Path(visit, "/evidence"), cookie, Png, "image/png", "before")),
        new("evidence", false, true, cookie => host.GetAsync(Path(visit, $"/evidence/{c.Evidence}"), cookie)),
        new("delete-evidence", true, true, cookie => host.SendAsync(HttpMethod.Delete, Path(visit, $"/evidence/{c.Evidence}"), cookie)),
        new("notes", true, false, cookie => host.SendAsync(HttpMethod.Put, Path(visit, "/notes"), cookie, new JsonObject { ["notes"] = "Secret note" })),
    ];

    [Fact]
    public async Task ProgressEndpoints_DenyUnavailableVisitsForeignNestedIdsNonPrimaryAndUnusableProfilesWithoutWriting()
    {
        var world = await database.SeedWorldAsync();
        var foreign = await database.SeedWorldAsync();
        await using var host = TechnicianHost.Create(database);
        var me = await database.SeedTechnicianAsync(host, world);
        var other = await database.SeedTechnicianAsync(host, world, first: "Otto");
        var walker = await database.SeedTechnicianAsync(host, world, first: "Walt");
        var inactive = await database.SeedTechnicianAsync(host, world, "inactive", "Ina");
        var foreignTech = await database.SeedTechnicianAsync(host, foreign, first: "Fay");
        var (unlinked, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.TechnicianRoleId, "Una", "Linked");
        host.SetNow(Noon);
        var user = me.Member.UserId;
        var foreignItem = await database.SeedCatalogItemAsync(foreign.Org, "product", "Pipe foreign");

        var mine = await database.SeedJobAsync(world, user, me.Profile, "in_progress", Noon);
        var mineChildren = await SeedChildrenAsync(world, mine, user);
        await database.AssignAsync(mine.Visit, walker.Profile, user, primary: false);

        // AC-01: other technicians', released, unscheduled, cancelled, other-organization and random visits.
        var hidden = new List<(Guid Visit, Children Children)>();

        foreach (var (technician, status) in new[]
        {
            (other.Profile, "in_progress"),
            (me.Profile, "in_progress"),
            (me.Profile, "unscheduled"),
            (me.Profile, "cancelled"),
        })
        {
            var seeded = await database.SeedJobAsync(world, user, technician, status, Noon, title: "Secret job");

            if (hidden.Count == 1)
            {
                await database.UnassignAsync(seeded.Visit);
            }

            hidden.Add((seeded.Visit, await SeedChildrenAsync(world, seeded, user)));
        }

        var foreignJob = await database.SeedJobAsync(foreign, foreignTech.Member.UserId, foreignTech.Profile, "in_progress", Noon, title: "Secret foreign job");
        var foreignChildren = await SeedChildrenAsync(foreign, foreignJob, foreignTech.Member.UserId);
        hidden.Add((foreignJob.Visit, foreignChildren));
        hidden.Add((Guid.NewGuid(), new Children(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid())));

        var states = new Dictionary<Guid, string>();

        foreach (var (visit, _) in hidden.Take(hidden.Count - 1))
        {
            states[visit] = await database.ProgressStateAsync(visit);
        }

        var bodies = new HashSet<string>();

        foreach (var (visit, children) in hidden)
        {
            foreach (var call in Calls(host, visit, children, foreignItem))
            {
                var response = await call.Send(me.Cookie);
                var text = await response.Content.ReadAsStringAsync();

                Assert.True(response.StatusCode == HttpStatusCode.NotFound, $"{call.Name}: {response.StatusCode} {text}");
                Assert.DoesNotContain("Secret", text);
                bodies.Add(await TechnicianVisitSeed.WithoutTraceAsync(response));
            }
        }

        // AC-01: ids of another visit, another work order and another organization through the caller's own visit.
        var mineBefore = await database.ProgressStateAsync(mine.Visit);

        foreach (var foreignIds in new[] { hidden[0].Children, foreignChildren })
        {
            foreach (var call in Calls(host, mine.Visit, foreignIds, foreignItem).Where(call => call.UsesNestedIds))
            {
                var response = await call.Send(me.Cookie);

                Assert.True(response.StatusCode == HttpStatusCode.NotFound, $"{call.Name}: {response.StatusCode}");
                bodies.Add(await TechnicianVisitSeed.WithoutTraceAsync(response));
            }
        }

        Assert.Equal(mineBefore, await database.ProgressStateAsync(mine.Visit));

        foreach (var (visit, state) in states)
        {
            Assert.Equal(state, await database.ProgressStateAsync(visit));
        }

        var notFound = Assert.Single(bodies);
        Assert.DoesNotContain("\"code\"", notFound);
        Assert.Equal(NotAvailable, JsonNode.Parse(notFound)!["title"]!.GetValue<string>());

        // AC-02: an actively assigned non-primary technician reads everything and writes nothing.
        foreach (var call in Calls(host, mine.Visit, mineChildren, foreignItem))
        {
            var response = await call.Send(walker.Cookie);

            if (call.Mutation)
            {
                var problem = await ProblemAsync(response, HttpStatusCode.Forbidden);

                Assert.Equal("not_primary_technician", problem["code"]!.GetValue<string>());
                Assert.Equal("The primary technician manages this job.", problem["title"]!.GetValue<string>());
            }
            else
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
        }

        Assert.Equal(mineBefore, await database.ProgressStateAsync(mine.Visit));

        // AC-02: no linked profile is a 404 with a code; an inactive profile is a 403 with a code, for reads and writes.
        foreach (var call in Calls(host, mine.Visit, mineChildren, foreignItem))
        {
            var unlinkedProblem = await ProblemAsync(await call.Send(unlinked), HttpStatusCode.NotFound);
            var inactiveProblem = await ProblemAsync(await call.Send(inactive.Cookie), HttpStatusCode.Forbidden);

            Assert.Equal("technician_profile_not_linked", unlinkedProblem["code"]!.GetValue<string>());
            Assert.Equal("technician_inactive", inactiveProblem["code"]!.GetValue<string>());
        }

        Assert.Equal(mineBefore, await database.ProgressStateAsync(mine.Visit));
    }

    [Fact]
    public async Task StartJob_WritesOnceStartsAScheduledWorkOrderOnlyAndRefusesEveryOtherState()
    {
        var world = await database.SeedWorldAsync();
        await using var host = TechnicianHost.Create(database);
        var me = await database.SeedTechnicianAsync(host, world);
        host.SetNow(Noon);
        var user = me.Member.UserId;
        var job = await database.SeedJobAsync(world, user, me.Profile, "on_the_way", Noon.AddHours(1));
        await database.SeedTravelEntryAsync(job.Visit, me.Profile, Noon.AddMinutes(-20), Noon.AddMinutes(-5));

        // AC-03: the effective start.
        var response = await host.PostAsync(Path(job.Visit, "/start-job"), me.Cookie);
        var started = await TechnicianVisitSeed.ReadAsync(response);

        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.True(started["changed"]!.GetValue<bool>());
        Assert.Equal("in_progress", started["visit"]!["status"]!.GetValue<string>());
        Assert.Equal(Noon, At(started["visit"]!["actualStartedAt"]));
        Assert.Equal("work", started["visit"]!["time"]!["activeEntry"]!["type"]!.GetValue<string>());
        Assert.Equal(Noon, At(started["visit"]!["time"]!["activeEntry"]!["startedAt"]));

        Assert.Equal(
            1,
            await database.ScalarAsync<long>(
                "SELECT COUNT(*) FROM visits WHERE id = @v AND status = 'in_progress' AND actual_started_at = @n AND updated_at >= @n",
                ("v", job.Visit),
                ("n", Noon)));
        Assert.Equal(
            1,
            await database.ScalarAsync<long>(
                "SELECT COUNT(*) FROM visit_status_history WHERE visit_id = @v AND from_status = 'on_the_way' AND to_status = 'in_progress' AND changed_by_user_id = @u AND reason IS NULL AND changed_at = @n",
                ("v", job.Visit),
                ("u", user),
                ("n", Noon)));
        Assert.Equal(
            1,
            await database.ScalarAsync<long>(
                "SELECT COUNT(*) FROM visit_time_entries WHERE visit_id = @v AND technician_id = @t AND entry_type = 'work' AND started_at = @n AND ended_at IS NULL",
                ("v", job.Visit),
                ("t", me.Profile),
                ("n", Noon)));
        Assert.Equal("in_progress", await database.ScalarAsync<string>("SELECT status::text FROM work_orders WHERE id = @w", ("w", job.Order)));
        Assert.Equal(
            1,
            await database.ScalarAsync<long>(
                """
                SELECT COUNT(*) FROM audit_logs WHERE entity_id = @v AND action = 'visit.job_started' AND entity_type = 'visit'
                    AND organization_id = @o AND actor_user_id = @u AND branch_id = @b
                    AND before_data->>'status' = 'on_the_way' AND after_data->>'status' = 'in_progress'
                    AND metadata->>'workOrderId' = @w AND metadata->>'visitNumber' = '1' AND metadata->>'workOrderStatusChanged' = 'true'
                """,
                ("v", job.Visit),
                ("o", world.Org),
                ("u", user),
                ("b", world.BranchA),
                ("w", job.Order.ToString())));

        var audit = await DispatchSeed.AuditTextAsync(database, job.Visit);
        Assert.DoesNotContain("Carla", audit);
        Assert.DoesNotContain("carla@example.com", audit);
        Assert.DoesNotContain("1 Seed St", audit);
        Assert.Equal(0, host.Sender.Attempts);

        // AC-03: the repeat writes nothing.
        var after = await database.ProgressStateAsync(job.Visit);
        host.Time.Advance(TimeSpan.FromMinutes(5));
        var repeated = await TechnicianVisitSeed.ReadAsync(await host.PostAsync(Path(job.Visit, "/start-job"), me.Cookie));

        Assert.False(repeated["changed"]!.GetValue<bool>());
        Assert.Equal(Noon, At(repeated["visit"]!["actualStartedAt"]));
        Assert.Equal(after, await database.ProgressStateAsync(job.Visit));

        // AC-04: before arrival, from another status or after completion nothing starts.
        foreach (var (status, travelOpen) in new[] { ("assigned", false), ("on_the_way", true), ("on_the_way", false), ("completed", false) })
        {
            var refused = await database.SeedJobAsync(world, user, me.Profile, status, Noon.AddHours(2));

            if (travelOpen)
            {
                await database.SeedTravelEntryAsync(refused.Visit, me.Profile, Noon.AddMinutes(-5));
            }

            var before = await database.ProgressStateAsync(refused.Visit);
            var problem = await ProblemAsync(await host.PostAsync(Path(refused.Visit, "/start-job"), me.Cookie), HttpStatusCode.Conflict);

            Assert.Equal("visit_status_invalid", problem["code"]!.GetValue<string>());
            Assert.Equal("This job can't be started in its current state.", problem["title"]!.GetValue<string>());
            Assert.Equal(before, await database.ProgressStateAsync(refused.Visit));
        }

        // AC-04: a work order that is already in progress stays untouched.
        var running = await database.SeedJobAsync(world, user, me.Profile, "on_the_way", Noon.AddHours(3));
        await database.SeedTravelEntryAsync(running.Visit, me.Profile, Noon.AddMinutes(-20), Noon.AddMinutes(-15));
        await database.ExecuteAsync("UPDATE work_orders SET status = 'in_progress' WHERE id = @w", ("w", running.Order));
        var orderUpdated = await database.ScalarAsync<DateTimeOffset>("SELECT updated_at FROM work_orders WHERE id = @w", ("w", running.Order));

        var second = await TechnicianVisitSeed.ReadAsync(await host.PostAsync(Path(running.Visit, "/start-job"), me.Cookie));

        Assert.True(second["changed"]!.GetValue<bool>());
        Assert.Equal(orderUpdated, await database.ScalarAsync<DateTimeOffset>("SELECT updated_at FROM work_orders WHERE id = @w", ("w", running.Order)));
        Assert.Equal(
            1,
            await database.ScalarAsync<long>(
                "SELECT COUNT(*) FROM audit_logs WHERE entity_id = @v AND action = 'visit.job_started' AND metadata->>'workOrderStatusChanged' = 'false'",
                ("v", running.Visit)));
    }

    [Fact]
    public async Task PauseAndResume_SwitchEntriesOnceAndEveryStatusGuardRefusesWithoutWriting()
    {
        var world = await database.SeedWorldAsync();
        await using var host = TechnicianHost.Create(database);
        var me = await database.SeedTechnicianAsync(host, world);
        host.SetNow(Noon);
        var user = me.Member.UserId;
        var job = await database.SeedJobAsync(world, user, me.Profile, "in_progress", Noon);
        await database.SeedEntryAsync(job.Visit, me.Profile, "work", Noon.AddMinutes(-20));

        // AC-05: pause closes the work entry and opens the pause entry.
        var paused = await TechnicianVisitSeed.ReadAsync(await host.PostAsync(Path(job.Visit, "/pause"), me.Cookie));

        Assert.True(paused["changed"]!.GetValue<bool>());
        Assert.Equal("paused", paused["visit"]!["status"]!.GetValue<string>());
        Assert.Equal("pause", paused["visit"]!["time"]!["activeEntry"]!["type"]!.GetValue<string>());
        Assert.Equal(1200, paused["visit"]!["time"]!["workSeconds"]!.GetValue<int>());
        Assert.Equal(
            1,
            await database.ScalarAsync<long>(
                "SELECT COUNT(*) FROM visit_time_entries WHERE visit_id = @v AND entry_type = 'work' AND ended_at = @n",
                ("v", job.Visit),
                ("n", Noon)));
        Assert.Equal(
            1,
            await database.ScalarAsync<long>(
                "SELECT COUNT(*) FROM visit_time_entries WHERE visit_id = @v AND entry_type = 'pause' AND started_at = @n AND ended_at IS NULL AND technician_id = @t",
                ("v", job.Visit),
                ("n", Noon),
                ("t", me.Profile)));
        Assert.Equal(
            1,
            await database.ScalarAsync<long>(
                "SELECT COUNT(*) FROM visit_status_history WHERE visit_id = @v AND from_status = 'in_progress' AND to_status = 'paused' AND reason IS NULL AND changed_by_user_id = @u",
                ("v", job.Visit),
                ("u", user)));
        Assert.Equal(
            1,
            await database.ScalarAsync<long>(
                "SELECT COUNT(*) FROM audit_logs WHERE entity_id = @v AND action = 'visit.paused' AND metadata->>'workOrderId' = @w AND metadata->>'visitNumber' = '1'",
                ("v", job.Visit),
                ("w", job.Order.ToString())));

        var afterPause = await database.ProgressStateAsync(job.Visit);
        var repeatedPause = await TechnicianVisitSeed.ReadAsync(await host.PostAsync(Path(job.Visit, "/pause"), me.Cookie));

        Assert.False(repeatedPause["changed"]!.GetValue<bool>());
        Assert.Equal(afterPause, await database.ProgressStateAsync(job.Visit));

        // AC-06: edits are allowed while paused.
        var edited = await TechnicianVisitSeed.ReadAsync(
            await host.SendAsync(HttpMethod.Put, Path(job.Visit, "/notes"), me.Cookie, new JsonObject { ["notes"] = "Waiting for parts" }));
        Assert.Equal("Waiting for parts", edited["technicianNotes"]!.GetValue<string>());

        // AC-05: resume closes the pause entry, adds its whole seconds and opens a work entry.
        host.Time.Advance(TimeSpan.FromSeconds(90.5));
        var resumed = await TechnicianVisitSeed.ReadAsync(await host.PostAsync(Path(job.Visit, "/resume"), me.Cookie));

        Assert.True(resumed["changed"]!.GetValue<bool>());
        Assert.Equal("in_progress", resumed["visit"]!["status"]!.GetValue<string>());
        Assert.Equal("work", resumed["visit"]!["time"]!["activeEntry"]!["type"]!.GetValue<string>());
        Assert.Equal(90, resumed["visit"]!["time"]!["pauseSeconds"]!.GetValue<int>());
        Assert.Equal(1200, resumed["visit"]!["time"]!["workSeconds"]!.GetValue<int>());
        Assert.Equal(90, await database.ScalarAsync<int>("SELECT pause_seconds FROM visits WHERE id = @v", ("v", job.Visit)));
        Assert.Equal(3, await database.ScalarAsync<long>("SELECT COUNT(*) FROM visit_time_entries WHERE visit_id = @v", ("v", job.Visit)));
        Assert.Equal(
            1,
            await database.ScalarAsync<long>(
                "SELECT COUNT(*) FROM visit_time_entries WHERE visit_id = @v AND ended_at IS NULL AND entry_type = 'work'", ("v", job.Visit)));
        Assert.Equal(
            0,
            await database.ScalarAsync<long>(
                "SELECT COUNT(*) FROM visit_time_entries WHERE visit_id = @v AND ended_at IS NOT NULL AND ended_at <= started_at", ("v", job.Visit)));
        Assert.Equal(
            1,
            await database.ScalarAsync<long>(
                "SELECT COUNT(*) FROM audit_logs WHERE entity_id = @v AND action = 'visit.resumed'", ("v", job.Visit)));

        var afterResume = await database.ProgressStateAsync(job.Visit);
        var repeatedResume = await TechnicianVisitSeed.ReadAsync(await host.PostAsync(Path(job.Visit, "/resume"), me.Cookie));

        Assert.False(repeatedResume["changed"]!.GetValue<bool>());
        Assert.Equal(afterResume, await database.ProgressStateAsync(job.Visit));

        // AC-06: a visit that is not being worked refuses pause, resume and every edit.
        var stranger = Guid.NewGuid();
        var refusals = new List<(Guid Visit, string Name, Func<Guid, Task<HttpResponseMessage>> Send, string Message)>();

        foreach (var (status, travelOpen) in new[] { ("on_the_way", true), ("assigned", false), ("completed", false) })
        {
            var refused = await database.SeedJobAsync(world, user, me.Profile, status, Noon.AddHours(2));

            if (travelOpen)
            {
                await database.SeedTravelEntryAsync(refused.Visit, me.Profile, Noon.AddMinutes(-5));
            }

            refusals.Add((refused.Visit, status, visit => host.PostAsync(Path(visit, "/pause"), me.Cookie), PauseResumeInvalid));
            refusals.Add((refused.Visit, status, visit => host.PostAsync(Path(visit, "/resume"), me.Cookie), PauseResumeInvalid));
            refusals.Add((refused.Visit, status, visit => host.SendAsync(HttpMethod.Post, Path(visit, "/tasks"), me.Cookie, new JsonObject { ["label"] = "x" }), EditInvalid));
            refusals.Add((refused.Visit, status, visit => host.SendAsync(HttpMethod.Patch, Path(visit, $"/tasks/{stranger}"), me.Cookie, new JsonObject { ["isCompleted"] = true }), EditInvalid));
            refusals.Add((refused.Visit, status, visit => host.SendAsync(HttpMethod.Put, Path(visit, $"/planned-materials/{stranger}"), me.Cookie, new JsonObject { ["usedQuantity"] = 1 }), EditInvalid));
            refusals.Add((refused.Visit, status, visit => host.SendAsync(HttpMethod.Post, Path(visit, "/materials"), me.Cookie, new JsonObject { ["quantity"] = 1, ["description"] = "x", ["unit"] = "ea" }), EditInvalid));
            refusals.Add((refused.Visit, status, visit => host.SendAsync(HttpMethod.Put, Path(visit, $"/materials/{stranger}"), me.Cookie, new JsonObject { ["quantity"] = 1 }), EditInvalid));
            refusals.Add((refused.Visit, status, visit => host.UploadAsync(Path(visit, "/evidence"), me.Cookie, Png, "image/png", "before"), EditInvalid));
            refusals.Add((refused.Visit, status, visit => host.SendAsync(HttpMethod.Delete, Path(visit, $"/evidence/{stranger}"), me.Cookie), EditInvalid));
            refusals.Add((refused.Visit, status, visit => host.SendAsync(HttpMethod.Put, Path(visit, "/notes"), me.Cookie, new JsonObject { ["notes"] = "x" }), EditInvalid));
        }

        // AC-06: a paused visit without its pause entry cannot resume; a visit in progress without its work entry cannot pause.
        var orphanPaused = await database.SeedJobAsync(world, user, me.Profile, "paused", Noon.AddHours(3));
        var orphanWorking = await database.SeedJobAsync(world, user, me.Profile, "in_progress", Noon.AddHours(4));
        refusals.Add((orphanPaused.Visit, "paused", visit => host.PostAsync(Path(visit, "/resume"), me.Cookie), PauseResumeInvalid));
        refusals.Add((orphanWorking.Visit, "in_progress", visit => host.PostAsync(Path(visit, "/pause"), me.Cookie), PauseResumeInvalid));

        foreach (var (visit, name, send, message) in refusals)
        {
            var before = await database.ProgressStateAsync(visit);
            var response = await send(visit);
            var problem = await ProblemAsync(response, HttpStatusCode.Conflict);

            Assert.True(
                problem["code"]!.GetValue<string>() == "visit_status_invalid" && problem["title"]!.GetValue<string>() == message,
                $"{name}: {problem.ToJsonString()}");
            Assert.Equal(before, await database.ProgressStateAsync(visit));
        }

        // BR-05: pausing a paused visit stays an unchanged repeat even without the entry.
        var unchanged = await TechnicianVisitSeed.ReadAsync(await host.PostAsync(Path(orphanPaused.Visit, "/pause"), me.Cookie));
        Assert.False(unchanged["changed"]!.GetValue<bool>());
    }

    [Fact]
    public async Task ConcurrentTransitions_ProduceExactlyOneChangePerTransition()
    {
        var world = await database.SeedWorldAsync();
        await using var host = TechnicianHost.Create(database);
        var me = await database.SeedTechnicianAsync(host, world);
        host.SetNow(Noon);
        var job = await database.SeedJobAsync(world, me.Member.UserId, me.Profile, "on_the_way", Noon.AddHours(1));
        await database.SeedTravelEntryAsync(job.Visit, me.Profile, Noon.AddMinutes(-20), Noon.AddMinutes(-5));

        // AC-07: three identical requests per transition, each in turn.
        foreach (var (action, history, entries, closedEntries, audit) in new[]
        {
            ("start-job", 1, 1, 0, "visit.job_started"),
            ("pause", 2, 2, 1, "visit.paused"),
            ("resume", 3, 3, 2, "visit.resumed"),
        })
        {
            host.Time.Advance(TimeSpan.FromSeconds(30));

            var responses = await Task.WhenAll(
                Enumerable.Range(0, 3).Select(_ => host.PostAsync(Path(job.Visit, $"/{action}"), me.Cookie)));
            var changes = new List<bool>();

            foreach (var response in responses)
            {
                changes.Add((await TechnicianVisitSeed.ReadAsync(response))["changed"]!.GetValue<bool>());
            }

            Assert.True(changes.Count(changed => changed) == 1, $"{action}: {string.Join(',', changes)}");
            Assert.Equal(history, await database.ScalarAsync<long>("SELECT COUNT(*) FROM visit_status_history WHERE visit_id = @v", ("v", job.Visit)));
            Assert.Equal(
                entries,
                await database.ScalarAsync<long>("SELECT COUNT(*) FROM visit_time_entries WHERE visit_id = @v AND entry_type <> 'travel'", ("v", job.Visit)));
            Assert.Equal(
                closedEntries,
                await database.ScalarAsync<long>(
                    "SELECT COUNT(*) FROM visit_time_entries WHERE visit_id = @v AND entry_type <> 'travel' AND ended_at IS NOT NULL", ("v", job.Visit)));
            Assert.Equal(1, await database.AuditCountAsync(job.Visit, audit));
            Assert.Equal(
                1,
                await database.ScalarAsync<long>(
                    "SELECT COUNT(*) FROM visit_time_entries WHERE visit_id = @v AND entry_type <> 'travel' AND ended_at IS NULL", ("v", job.Visit)));
        }
    }

    [Fact]
    public async Task TaskEndpoints_CompleteCommentAndAddOptionalTasksWithinTheLimits()
    {
        var world = await database.SeedWorldAsync();
        await using var host = TechnicianHost.Create(database);
        var me = await database.SeedTechnicianAsync(host, world);
        host.SetNow(Noon);
        var user = me.Member.UserId;
        var job = await database.SeedJobAsync(world, user, me.Profile, "in_progress", Noon);
        var seeded = await SeedChildrenAsync(world, job, user);
        var path = Path(job.Visit, $"/tasks/{seeded.Task}");

        // AC-08: completing sets completer and time, repeating is a no-op, uncompleting clears all three.
        var completed = await TechnicianVisitSeed.ReadAsync(
            await host.SendAsync(HttpMethod.Patch, path, me.Cookie, new JsonObject { ["isCompleted"] = true }));

        Assert.True(TaskOf(completed, seeded.Task)["isCompleted"]!.GetValue<bool>());
        Assert.Equal(Noon, At(TaskOf(completed, seeded.Task)["completedAt"]));
        Assert.Equal(
            1,
            await database.ScalarAsync<long>(
                "SELECT COUNT(*) FROM visit_checklist_items WHERE id = @t AND is_completed AND completed_by_user_id = @u AND completed_at = @n",
                ("t", seeded.Task),
                ("u", user),
                ("n", Noon)));

        var afterComplete = await database.ProgressStateAsync(job.Visit);
        host.Time.Advance(TimeSpan.FromMinutes(5));
        var repeated = await TechnicianVisitSeed.ReadAsync(
            await host.SendAsync(HttpMethod.Patch, path, me.Cookie, new JsonObject { ["isCompleted"] = true }));

        Assert.Equal(Noon, At(TaskOf(repeated, seeded.Task)["completedAt"]));
        Assert.Equal(afterComplete, await database.ProgressStateAsync(job.Visit));

        var uncompleted = await TechnicianVisitSeed.ReadAsync(
            await host.SendAsync(HttpMethod.Patch, path, me.Cookie, new JsonObject { ["isCompleted"] = false }));

        Assert.False(TaskOf(uncompleted, seeded.Task)["isCompleted"]!.GetValue<bool>());
        Assert.Null(TaskOf(uncompleted, seeded.Task)["completedAt"]);
        Assert.Equal(
            1,
            await database.ScalarAsync<long>(
                "SELECT COUNT(*) FROM visit_checklist_items WHERE id = @t AND NOT is_completed AND completed_by_user_id IS NULL AND completed_at IS NULL",
                ("t", seeded.Task)));

        // AC-08: comments are trimmed, empty clears, 1000 characters pass and 1001 are rejected.
        var trimmed = await TechnicianVisitSeed.ReadAsync(
            await host.SendAsync(HttpMethod.Patch, path, me.Cookie, new JsonObject { ["notes"] = "  Valve is stuck  " }));
        Assert.Equal("Valve is stuck", TaskOf(trimmed, seeded.Task)["notes"]!.GetValue<string>());

        var longest = await TechnicianVisitSeed.ReadAsync(
            await host.SendAsync(HttpMethod.Patch, path, me.Cookie, new JsonObject { ["notes"] = new string('n', 1000) }));
        Assert.Equal(1000, TaskOf(longest, seeded.Task)["notes"]!.GetValue<string>().Length);

        var before = await database.ProgressStateAsync(job.Visit);

        foreach (var body in new[]
        {
            new JsonObject { ["notes"] = new string('n', 1001) },
            new JsonObject(),
        })
        {
            var problem = await ProblemAsync(await host.SendAsync(HttpMethod.Patch, path, me.Cookie, body), HttpStatusCode.BadRequest);

            Assert.NotNull(problem["errors"]);
        }

        Assert.Equal(before, await database.ProgressStateAsync(job.Visit));

        var cleared = await TechnicianVisitSeed.ReadAsync(
            await host.SendAsync(HttpMethod.Patch, path, me.Cookie, new JsonObject { ["notes"] = "   " }));
        Assert.Null(TaskOf(cleared, seeded.Task)["notes"]);

        // AC-08: an optional task is added last with the next sort order.
        var created = await TechnicianVisitSeed.ReadAsync(
            await host.SendAsync(HttpMethod.Post, Path(job.Visit, "/tasks"), me.Cookie, new JsonObject { ["label"] = "  Sweep floor  " }),
            HttpStatusCode.Created);
        var last = created["tasks"]!.AsArray().Last()!;

        Assert.Equal("Sweep floor", last["label"]!.GetValue<string>());
        Assert.False(last["isRequired"]!.GetValue<bool>());
        Assert.False(last["isCompleted"]!.GetValue<bool>());
        Assert.Equal(
            1,
            await database.ScalarAsync<long>(
                "SELECT COUNT(*) FROM visit_checklist_items WHERE visit_id = @v AND label = 'Sweep floor' AND NOT is_required AND template_item_id IS NULL AND sort_order = 2",
                ("v", job.Visit)));

        var withoutTasks = await database.SeedJobAsync(world, user, me.Profile, "in_progress", Noon.AddHours(1));
        await host.SendAsync(HttpMethod.Post, Path(withoutTasks.Visit, "/tasks"), me.Cookie, new JsonObject { ["label"] = "First" });
        Assert.Equal(0, await database.ScalarAsync<int>("SELECT sort_order FROM visit_checklist_items WHERE visit_id = @v", ("v", withoutTasks.Visit)));

        var beforeLabels = await database.ProgressStateAsync(job.Visit);

        foreach (var label in new JsonNode?[] { "   ", null, new string('l', 241) })
        {
            var problem = await ProblemAsync(
                await host.SendAsync(HttpMethod.Post, Path(job.Visit, "/tasks"), me.Cookie, new JsonObject { ["label"] = label }),
                HttpStatusCode.BadRequest);

            Assert.NotNull(problem["errors"]!["label"]);
        }

        Assert.Equal(beforeLabels, await database.ProgressStateAsync(job.Visit));

        // AC-08: fifty tasks is the limit.
        await database.ExecuteAsync(
            "INSERT INTO visit_checklist_items (visit_id, label, is_required, sort_order) SELECT @v, 'Filler ' || i, false, 10 + i FROM generate_series(1, 47) i",
            ("v", job.Visit));
        var filled = await database.ProgressStateAsync(job.Visit);
        var limit = await ProblemAsync(
            await host.SendAsync(HttpMethod.Post, Path(job.Visit, "/tasks"), me.Cookie, new JsonObject { ["label"] = "One too many" }),
            HttpStatusCode.Conflict);

        Assert.Equal("task_limit_reached", limit["code"]!.GetValue<string>());
        Assert.Equal("This job already has the maximum number of tasks.", limit["title"]!.GetValue<string>());
        Assert.Equal(filled, await database.ProgressStateAsync(job.Visit));
    }

    [Fact]
    public async Task MaterialEndpoints_RecordPlannedAndAdditionalMaterialsAndSearchTheCatalogWithoutPrices()
    {
        var world = await database.SeedWorldAsync();
        var foreign = await database.SeedWorldAsync();
        await using var host = TechnicianHost.Create(database);
        var me = await database.SeedTechnicianAsync(host, world);
        host.SetNow(Noon);
        var user = me.Member.UserId;
        var job = await database.SeedJobAsync(world, user, me.Profile, "in_progress", Noon);
        var seeded = await SeedChildrenAsync(world, job, user);
        await database.ExecuteAsync("DELETE FROM visit_materials WHERE visit_id = @v", ("v", job.Visit));

        var copper = await database.SeedCatalogItemAsync(world.Org, "product", "Copper pipe", cost: 4.25m);
        var piping = Guid.NewGuid();
        await database.ExecuteAsync("UPDATE catalog_items SET sku = 'CP-100' WHERE id = @i", ("i", copper));
        await database.ExecuteAsync(
            "INSERT INTO work_order_planned_materials (id, organization_id, work_order_id, catalog_item_id, description, quantity, unit, source, sort_order) VALUES (@p, @o, @w, @c, 'Pipe', 3, 'm', 'truck_stock', 1)",
            ("p", piping),
            ("o", world.Org),
            ("w", job.Order),
            ("c", copper));

        // AC-09: positive quantities create and update exactly one linked row; more than three decimals are rejected.
        var planned = Path(job.Visit, $"/planned-materials/{seeded.Planned}");
        var first = await TechnicianVisitSeed.ReadAsync(
            await host.SendAsync(HttpMethod.Put, planned, me.Cookie, new JsonObject { ["usedQuantity"] = 1.5m }));

        Assert.Equal(1.5m, first["plannedMaterials"]!.AsArray().Single(item => item!["id"]!.GetValue<Guid>() == seeded.Planned)!["usedQuantity"]!.GetValue<decimal>());
        Assert.Equal(
            1,
            await database.ScalarAsync<long>(
                "SELECT COUNT(*) FROM visit_materials WHERE visit_id = @v AND planned_material_id = @p AND description = 'Valve' AND unit = 'ea' AND quantity = 1.5 AND NOT billable AND unit_cost = 0 AND catalog_item_id IS NULL",
                ("v", job.Visit),
                ("p", seeded.Planned)));

        await host.SendAsync(HttpMethod.Put, planned, me.Cookie, new JsonObject { ["usedQuantity"] = 2 });
        Assert.Equal(
            1,
            await database.ScalarAsync<long>(
                "SELECT COUNT(*) FROM visit_materials WHERE visit_id = @v AND planned_material_id = @p AND quantity = 2", ("v", job.Visit), ("p", seeded.Planned)));

        await host.SendAsync(HttpMethod.Put, Path(job.Visit, $"/planned-materials/{piping}"), me.Cookie, new JsonObject { ["usedQuantity"] = 1 });
        Assert.Equal(
            1,
            await database.ScalarAsync<long>(
                "SELECT COUNT(*) FROM visit_materials WHERE visit_id = @v AND planned_material_id = @p AND catalog_item_id = @c AND unit_cost = 4.25 AND NOT billable",
                ("v", job.Visit),
                ("p", piping),
                ("c", copper)));

        var beforeInvalid = await database.ProgressStateAsync(job.Visit);

        foreach (var quantity in new[] { 1.2345m, -1m, 100000m })
        {
            var problem = await ProblemAsync(
                await host.SendAsync(HttpMethod.Put, planned, me.Cookie, new JsonObject { ["usedQuantity"] = quantity }),
                HttpStatusCode.BadRequest);

            Assert.NotNull(problem["errors"]!["usedQuantity"]);
        }

        Assert.Equal(beforeInvalid, await database.ProgressStateAsync(job.Visit));

        await host.SendAsync(HttpMethod.Put, planned, me.Cookie, new JsonObject { ["usedQuantity"] = 0 });
        Assert.Equal(
            0,
            await database.ScalarAsync<long>(
                "SELECT COUNT(*) FROM visit_materials WHERE visit_id = @v AND planned_material_id = @p", ("v", job.Visit), ("p", seeded.Planned)));

        var afterDelete = await database.ProgressStateAsync(job.Visit);
        Assert.Equal(
            0m,
            (await TechnicianVisitSeed.ReadAsync(await host.SendAsync(HttpMethod.Put, planned, me.Cookie, new JsonObject { ["usedQuantity"] = 0 })))["plannedMaterials"]!
                .AsArray().Single(item => item!["id"]!.GetValue<Guid>() == seeded.Planned)!["usedQuantity"]!.GetValue<decimal>());
        Assert.Equal(afterDelete, await database.ProgressStateAsync(job.Visit));

        // AC-09: one audit row per change, with ids and quantities only.
        Assert.Equal(4, await database.AuditCountAsync(job.Visit, "visit.material_recorded"));
        Assert.Equal(
            1,
            await database.ScalarAsync<long>(
                """
                SELECT COUNT(*) FROM audit_logs WHERE entity_id = @v AND action = 'visit.material_recorded'
                    AND metadata->>'plannedMaterialId' = @p AND (metadata->>'quantityBefore')::numeric = 0 AND (metadata->>'quantityAfter')::numeric = 1.5
                    AND metadata->>'workOrderId' = @w AND metadata->>'visitNumber' = '1' AND metadata->>'materialId' IS NOT NULL
                """,
                ("v", job.Visit),
                ("p", seeded.Planned.ToString()),
                ("w", job.Order.ToString())));

        // AC-10: an active product and free text create additional rows with the BR-10 values.
        var product = await TechnicianVisitSeed.ReadAsync(
            await host.SendAsync(HttpMethod.Post, Path(job.Visit, "/materials"), me.Cookie, new JsonObject { ["quantity"] = 2, ["catalogItemId"] = copper }),
            HttpStatusCode.Created);
        var productRow = product["additionalMaterials"]!.AsArray().Single()!;

        Assert.Equal("Copper pipe", productRow["description"]!.GetValue<string>());
        Assert.Equal("unit", productRow["unit"]!.GetValue<string>());
        Assert.Equal(copper, productRow["catalogItemId"]!.GetValue<Guid>());
        Assert.Equal(
            1,
            await database.ScalarAsync<long>(
                "SELECT COUNT(*) FROM visit_materials WHERE visit_id = @v AND catalog_item_id = @c AND planned_material_id IS NULL AND unit_cost = 4.25 AND NOT billable AND quantity = 2",
                ("v", job.Visit),
                ("c", copper)));

        var text = await TechnicianVisitSeed.ReadAsync(
            await host.SendAsync(
                HttpMethod.Post,
                Path(job.Visit, "/materials"),
                me.Cookie,
                new JsonObject { ["quantity"] = 1.5m, ["description"] = " Duct tape ", ["unit"] = " roll " }),
            HttpStatusCode.Created);

        Assert.Equal(["Copper pipe", "Duct tape"], text["additionalMaterials"]!.AsArray().Select(item => item!["description"]!.GetValue<string>()));
        Assert.Equal(
            1,
            await database.ScalarAsync<long>(
                "SELECT COUNT(*) FROM visit_materials WHERE visit_id = @v AND description = 'Duct tape' AND unit = 'roll' AND unit_cost = 0 AND catalog_item_id IS NULL AND NOT billable",
                ("v", job.Visit)));

        // AC-10: both sources, neither, zero and too many decimals are 400.
        var beforeBad = await database.ProgressStateAsync(job.Visit);

        foreach (var body in new[]
        {
            new JsonObject { ["quantity"] = 1, ["catalogItemId"] = copper, ["description"] = "x", ["unit"] = "ea" },
            new JsonObject { ["quantity"] = 1 },
            new JsonObject { ["quantity"] = 0, ["description"] = "x", ["unit"] = "ea" },
            new JsonObject { ["quantity"] = 1.2345m, ["description"] = "x", ["unit"] = "ea" },
            new JsonObject { ["quantity"] = 1, ["description"] = "x" },
            new JsonObject { ["quantity"] = 1, ["description"] = new string('d', 241), ["unit"] = "ea" },
        })
        {
            Assert.NotNull((await ProblemAsync(await host.SendAsync(HttpMethod.Post, Path(job.Visit, "/materials"), me.Cookie, body), HttpStatusCode.BadRequest))["errors"]);
        }

        // AC-10: inactive, service, other-organization and unknown catalog items are the identical 404.
        var inactive = await database.SeedCatalogItemAsync(world.Org, "product", "Pipe retired", active: false);
        var service = await database.SeedCatalogItemAsync(world.Org, "service", "Pipe service");
        var foreignProduct = await database.SeedCatalogItemAsync(foreign.Org, "product", "Pipe foreign");
        var bodies = new HashSet<string>();

        foreach (var item in new[] { inactive, service, foreignProduct, Guid.NewGuid() })
        {
            var response = await host.SendAsync(
                HttpMethod.Post, Path(job.Visit, "/materials"), me.Cookie, new JsonObject { ["quantity"] = 1, ["catalogItemId"] = item });

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            bodies.Add(await TechnicianVisitSeed.WithoutTraceAsync(response));
        }

        Assert.Single(bodies);
        Assert.Equal(beforeBad, await database.ProgressStateAsync(job.Visit));

        // AC-10: PUT updates an additional material, 0 deletes it and a planned-linked row is the identical 404.
        var tapeId = text["additionalMaterials"]!.AsArray()[1]!["id"]!.GetValue<Guid>();
        var tape = Path(job.Visit, $"/materials/{tapeId}");
        var updated = await TechnicianVisitSeed.ReadAsync(await host.SendAsync(HttpMethod.Put, tape, me.Cookie, new JsonObject { ["quantity"] = 3 }));
        Assert.Equal(3m, updated["additionalMaterials"]!.AsArray()[1]!["quantity"]!.GetValue<decimal>());
        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(HttpMethod.Put, tape, me.Cookie, new JsonObject { ["quantity"] = -1 })).StatusCode);

        var linkedId = await database.ScalarAsync<Guid>(
            "SELECT id FROM visit_materials WHERE visit_id = @v AND planned_material_id = @p", ("v", job.Visit), ("p", piping));
        var linkedBefore = await database.ProgressStateAsync(job.Visit);
        var linked = await host.SendAsync(
            HttpMethod.Put, Path(job.Visit, $"/materials/{linkedId}"), me.Cookie, new JsonObject { ["quantity"] = 9 });

        Assert.Equal(HttpStatusCode.NotFound, linked.StatusCode);
        Assert.Equal(linkedBefore, await database.ProgressStateAsync(job.Visit));

        await host.SendAsync(HttpMethod.Put, tape, me.Cookie, new JsonObject { ["quantity"] = 0 });
        Assert.Equal(0, await database.ScalarAsync<long>("SELECT COUNT(*) FROM visit_materials WHERE id = @m", ("m", tapeId)));

        // AC-10: fifty additional materials is the limit, for both sources.
        await database.ExecuteAsync(
            "INSERT INTO visit_materials (visit_id, description, quantity, unit) SELECT @v, 'Filler ' || i, 1, 'ea' FROM generate_series(1, 49) i",
            ("v", job.Visit));
        var filled = await database.ProgressStateAsync(job.Visit);

        foreach (var body in new[]
        {
            new JsonObject { ["quantity"] = 1, ["catalogItemId"] = copper },
            new JsonObject { ["quantity"] = 1, ["description"] = "More", ["unit"] = "ea" },
        })
        {
            var problem = await ProblemAsync(await host.SendAsync(HttpMethod.Post, Path(job.Visit, "/materials"), me.Cookie, body), HttpStatusCode.Conflict);

            Assert.Equal("material_limit_reached", problem["code"]!.GetValue<string>());
            Assert.Equal("This job already has the maximum number of materials.", problem["title"]!.GetValue<string>());
        }

        Assert.Equal(filled, await database.ProgressStateAsync(job.Visit));

        // AC-11: the lookup returns active products of the organization only, by name or SKU, without prices.
        await database.SeedCatalogItemAsync(world.Org, "product", "Pipe clamp");
        var byName = await TechnicianVisitSeed.ReadAsync(await host.GetAsync(Path(job.Visit, "/material-catalog?search=PIPE"), me.Cookie));
        var names = byName.AsArray().Select(item => item!["name"]!.GetValue<string>()).ToArray();

        Assert.Equal(["Copper pipe", "Pipe clamp"], names);
        Assert.All(byName.AsArray(), item => Assert.Equal(["id", "name", "unit"], item!.AsObject().Select(property => property.Key).Order()));

        var bySku = await TechnicianVisitSeed.ReadAsync(await host.GetAsync(Path(job.Visit, "/material-catalog?search=cp-1"), me.Cookie));
        Assert.Equal("Copper pipe", Assert.Single(bySku.AsArray())!["name"]!.GetValue<string>());

        await database.ExecuteAsync(
            "INSERT INTO catalog_items (organization_id, type, name, unit_cost, unit_price) SELECT @o, 'product'::catalog_item_type, 'Fitting ' || lpad(i::text, 2, '0'), 1, 1 FROM generate_series(1, 25) i",
            ("o", world.Org));
        var capped = await TechnicianVisitSeed.ReadAsync(await host.GetAsync(Path(job.Visit, "/material-catalog?search=fitting"), me.Cookie));
        Assert.Equal(20, capped.AsArray().Count);
        Assert.Equal("Fitting 01", capped.AsArray()[0]!["name"]!.GetValue<string>());

        foreach (var search in new[] { "?search=p", "?search=" + new string('s', 81), string.Empty })
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await host.GetAsync(Path(job.Visit, "/material-catalog" + search), me.Cookie)).StatusCode);
        }
    }

    [Fact]
    public async Task EvidenceEndpoints_StoreValidatedImagesServeThemSafelyAndDeleteThemWithAudit()
    {
        var world = await database.SeedWorldAsync();
        await using var host = TechnicianHost.Create(database);
        var me = await database.SeedTechnicianAsync(host, world);
        host.SetNow(Noon);
        var user = me.Member.UserId;
        var job = await database.SeedJobAsync(world, user, me.Profile, "in_progress", Noon);
        var otherJob = await database.SeedJobAsync(world, user, me.Profile, "in_progress", Noon.AddHours(2));
        var other = await SeedChildrenAsync(world, otherJob, user);
        var upload = Path(job.Visit, "/evidence");

        // AC-12: a JPEG with a path-laden file name, then a PNG with a parameterized upper-case declared type.
        var response = await host.UploadAsync(upload, me.Cookie, Jpeg, "image/jpeg", "before", "..\\..\\evil/secret-photo.jpg");
        var created = await TechnicianVisitSeed.ReadAsync(response, HttpStatusCode.Created);
        var evidence = created["evidence"]!.AsArray().Single()!;

        Assert.Equal("before", evidence["type"]!.GetValue<string>());
        Assert.DoesNotContain("content", evidence.AsObject().Select(property => property.Key));
        var jpegId = evidence["id"]!.GetValue<Guid>();

        Assert.Equal(
            1,
            await database.ScalarAsync<long>(
                "SELECT COUNT(*) FROM visit_evidence WHERE id = @e AND file_name = 'secret-photo.jpg' AND mime_type = 'image/jpeg' AND size_bytes = @n AND storage_key IS NULL AND caption IS NULL AND evidence_type = 'before' AND uploaded_by_user_id = @u AND content = @c",
                ("e", jpegId),
                ("n", (long)Jpeg.Length),
                ("u", user),
                ("c", Jpeg)));

        var afterPng = await TechnicianVisitSeed.ReadAsync(
            await host.UploadAsync(upload, me.Cookie, Png, "IMAGE/PNG; charset=binary", "after", "x.png"), HttpStatusCode.Created);
        Assert.Equal(["before", "after"], afterPng["evidence"]!.AsArray().Select(item => item!["type"]!.GetValue<string>()));

        Assert.Equal(2, await database.AuditCountAsync(job.Visit, "visit.evidence_added"));
        Assert.Equal(
            1,
            await database.ScalarAsync<long>(
                "SELECT COUNT(*) FROM audit_logs WHERE entity_id = @v AND action = 'visit.evidence_added' AND metadata->>'evidenceId' = @e AND metadata->>'evidenceType' = 'before' AND (metadata->>'sizeBytes')::bigint = @n AND metadata->>'workOrderId' = @w",
                ("v", job.Visit),
                ("e", jpegId.ToString()),
                ("n", (long)Jpeg.Length),
                ("w", job.Order.ToString())));

        var audit = await DispatchSeed.AuditTextAsync(database, job.Visit);
        Assert.DoesNotContain("secret-photo", audit);
        Assert.DoesNotContain("evil", audit);

        // AC-12: mismatched, unsupported and oversized content, a missing file type or evidence type and an empty file write nothing.
        var before = await database.ProgressStateAsync(job.Visit);
        var oversized = new byte[(10 * 1024 * 1024) + 1];
        Jpeg.CopyTo(oversized, 0);

        foreach (var (bytes, declared, type, name) in new (byte[], string, string?, string)[]
        {
            (Png, "image/jpeg", "before", "renamed.jpg"),
            ("GIF89a"u8.ToArray(), "image/gif", "before", "anim.gif"),
            ("just text"u8.ToArray(), "image/jpeg", "before", "text.jpg"),
            (oversized, "image/jpeg", "before", "big.jpg"),
            ([], "image/jpeg", "before", "empty.jpg"),
            (Jpeg, "image/jpeg", null, "no-type.jpg"),
            (Jpeg, "image/jpeg", "during", "during.jpg"),
        })
        {
            var problem = await ProblemAsync(
                await host.UploadAsync(upload, me.Cookie, bytes, declared, type, name), HttpStatusCode.BadRequest);

            Assert.NotNull(problem["errors"]);

            if (type == "before")
            {
                Assert.Equal(PhotoMessage, problem["errors"]!["file"]![0]!.GetValue<string>());
            }
        }

        Assert.Equal(before, await database.ProgressStateAsync(job.Visit));

        // AC-13: the photo is served with the stored type and the safe headers; photos of other visits are the identical 404.
        var image = await host.GetAsync(Path(job.Visit, $"/evidence/{jpegId}"), me.Cookie);

        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.Equal("image/jpeg", image.Content.Headers.ContentType?.MediaType);
        Assert.Equal("inline", image.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("nosniff", image.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("no-store", image.Headers.CacheControl?.ToString());
        Assert.Equal("default-src 'none'; sandbox", image.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal(Jpeg, await image.Content.ReadAsByteArrayAsync());

        var bodies = new HashSet<string>();

        foreach (var id in new[] { other.Evidence, Guid.NewGuid() })
        {
            var rejected = await host.GetAsync(Path(job.Visit, $"/evidence/{id}"), me.Cookie);
            var deleted = await host.SendAsync(HttpMethod.Delete, Path(job.Visit, $"/evidence/{id}"), me.Cookie);

            Assert.Equal(HttpStatusCode.NotFound, rejected.StatusCode);
            Assert.Null(rejected.Content.Headers.ContentDisposition);
            Assert.Equal(HttpStatusCode.NotFound, deleted.StatusCode);
            bodies.Add(await TechnicianVisitSeed.WithoutTraceAsync(rejected));
            bodies.Add(await TechnicianVisitSeed.WithoutTraceAsync(deleted));
        }

        Assert.Single(bodies);
        Assert.Equal(1, await database.ScalarAsync<long>("SELECT COUNT(*) FROM visit_evidence WHERE visit_id = @v", ("v", otherJob.Visit)));

        // AC-13: delete removes the photo with one audit row; deleting again is a 404.
        var removed = await TechnicianVisitSeed.ReadAsync(await host.SendAsync(HttpMethod.Delete, Path(job.Visit, $"/evidence/{jpegId}"), me.Cookie));

        Assert.Equal(["after"], removed["evidence"]!.AsArray().Select(item => item!["type"]!.GetValue<string>()));
        Assert.Equal(
            1,
            await database.ScalarAsync<long>(
                "SELECT COUNT(*) FROM audit_logs WHERE entity_id = @v AND action = 'visit.evidence_deleted' AND metadata->>'evidenceId' = @e AND metadata->>'evidenceType' = 'before'",
                ("v", job.Visit),
                ("e", jpegId.ToString())));
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Delete, Path(job.Visit, $"/evidence/{jpegId}"), me.Cookie)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.GetAsync(Path(job.Visit, $"/evidence/{jpegId}"), me.Cookie)).StatusCode);

        // AC-12: twenty photos is the limit.
        await database.ExecuteAsync(
            "INSERT INTO visit_evidence (visit_id, file_name, content, mime_type, size_bytes, evidence_type, uploaded_by_user_id) SELECT @v, 'f.png', @c, 'image/png', @n, 'after', @u FROM generate_series(1, 19) i",
            ("v", job.Visit),
            ("c", Png),
            ("n", (long)Png.Length),
            ("u", user));
        var filled = await database.ProgressStateAsync(job.Visit);
        var limit = await ProblemAsync(await host.UploadAsync(upload, me.Cookie, Png, "image/png", "before"), HttpStatusCode.Conflict);

        Assert.Equal("evidence_limit_reached", limit["code"]!.GetValue<string>());
        Assert.Equal("This job already has the maximum number of photos.", limit["title"]!.GetValue<string>());
        Assert.Equal(filled, await database.ProgressStateAsync(job.Visit));
    }

    [Fact]
    public async Task NotesAndDetail_SaveTrimmedNotesWithoutLoggingThemAndMapTheProgressData()
    {
        var world = await database.SeedWorldAsync();
        await using var host = TechnicianHost.Create(database);
        var me = await database.SeedTechnicianAsync(host, world);
        host.SetNow(Noon);
        var user = me.Member.UserId;
        var job = await database.SeedJobAsync(world, user, me.Profile, "in_progress", Noon, minutes: 45);
        var seeded = await SeedChildrenAsync(world, job, user);
        await database.ExecuteAsync("DELETE FROM visit_materials WHERE visit_id = @v", ("v", job.Visit));
        var notes = Path(job.Visit, "/notes");

        // AC-14: 4000 characters are saved trimmed, 4001 are rejected, whitespace clears; the text is never audited or logged.
        var body = new string('n', 3998) + "ZQ";
        var saved = await TechnicianVisitSeed.ReadAsync(
            await host.SendAsync(HttpMethod.Put, notes, me.Cookie, new JsonObject { ["notes"] = "  " + body + "  " }));

        Assert.Equal(body, saved["technicianNotes"]!.GetValue<string>());
        Assert.Equal(body, await database.ScalarAsync<string>("SELECT completion_summary FROM visits WHERE id = @v", ("v", job.Visit)));

        var tooLong = await ProblemAsync(
            await host.SendAsync(HttpMethod.Put, notes, me.Cookie, new JsonObject { ["notes"] = body + "X" }), HttpStatusCode.BadRequest);
        Assert.NotNull(tooLong["errors"]!["notes"]);
        Assert.Equal(body, await database.ScalarAsync<string>("SELECT completion_summary FROM visits WHERE id = @v", ("v", job.Visit)));

        var cleared = await TechnicianVisitSeed.ReadAsync(
            await host.SendAsync(HttpMethod.Put, notes, me.Cookie, new JsonObject { ["notes"] = "   " }));
        Assert.Null(cleared["technicianNotes"]);
        Assert.True(await database.ScalarAsync<bool>("SELECT completion_summary IS NULL FROM visits WHERE id = @v", ("v", job.Visit)));

        await host.SendAsync(HttpMethod.Put, notes, me.Cookie, new JsonObject { ["notes"] = "Customer asked for ZQSECRETNOTE" });
        Assert.DoesNotContain("ZQSECRETNOTE", await DispatchSeed.AuditTextAsync(database, job.Visit));
        Assert.DoesNotContain(host.Logs.Entries, entry => entry.Message.Contains("ZQSECRETNOTE", StringComparison.Ordinal));
        Assert.DoesNotContain(
            host.Logs.Entries, entry => entry.Level >= LogLevel.Warning && entry.Message.Contains("Customer asked", StringComparison.Ordinal));

        // AC-15: closed work and pause entries, an open work entry, the estimate, tasks, materials and photos.
        await database.ExecuteAsync("DELETE FROM visit_evidence WHERE visit_id = @v", ("v", job.Visit));
        await database.SeedEntryAsync(job.Visit, me.Profile, "work", Noon.AddMinutes(-60), Noon.AddMinutes(-40));
        await database.SeedEntryAsync(job.Visit, me.Profile, "work", Noon.AddMinutes(-30), Noon.AddMinutes(-20).AddSeconds(0.9));
        await database.SeedEntryAsync(job.Visit, me.Profile, "pause", Noon.AddMinutes(-40), Noon.AddMinutes(-30).AddSeconds(5));
        await database.SeedEntryAsync(job.Visit, me.Profile, "work", Noon.AddMinutes(-10));
        await database.ExecuteAsync("UPDATE visits SET actual_started_at = @a WHERE id = @v", ("a", Noon.AddMinutes(-60)), ("v", job.Visit));
        await database.ExecuteAsync(
            "UPDATE visit_checklist_items SET notes = 'Stuck valve', is_completed = true, completed_at = @c, completed_by_user_id = @u WHERE id = @t",
            ("c", Noon.AddMinutes(-15)),
            ("u", user),
            ("t", seeded.Task));
        await database.ExecuteAsync(
            "INSERT INTO visit_materials (visit_id, planned_material_id, description, quantity, unit) VALUES (@v, @p, 'Valve', 2, 'ea')",
            ("v", job.Visit),
            ("p", seeded.Planned));
        await database.ExecuteAsync(
            "INSERT INTO visit_evidence (id, visit_id, file_name, content, mime_type, size_bytes, evidence_type, uploaded_by_user_id, created_at) VALUES (gen_random_uuid(), @v, 'a.png', @c, 'image/png', @n, 'after', @u, @t2), (gen_random_uuid(), @v, 'b.png', @c, 'image/png', @n, 'before', @u, @t1)",
            ("v", job.Visit),
            ("c", Png),
            ("n", (long)Png.Length),
            ("u", user),
            ("t1", Noon.AddMinutes(-20)),
            ("t2", Noon.AddMinutes(-10)));
        await host.SendAsync(
            HttpMethod.Post, Path(job.Visit, "/materials"), me.Cookie, new JsonObject { ["quantity"] = 1, ["description"] = "Alpha", ["unit"] = "ea" });
        var beta = await TechnicianVisitSeed.ReadAsync(
            await host.SendAsync(
                HttpMethod.Post, Path(job.Visit, "/materials"), me.Cookie, new JsonObject { ["quantity"] = 2, ["description"] = "Beta", ["unit"] = "kg" }),
            HttpStatusCode.Created);

        var response = await host.GetAsync(Path(job.Visit), me.Cookie);
        var text = await response.Content.ReadAsStringAsync();
        var detail = await TechnicianVisitSeed.ReadAsync(response);

        Assert.Equal(Noon.AddMinutes(-60), At(detail["actualStartedAt"]));
        Assert.Equal(1200 + 600, detail["time"]!["workSeconds"]!.GetValue<int>());
        Assert.Equal(605, detail["time"]!["pauseSeconds"]!.GetValue<int>());
        Assert.Equal("work", detail["time"]!["activeEntry"]!["type"]!.GetValue<string>());
        Assert.Equal(Noon.AddMinutes(-10), At(detail["time"]!["activeEntry"]!["startedAt"]));
        Assert.Equal(120, detail["time"]!["estimatedMinutes"]!.GetValue<int>());

        var task = TaskOf(detail, seeded.Task);
        Assert.Equal("Stuck valve", task["notes"]!.GetValue<string>());
        Assert.Equal(Noon.AddMinutes(-15), At(task["completedAt"]));
        Assert.Equal(["Shut off water", "Photograph"], detail["tasks"]!.AsArray().Select(item => item!["label"]!.GetValue<string>()));
        Assert.Equal(2m, detail["plannedMaterials"]!.AsArray().Single()!["usedQuantity"]!.GetValue<decimal>());
        Assert.Equal(["Alpha", "Beta"], detail["additionalMaterials"]!.AsArray().Select(item => item!["description"]!.GetValue<string>()));
        Assert.Null(detail["additionalMaterials"]!.AsArray()[0]!["catalogItemId"]);
        Assert.Equal(["before", "after"], detail["evidence"]!.AsArray().Select(item => item!["type"]!.GetValue<string>()));
        Assert.Equal("Customer asked for ZQSECRETNOTE", detail["technicianNotes"]!.GetValue<string>());
        Assert.Equal(2, beta["additionalMaterials"]!.AsArray().Count);
        Assert.DoesNotContain("\"content\"", text);

        // AC-15: without an estimate the scheduled window length is used.
        await database.ExecuteAsync("UPDATE work_orders SET estimated_duration_minutes = NULL WHERE id = @w", ("w", job.Order));
        var windowed = await TechnicianVisitSeed.ReadAsync(await host.GetAsync(Path(job.Visit), me.Cookie));
        Assert.Equal(45, windowed["time"]!["estimatedMinutes"]!.GetValue<int>());
    }
}
