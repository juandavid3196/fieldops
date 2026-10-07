using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.ServiceRequests;
using FieldOps.IntegrationTests.Team;

namespace FieldOps.IntegrationTests.Dispatch;

/// <summary>Recurring occurrences, concurrency and customer notifications of the dispatch (dispatch-calendar AC-15, AC-16, AC-18).</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class DispatchFlowTests(CompanySettingsDatabaseFixture database)
{
    // AC-16, AC-15: one occurrence per first schedule (also under concurrent saves), none beyond the count, and serialized technicians.
    [Fact]
    public async Task Recurrence_MaterializesOnce_AndConcurrentDispatchesSerializeTechnicianOverlaps()
    {
        var world = await database.SeedWorldAsync();
        await using var host = RequestsHost.Create(database);
        var (owner, ownerMember) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var user = ownerMember.UserId;
        var ann = await database.SeedAvailableTechAsync(world.Org, world.BranchA, "Ann", "Alpha");
        var order = await database.SeedOrderAsync(
            world, user, jobType: "recurring", frequency: "weekly", count: 3, tasks: ["Inspect", "Clear", "Test"]);
        var slot = DispatchSeed.Future(4, 9);

        // Visit 1 receives its first schedule: visit 2 is created unscheduled with the window one week later.
        var first = await DispatchSeed.ReadAsync(
            await DispatchSeed.PutAsync(host, owner, order.Visit, DispatchSeed.Body(slot, await DispatchSeed.TokenAsync(host, owner, order.Visit))));
        var second = first["materializedVisitId"]!.GetValue<Guid>();

        Assert.Equal(
            $"2|unscheduled|{slot.AddDays(7).ToUnixTimeSeconds()}|{slot.AddDays(7).AddHours(2).ToUnixTimeSeconds()}",
            await database.ScalarAsync<string>(
                "SELECT visit_number || '|' || status::text || '|' || EXTRACT(EPOCH FROM preferred_start)::bigint || '|' || EXTRACT(EPOCH FROM preferred_end)::bigint FROM visits WHERE id = @v",
                ("v", second)));
        Assert.Equal(
            "Inspect|Clear|Test",
            await database.ScalarAsync<string>("SELECT string_agg(label, '|' ORDER BY sort_order) FROM visit_checklist_items WHERE visit_id = @v", ("v", second)));
        Assert.Equal(
            1L,
            await database.ScalarAsync<long>("SELECT COUNT(*) FROM visit_status_history WHERE visit_id = @v AND from_status IS NULL AND to_status = 'unscheduled'", ("v", second)));
        Assert.Contains($"\"materializedVisitId\":\"{second}\"", (await database.AuditTextAsync(order.Visit)).Replace(" ", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);

        // Rescheduling or repeating visit 1 creates nothing.
        var token = first["visit"]!["updatedAt"]!.GetValue<string>();
        var moved = await DispatchSeed.ReadAsync(await DispatchSeed.PutAsync(host, owner, order.Visit, DispatchSeed.Body(slot.AddHours(2), token)));
        Assert.Null(moved["materializedVisitId"]);
        await DispatchSeed.ReadAsync(await DispatchSeed.PutAsync(host, owner, order.Visit, DispatchSeed.Body(slot.AddHours(2), moved["visit"]!["updatedAt"]!.GetValue<string>())));
        Assert.Equal(2L, await database.ScalarAsync<long>("SELECT COUNT(*) FROM visits WHERE work_order_id = @w", ("w", order.Order)));

        // Visit 2 then visit 3: visit 3 appears, visit 4 never does.
        var secondSaved = await DispatchSeed.ReadAsync(
            await DispatchSeed.PutAsync(host, owner, second, DispatchSeed.Body(slot.AddDays(7), await DispatchSeed.TokenAsync(host, owner, second), technicians: [ann])));
        var third = secondSaved["materializedVisitId"]!.GetValue<Guid>();
        Assert.Equal(3, await database.ScalarAsync<int>("SELECT visit_number FROM visits WHERE id = @v", ("v", third)));
        Assert.Equal(
            slot.AddDays(14).ToUnixTimeSeconds(),
            await database.ScalarAsync<long>("SELECT EXTRACT(EPOCH FROM preferred_start)::bigint FROM visits WHERE id = @v", ("v", third)));
        var thirdSaved = await DispatchSeed.ReadAsync(
            await DispatchSeed.PutAsync(host, owner, third, DispatchSeed.Body(slot.AddDays(14), await DispatchSeed.TokenAsync(host, owner, third))));
        Assert.Null(thirdSaved["materializedVisitId"]);
        Assert.Equal(3L, await database.ScalarAsync<long>("SELECT COUNT(*) FROM visits WHERE work_order_id = @w", ("w", order.Order)));

        // The same first save sent twice at once: one succeeds and creates visit 2, the other loses the race on the token.
        var racing = await database.SeedOrderAsync(world, user, jobType: "recurring", frequency: "weekly", count: 3, tasks: ["Inspect"]);
        var racingToken = await DispatchSeed.TokenAsync(host, owner, racing.Visit);
        var racers = await Task.WhenAll(
            DispatchSeed.PutAsync(host, owner, racing.Visit, DispatchSeed.Body(slot, racingToken)),
            DispatchSeed.PutAsync(host, owner, racing.Visit, DispatchSeed.Body(slot, racingToken)));

        Assert.Equal(1, racers.Count(response => response.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, racers.Count(response => response.StatusCode == HttpStatusCode.Conflict));
        await DispatchSeed.ProblemAsync(racers.Single(response => response.StatusCode == HttpStatusCode.Conflict), HttpStatusCode.Conflict, "visit_changed");
        Assert.Equal(2L, await database.ScalarAsync<long>("SELECT COUNT(*) FROM visits WHERE work_order_id = @w", ("w", racing.Order)));

        // AC-15: two dispatches of different visits to the same technician and slot, no justification: exactly one wins.
        for (var round = 0; round < 3; round++)
        {
            var contested = DispatchSeed.Future(6 + round, 10);
            var left = await database.SeedOrderAsync(world, user);
            var right = await database.SeedOrderAsync(world, user);
            var leftToken = await DispatchSeed.TokenAsync(host, owner, left.Visit);
            var rightToken = await DispatchSeed.TokenAsync(host, owner, right.Visit);

            var results = await Task.WhenAll(
                DispatchSeed.PutAsync(host, owner, left.Visit, DispatchSeed.Body(contested, leftToken, technicians: [ann])),
                DispatchSeed.PutAsync(host, owner, right.Visit, DispatchSeed.Body(contested.AddMinutes(30), rightToken, technicians: [ann])));

            Assert.Equal(1, results.Count(response => response.StatusCode == HttpStatusCode.OK));
            var loser = results.Single(response => response.StatusCode == HttpStatusCode.Conflict);
            var problem = await DispatchSeed.ProblemAsync(loser, HttpStatusCode.Conflict, "scheduling_conflicts");
            Assert.Equal(["overlap"], DispatchSeed.Codes(problem["conflicts"]));
            Assert.Equal(
                1L,
                await database.ScalarAsync<long>(
                    "SELECT COUNT(*) FROM visit_assignments WHERE technician_id = @t AND unassigned_at IS NULL AND visit_id IN (@a, @b)",
                    ("t", ann),
                    ("a", left.Visit),
                    ("b", right.Visit)));
        }

        // The BR-22 partial unique indexes reject a second active row and a second active primary.
        var guarded = await database.SeedOrderAsync(world, user);
        var bob = await database.SeedAvailableTechAsync(world.Org, world.BranchA, "Bob", "Bravo");
        await database.AssignAsync(guarded.Visit, ann, user);
        var duplicate = await Assert.ThrowsAnyAsync<Exception>(() => database.AssignAsync(guarded.Visit, ann, user, primary: false));
        Assert.Contains("ux_visit_assignments_active", duplicate.ToString(), StringComparison.Ordinal);
        var secondPrimary = await Assert.ThrowsAnyAsync<Exception>(() => database.AssignAsync(guarded.Visit, bob, user));
        Assert.Contains("ux_visit_assignments_primary", secondPrimary.ToString(), StringComparison.Ordinal);
        await database.ExecuteAsync("UPDATE visit_assignments SET unassigned_at = now() WHERE visit_id = @v", ("v", guarded.Visit));
        await database.AssignAsync(guarded.Visit, ann, user);
    }

    // AC-18: exactly one email per BR-17 case after commit, subjects and bodies without note, reason or contact data, and a failure that stays a 200.
    [Fact]
    public async Task Notifications_SendOneEmailPerBr17Case_AfterCommit_AndAFailureOnlyLogsTheVisitId()
    {
        var world = await database.SeedWorldAsync();
        await using var host = RequestsHost.Create(database);
        var (owner, ownerMember) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var user = ownerMember.UserId;
        var organization = await database.ScalarAsync<string>("SELECT name FROM organizations WHERE id = @o", ("o", world.Org));
        var ann = await database.SeedAvailableTechAsync(world.Org, world.BranchA, "Ann", "Alpha");
        var bob = await database.SeedAvailableTechAsync(world.Org, world.BranchA, "Bob", "Bravo");
        var cy = await database.SeedAvailableTechAsync(world.Org, world.BranchA, "Cy", "Charlie");
        await database.ExecuteAsync("UPDATE technician_profiles SET email = 'ann.secret@example.com', phone = '5559990000' WHERE id = @t", ("t", ann));
        var order = await database.SeedOrderAsync(world, user);
        var visit = order.Visit;
        var morning = DispatchSeed.Future(4, 9);
        var afternoon = DispatchSeed.Future(4, 14);
        var blocker = await database.SeedOrderAsync(world, user, status: "scheduled", title: "Blocking job");
        await database.SeedVisitAsync(world.Org, blocker.Order, 2, "assigned", afternoon.AddMinutes(30), afternoon.AddMinutes(90), ann, user);
        var number = $"WO-{order.Number}";
        const string note = "INTERNAL-NOTE-ONLY";
        const string reason = "REASON-SECRET-9999";

        var token = await DispatchSeed.TokenAsync(host, owner, visit);
        var sent = host.Sender.Messages.Count;

        async Task<JsonNode> SaveAsync(JsonObject body)
        {
            var response = await DispatchSeed.ReadAsync(await DispatchSeed.PutAsync(host, owner, visit, body));
            token = response["visit"]!["updatedAt"]!.GetValue<string>();

            return response;
        }

        // First schedule with technician details: one "scheduled" email.
        var scheduled = await SaveAsync(DispatchSeed.Body(morning, token, technicians: [ann], note: note, notify: true));
        Assert.True(scheduled["notified"]!.GetValue<bool>());
        Assert.Equal(sent + 1, host.Sender.Messages.Count);
        var email = host.Sender.Messages[^1];
        Assert.Equal("carla@example.com", email.To);
        Assert.Equal($"{organization}: visit scheduled for {number}", email.Subject);
        Assert.Contains("Hi Pat,", email.TextBody, StringComparison.Ordinal);
        Assert.Contains(
            $"Your service visit for Fix drain is scheduled for {morning.ToString("ddd, MMM d", CultureInfo.InvariantCulture)} with arrival between 9:00 AM and 11:00 AM.",
            email.TextBody,
            StringComparison.Ordinal);
        Assert.Contains("Your technician: Ann Alpha.", email.TextBody, StringComparison.Ordinal);
        Assert.Contains("Questions? Call us at +1 555 010 0100.", email.TextBody, StringComparison.Ordinal);
        Assert.Contains(organization, email.TextBody, StringComparison.Ordinal);

        // A conflicting reschedule with a justification: one "rescheduled" email that never carries the reason or the note.
        var rescheduled = await SaveAsync(DispatchSeed.Body(afternoon, token, technicians: [ann], note: note, notify: true, reason: reason));
        Assert.True(rescheduled["notified"]!.GetValue<bool>());
        Assert.Equal(sent + 2, host.Sender.Messages.Count);
        Assert.Equal($"{organization}: visit rescheduled for {number}", host.Sender.Messages[^1].Subject);
        Assert.Contains("between 2:00 PM and 4:00 PM", host.Sender.Messages[^1].TextBody, StringComparison.Ordinal);

        // Technician change with details on: one "technician update" email naming the new technician.
        await SaveAsync(DispatchSeed.Body(afternoon, token, technicians: [bob], note: note, notify: true));
        Assert.Equal(sent + 3, host.Sender.Messages.Count);
        Assert.Equal($"{organization}: technician update for {number}", host.Sender.Messages[^1].Subject);
        Assert.Contains("Your technician: Bob Bravo.", host.Sender.Messages[^1].TextBody, StringComparison.Ordinal);

        // No email: technician change with details off, every technician removed, a note-only change, email unchecked.
        var quiet = await SaveAsync(DispatchSeed.Body(afternoon, token, technicians: [cy], note: note, notify: true, details: false));
        Assert.False(quiet["notified"]!.GetValue<bool>());
        await SaveAsync(DispatchSeed.Body(afternoon, token, note: note, notify: true, details: true));
        await SaveAsync(DispatchSeed.Body(afternoon, token, note: "A different note", notify: true));
        await SaveAsync(DispatchSeed.Body(morning, token, note: "A different note", notify: false));
        Assert.Equal(sent + 3, host.Sender.Messages.Count);

        // A provider failure: still a 200, nothing sent, and only the visit id and the failure category are logged.
        host.Sender.Fail = true;
        var failed = await SaveAsync(DispatchSeed.Body(afternoon, token, note: "A different note", notify: true));
        host.Sender.Fail = false;
        Assert.False(failed["notified"]!.GetValue<bool>());
        Assert.Equal("scheduled", failed["visit"]!["status"]!.GetValue<string>());
        Assert.Equal(sent + 3, host.Sender.Messages.Count);
        var log = Assert.Single(host.Logs.Entries, entry => entry.Message.Contains("Visit email failed", StringComparison.Ordinal));
        Assert.Contains(visit.ToString(), log.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("carla@example.com", log.Message, StringComparison.Ordinal);

        // Never in an email: the note, the reason or technician contact data. No notification rows, preferences untouched.
        foreach (var message in host.Sender.Messages.Skip(sent))
        {
            foreach (var secret in new[] { note, reason, "ann.secret@example.com", "5559990000" })
            {
                Assert.DoesNotContain(secret, message.TextBody + message.HtmlBody + message.Subject, StringComparison.Ordinal);
            }
        }

        Assert.Equal(0L, await database.ScalarAsync<long>("SELECT COUNT(*) FROM notifications WHERE organization_id = @o", ("o", world.Org)));
        Assert.Equal(
            "true|true|true",
            await database.ScalarAsync<string>(
                "SELECT notify_customer_when_scheduled || '|' || send_technician_details || '|' || send_arrival_reminder FROM work_orders WHERE id = @w", ("w", order.Order)));
        Assert.Contains("\"notified\":\"email\"", (await database.AuditTextAsync(visit)).Replace(" ", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
    }
}
