using System.Net;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.ServiceRequests;
using FieldOps.IntegrationTests.Team;
using FieldOps.IntegrationTests.TechnicianVisits;

namespace FieldOps.IntegrationTests.Dispatch;

/// <summary>Cancel visit (technician-schedule-active-visits AC-11 to AC-15) and the cancellation fields of the dispatch drawer (FR-10).</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class DispatchCancelTests(CompanySettingsDatabaseFixture database)
{
    private const string Reason = "Customer no longer needs the service";

    private const string NotCancellable = "This visit can't be cancelled in its current state.";

    private async Task<SeededOrder> SeedAsync(
        RequestWorld world,
        Guid user,
        string visitStatus,
        string orderStatus = "scheduled",
        string jobType = "one_time",
        Guid? tech = null,
        string[]? tasks = null,
        DateTimeOffset? start = null)
    {
        var begin = start ?? DateTimeOffset.UtcNow.AddDays(-3);
        var scheduled = visitStatus != "unscheduled";
        var order = await database.SeedOrderAsync(
            world,
            user,
            status: orderStatus,
            jobType: jobType,
            frequency: jobType == "recurring" ? "weekly" : null,
            count: jobType == "recurring" ? (short)3 : null,
            tasks: tasks,
            visitStatus: visitStatus,
            start: scheduled ? begin : null,
            end: scheduled ? begin.AddHours(1) : null);

        if (tech is { } assignee)
        {
            await database.AssignAsync(order.Visit, assignee, user);
        }

        return order;
    }

    private static async Task<HttpResponseMessage> CancelAsync(
        RequestsHost host, string cookie, Guid visit, string? reason = Reason, string? updatedAt = null)
    {
        updatedAt ??= await DispatchSeed.TokenAsync(host, cookie, visit);

        return await host.SendAsync(
            HttpMethod.Post, $"/dispatch/visits/{visit}/cancel", cookie, new JsonObject { ["reason"] = reason, ["updatedAt"] = updatedAt });
    }

    private Task<long> CountAsync(string table, string column, Guid id, string extra = "") =>
        database.ScalarAsync<long>($"SELECT COUNT(*) FROM {table} WHERE {column} = @id {extra}", ("id", id));

    private Task<long> CancelAuditsAsync(Guid visit) =>
        CountAsync("audit_logs", "entity_id", visit, "AND action = 'visit.cancelled'");

    private Task<string> TextAsync(string sql, Guid id) => database.ScalarAsync<string>(sql, ("id", id));

    [Fact]
    public async Task Cancel_ClosesEntriesWritesHistoryAuditReplacementAndWorkOrderRulesAtomically()
    {
        var world = await database.SeedWorldAsync();
        await using var host = RequestsHost.Create(database);
        var (owner, member) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId, "Olive", "Owner");
        var user = member.UserId;
        var tech = await database.SeedTechAsync(world.Org, world.BranchA, "Ann", "Alpha");
        var now = DateTimeOffset.UtcNow;

        // AC-13: an in-progress one-time visit with an open work entry, completed and pending tasks, a material and a photo.
        var job = await SeedAsync(world, user, "in_progress", "in_progress", tech: tech, tasks: ["Template task"]);
        var template = await database.ScalarAsync<Guid>("SELECT id FROM work_order_checklist_templates WHERE work_order_id = @o", ("o", job.Order));
        await database.SeedEntryAsync(job.Visit, tech, "work", now.AddHours(-3), now.AddHours(-2));
        await database.SeedEntryAsync(job.Visit, tech, "work", now.AddHours(-1));
        await database.ExecuteAsync(
            "INSERT INTO visit_checklist_items (visit_id, label, is_required, is_completed, completed_by_user_id, completed_at, template_item_id, sort_order) VALUES (@v, 'Done required', true, true, @u, @c, NULL, 0), (@v, 'Pending A', true, false, NULL, NULL, @t, 1), (@v, 'Pending optional', false, false, NULL, NULL, NULL, 2)",
            ("v", job.Visit),
            ("u", user),
            ("c", now.AddHours(-2)),
            ("t", template));
        await database.ExecuteAsync(
            "INSERT INTO visit_materials (visit_id, description, quantity, unit) VALUES (@v, 'Valve', 1, 'ea')", ("v", job.Visit));
        await database.ExecuteAsync(
            "INSERT INTO visit_evidence (visit_id, file_name, content, mime_type, size_bytes, evidence_type, uploaded_by_user_id) VALUES (@v, 'b.png', @c, 'image/png', 12, 'before', @u)",
            ("v", job.Visit),
            ("c", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4 }),
            ("u", user));
        var notifications = await CountAsync("notifications", "organization_id", world.Org);

        var response = await CancelAsync(host, owner, job.Visit, $"  {Reason}  ");
        var result = await DispatchSeed.ReadAsync(response);
        var replacement = result["replacementVisitId"]!.GetValue<Guid>();

        Assert.Equal("cancelled", result["visit"]!["status"]!.GetValue<string>());
        Assert.Equal(Reason, result["visit"]!["cancellation"]!["reason"]!.GetValue<string>());
        Assert.Equal("Olive Owner", result["visit"]!["cancellation"]!["cancelledByName"]!.GetValue<string>());
        Assert.Equal("ready_to_schedule", result["workOrderStatus"]!.GetValue<string>());
        Assert.Equal("cancelled", await TextAsync("SELECT status::text FROM visits WHERE id = @id", job.Visit));
        Assert.Equal(0, await CountAsync("visit_time_entries", "visit_id", job.Visit, "AND ended_at IS NULL"));
        Assert.Equal(2, await CountAsync("visit_time_entries", "visit_id", job.Visit, "AND ended_at > started_at"));
        Assert.Equal(Reason, await TextAsync("SELECT reason FROM visit_status_history WHERE visit_id = @id AND to_status = 'cancelled'", job.Visit));
        Assert.Equal(1, await CountAsync("visit_status_history", "visit_id", job.Visit, "AND to_status = 'cancelled' AND from_status = 'in_progress'"));
        Assert.Equal(1, await CancelAuditsAsync(job.Visit));
        Assert.Equal(1, await CountAsync("audit_logs", "entity_id", job.Order, "AND action = 'work_order.status_changed'"));
        Assert.Equal("ready_to_schedule", await TextAsync("SELECT status::text FROM work_orders WHERE id = @id", job.Order));

        var audit = await database.AuditTextAsync(job.Visit);
        Assert.Contains("replacementVisitId", audit);
        Assert.DoesNotContain("no longer needs", audit);
        Assert.DoesNotContain("no longer needs", await database.AuditTextAsync(job.Order));

        Assert.Equal(
            "2|unscheduled|true|true|true|true|true",
            await TextAsync(
                "SELECT visit_number || '|' || status::text || '|' || (scheduled_start IS NULL)::text || '|' || (scheduled_end IS NULL)::text || '|' || (preferred_start IS NULL)::text || '|' || (arrival_window_start IS NULL)::text || '|' || (dispatch_note IS NULL)::text FROM visits WHERE id = @id",
                replacement));
        Assert.Equal(
            "null>unscheduled",
            await TextAsync("SELECT string_agg(COALESCE(from_status::text, 'null') || '>' || to_status::text, ',') FROM visit_status_history WHERE visit_id = @id", replacement));
        Assert.Equal(
            "Pending A:true:false:true,Pending optional:false:false:false",
            await TextAsync(
                "SELECT string_agg(label || ':' || is_required::text || ':' || is_completed::text || ':' || (template_item_id IS NOT NULL)::text, ',' ORDER BY sort_order) FROM visit_checklist_items WHERE visit_id = @id",
                replacement));
        Assert.Equal(3, await CountAsync("visit_checklist_items", "visit_id", job.Visit));
        Assert.Equal(1, await CountAsync("visit_materials", "visit_id", job.Visit));
        Assert.Equal(1, await CountAsync("visit_evidence", "visit_id", job.Visit));
        Assert.Equal(1, await database.ActiveAssignmentsAsync(job.Visit));
        Assert.Equal(0, await database.ActiveAssignmentsAsync(replacement));
        Assert.Equal(notifications, await CountAsync("notifications", "organization_id", world.Org));
        Assert.Empty(host.Sender.Messages);

        // A paused visit with an open pause entry adds its seconds; another assigned visit keeps the work order in progress.
        var paused = await SeedAsync(world, user, "paused", "in_progress", tech: tech);
        await database.SeedEntryAsync(paused.Visit, tech, "work", now.AddHours(-2), now.AddMinutes(-10));
        await database.SeedEntryAsync(paused.Visit, tech, "pause", now.AddMinutes(-10));
        await database.SeedVisitAsync(world.Org, paused.Order, 2, "assigned", now.AddDays(1), now.AddDays(1).AddHours(1), tech, user);

        var pausedResult = await DispatchSeed.ReadAsync(await CancelAsync(host, owner, paused.Visit));
        var pauseSeconds = await database.ScalarAsync<int>("SELECT pause_seconds FROM visits WHERE id = @id", ("id", paused.Visit));
        Assert.InRange(pauseSeconds, 599, 720);
        Assert.Equal("in_progress", pausedResult["workOrderStatus"]!.GetValue<string>());
        Assert.Equal(0, await CountAsync("audit_logs", "entity_id", paused.Order, "AND action = 'work_order.status_changed'"));
        Assert.Equal(
            3,
            await database.ScalarAsync<int>("SELECT visit_number FROM visits WHERE id = @id", ("id", pausedResult["replacementVisitId"]!.GetValue<Guid>())));

        // AC-14: a recurring order skips the cancelled occurrence; the other occurrences keep their numbers.
        var recurring = await SeedAsync(world, user, "completed", "scheduled", "recurring");
        var occurrence2 = await database.SeedVisitAsync(world.Org, recurring.Order, 2, "assigned", now.AddDays(1), now.AddDays(1).AddHours(1), tech, user);
        await database.SeedVisitAsync(world.Org, recurring.Order, 3, "unscheduled");

        var skipped = await DispatchSeed.ReadAsync(await CancelAsync(host, owner, occurrence2));
        Assert.Null(skipped["replacementVisitId"]);
        Assert.Equal("ready_to_schedule", skipped["workOrderStatus"]!.GetValue<string>());
        Assert.Equal(
            "1:completed,2:cancelled,3:unscheduled",
            await TextAsync("SELECT string_agg(visit_number || ':' || status::text, ',' ORDER BY visit_number) FROM visits WHERE work_order_id = @id", recurring.Order));
        Assert.Equal(1, await CountAsync("audit_logs", "entity_id", recurring.Order, "AND action = 'work_order.status_changed'"));

        // AC-12: every eligible status on any date, reasons of 10 and 500 characters.
        foreach (var (status, reason) in new[] { ("scheduled", new string('a', 10)), ("assigned", new string('b', 500)), ("on_the_way", Reason) })
        {
            var eligible = await SeedAsync(world, user, status, start: now.AddDays(status == "assigned" ? 5 : -9));
            Assert.Equal(HttpStatusCode.OK, (await CancelAsync(host, owner, eligible.Visit, reason)).StatusCode);
        }

        // Invalid reasons, stale updatedAt, ineligible statuses and closed work orders: nothing is written.
        var guarded = await SeedAsync(world, user, "assigned");
        var guardedToken = await DispatchSeed.TokenAsync(host, owner, guarded.Visit);
        var guardedBefore = await database.ProgressStateAsync(guarded.Visit);

        foreach (var invalid in new string?[] { new string('c', 9), "         x         ", new string('d', 501), null, string.Empty })
        {
            var problem = await DispatchSeed.ProblemAsync(
                await CancelAsync(host, owner, guarded.Visit, invalid, guardedToken), HttpStatusCode.BadRequest, errorKey: "reason");
            Assert.Equal("Enter a reason between 10 and 500 characters.", problem["errors"]!["reason"]![0]!.GetValue<string>());
        }

        await database.ExecuteAsync("UPDATE visits SET updated_at = updated_at + interval '1 second' WHERE id = @v", ("v", guarded.Visit));
        guardedBefore = await database.ProgressStateAsync(guarded.Visit);
        await DispatchSeed.ProblemAsync(await CancelAsync(host, owner, guarded.Visit, Reason, guardedToken), HttpStatusCode.Conflict, "visit_changed");
        Assert.Equal(guardedBefore, await database.ProgressStateAsync(guarded.Visit));

        var refusals = new List<Guid>();

        foreach (var status in new[] { "unscheduled", "completed", "needs_correction", "approved", "cancelled" })
        {
            refusals.Add((await SeedAsync(world, user, status)).Visit);
        }

        foreach (var closed in new[] { "cancelled", "completed", "approved_for_billing" })
        {
            refusals.Add((await SeedAsync(world, user, "scheduled", closed)).Visit);
        }

        foreach (var visit in refusals)
        {
            var before = await database.ProgressStateAsync(visit);
            var refused = await DispatchSeed.ProblemAsync(await CancelAsync(host, owner, visit), HttpStatusCode.Conflict, "visit_not_cancellable");

            Assert.Equal(NotCancellable, refused["title"]!.GetValue<string>());
            Assert.Equal(before, await database.ProgressStateAsync(visit));
            Assert.Equal(0, await CancelAuditsAsync(visit));
        }
    }

    [Fact]
    public async Task Cancel_AuthorizesManageRolesInScopeAndShowsTheCancellationToEveryRoleThatReadsTheDrawer()
    {
        var world = await database.SeedWorldAsync();
        var foreign = await database.SeedWorldAsync();
        await using var host = RequestsHost.Create(database);
        var (owner, ownerMember) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId, "Olive", "Owner");
        var (manager, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OperationsManagerRoleId, "Mo", "Manager");
        var (dispatcher, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.DispatcherRoleId, "Dee", "Spatch", world.BranchA);
        var (outOfScope, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.DispatcherRoleId, "Bea", "Bravo", world.BranchB);
        var (viewer, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.ViewerRoleId, "Vic", "Viewer");
        var (accounting, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.AccountingRoleId, "Acc", "Ounting");
        var (technician, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.TechnicianRoleId, "Tec", "Hnician");
        var (foreignOwner, _) = await host.SignInAsync(database, foreign.Org, CompanySettingsDatabaseFixture.OwnerRoleId, "Fay", "Foreign");
        var user = ownerMember.UserId;

        // AC-11: Owner, Operations Manager and a Dispatcher in scope cancel; everyone else leaves the visit untouched.
        foreach (var cookie in new[] { owner, manager, dispatcher })
        {
            var job = await SeedAsync(world, user, "assigned");
            var ok = await DispatchSeed.ReadAsync(await CancelAsync(host, cookie, job.Visit));

            Assert.Equal("cancelled", ok["visit"]!["status"]!.GetValue<string>());
            Assert.NotNull(ok["replacementVisitId"]);
        }

        var protectedJob = await SeedAsync(world, user, "assigned");
        var token = await DispatchSeed.TokenAsync(host, owner, protectedJob.Visit);
        var before = await database.ProgressStateAsync(protectedJob.Visit);
        var hidden = new List<string>();

        foreach (var response in new[]
        {
            await CancelAsync(host, outOfScope, protectedJob.Visit, updatedAt: token),
            await CancelAsync(host, foreignOwner, protectedJob.Visit, updatedAt: token),
            await CancelAsync(host, owner, Guid.NewGuid(), updatedAt: token),
        })
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            hidden.Add(await TechnicianVisitSeed.WithoutTraceAsync(response));
        }

        Assert.Single(hidden.Distinct());

        foreach (var cookie in new[] { viewer, accounting, technician })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await CancelAsync(host, cookie, protectedJob.Visit, updatedAt: token)).StatusCode);
        }

        Assert.Equal(before, await database.ProgressStateAsync(protectedJob.Visit));
        Assert.Equal(0, await CancelAuditsAsync(protectedJob.Visit));

        // FR-10: canCancel and cancelCreatesReplacement for Manage roles, none for Viewer.
        var asOwner = await DispatchSeed.ReadAsync(await DispatchSeed.GetVisitAsync(host, owner, protectedJob.Visit));
        var asViewer = await DispatchSeed.ReadAsync(await DispatchSeed.GetVisitAsync(host, viewer, protectedJob.Visit));
        Assert.True(asOwner["canCancel"]!.GetValue<bool>());
        Assert.True(asOwner["cancelCreatesReplacement"]!.GetValue<bool>());
        Assert.Null(asOwner["cancellation"]);
        Assert.False(asViewer["canCancel"]!.GetValue<bool>());

        var recurring = await SeedAsync(world, user, "in_progress", "in_progress", "recurring");
        var recurringDetail = await DispatchSeed.ReadAsync(await DispatchSeed.GetVisitAsync(host, owner, recurring.Visit));
        Assert.True(recurringDetail["canCancel"]!.GetValue<bool>());
        Assert.False(recurringDetail["cancelCreatesReplacement"]!.GetValue<bool>());

        foreach (var (status, orderStatus) in new[] { ("completed", "completed"), ("scheduled", "approved_for_billing") })
        {
            var closed = await SeedAsync(world, user, status, orderStatus);
            Assert.False((await DispatchSeed.ReadAsync(await DispatchSeed.GetVisitAsync(host, owner, closed.Visit)))["canCancel"]!.GetValue<bool>());
        }

        // A cancelled visit shows who, when and why to the Viewer too, read-only.
        var done = await SeedAsync(world, user, "paused", "in_progress");
        await DispatchSeed.ReadAsync(await CancelAsync(host, dispatcher, done.Visit));
        var cancelledDrawer = await DispatchSeed.ReadAsync(await DispatchSeed.GetVisitAsync(host, viewer, done.Visit));
        Assert.Equal("cancelled", cancelledDrawer["status"]!.GetValue<string>());
        Assert.Equal(Reason, cancelledDrawer["cancellation"]!["reason"]!.GetValue<string>());
        Assert.Equal("Dee Spatch", cancelledDrawer["cancellation"]!["cancelledByName"]!.GetValue<string>());
        Assert.NotNull(cancelledDrawer["cancellation"]!["cancelledAt"]);
        Assert.False(cancelledDrawer["canCancel"]!.GetValue<bool>());
        Assert.True(cancelledDrawer["isLocked"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Cancel_SerializesAgainstTechnicianActionsAndItselfWithOneCancellationOneReplacementAndOneAudit()
    {
        var world = await database.SeedWorldAsync();
        await using var staff = RequestsHost.Create(database);
        await using var technicians = TechnicianHost.Create(database);
        var (owner, ownerMember) = await staff.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId, "Olive", "Owner");
        var user = ownerMember.UserId;

        for (var round = 0; round < 2; round++)
        {
            // AC-15: cancel against pause and against start travel on the same visit.
            foreach (var action in new[] { "pause", "start-travel" })
            {
                // A fresh technician per race: an earlier winner must not leave another active visit behind.
                var me = await database.SeedTechnicianAsync(technicians, world, first: $"R{round}{action[..2]}");
                var running = action == "pause";
                var job = await SeedAsync(
                    world,
                    user,
                    running ? "in_progress" : "assigned",
                    running ? "in_progress" : "scheduled",
                    tech: me.Profile,
                    start: running ? DateTimeOffset.UtcNow.AddHours(-2) : DateTimeOffset.UtcNow);

                if (running)
                {
                    await database.SeedEntryAsync(job.Visit, me.Profile, "work", DateTimeOffset.UtcNow.AddHours(-1));
                }

                var token = await DispatchSeed.TokenAsync(staff, owner, job.Visit);
                var cancel = CancelAsync(staff, owner, job.Visit, Reason, token);
                var technicianAction = technicians.PostAsync($"/technician/visits/{job.Visit}/{action}", me.Cookie);
                await Task.WhenAll(cancel, technicianAction);

                var cancelResponse = await cancel;
                var actionResponse = await technicianAction;
                var status = await TextAsync("SELECT status::text FROM visits WHERE id = @id", job.Visit);

                Assert.True((int)cancelResponse.StatusCode < 500 && (int)actionResponse.StatusCode < 500, $"{action}: {cancelResponse.StatusCode} {actionResponse.StatusCode}");

                if (cancelResponse.StatusCode == HttpStatusCode.OK)
                {
                    var lost = await TechnicianVisitSeed.ReadAsync(actionResponse, HttpStatusCode.Conflict);
                    Assert.Equal("visit_status_invalid", lost["code"]!.GetValue<string>());
                    Assert.Equal("cancelled", status);
                    Assert.Equal(1, await CancelAuditsAsync(job.Visit));
                    Assert.Equal(2, await CountAsync("visits", "work_order_id", job.Order));
                    Assert.Equal(0, await CountAsync("visit_time_entries", "visit_id", job.Visit, "AND ended_at IS NULL AND entry_type <> 'pause'"));
                }
                else
                {
                    var lost = await DispatchSeed.ReadAsync(cancelResponse, HttpStatusCode.Conflict);
                    Assert.Contains(lost["code"]!.GetValue<string>(), new[] { "visit_changed", "visit_not_cancellable" });
                    Assert.Equal(HttpStatusCode.OK, actionResponse.StatusCode);
                    Assert.Equal(running ? "paused" : "on_the_way", status);
                    Assert.Equal(0, await CancelAuditsAsync(job.Visit));
                    Assert.Equal(1, await CountAsync("visits", "work_order_id", job.Order));
                }
            }

            // Two concurrent cancels of the same version: exactly one wins.
            var twice = await SeedAsync(world, user, "in_progress", "in_progress");
            var sharedToken = await DispatchSeed.TokenAsync(staff, owner, twice.Visit);
            var responses = await Task.WhenAll(
                CancelAsync(staff, owner, twice.Visit, Reason, sharedToken),
                CancelAsync(staff, owner, twice.Visit, "A second and different reason", sharedToken));

            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);

            var loser = responses.Single(response => response.StatusCode != HttpStatusCode.OK);
            var loserProblem = await DispatchSeed.ReadAsync(loser, HttpStatusCode.Conflict);
            Assert.Contains(loserProblem["code"]!.GetValue<string>(), new[] { "visit_changed", "visit_not_cancellable" });
            Assert.Equal(1, await CancelAuditsAsync(twice.Visit));
            Assert.Equal(1, await CountAsync("visit_status_history", "visit_id", twice.Visit, "AND to_status = 'cancelled'"));
            Assert.Equal(2, await CountAsync("visits", "work_order_id", twice.Order));
        }
    }
}
