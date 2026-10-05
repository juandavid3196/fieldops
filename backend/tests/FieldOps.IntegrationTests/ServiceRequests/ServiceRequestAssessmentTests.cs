using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.Team;
using FieldOps.IntegrationTests.Users;

namespace FieldOps.IntegrationTests.ServiceRequests;

/// <summary>
/// Schedule-assessment backend: schedule/reschedule/cancel with validation, 409 conflicts and concurrency, customer
/// email, planner and calendar, and authorization (AC-02, AC-03, AC-05 to AC-18, AC-22).
/// </summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class ServiceRequestAssessmentTests(CompanySettingsDatabaseFixture database)
{
    private const string Purpose = "Inspect the leaking pipe";

    private static string Url(Guid id, string suffix) => $"/service-requests/{id}/{suffix}";

    private static string Error(JsonNode problem, string key) =>
        problem["errors"]![key]!.AsArray().Single()!.GetValue<string>();

    private static string Code(JsonNode problem) => problem["code"]!.GetValue<string>();

    /// <summary>A UTC Monday at least three days ahead; the organization time zone of the seeded world is UTC.</summary>
    private static DateTimeOffset Monday()
    {
        var day = DateTimeOffset.UtcNow.Date.AddDays(3);

        while (day.DayOfWeek != DayOfWeek.Monday)
        {
            day = day.AddDays(1);
        }

        return new DateTimeOffset(day, TimeSpan.Zero);
    }

    private static JsonObject Slot(
        DateTimeOffset day,
        int hour,
        int minutes = 60,
        Guid? technician = null,
        Guid? branch = null,
        string? purpose = Purpose,
        string? instructions = null,
        bool? notify = false)
    {
        var start = day.AddHours(hour);

        return new JsonObject
        {
            ["start"] = ServiceRequestSeed.Local(start),
            ["end"] = ServiceRequestSeed.Local(start.AddMinutes(minutes)),
            ["technicianId"] = technician,
            ["branchId"] = branch,
            ["purpose"] = purpose,
            ["internalInstructions"] = instructions,
            ["notifyCustomer"] = notify,
        };
    }

    private static JsonObject With(JsonObject body, string key, JsonNode? value)
    {
        body[key] = value;

        return body;
    }

    private static string PlannerUrl(Guid id, DateTimeOffset day, int hour, int minutes = 60, Guid? branch = null) =>
        Url(id, $"assessment/planner?date={day:yyyy-MM-dd}&start={hour:00}:00&durationMinutes={minutes}")
        + (branch is null ? string.Empty : $"&branchId={branch}");

    private static string CalendarUrl(Guid id, Guid technician, DateTimeOffset from, int days = 7) =>
        Url(id, $"assessment/calendar?technicianId={technician}&from={from:yyyy-MM-dd}&to={from.AddDays(days - 1):yyyy-MM-dd}");

    private static DateTimeOffset Time(JsonNode? node) =>
        DateTimeOffset.Parse(node!.GetValue<string>(), CultureInfo.InvariantCulture);

    // AC-09, AC-10, AC-13, AC-18, AC-20, AC-22, FR-10: validation per field, warning accepted, purpose and
    // instructions stored and returned but never audited, reschedule keeps the row, cancel returns to review.
    [Fact]
    public async Task Assessments_ValidateAndPersistScheduleRescheduleAndCancel()
    {
        var world = await database.SeedWorldAsync();
        var techA = await database.SeedTechAsync(world.Org, world.BranchA, "Tina", "Tech");
        var techC = await database.SeedTechAsync(world.Org, world.BranchA, "Sam", "Smith");
        var techB = await database.SeedTechAsync(world.Org, world.BranchB, "Bob", "Builder");
        var inactive = await database.SeedTechAsync(world.Org, world.BranchA, "Ida", "Idle", "inactive");
        var day = Monday();

        await using var host = RequestsHost.Create(database);
        var (owner, ownerMember) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var request = await database.SeedRequestAsync(world, status: "needs_review");

        var invalid = new (JsonObject Body, string Key, string? Message)[]
        {
            (Slot(day, 10, branch: world.BranchA), "technicianId", "Choose a technician."),
            (Slot(day.AddDays(-10), 10, technician: techA, branch: world.BranchA), "start", null),
            (With(Slot(day, 10, technician: techA, branch: world.BranchA), "start", ServiceRequestSeed.Local(day.AddHours(10).AddMinutes(30))), "start", "Choose an arrival window."),
            (Slot(day, 10, 45, techA, world.BranchA), "end", "Choose an estimated duration."),
            (Slot(day, 22, 240, techA, world.BranchA), "end", null),
            (Slot(day, 10, technician: techA, branch: world.BranchA, purpose: "  "), "purpose", "Enter the purpose of the assessment."),
            (Slot(day, 10, technician: techA, branch: world.BranchA, purpose: new string('p', 501)), "purpose", "Purpose must be 500 characters or fewer."),
            (Slot(day, 10, technician: techA, branch: world.BranchA, instructions: new string('i', 2001)), "internalInstructions", "Internal instructions must be 2000 characters or fewer."),
            (Slot(day, 10, technician: techA, branch: world.BranchA, notify: null), "notifyCustomer", null),
            (Slot(day, 10, technician: techA), "branchId", null),
            (Slot(day, 10, technician: techB, branch: world.BranchA), "technicianId", null),
            (Slot(day, 10, technician: inactive, branch: world.BranchA), "technicianId", null),
            (Slot(day, 10, technician: Guid.NewGuid(), branch: world.BranchA), "technicianId", null),
        };

        foreach (var (body, key, message) in invalid)
        {
            var response = await host.SendAsync(HttpMethod.Post, Url(request.Id, "assessment"), owner, body);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var error = Error(await RequestsHost.ReadAsync(response), key);

            if (message is not null)
            {
                Assert.Equal(message, error);
            }
        }

        Assert.Equal("needs_review", await database.StatusOfAsync(request.Id));
        Assert.Equal(0, await database.CountAsync("assessments", request.Id));
        Assert.Equal(0, await database.CountAsync("audit_logs", request.Id));
        Assert.False(await database.ScalarAsync<bool>("SELECT branch_id IS NOT NULL FROM service_requests WHERE id = @r", ("r", request.Id)));

        // Valid schedule: the technician has no availability, which is a warning and never blocks (AC-10).
        var scheduled = await host.SendAsync(
            HttpMethod.Post,
            Url(request.Id, "assessment"),
            owner,
            Slot(day, 10, 90, techA, world.BranchA, instructions: "  Gate code 4921  "));
        Assert.Equal(HttpStatusCode.OK, scheduled.StatusCode);
        var detail = await RequestsHost.ReadAsync(scheduled);
        Assert.Equal("assessment_scheduled", detail["status"]!.GetValue<string>());
        Assert.Equal("Alpha Branch", detail["branch"]!["name"]!.GetValue<string>());
        Assert.Equal("Tina Tech", detail["assessment"]!["technician"]!["name"]!.GetValue<string>());
        Assert.Equal(Purpose, detail["assessment"]!["purpose"]!.GetValue<string>());
        Assert.Equal("Gate code 4921", detail["assessment"]!["internalInstructions"]!.GetValue<string>());
        Assert.Equal(day.AddHours(11.5), Time(detail["assessment"]!["end"]));
        var assessmentId = detail["assessment"]!["id"]!.GetValue<string>();
        Assert.Equal(1, await database.CountAsync("assessments", request.Id));
        Assert.Equal(Purpose, await database.ScalarAsync<string>("SELECT purpose FROM assessments WHERE id = @a", ("a", Guid.Parse(assessmentId))));
        Assert.Equal("Gate code 4921", await database.ScalarAsync<string>("SELECT internal_notes FROM assessments WHERE id = @a", ("a", Guid.Parse(assessmentId))));
        Assert.Equal(3, await database.CountAsync("request_status_history", request.Id));
        Assert.Equal(1, await database.AuditCountAsync(request.Id, "service_request.assessment_scheduled"));
        var (before, after, metadata) = await database.GetLatestAuditAsync(world.Org, "service_request.assessment_scheduled");
        Assert.Equal("needs_review", JsonNode.Parse(before!)!["status"]!.GetValue<string>());
        Assert.Equal("assessment_scheduled", JsonNode.Parse(after!)!["status"]!.GetValue<string>());
        Assert.Equal(techA.ToString(), JsonNode.Parse(metadata!)!["technicianId"]!.GetValue<string>());
        Assert.Equal("none", JsonNode.Parse(metadata!)!["notified"]!.GetValue<string>());

        // BR-12, BR-16: purpose, instructions, names and addresses never reach the audit rows.
        var auditText = await database.AuditTextAsync(request.Id);
        Assert.DoesNotContain("leaking", auditText);
        Assert.DoesNotContain("Gate code", auditText);
        Assert.DoesNotContain("Tina", auditText);
        Assert.DoesNotContain("carla", auditText);

        var again = await host.SendAsync(HttpMethod.Post, Url(request.Id, "assessment"), owner, Slot(day, 14, technician: techA, branch: world.BranchA));
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("request_changed", Code(await RequestsHost.ReadAsync(again)));

        var card = (await RequestsHost.ReadAsync(await host.SendAsync(HttpMethod.Get, "/service-requests/pipeline", owner)))["columns"]![2]!["items"]!.AsArray()
            .Single(item => item!["id"]!.GetValue<string>() == request.Id.ToString())!;
        Assert.Equal("assessment", card["dateKind"]!.GetValue<string>());
        Assert.Equal("TT", card["avatar"]!["initials"]!.GetValue<string>());

        // Reschedule: same row, new technician, purpose and cleared instructions; one audit row and no new history.
        var historyBefore = await database.CountAsync("request_status_history", request.Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(HttpMethod.Put, Url(request.Id, "assessment"), owner, Slot(day, 14))).StatusCode);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await host.SendAsync(HttpMethod.Put, Url(request.Id, "assessment"), owner, Slot(day, 14, technician: techC, notify: null))).StatusCode);
        var rescheduled = await host.SendAsync(
            HttpMethod.Put, Url(request.Id, "assessment"), owner, Slot(day, 14, 120, techC, purpose: "Re-check the valve"));
        Assert.Equal(HttpStatusCode.OK, rescheduled.StatusCode);
        var rescheduledBody = await RequestsHost.ReadAsync(rescheduled);
        Assert.Equal(assessmentId, rescheduledBody["assessment"]!["id"]!.GetValue<string>());
        Assert.Equal("Sam Smith", rescheduledBody["assessment"]!["technician"]!["name"]!.GetValue<string>());
        Assert.Equal("Re-check the valve", rescheduledBody["assessment"]!["purpose"]!.GetValue<string>());
        Assert.Null(rescheduledBody["assessment"]!["internalInstructions"]);
        Assert.Equal("assessment_scheduled", rescheduledBody["status"]!.GetValue<string>());
        Assert.Equal(1, await database.CountAsync("assessments", request.Id));
        Assert.Equal(1, await database.AuditCountAsync(request.Id, "service_request.assessment_rescheduled"));
        Assert.Equal(historyBefore, await database.CountAsync("request_status_history", request.Id));
        Assert.DoesNotContain("valve", await database.AuditTextAsync(request.Id));

        // Cancel without a body: back to needs_review, no email; a second cancel is a 409.
        var cancelled = await host.SendAsync(HttpMethod.Post, Url(request.Id, "assessment/cancel"), owner);
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        Assert.Equal("needs_review", (await RequestsHost.ReadAsync(cancelled))["status"]!.GetValue<string>());
        Assert.Equal("cancelled", await database.ScalarAsync<string>("SELECT status::text FROM assessments WHERE id = @a", ("a", Guid.Parse(assessmentId))));
        Assert.Equal(1, await database.AuditCountAsync(request.Id, "service_request.assessment_cancelled"));
        Assert.Equal(HttpStatusCode.Conflict, (await host.SendAsync(HttpMethod.Post, Url(request.Id, "assessment/cancel"), owner)).StatusCode);
        Assert.Empty(host.Sender.Messages);
        Assert.Equal(ownerMember.UserId, await database.ScalarAsync<Guid>("SELECT created_by_user_id FROM assessments WHERE id = @a", ("a", Guid.Parse(assessmentId))));
    }

    // AC-11, AC-12: time off, assessment and visit conflicts are 409 with code and title; completed visits and adjacent
    // slots are fine; the rescheduled assessment ignores itself; concurrent bookings leave exactly one success.
    [Fact]
    public async Task Assessments_RejectBlockingConflictsAndSerializeConcurrentBookings()
    {
        var world = await database.SeedWorldAsync();
        var tech = await database.SeedTechAsync(world.Org, world.BranchA, "Tina", "Tech");
        var offTech = await database.SeedTechAsync(world.Org, world.BranchA, "Olga", "Off");
        var day = Monday();

        await using var host = RequestsHost.Create(database);
        var (owner, ownerMember) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);

        var busy = await database.SeedRequestAsync(world, status: "assessment_scheduled", branch: world.BranchA);
        await database.SeedAssessmentAsync(world.Org, busy.Id, day.AddHours(9.5), day.AddHours(10.5), ownerMember.UserId, tech);
        await database.SeedAssignmentAsync(world.Org, ownerMember.UserId, world.BranchA, tech, "scheduled", day.AddDays(1).AddHours(14), day.AddDays(1).AddHours(15));
        await database.SeedAssignmentAsync(world.Org, ownerMember.UserId, world.BranchA, tech, "completed", day.AddDays(1).AddHours(16), day.AddDays(1).AddHours(17));
        await database.SeedExceptionAsync(offTech, day.AddDays(2).AddHours(8), day.AddDays(2).AddHours(18));
        var request = await database.SeedRequestAsync(world, status: "needs_review", branch: world.BranchA);

        async Task<JsonNode> Conflict(HttpMethod method, JsonObject body, string code, string title)
        {
            var response = await host.SendAsync(method, Url(request.Id, "assessment"), owner, body);
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            var problem = await RequestsHost.ReadAsync(response);
            Assert.Equal(code, Code(problem));
            Assert.Equal(title, problem["title"]!.GetValue<string>());

            return problem;
        }

        const string Busy = "This technician already has a commitment at that time.";
        await Conflict(HttpMethod.Post, Slot(day, 9, 60, tech), "technician_conflict", Busy);
        await Conflict(HttpMethod.Post, Slot(day.AddDays(1), 14, 30, tech), "technician_conflict", Busy);
        await Conflict(HttpMethod.Post, Slot(day.AddDays(2), 10, 60, offTech), "technician_time_off", "This technician is off at that time.");
        Assert.Equal("needs_review", await database.StatusOfAsync(request.Id));
        Assert.Equal(0, await database.CountAsync("assessments", request.Id));
        Assert.Equal(0, await database.CountAsync("audit_logs", request.Id));

        // Adjacent to the busy assessment and over a completed visit: accepted.
        var scheduled = await host.SendAsync(HttpMethod.Post, Url(request.Id, "assessment"), owner, Slot(day.AddDays(1), 16, 60, tech));
        Assert.Equal(HttpStatusCode.OK, scheduled.StatusCode);

        // Reschedule: its own slot is excluded, other commitments still block.
        Assert.Equal(
            HttpStatusCode.OK,
            (await host.SendAsync(HttpMethod.Put, Url(request.Id, "assessment"), owner, Slot(day.AddDays(1), 16, 90, tech))).StatusCode);
        await Conflict(HttpMethod.Put, Slot(day, 10, 60, tech), "technician_conflict", Busy);
        await Conflict(HttpMethod.Put, Slot(day.AddDays(2), 9, 60, offTech), "technician_time_off", "This technician is off at that time.");
        Assert.Equal(1, await database.AuditCountAsync(request.Id, "service_request.assessment_rescheduled"));

        // Concurrent schedules of one technician and slot (two requests): exactly one wins.
        var first = await database.SeedRequestAsync(world, status: "needs_review", branch: world.BranchA);
        var second = await database.SeedRequestAsync(world, status: "needs_review", branch: world.BranchA);
        var target = day.AddDays(3);
        var results = await Task.WhenAll(
            host.SendAsync(HttpMethod.Post, Url(first.Id, "assessment"), owner, Slot(target, 14, 60, tech)),
            host.SendAsync(HttpMethod.Post, Url(second.Id, "assessment"), owner, Slot(target, 14, 60, tech)));
        Assert.Equal(
            [HttpStatusCode.OK, HttpStatusCode.Conflict],
            results.Select(result => result.StatusCode).Order().ToArray());
        var loser = results.Single(result => result.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal("technician_conflict", Code(await RequestsHost.ReadAsync(loser)));
        Assert.Equal(
            1,
            await database.ScalarAsync<long>(
                "SELECT COUNT(*) FROM assessments WHERE technician_id = @t AND status = 'scheduled' AND scheduled_start = @s",
                ("t", tech),
                ("s", target.AddHours(14))));
        Assert.Equal(1, await database.CountAsync("assessments", first.Id) + await database.CountAsync("assessments", second.Id));
    }

    // AC-15, AC-16, AC-17, AC-18, AC-22: one email after commit per operation, opt-out and missing-email rules,
    // failure tolerance and an audit that records the intent without content.
    [Fact]
    public async Task Assessments_SendCustomerEmailAfterCommitWithoutContentInAuditOrLogs()
    {
        var world = await database.SeedWorldAsync();
        var orgName = await database.ScalarAsync<string>("SELECT name FROM organizations WHERE id = @o", ("o", world.Org));
        var tech = await database.SeedTechAsync(world.Org, world.BranchA, "Tina", "Tech");
        var day = Monday();
        var dayText = day.ToString("ddd, MMM d", CultureInfo.InvariantCulture);
        var nextText = day.AddDays(1).ToString("ddd, MMM d", CultureInfo.InvariantCulture);

        await using var host = RequestsHost.Create(database);
        var (owner, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var request = await database.SeedRequestAsync(world, status: "needs_review", branch: world.BranchA);
        var notes = "Customer is <b>VIP</b>";

        // Schedule with Email on.
        var scheduled = await host.SendAsync(
            HttpMethod.Post, Url(request.Id, "assessment"), owner, Slot(day, 9, 60, tech, instructions: notes, notify: true));
        Assert.Equal(HttpStatusCode.OK, scheduled.StatusCode);
        var email = Assert.Single(host.Sender.Messages);
        Assert.Equal("carla@example.com", email.To);
        Assert.Equal($"{orgName}: assessment visit for REQ-{request.Number}", email.Subject);
        Assert.Contains("Hi Pat,", email.TextBody);
        Assert.Contains(
            $"Your assessment visit is scheduled for {dayText} between 9:00 AM and 10:00 AM. Tina Tech will inspect the issue before we prepare your quote.",
            email.TextBody);
        Assert.Contains("Questions? Call us at +1 555 010 0100.", email.TextBody);
        Assert.DoesNotContain("leaking", email.TextBody + email.HtmlBody);
        Assert.DoesNotContain("VIP", email.TextBody + email.HtmlBody);
        var (_, _, metadata) = await database.GetLatestAuditAsync(world.Org, "service_request.assessment_scheduled");
        Assert.Equal("email", JsonNode.Parse(metadata!)!["notified"]!.GetValue<string>());
        Assert.Contains(
            (await RequestsHost.ReadAsync(scheduled))["activity"]!.AsArray(),
            entry => entry!["kind"]!.GetValue<string>() == "assessment_scheduled"
                && entry["detail"]!.GetValue<string>().EndsWith(" · Customer notified by email", StringComparison.Ordinal));

        // Reschedule with Email on, then off: one more email only.
        Assert.Equal(
            HttpStatusCode.OK,
            (await host.SendAsync(HttpMethod.Put, Url(request.Id, "assessment"), owner, Slot(day.AddDays(1), 13, 60, tech, notify: true))).StatusCode);
        Assert.Equal(2, host.Sender.Messages.Count);
        Assert.Equal($"{orgName}: assessment visit rescheduled for REQ-{request.Number}", host.Sender.Messages[1].Subject);
        Assert.Contains(
            $"Your assessment visit has been rescheduled to {nextText} between 1:00 PM and 2:00 PM. Tina Tech will inspect",
            host.Sender.Messages[1].TextBody);
        Assert.Equal(
            HttpStatusCode.OK,
            (await host.SendAsync(HttpMethod.Put, Url(request.Id, "assessment"), owner, Slot(day.AddDays(1), 13, 60, tech, notify: false))).StatusCode);
        Assert.Equal(2, host.Sender.Messages.Count);
        var (_, _, offMetadata) = await database.GetLatestAuditAsync(world.Org, "service_request.assessment_rescheduled");
        Assert.Equal("none", JsonNode.Parse(offMetadata!)!["notified"]!.GetValue<string>());

        // Cancel with Email on (JSON body) sends the cancellation email for the current slot.
        var cancelled = await host.SendAsync(
            HttpMethod.Post, Url(request.Id, "assessment/cancel"), owner, new JsonObject { ["notifyCustomer"] = true });
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        Assert.Equal(3, host.Sender.Messages.Count);
        Assert.Equal($"{orgName}: assessment visit cancelled for REQ-{request.Number}", host.Sender.Messages[2].Subject);
        Assert.Contains(
            $"Your assessment visit on {nextText} between 1:00 PM and 2:00 PM has been cancelled.",
            host.Sender.Messages[2].TextBody);
        Assert.Equal(
            "email",
            JsonNode.Parse((await database.GetLatestAuditAsync(world.Org, "service_request.assessment_cancelled")).Item3!)!["notified"]!.GetValue<string>());
        Assert.Equal(0, await database.CountAsync("request_messages", request.Id));
        Assert.Equal(0, await database.ScalarAsync<long>("SELECT COUNT(*) FROM notifications WHERE organization_id = @o", ("o", world.Org)));

        // No recipient: notifyCustomer true is a 400 on every operation; false works.
        var guestOnly = await database.SeedRequestAsync(world, status: "needs_review", branch: world.BranchA, linkCustomer: false, guestEmail: null);
        var noEmail = await host.SendAsync(
            HttpMethod.Post, Url(guestOnly.Id, "assessment"), owner, Slot(day, 9, 60, tech, notify: true));
        Assert.Equal(HttpStatusCode.BadRequest, noEmail.StatusCode);
        Assert.Equal("This customer has no email address.", Error(await RequestsHost.ReadAsync(noEmail), "notifyCustomer"));
        Assert.Equal(0, await database.CountAsync("assessments", guestOnly.Id));
        Assert.Equal(
            HttpStatusCode.OK,
            (await host.SendAsync(HttpMethod.Post, Url(guestOnly.Id, "assessment"), owner, Slot(day, 9, 60, tech, notify: false))).StatusCode);
        var cancelNoEmail = await host.SendAsync(
            HttpMethod.Post, Url(guestOnly.Id, "assessment/cancel"), owner, new JsonObject { ["notifyCustomer"] = true });
        Assert.Equal(HttpStatusCode.BadRequest, cancelNoEmail.StatusCode);
        Assert.Equal("assessment_scheduled", await database.StatusOfAsync(guestOnly.Id));
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(HttpMethod.Post, Url(guestOnly.Id, "assessment/cancel"), owner)).StatusCode);
        Assert.Equal(3, host.Sender.Messages.Count);

        // A failing sender never rolls the schedule or the cancel back and never logs the recipient or purpose.
        var failing = await database.SeedRequestAsync(world, status: "needs_review", branch: world.BranchA);
        host.Sender.Fail = true;
        var failedSchedule = await host.SendAsync(
            HttpMethod.Post, Url(failing.Id, "assessment"), owner, Slot(day.AddDays(2), 9, 60, tech, purpose: "Secret purpose text", notify: true));
        var failedCancel = await host.SendAsync(
            HttpMethod.Post, Url(failing.Id, "assessment/cancel"), owner, new JsonObject { ["notifyCustomer"] = true });
        host.Sender.Fail = false;
        Assert.Equal(HttpStatusCode.OK, failedSchedule.StatusCode);
        Assert.Equal(HttpStatusCode.OK, failedCancel.StatusCode);
        Assert.Equal("needs_review", await database.StatusOfAsync(failing.Id));
        Assert.Equal(1, await database.AuditCountAsync(failing.Id, "service_request.assessment_scheduled"));
        Assert.Equal(1, await database.AuditCountAsync(failing.Id, "service_request.assessment_cancelled"));
        var logs = new CapturingLogs(host.Logs).AllText();
        Assert.Contains(failing.Id.ToString(), logs);
        Assert.DoesNotContain("carla@example.com", logs);
        Assert.DoesNotContain("Secret purpose", logs);
    }

    // AC-05, AC-06, AC-07, AC-08, FR-03, FR-04: eligible technicians, branch handling, slot states and order, workload with
    // the rescheduled assessment excluded, calendar contents and its 400s without leaking data.
    [Fact]
    public async Task PlannerAndCalendar_ReturnEligibleTechniciansStatesWorkloadAndCommitments()
    {
        var world = await database.SeedWorldAsync();
        var foreign = await database.SeedWorldAsync();
        var plumbing = await database.SeedSkillAsync(world.Org, "Plumbing");
        var day = Monday();

        await using var host = RequestsHost.Create(database);
        var (owner, ownerMember) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);

        async Task<Guid> Tech(string first, string status = "active", Guid? branch = null, string from = "09:00", string to = "17:00", bool withBreak = false)
        {
            var id = await database.SeedTechAsync(world.Org, branch ?? world.BranchA, first, "Tech", status);
            var slot = await database.SeedSlotAsync(id, 1, from, to);

            if (withBreak)
            {
                await database.SeedBreakAsync(slot, "12:00", "13:00");
            }

            return id;
        }

        var ava = await Tech("Ava", withBreak: true);
        await database.GiveSkillAsync(ava, plumbing, primary: true);
        var ben = await Tech("Ben");
        var cal = await Tech("Cal");
        var dee = await Tech("Dee");
        var eli = await Tech("Eli", from: "13:00");
        var fay = await database.SeedTechAsync(world.Org, world.BranchA, "Fay", "Tech");
        await Tech("Gus", status: "inactive");
        var hank = await Tech("Hank", branch: world.BranchB);
        var foreignTech = await database.SeedTechAsync(foreign.Org, foreign.BranchA, "Zed", "Foreign");

        var current = await database.SeedRequestAsync(world, status: "assessment_scheduled", branch: world.BranchA);
        await database.SeedAssessmentAsync(world.Org, current.Id, day.AddHours(14), day.AddHours(16), ownerMember.UserId, ava);
        var other = await database.SeedRequestAsync(world, status: "assessment_scheduled", branch: world.BranchA);
        await database.SeedAssessmentAsync(world.Org, other.Id, day.AddHours(10.5), day.AddHours(11.5), ownerMember.UserId, ben);
        await database.SeedAssignmentAsync(world.Org, ownerMember.UserId, world.BranchA, cal, "scheduled", day.AddHours(9.5), day.AddHours(10.5));
        await database.SeedExceptionAsync(dee, day.AddHours(9), day.AddHours(12));
        var request = await database.SeedRequestAsync(world, status: "needs_review", branch: world.BranchA);

        // Slot states and ordering (BR-04, BR-05).
        var planner = await host.SendAsync(HttpMethod.Get, PlannerUrl(request.Id, day, 10), owner);
        Assert.Equal(HttpStatusCode.OK, planner.StatusCode);
        Assert.True(planner.Headers.CacheControl!.NoStore);
        var body = await RequestsHost.ReadAsync(planner);
        Assert.Equal("UTC", body["timezone"]!.GetValue<string>());
        Assert.Equal(world.BranchA, body["branchId"]!.GetValue<Guid>());
        var items = body["technicians"]!.AsArray();
        Assert.Equal(
            ["Ava Tech", "Eli Tech", "Fay Tech", "Ben Tech", "Cal Tech", "Dee Tech"],
            items.Select(item => item!["name"]!.GetValue<string>()).ToArray());
        JsonNode Card(string name) => items.Single(item => item!["name"]!.GetValue<string>() == name)!;
        Assert.Equal("Plumbing", Card("Ava Tech")["primarySkill"]!.GetValue<string>());
        Assert.Null(Card("Ben Tech")["primarySkill"]);
        Assert.Equal("AT", Card("Ava Tech")["initials"]!.GetValue<string>());
        Assert.Equal("available", Card("Ava Tech")["slot"]!["state"]!.GetValue<string>());
        Assert.False(Card("Ava Tech")["slot"]!["blocking"]!.GetValue<bool>());
        Assert.Equal("available_after", Card("Eli Tech")["slot"]!["state"]!.GetValue<string>());
        Assert.Equal(day.AddHours(13), Time(Card("Eli Tech")["slot"]!["availableAfter"]));
        Assert.Equal("outside_availability", Card("Fay Tech")["slot"]!["state"]!.GetValue<string>());
        Assert.Equal("conflict", Card("Ben Tech")["slot"]!["state"]!.GetValue<string>());
        Assert.Equal(day.AddHours(10.5), Time(Card("Ben Tech")["slot"]!["from"]));
        Assert.Equal(day.AddHours(11.5), Time(Card("Ben Tech")["slot"]!["to"]));
        Assert.True(Card("Ben Tech")["slot"]!["blocking"]!.GetValue<bool>());
        Assert.Equal("conflict", Card("Cal Tech")["slot"]!["state"]!.GetValue<string>());
        Assert.Equal("time_off", Card("Dee Tech")["slot"]!["state"]!.GetValue<string>());

        // Workload of the week: 120 of 420 minutes, a visit and an assessment of 60 of 480, none, and no availability.
        Assert.Equal(29, Card("Ava Tech")["workload"]!["percent"]!.GetValue<int>());
        Assert.Equal(13, Card("Cal Tech")["workload"]!["percent"]!.GetValue<int>());
        Assert.Equal(13, Card("Ben Tech")["workload"]!["percent"]!.GetValue<int>());
        Assert.Equal("percent", Card("Dee Tech")["workload"]!["state"]!.GetValue<string>());
        Assert.Equal("none", Card("Fay Tech")["workload"]!["state"]!.GetValue<string>());
        Assert.Null(Card("Fay Tech")["workload"]!["percent"]);

        // Reschedule mode: the current assessment is excluded from state and workload; for another request it blocks.
        var reschedule = await RequestsHost.ReadAsync(await host.SendAsync(HttpMethod.Get, PlannerUrl(current.Id, day, 14), owner));
        var ownCard = reschedule["technicians"]!.AsArray().Single(item => item!["name"]!.GetValue<string>() == "Ava Tech")!;
        Assert.Equal("available", ownCard["slot"]!["state"]!.GetValue<string>());
        Assert.Equal(0, ownCard["workload"]!["percent"]!.GetValue<int>());
        var blocked = await RequestsHost.ReadAsync(await host.SendAsync(HttpMethod.Get, PlannerUrl(request.Id, day, 14), owner));
        Assert.Equal(
            "conflict",
            blocked["technicians"]!.AsArray().Single(item => item!["name"]!.GetValue<string>() == "Ava Tech")!["slot"]!["state"]!.GetValue<string>());

        // Branch handling (BR-04), status and query validation.
        var branchless = await database.SeedRequestAsync(world, status: "needs_review");
        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(HttpMethod.Get, PlannerUrl(branchless.Id, day, 10), owner)).StatusCode);
        var viaBranch = await RequestsHost.ReadAsync(await host.SendAsync(HttpMethod.Get, PlannerUrl(branchless.Id, day, 10, branch: world.BranchB), owner));
        Assert.Equal(["Hank Tech"], viaBranch["technicians"]!.AsArray().Select(item => item!["name"]!.GetValue<string>()).ToArray());
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await host.SendAsync(HttpMethod.Get, PlannerUrl(branchless.Id, day, 10, branch: foreign.BranchA), owner)).StatusCode);
        var branched = await host.SendAsync(HttpMethod.Get, PlannerUrl(request.Id, day, 10, branch: world.BranchA), owner);
        Assert.Equal(HttpStatusCode.BadRequest, branched.StatusCode);
        Assert.Equal("This request already has a branch.", Error(await RequestsHost.ReadAsync(branched), "branchId"));
        var closed = await database.SeedRequestAsync(world, status: "ready_for_quote", branch: world.BranchA);
        var notSchedulable = await host.SendAsync(HttpMethod.Get, PlannerUrl(closed.Id, day, 10), owner);
        Assert.Equal(HttpStatusCode.Conflict, notSchedulable.StatusCode);
        Assert.Equal("request_changed", Code(await RequestsHost.ReadAsync(notSchedulable)));

        foreach (var (query, key) in new[]
        {
            ("date=tomorrow&start=10:00&durationMinutes=60", "date"),
            ($"date={day:yyyy-MM-dd}&start=10:30&durationMinutes=60", "start"),
            ($"date={day:yyyy-MM-dd}&start=10:00&durationMinutes=45", "durationMinutes"),
        })
        {
            var response = await host.SendAsync(HttpMethod.Get, Url(request.Id, $"assessment/planner?{query}"), owner);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.NotNull(Error(await RequestsHost.ReadAsync(response), key));
        }

        // Calendar (BR-07): availability minus breaks, time off, labelled commitments and the current assessment.
        var calendarResponse = await host.SendAsync(HttpMethod.Get, CalendarUrl(current.Id, ava, day), owner);
        Assert.Equal(HttpStatusCode.OK, calendarResponse.StatusCode);
        Assert.True(calendarResponse.Headers.CacheControl!.NoStore);
        var calendar = await RequestsHost.ReadAsync(calendarResponse);
        Assert.Equal(7, calendar["days"]!.AsArray().Count);
        var monday = calendar["days"]![0]!;
        Assert.Equal(day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), monday["date"]!.GetValue<string>());
        Assert.Equal(2, monday["availability"]!.AsArray().Count);
        Assert.Equal(day.AddHours(9), Time(monday["availability"]![0]!["start"]));
        Assert.Equal(day.AddHours(12), Time(monday["availability"]![0]!["end"]));
        Assert.Equal(day.AddHours(12), Time(monday["breaks"]![0]!["start"]));
        Assert.Empty(calendar["days"]![1]!["availability"]!.AsArray());
        var currentEvent = Assert.Single(calendar["events"]!.AsArray());
        Assert.Equal("assessment", currentEvent!["kind"]!.GetValue<string>());
        Assert.Equal("Drain cleaning", currentEvent["label"]!.GetValue<string>());
        Assert.True(currentEvent["isCurrent"]!.GetValue<bool>());
        var forNew = await RequestsHost.ReadAsync(await host.SendAsync(HttpMethod.Get, CalendarUrl(request.Id, ava, day), owner));
        Assert.False(forNew["events"]![0]!["isCurrent"]!.GetValue<bool>());
        var visitCalendar = await RequestsHost.ReadAsync(await host.SendAsync(HttpMethod.Get, CalendarUrl(request.Id, cal, day, 1), owner));
        Assert.Equal("Visit", visitCalendar["events"]![0]!["label"]!.GetValue<string>());
        Assert.Equal("visit", visitCalendar["events"]![0]!["kind"]!.GetValue<string>());
        var off = await RequestsHost.ReadAsync(await host.SendAsync(HttpMethod.Get, CalendarUrl(request.Id, dee, day, 1), owner));
        Assert.Equal(day.AddHours(9), Time(off["days"]![0]!["timeOff"]![0]!["start"]));
        var text = calendar.ToJsonString() + visitCalendar.ToJsonString();
        Assert.DoesNotContain("Carla", text);
        Assert.DoesNotContain("Seed St", text);

        // Range, foreign and other-branch technicians: 400 without any data.
        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(HttpMethod.Get, CalendarUrl(request.Id, ava, day, 8), owner)).StatusCode);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await host.SendAsync(HttpMethod.Get, Url(request.Id, $"assessment/calendar?technicianId={ava}&from={day.AddDays(2):yyyy-MM-dd}&to={day:yyyy-MM-dd}"), owner)).StatusCode);
        foreach (var technician in new[] { hank, foreignTech, Guid.NewGuid() })
        {
            var rejected = await host.SendAsync(HttpMethod.Get, CalendarUrl(request.Id, technician, day), owner);
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            var problem = await RequestsHost.ReadAsync(rejected);
            Assert.NotNull(Error(problem, "technicianId"));
            Assert.Null(problem["events"]);
            Assert.Null(problem["days"]);
        }
    }

    // AC-02, AC-03, FR-11: a request of another organization or outside the branch scope is a plain 404 on the planner and
    // the mutations; read roles, Technician and Accounting get 403.
    [Fact]
    public async Task PlanningEndpoints_DenyOtherOrganizationsOutOfScopeCallersAndNonManagers()
    {
        var world = await database.SeedWorldAsync();
        var foreign = await database.SeedWorldAsync();
        var tech = await database.SeedTechAsync(world.Org, world.BranchA, "Tina", "Tech");
        var day = Monday();

        await using var host = RequestsHost.Create(database);
        var (foreignOwner, _) = await host.SignInAsync(database, foreign.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var (limited, _) = await host.SignInAsync(
            database, world.Org, CompanySettingsDatabaseFixture.DispatcherRoleId, "Lim", "Ited", world.BranchB);
        var request = await database.SeedRequestAsync(world, status: "needs_review", branch: world.BranchA);

        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Get, PlannerUrl(request.Id, day, 10), foreignOwner)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Get, CalendarUrl(request.Id, tech, day), foreignOwner)).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await host.SendAsync(HttpMethod.Post, Url(request.Id, "assessment"), foreignOwner, Slot(day, 10, technician: tech, branch: world.BranchA))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Get, PlannerUrl(request.Id, day, 10), limited)).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await host.SendAsync(HttpMethod.Post, Url(request.Id, "assessment"), limited, Slot(day, 10, technician: tech))).StatusCode);
        Assert.Equal(0, await database.CountAsync("assessments", request.Id));

        foreach (var role in new[]
        {
            CompanySettingsDatabaseFixture.ViewerRoleId,
            CompanySettingsDatabaseFixture.OperationsManagerRoleId,
            CompanySettingsDatabaseFixture.AccountingRoleId,
            CompanySettingsDatabaseFixture.TechnicianRoleId,
        })
        {
            var (cookie, _) = await host.SignInAsync(database, world.Org, role);

            Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(HttpMethod.Get, PlannerUrl(request.Id, day, 10), cookie)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(HttpMethod.Get, CalendarUrl(request.Id, tech, day), cookie)).StatusCode);
            Assert.Equal(
                HttpStatusCode.Forbidden,
                (await host.SendAsync(HttpMethod.Post, Url(request.Id, "assessment"), cookie, Slot(day, 10, technician: tech, branch: world.BranchA))).StatusCode);
        }

        Assert.Equal(0, await database.CountAsync("assessments", request.Id));
        Assert.Equal("needs_review", await database.StatusOfAsync(request.Id));
    }
}
