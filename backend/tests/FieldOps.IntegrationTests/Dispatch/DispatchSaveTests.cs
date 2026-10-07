using System.Net;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.Customers;
using FieldOps.IntegrationTests.ServiceRequests;
using FieldOps.IntegrationTests.Team;

namespace FieldOps.IntegrationTests.Dispatch;

/// <summary>Dispatch lifecycle, conflicts and justification, hard rules and field validation (dispatch-calendar AC-06, AC-08 to AC-14).</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class DispatchSaveTests(CompanySettingsDatabaseFixture database)
{
    // AC-06, AC-08, AC-09, AC-14: prefill, statuses, histories, one primary, work order status, audit without the note, repeat and stale token.
    [Fact]
    public async Task Dispatch_MovesThroughScheduledAndAssigned_WithHistoryPrimaryWorkOrderStatusAndAudit()
    {
        var world = await database.SeedWorldAsync();
        await using var host = RequestsHost.Create(database);
        var (owner, ownerMember) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var user = ownerMember.UserId;
        var ann = await database.SeedAvailableTechAsync(world.Org, world.BranchA, "Ann", "Alpha");
        var bob = await database.SeedAvailableTechAsync(world.Org, world.BranchA, "Bob", "Bravo");
        var preferred = DispatchSeed.Future(3, 10);
        var prefill = await database.SeedOrderAsync(world, user, title: "Prefilled", minutes: 90, preferredStart: preferred, preferredEnd: preferred.AddHours(2));
        var order = await database.SeedOrderAsync(world, user);
        var visit = order.Visit;
        var slot = DispatchSeed.Future(4, 9);

        // BR-08 prefill: the date and time of the preferred window, end = start + duration, window start + 2 h, nothing written.
        var detail = await DispatchSeed.ReadAsync(await DispatchSeed.GetVisitAsync(host, owner, prefill.Visit));
        Assert.Equal(DispatchSeed.Date(preferred), detail["values"]!["date"]!.GetValue<string>());
        Assert.Equal("10:00", detail["values"]!["start"]!.GetValue<string>());
        Assert.Equal("11:30", detail["values"]!["end"]!.GetValue<string>());
        Assert.Equal("start_plus_2h", detail["values"]!["arrivalWindow"]!.GetValue<string>());
        Assert.Empty(detail["values"]!["technicianIds"]!.AsArray());
        Assert.True(detail["values"]!["notifyCustomer"]!.GetValue<bool>());
        Assert.True(detail["customer"]!["hasEmail"]!.GetValue<bool>());
        Assert.Equal("Prefilled", detail["workOrder"]!["title"]!.GetValue<string>());
        Assert.Equal(["Ann Alpha", "Bob Bravo"], detail["technicians"]!.AsArray().Select(item => item!["name"]!.GetValue<string>()).ToArray());
        Assert.True(detail["canManage"]!.GetValue<bool>());
        Assert.False(detail["isLocked"]!.GetValue<bool>());
        Assert.Equal("unscheduled|-|0|0|0", await database.VisitStateAsync(prefill.Visit));

        var token0 = await DispatchSeed.TokenAsync(host, owner, visit);

        // Schedule without technicians: unscheduled -> scheduled, the work order becomes scheduled.
        var first = await DispatchSeed.PutAsync(host, owner, visit, DispatchSeed.Body(slot, token0));
        Assert.Equal("no-store", first.Headers.CacheControl?.ToString());
        var scheduled = await DispatchSeed.ReadAsync(first);
        Assert.Equal("scheduled", scheduled["visit"]!["status"]!.GetValue<string>());
        Assert.Equal("scheduled", scheduled["workOrderStatus"]!.GetValue<string>());
        Assert.False(scheduled["notified"]!.GetValue<bool>());
        Assert.Null(scheduled["materializedVisitId"]);
        Assert.Equal(DispatchSeed.Date(slot), scheduled["visit"]!["values"]!["date"]!.GetValue<string>());
        Assert.Equal("scheduled", await database.ScalarAsync<string>("SELECT status::text FROM work_orders WHERE id = @w", ("w", order.Order)));
        Assert.Equal(1L, await database.ScalarAsync<long>("SELECT COUNT(*) FROM audit_logs WHERE entity_id = @w AND action = 'work_order.status_changed'", ("w", order.Order)));
        var token1 = scheduled["visit"]!["updatedAt"]!.GetValue<string>();
        Assert.NotEqual(token0, token1);

        // Two technicians, ann primary: scheduled -> assigned.
        var two = await DispatchSeed.ReadAsync(await DispatchSeed.PutAsync(host, owner, visit, DispatchSeed.Body(slot, token1, technicians: [ann, bob], primary: ann)));
        Assert.Equal("assigned", two["visit"]!["status"]!.GetValue<string>());
        Assert.Equal(2L, await database.ActiveAssignmentsAsync(visit));
        Assert.Equal(
            $"{ann}|{user}",
            await database.ScalarAsync<string>(
                "SELECT technician_id || '|' || assigned_by_user_id FROM visit_assignments WHERE visit_id = @v AND unassigned_at IS NULL AND is_primary", ("v", visit)));

        // The primary moves to bob while ann stays (the partial unique index never sees two primaries).
        var swapped = await DispatchSeed.ReadAsync(await DispatchSeed.PutAsync(
            host, owner, visit, DispatchSeed.Body(slot, two["visit"]!["updatedAt"]!.GetValue<string>(), technicians: [ann, bob], primary: bob)));
        Assert.Equal(bob, swapped["visit"]!["values"]!["primaryTechnicianId"]!.GetValue<Guid>());
        Assert.Equal(1L, await database.ScalarAsync<long>("SELECT COUNT(*) FROM visit_assignments WHERE visit_id = @v AND unassigned_at IS NULL AND is_primary", ("v", visit)));
        Assert.Equal(2L, await database.ScalarAsync<long>("SELECT COUNT(*) FROM visit_status_history WHERE visit_id = @v", ("v", visit)));

        // Ann leaves with a note, then every technician leaves: assigned -> scheduled, closed rows keep their history.
        var note = "TOP SECRET NOTE for the technician";
        var noted = await DispatchSeed.ReadAsync(await DispatchSeed.PutAsync(
            host, owner, visit, DispatchSeed.Body(slot, swapped["visit"]!["updatedAt"]!.GetValue<string>(), technicians: [bob], note: note)));
        Assert.Equal(note, noted["visit"]!["values"]!["dispatchNote"]!.GetValue<string>());
        Assert.Equal(1L, await database.ActiveAssignmentsAsync(visit));
        var emptied = await DispatchSeed.ReadAsync(await DispatchSeed.PutAsync(
            host, owner, visit, DispatchSeed.Body(slot, noted["visit"]!["updatedAt"]!.GetValue<string>(), note: note)));
        Assert.Equal("scheduled", emptied["visit"]!["status"]!.GetValue<string>());
        Assert.Equal(0L, await database.ActiveAssignmentsAsync(visit));
        Assert.Equal(2L, await database.ScalarAsync<long>("SELECT COUNT(*) FROM visit_assignments WHERE visit_id = @v AND unassigned_at IS NOT NULL", ("v", visit)));
        Assert.Equal(3L, await database.ScalarAsync<long>("SELECT COUNT(*) FROM visit_status_history WHERE visit_id = @v", ("v", visit)));
        Assert.Equal(
            1L,
            await database.ScalarAsync<long>(
                "SELECT COUNT(*) FROM visit_status_history WHERE visit_id = @v AND from_status = 'assigned' AND to_status = 'scheduled' AND changed_by_user_id = @u AND reason IS NULL",
                ("v", visit),
                ("u", user)));

        // A technician can be added again later: a new row, the closed one stays.
        var again = await DispatchSeed.ReadAsync(await DispatchSeed.PutAsync(
            host, owner, visit, DispatchSeed.Body(slot, emptied["visit"]!["updatedAt"]!.GetValue<string>(), technicians: [ann], window: "around_1h", note: note)));
        Assert.Equal("assigned", again["visit"]!["status"]!.GetValue<string>());
        Assert.Equal("around_1h", again["visit"]!["values"]!["arrivalWindow"]!.GetValue<string>());
        Assert.Equal(3L, await database.ScalarAsync<long>("SELECT COUNT(*) FROM visit_assignments WHERE visit_id = @v", ("v", visit)));

        // BR-19: one audit row per effective save, ids and statuses only, never the note text.
        Assert.Equal(6L, await database.ScalarAsync<long>("SELECT COUNT(*) FROM audit_logs WHERE entity_id = @v AND action = 'visit.dispatched'", ("v", visit)));
        var audit = (await database.AuditTextAsync(visit)).Replace(" ", string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("TOPSECRET", audit, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"noteChanged\":true", audit, StringComparison.Ordinal);
        Assert.Contains("\"arrivalWindowStart\"", audit, StringComparison.Ordinal);
        Assert.Contains("\"primaryTechnicianId\"", audit, StringComparison.Ordinal);
        Assert.Contains("\"materializedVisitId\":null", audit, StringComparison.Ordinal);
        Assert.Equal(
            $"visit|{user}|{world.BranchA}",
            await database.ScalarAsync<string>(
                "SELECT entity_type || '|' || actor_user_id || '|' || branch_id FROM audit_logs WHERE entity_id = @v AND action = 'visit.dispatched' ORDER BY id LIMIT 1", ("v", visit)));
        var statusAudit = (await database.AuditTextAsync(order.Order)).Replace(" ", string.Empty, StringComparison.Ordinal);
        Assert.Contains("\"status\":\"ready_to_schedule\"", statusAudit, StringComparison.Ordinal);
        Assert.Contains("\"status\":\"scheduled\"", statusAudit, StringComparison.Ordinal);
        Assert.Contains($"\"visitId\":\"{visit}\"", statusAudit, StringComparison.Ordinal);

        // The jobs list and detail show the status and the visit status (BR-16).
        var jobs = await DispatchSeed.ReadAsync(await host.SendAsync(HttpMethod.Get, "/work-orders?page=1&pageSize=100", owner));
        Assert.Equal(
            "scheduled",
            jobs["items"]!.AsArray().Single(item => item!["id"]!.GetValue<Guid>() == order.Order)!["status"]!.GetValue<string>());
        Assert.Equal("assigned", await database.ScalarAsync<string>("SELECT status::text FROM visits WHERE id = @v", ("v", visit)));

        // AC-14: the identical request is a 200 without any new row; a stale token is a 409 with its code.
        var latest = again["visit"]!["updatedAt"]!.GetValue<string>();
        var repeated = await DispatchSeed.ReadAsync(
            await DispatchSeed.PutAsync(host, owner, visit, DispatchSeed.Body(slot, latest, technicians: [ann], window: "around_1h", note: note)));
        Assert.Equal(latest, repeated["visit"]!["updatedAt"]!.GetValue<string>());
        Assert.False(repeated["notified"]!.GetValue<bool>());
        Assert.Equal(6L, await database.ScalarAsync<long>("SELECT COUNT(*) FROM audit_logs WHERE entity_id = @v AND action = 'visit.dispatched'", ("v", visit)));
        Assert.Equal(4L, await database.ScalarAsync<long>("SELECT COUNT(*) FROM visit_status_history WHERE visit_id = @v", ("v", visit)));
        Assert.Equal(3L, await database.ScalarAsync<long>("SELECT COUNT(*) FROM visit_assignments WHERE visit_id = @v", ("v", visit)));

        var stale = await DispatchSeed.PutAsync(host, owner, visit, DispatchSeed.Body(slot, token1, technicians: [bob], note: "other"));
        Assert.Equal("no-store", stale.Headers.CacheControl?.ToString());
        await DispatchSeed.ProblemAsync(stale, HttpStatusCode.Conflict, "visit_changed");
        Assert.Equal(6L, await database.ScalarAsync<long>("SELECT COUNT(*) FROM audit_logs WHERE entity_id = @v AND action = 'visit.dispatched'", ("v", visit)));
    }

    // AC-10: each conflict type is a 409 with its code and no write, then a 200 with a valid justification; the audit keeps both.
    [Theory]
    [InlineData("missing_skills", "missing_skills")]
    [InlineData("time_off", "time_off")]
    [InlineData("break", "break")]
    [InlineData("outside_availability", "outside_availability")]
    [InlineData("overlap_visit", "overlap")]
    [InlineData("overlap_assessment", "overlap")]
    [InlineData("combined_skills", "")]
    public async Task Conflicts_AreReportedWithoutJustification_AndSavedWithOne(string kind, string expectedCode)
    {
        var world = await database.SeedWorldAsync();
        await using var host = RequestsHost.Create(database);
        var (owner, ownerMember) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var user = ownerMember.UserId;
        var welding = await database.SeedSkillAsync(world.Org, "Welding");
        var plumbing = await database.SeedSkillAsync(world.Org, "Plumbing");
        var slot = DispatchSeed.Future(4, kind == "break" ? 12 : kind == "outside_availability" ? 19 : 9);
        var combined = kind == "combined_skills";
        var ann = await database.SeedAvailableTechAsync(world.Org, world.BranchA, "Ann", "Alpha", "active", true, combined ? [welding] : []);
        var bob = await database.SeedAvailableTechAsync(world.Org, world.BranchA, "Bob", "Bravo", "active", true, [plumbing]);
        var order = await database.SeedOrderAsync(
            world, user, skills: kind == "missing_skills" ? [welding] : combined ? [welding, plumbing] : []);
        string? overlapNumber = null;

        switch (kind)
        {
            case "time_off":
                await database.SeedExceptionAsync(ann, DispatchSeed.Future(4, 8), DispatchSeed.Future(4, 18));
                break;
            case "overlap_visit":
                var other = await database.SeedOrderAsync(world, user, status: "scheduled", title: "Other job");
                await database.SeedVisitAsync(world.Org, other.Order, 2, "assigned", slot.AddMinutes(30), slot.AddMinutes(90), ann, user);
                overlapNumber = $"WO-{other.Number}";
                break;
            case "overlap_assessment":
                var request = await database.SeedRequestAsync(world, status: "assessment_scheduled");
                await database.SeedAssessmentAsync(world.Org, request.Id, slot, slot.AddHours(1), user, ann);
                break;
        }

        var selection = combined ? new[] { ann, bob } : [ann];
        var token = await DispatchSeed.TokenAsync(host, owner, order.Visit);
        var evaluation = await DispatchSeed.ReadAsync(await DispatchSeed.EvaluateAsync(host, owner, order.Visit, DispatchSeed.EvaluationBody(slot, 60, selection)));

        if (combined)
        {
            Assert.Empty(evaluation["conflicts"]!.AsArray());
            Assert.True(evaluation["skills"]!["passed"]!.GetValue<bool>());
            Assert.StartsWith("Skills match", evaluation["skills"]!["label"]!.GetValue<string>(), StringComparison.Ordinal);

            var done = await DispatchSeed.ReadAsync(await DispatchSeed.PutAsync(host, owner, order.Visit, DispatchSeed.Body(slot, token, technicians: selection, reason: "ignored without conflicts")));
            Assert.Equal("assigned", done["visit"]!["status"]!.GetValue<string>());
            Assert.Contains("\"overrideReason\":null", (await database.AuditTextAsync(order.Visit)).Replace(" ", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);

            return;
        }

        Assert.Equal([expectedCode], DispatchSeed.Codes(evaluation["conflicts"]));
        Assert.Single(evaluation["impact"]!.AsArray());
        Assert.Contains("No previous job", evaluation["impact"]![0]!["previous"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.DoesNotContain(
            evaluation["ranking"]!.AsArray(),
            item => item!["technicianId"]!.GetValue<Guid>() == ann && item["bestMatch"]!.GetValue<bool>());

        if (kind == "overlap_visit")
        {
            Assert.StartsWith($"Overlaps #{overlapNumber} ", evaluation["conflicts"]![0]!["label"]!.GetValue<string>(), StringComparison.Ordinal);
            Assert.False(evaluation["checks"]![0]!["overlap"]!["passed"]!.GetValue<bool>());
        }

        if (kind == "overlap_assessment")
        {
            Assert.StartsWith("Overlaps assessment ", evaluation["conflicts"]![0]!["label"]!.GetValue<string>(), StringComparison.Ordinal);
        }

        // Without a justification: 409 with the codes and the list, and nothing written.
        var blocked = await DispatchSeed.PutAsync(host, owner, order.Visit, DispatchSeed.Body(slot, token, technicians: [ann]));
        var problem = await DispatchSeed.ProblemAsync(blocked, HttpStatusCode.Conflict, "scheduling_conflicts");
        Assert.Equal([expectedCode], DispatchSeed.Codes(problem["conflicts"]));
        Assert.Equal("unscheduled|-|0|0|0", await database.VisitStateAsync(order.Visit));

        // A 9-character reason is a 400; a valid one (trimmed) saves and the audit stores the conflicts and the reason.
        await DispatchSeed.ProblemAsync(
            await DispatchSeed.PutAsync(host, owner, order.Visit, DispatchSeed.Body(slot, token, technicians: [ann], reason: "123456789")),
            HttpStatusCode.BadRequest,
            errorKey: "overrideReason");
        var saved = await DispatchSeed.ReadAsync(
            await DispatchSeed.PutAsync(host, owner, order.Visit, DispatchSeed.Body(slot, token, technicians: [ann], reason: "  Customer approved the overlap  ")));
        Assert.Equal("assigned", saved["visit"]!["status"]!.GetValue<string>());
        var audit = (await database.AuditTextAsync(order.Visit)).Replace(" ", string.Empty, StringComparison.Ordinal);
        Assert.Contains($"\"code\":\"{expectedCode}\"", audit, StringComparison.Ordinal);
        Assert.Contains("\"overrideReason\":\"Customerapprovedtheoverlap\"", audit, StringComparison.Ordinal);
    }

    // AC-11, AC-12: foreign, out-of-scope, inactive and other-branch technicians and locked visits are never overridable.
    [Fact]
    public async Task HardRules_AreNeverBypassedByAJustification()
    {
        var world = await database.SeedWorldAsync();
        var foreign = await database.SeedWorldAsync();
        await using var host = RequestsHost.Create(database);
        var (owner, ownerMember) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var (dispatcher, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.DispatcherRoleId, "Dee", "Spatch", world.BranchA);
        var user = ownerMember.UserId;
        var ann = await database.SeedAvailableTechAsync(world.Org, world.BranchA, "Ann", "Alpha");
        var inactive = await database.SeedAvailableTechAsync(world.Org, world.BranchA, "Ina", "Inactive", "inactive");
        var otherBranch = await database.SeedAvailableTechAsync(world.Org, world.BranchB, "Bob", "Bravo");
        var foreignTech = await database.SeedAvailableTechAsync(foreign.Org, foreign.BranchA, "Fay", "Foreign");
        var order = await database.SeedOrderAsync(world, user);
        var slot = DispatchSeed.Future(4, 9);
        var token = await DispatchSeed.TokenAsync(host, owner, order.Visit);

        foreach (var reason in new string?[] { null, "A perfectly valid justification" })
        {
            foreach (var (cookie, tech, expected) in new[]
            {
                (owner, foreignTech, HttpStatusCode.NotFound),
                (dispatcher, otherBranch, HttpStatusCode.NotFound),
                (owner, inactive, HttpStatusCode.BadRequest),
                (owner, otherBranch, HttpStatusCode.BadRequest),
            })
            {
                var evaluated = await DispatchSeed.EvaluateAsync(host, cookie, order.Visit, DispatchSeed.EvaluationBody(slot, 60, tech));
                var put = await DispatchSeed.PutAsync(host, cookie, order.Visit, DispatchSeed.Body(slot, token, technicians: [tech], reason: reason));

                Assert.Equal(expected, evaluated.StatusCode);
                Assert.Equal(expected, put.StatusCode);

                if (expected == HttpStatusCode.BadRequest)
                {
                    await DispatchSeed.ProblemAsync(put, expected, errorKey: "technicianIds");
                }
            }
        }

        Assert.Equal("unscheduled|-|0|0|0", await database.VisitStateAsync(order.Visit));

        // BR-13: started or closed visits and visits of closed work orders are read-only, override or not.
        var locked = new List<Guid>();

        foreach (var status in new[] { "on_the_way", "in_progress", "paused", "completed", "needs_correction", "approved", "cancelled" })
        {
            locked.Add((await database.SeedOrderAsync(world, user, status: "in_progress", visitStatus: status, start: slot, end: slot.AddHours(1))).Visit);
        }

        foreach (var orderStatus in new[] { "cancelled", "completed", "approved_for_billing" })
        {
            locked.Add((await database.SeedOrderAsync(world, user, status: orderStatus, visitStatus: "scheduled", start: slot, end: slot.AddHours(1))).Visit);
        }

        foreach (var visit in locked)
        {
            var before = await database.VisitStateAsync(visit);
            var detail = await DispatchSeed.ReadAsync(await DispatchSeed.GetVisitAsync(host, owner, visit));

            Assert.True(detail["isLocked"]!.GetValue<bool>());
            await DispatchSeed.ProblemAsync(
                await DispatchSeed.PutAsync(host, owner, visit, DispatchSeed.Body(slot.AddHours(2), detail["updatedAt"]!.GetValue<string>(), technicians: [ann], reason: "A perfectly valid justification")),
                HttpStatusCode.Conflict,
                "visit_locked");
            await DispatchSeed.ProblemAsync(
                await DispatchSeed.EvaluateAsync(host, owner, visit, DispatchSeed.EvaluationBody(slot.AddHours(2), 60, ann)),
                HttpStatusCode.Conflict,
                "visit_locked");
            Assert.Equal(before, await database.VisitStateAsync(visit));
        }
    }

    // AC-13: every BR-11 rule is a 400 with its key and no change; a started-late visit can still change technicians.
    [Fact]
    public async Task Validation_RejectsEachInvalidValue_AndAPassedVisitCanStillChangeTechnicians()
    {
        var world = await database.SeedWorldAsync();
        await using var host = RequestsHost.Create(database);
        var (owner, ownerMember) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var user = ownerMember.UserId;
        var ann = await database.SeedAvailableTechAsync(world.Org, world.BranchA, "Ann", "Alpha");
        var bob = await database.SeedAvailableTechAsync(world.Org, world.BranchA, "Bob", "Bravo");
        var order = await database.SeedOrderAsync(world, user);
        var noEmail = await database.SeedCustomerAsync(world.Org, world.BranchA, "No Email", email: null, phone: null);
        var property = (await database.QueryGuidsAsync("SELECT id FROM properties WHERE customer_id = @c", ("c", noEmail))).Single();
        var withoutEmail = await database.SeedOrderAsync(world, user, customer: noEmail, property: property);
        var slot = DispatchSeed.Future(4, 9);
        var token = await DispatchSeed.TokenAsync(host, owner, order.Visit);
        var many = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToArray();

        JsonObject Valid() => DispatchSeed.Body(slot, token, technicians: [ann]);

        var cases = new (string Key, Action<JsonObject> Change, Guid? Visit)[]
        {
            ("date", body => body["date"] = null, null),
            ("date", body => body["date"] = DispatchSeed.Date(DateTimeOffset.UtcNow.AddDays(-1)), null),
            ("date", body => body["date"] = "10/05/2030", null),
            ("start", body => body["start"] = null, null),
            ("start", body => body["start"] = "09:10", null),
            ("start", body => { body["date"] = DispatchSeed.Date(DateTimeOffset.UtcNow); body["start"] = "00:00"; body["end"] = "01:00"; }, null),
            ("end", body => body["end"] = null, null),
            ("end", body => body["end"] = "08:45", null),
            ("end", body => body["end"] = "09:00", null),
            ("end", body => { body["start"] = "06:00"; body["end"] = "19:00"; }, null),
            ("arrivalWindow", body => body["arrivalWindow"] = "whenever", null),
            ("arrivalWindow", body => { body["start"] = "00:15"; body["end"] = "01:00"; body["arrivalWindow"] = "around_1h"; }, null),
            ("arrivalWindow", body => { body["start"] = "23:00"; body["end"] = "23:30"; body["arrivalWindow"] = "start_plus_2h"; }, null),
            ("technicianIds", body => body["technicianIds"] = new JsonArray([.. many.Select(id => (JsonNode?)JsonValue.Create(id))]), null),
            ("technicianIds", body => body["technicianIds"] = new JsonArray(JsonValue.Create(ann), JsonValue.Create(ann)), null),
            ("technicianIds", body => body["technicianIds"] = new JsonArray(JsonValue.Create("not-a-guid")), null),
            ("primaryTechnicianId", body => body["primaryTechnicianId"] = null, null),
            ("primaryTechnicianId", body => body["primaryTechnicianId"] = bob, null),
            ("primaryTechnicianId", body => { body["technicianIds"] = new JsonArray(); body["primaryTechnicianId"] = ann; }, null),
            ("dispatchNote", body => body["dispatchNote"] = new string('n', 1001), null),
            ("overrideReason", body => body["overrideReason"] = "short", null),
            ("overrideReason", body => body["overrideReason"] = new string('r', 501), null),
            ("notifyCustomer", body => body["notifyCustomer"] = null, null),
            ("sendTechnicianDetails", body => body["sendTechnicianDetails"] = null, null),
            ("notifyCustomer", body => body["notifyCustomer"] = true, withoutEmail.Visit),
            ("updatedAt", body => body["updatedAt"] = null, null),
            ("updatedAt", body => body["updatedAt"] = "yesterday", null),
        };

        foreach (var (key, change, visit) in cases)
        {
            var target = visit ?? order.Visit;
            var body = Valid();
            change(body);

            await DispatchSeed.ProblemAsync(await DispatchSeed.PutAsync(host, owner, target, body), HttpStatusCode.BadRequest, errorKey: key);
            Assert.Equal("unscheduled|-|0|0|0", await database.VisitStateAsync(target));
        }

        Assert.False((await DispatchSeed.ReadAsync(await DispatchSeed.GetVisitAsync(host, owner, withoutEmail.Visit)))["customer"]!["hasEmail"]!.GetValue<bool>());
        Assert.False((await DispatchSeed.ReadAsync(await DispatchSeed.GetVisitAsync(host, owner, withoutEmail.Visit)))["values"]!["notifyCustomer"]!.GetValue<bool>());

        // Evaluation applies the same shape rules.
        await DispatchSeed.ProblemAsync(
            await DispatchSeed.EvaluateAsync(host, owner, order.Visit, DispatchSeed.EvaluationBody(DispatchSeed.Future(-2, 9), 60, ann)),
            HttpStatusCode.BadRequest,
            errorKey: "date");

        // A visit whose stored start passed without starting can still change technicians, note and notification.
        var passed = DispatchSeed.Future(-1, 9);
        var late = await database.SeedOrderAsync(world, user, status: "scheduled", visitStatus: "assigned", start: passed, end: passed.AddHours(1));
        await database.AssignAsync(late.Visit, ann, user);
        var lateToken = await DispatchSeed.TokenAsync(host, owner, late.Visit);
        var changed = await DispatchSeed.ReadAsync(
            await DispatchSeed.PutAsync(host, owner, late.Visit, DispatchSeed.Body(passed, lateToken, technicians: [bob], note: "Swap")));

        Assert.Equal("assigned", changed["visit"]!["status"]!.GetValue<string>());
        Assert.Equal([bob], changed["visit"]!["values"]!["technicianIds"]!.AsArray().Select(id => id!.GetValue<Guid>()).ToArray());
        Assert.Equal(DispatchSeed.Date(passed), changed["visit"]!["values"]!["date"]!.GetValue<string>());

        // Moving the passed visit to another past time is rejected.
        await DispatchSeed.ProblemAsync(
            await DispatchSeed.PutAsync(host, owner, late.Visit, DispatchSeed.Body(passed.AddHours(1), changed["visit"]!["updatedAt"]!.GetValue<string>(), technicians: [bob])),
            HttpStatusCode.BadRequest,
            errorKey: "date");
    }
}
