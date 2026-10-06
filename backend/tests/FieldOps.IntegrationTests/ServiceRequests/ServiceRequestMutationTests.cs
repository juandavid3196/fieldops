using System.Net;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.Team;
using FieldOps.IntegrationTests.Users;

namespace FieldOps.IntegrationTests.ServiceRequests;

/// <summary>Transitions, assignee/priority/branch rules, notes, information requests and customer responses: AC-09 to AC-15, AC-18, AC-19, AC-23.</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class ServiceRequestMutationTests(CompanySettingsDatabaseFixture database)
{
    private const string ConflictTitle = "This request changed. Refresh to see the latest.";

    private static JsonObject Body(string key, object? value) => new() { [key] = JsonValue.Create(value) };

    private static string Error(JsonNode problem, string key) =>
        problem["errors"]![key]!.AsArray().Single()!.GetValue<string>();

    private static string Url(Guid id, string suffix) => $"/service-requests/{id}/{suffix}";

    // AC-09, AC-10, AC-18, AC-19, AC-23: every transition writes one history and one audit row; invalid ones are 409 and
    // change nothing; concurrent conflicting transitions leave exactly one success.
    [Fact]
    public async Task Transitions_WriteHistoryAndAuditAndRejectInvalidAndConcurrentChanges()
    {
        var world = await database.SeedWorldAsync();
        await using var host = RequestsHost.Create(database);
        var (cookie, member) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.DispatcherRoleId, "Dina", "Dispatcher");

        var request = await database.SeedRequestAsync(world);
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(HttpMethod.Post, Url(request.Id, "start-review"), cookie)).StatusCode);
        Assert.Equal("needs_review", await database.StatusOfAsync(request.Id));
        Assert.Equal(2, await database.CountAsync("request_status_history", request.Id));
        Assert.Equal(1, await database.AuditCountAsync(request.Id, "service_request.status_changed"));
        var changedBy = await database.ScalarAsync<Guid>(
            "SELECT changed_by_user_id FROM request_status_history WHERE request_id = @r AND to_status = 'needs_review'", ("r", request.Id));
        Assert.Equal(member.UserId, changedBy);

        // Not allowed from the current status: 409 with the contract title and no extra rows.
        foreach (var suffix in new[] { "start-review", "move-to-review", "assessment/cancel" })
        {
            var conflict = await host.SendAsync(HttpMethod.Post, Url(request.Id, suffix), cookie);
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
            Assert.Equal(ConflictTitle, (await RequestsHost.ReadAsync(conflict))["title"]!.GetValue<string>());
        }

        var completeConflict = await host.CompleteAssessmentAsync(request.Id, cookie);
        Assert.Equal(HttpStatusCode.Conflict, completeConflict.StatusCode);
        Assert.Equal(ConflictTitle, (await RequestsHost.ReadAsync(completeConflict))["title"]!.GetValue<string>());

        Assert.Equal(2, await database.CountAsync("request_status_history", request.Id));
        Assert.Equal(1, await database.CountAsync("audit_logs", request.Id));

        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(HttpMethod.Post, Url(request.Id, "ready-for-quote"), cookie)).StatusCode);
        Assert.Equal(1, await database.AuditCountAsync(request.Id, "service_request.ready_for_quote"));
        Assert.Equal(0, await database.CountAsync("assessments", request.Id));
        var moved = await host.SendAsync(HttpMethod.Post, Url(request.Id, "move-to-review"), cookie);
        Assert.Equal("needs_review", (await RequestsHost.ReadAsync(moved))["status"]!.GetValue<string>());

        var closed = await database.SeedRequestAsync(world, status: "cancelled");
        Assert.Equal(HttpStatusCode.Conflict, (await host.SendAsync(HttpMethod.Post, Url(closed.Id, "ready-for-quote"), cookie)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await host.SendAsync(HttpMethod.Post, Url(closed.Id, "notes"), cookie, Body("body", "x"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await host.SendAsync(HttpMethod.Put, Url(closed.Id, "priority"), cookie, Body("urgency", "urgent"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(HttpMethod.Get, $"/service-requests/{closed.Id}", cookie)).StatusCode);

        // Complete assessment: not started is 409 with its own title; started completes in one transaction.
        var future = await database.SeedRequestAsync(world, status: "assessment_scheduled", branch: world.BranchA);
        var futureAssessment = await database.SeedAssessmentAsync(
            world.Org, future.Id, DateTimeOffset.UtcNow.AddHours(5), DateTimeOffset.UtcNow.AddHours(6), member.UserId);
        var notStarted = await host.CompleteAssessmentAsync(future.Id, cookie);
        Assert.Equal(HttpStatusCode.Conflict, notStarted.StatusCode);
        Assert.Equal("This assessment hasn't started yet.", (await RequestsHost.ReadAsync(notStarted))["title"]!.GetValue<string>());
        Assert.Equal("assessment_scheduled", await database.StatusOfAsync(future.Id));
        Assert.Equal("scheduled", await database.ScalarAsync<string>("SELECT status::text FROM assessments WHERE id = @a", ("a", futureAssessment)));
        Assert.Equal(HttpStatusCode.Conflict, (await host.SendAsync(HttpMethod.Post, Url(future.Id, "ready-for-quote"), cookie)).StatusCode);

        var started = await database.SeedRequestAsync(world, status: "assessment_scheduled", branch: world.BranchA);
        var startedAssessment = await database.SeedAssessmentAsync(
            world.Org, started.Id, DateTimeOffset.UtcNow.AddHours(-2), DateTimeOffset.UtcNow.AddHours(-1), member.UserId);
        Assert.Equal(HttpStatusCode.OK, (await host.CompleteAssessmentAsync(started.Id, cookie)).StatusCode);
        Assert.Equal("ready_for_quote", await database.StatusOfAsync(started.Id));
        Assert.Equal("completed", await database.ScalarAsync<string>("SELECT status::text FROM assessments WHERE id = @a", ("a", startedAssessment)));
        Assert.True(await database.ScalarAsync<bool>("SELECT completed_at IS NOT NULL FROM assessments WHERE id = @a", ("a", startedAssessment)));
        Assert.Equal(1, await database.AuditCountAsync(started.Id, "service_request.assessment_completed"));
        Assert.Equal(1, await database.ScalarAsync<long>(
            "SELECT COUNT(*) FROM request_status_history WHERE request_id = @r AND to_status = 'ready_for_quote'", ("r", started.Id)));

        // Cancel: reason validated, assessment cancelled with it, one history row with the reason and one audit row.
        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(HttpMethod.Post, Url(future.Id, "cancel"), cookie, Body("reason", "  "))).StatusCode);
        var tooLong = await host.SendAsync(HttpMethod.Post, Url(future.Id, "cancel"), cookie, Body("reason", new string('r', 501)));
        Assert.Equal("reason", ((JsonObject)(await RequestsHost.ReadAsync(tooLong))["errors"]!).Single().Key);
        var cancelled = await host.SendAsync(HttpMethod.Post, Url(future.Id, "cancel"), cookie, Body("reason", "Customer moved away"));
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        Assert.Equal("cancelled", await database.StatusOfAsync(future.Id));
        Assert.True(await database.ScalarAsync<bool>("SELECT cancelled_at IS NOT NULL FROM service_requests WHERE id = @r", ("r", future.Id)));
        Assert.Equal("cancelled", await database.ScalarAsync<string>("SELECT status::text FROM assessments WHERE id = @a", ("a", futureAssessment)));
        Assert.Equal("Customer moved away", await database.ScalarAsync<string>(
            "SELECT reason FROM request_status_history WHERE request_id = @r AND to_status = 'cancelled'", ("r", future.Id)));
        Assert.Equal(1, await database.AuditCountAsync(future.Id, "service_request.status_changed"));
        Assert.Equal(0, await database.AuditCountAsync(future.Id, "service_request.assessment_cancelled"));
        Assert.DoesNotContain("Customer moved", await database.AuditTextAsync(future.Id));
        var detail = await RequestsHost.ReadAsync(await host.SendAsync(HttpMethod.Get, $"/service-requests/{future.Id}", cookie));
        Assert.Contains(
            detail["activity"]!.AsArray(),
            entry => entry!["label"]!.GetValue<string>() == "Status changed to Cancelled" && entry["detail"]!.GetValue<string>() == "Customer moved away");

        // Concurrency: exactly one of two conflicting transitions succeeds and only one set of rows is written.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var review = await database.SeedRequestAsync(world, status: "needs_review", branch: world.BranchA);
            var tomorrow = DateTimeOffset.UtcNow.AddDays(1);
            var technician = await database.SeedTechAsync(world.Org, world.BranchA, "Tess", "Tech");
            var slot = new JsonObject
            {
                ["technicianId"] = technician,
                ["purpose"] = "Inspect the leak",
                ["notifyCustomer"] = false,
                ["start"] = ServiceRequestSeed.Local(new DateTimeOffset(tomorrow.Year, tomorrow.Month, tomorrow.Day, 10, 0, 0, TimeSpan.Zero)),
                ["end"] = ServiceRequestSeed.Local(new DateTimeOffset(tomorrow.Year, tomorrow.Month, tomorrow.Day, 11, 0, 0, TimeSpan.Zero)),
            };

            var results = await Task.WhenAll(
                host.SendAsync(HttpMethod.Post, Url(review.Id, "ready-for-quote"), cookie),
                host.SendAsync(HttpMethod.Post, Url(review.Id, "assessment"), cookie, slot));

            Assert.Equal(
                [HttpStatusCode.OK, HttpStatusCode.Conflict],
                results.Select(result => result.StatusCode).Order().ToArray());
            Assert.Equal(3, await database.CountAsync("request_status_history", review.Id));
            Assert.Equal(1, await database.CountAsync("audit_logs", review.Id));

            var fresh = await database.SeedRequestAsync(world);
            var twice = await Task.WhenAll(
                host.SendAsync(HttpMethod.Post, Url(fresh.Id, "start-review"), cookie),
                host.SendAsync(HttpMethod.Post, Url(fresh.Id, "start-review"), cookie));
            Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], twice.Select(result => result.StatusCode).Order().ToArray());
            Assert.Equal(2, await database.CountAsync("request_status_history", fresh.Id));
            Assert.Equal(1, await database.CountAsync("audit_logs", fresh.Id));
        }
    }

    // AC-11, AC-12: assignee eligibility (role, branch scope, tenant), no-op repeats, priority and branch rules.
    [Fact]
    public async Task AssigneePriorityAndBranch_EnforceEligibilityAndSkipNoOps()
    {
        var world = await database.SeedWorldAsync();
        var foreign = await database.SeedWorldAsync();
        var inactiveBranch = await database.SeedBranchAsync(world.Org, "Closed Branch", isActive: false);
        await using var host = RequestsHost.Create(database);
        var (owner, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId, "Ola", "Owner");
        var (_, opsManager) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OperationsManagerRoleId);
        var (limited, dispatcherA) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.DispatcherRoleId, "Dina", "Dispatcher", world.BranchA);
        var (_, dispatcherB) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.DispatcherRoleId, "Dora", "Dispatcher", world.BranchB);
        var (_, foreignOwner) = await host.SignInAsync(database, foreign.Org, CompanySettingsDatabaseFixture.OwnerRoleId);

        var request = await database.SeedRequestAsync(world, branch: world.BranchA);
        const string notAllowed = "Choose a team member who can manage this request.";

        foreach (var userId in new[] { opsManager.UserId, dispatcherB.UserId, foreignOwner.UserId, Guid.NewGuid() })
        {
            var response = await host.SendAsync(HttpMethod.Put, Url(request.Id, "assignee"), owner, Body("assigneeUserId", userId));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(notAllowed, Error(await RequestsHost.ReadAsync(response), "assigneeUserId"));
        }

        Assert.Equal("new", await database.StatusOfAsync(request.Id));
        Assert.Equal(0, await database.CountAsync("audit_logs", request.Id));

        var assigned = await host.SendAsync(HttpMethod.Put, Url(request.Id, "assignee"), owner, Body("assigneeUserId", dispatcherA.UserId));
        Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);
        var assignedBody = await RequestsHost.ReadAsync(assigned);
        Assert.Equal("needs_review", assignedBody["status"]!.GetValue<string>());
        Assert.Equal("Dina Dispatcher", assignedBody["assignee"]!["name"]!.GetValue<string>());
        Assert.Equal(1, await database.AuditCountAsync(request.Id, "service_request.assigned"));
        var (before, after, _) = await database.GetLatestAuditAsync(world.Org, "service_request.assigned");
        Assert.Equal("new", JsonNode.Parse(before!)!["status"]!.GetValue<string>());
        Assert.Equal("needs_review", JsonNode.Parse(after!)!["status"]!.GetValue<string>());
        Assert.Equal(dispatcherA.UserId.ToString(), JsonNode.Parse(after!)!["assigneeUserId"]!.GetValue<string>());
        Assert.Equal(2, await database.CountAsync("request_status_history", request.Id));

        // Repeating is a no-op: still 200, nothing written. Unassigning keeps the status.
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(HttpMethod.Put, Url(request.Id, "assignee"), owner, Body("assigneeUserId", dispatcherA.UserId))).StatusCode);
        Assert.Equal(1, await database.CountAsync("audit_logs", request.Id));
        Assert.Equal(2, await database.CountAsync("request_status_history", request.Id));
        var unassigned = await RequestsHost.ReadAsync(await host.SendAsync(HttpMethod.Put, Url(request.Id, "assignee"), owner, Body("assigneeUserId", null)));
        Assert.Null(unassigned["assignee"]);
        Assert.Equal("needs_review", unassigned["status"]!.GetValue<string>());
        Assert.Equal(2, await database.AuditCountAsync(request.Id, "service_request.assigned"));

        // Priority: persisted with audit and activity, same value is a no-op, invalid value is 400.
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(HttpMethod.Put, Url(request.Id, "priority"), limited, Body("urgency", "urgent"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(HttpMethod.Put, Url(request.Id, "priority"), limited, Body("urgency", "urgent"))).StatusCode);
        Assert.Equal(1, await database.AuditCountAsync(request.Id, "service_request.priority_changed"));
        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(HttpMethod.Put, Url(request.Id, "priority"), limited, Body("urgency", "high"))).StatusCode);

        // Branch: in-scope persists; out-of-scope, foreign, inactive and clearing are 400 on branchId; assignee scope is checked.
        var branchless = await database.SeedRequestAsync(world);
        const string noBranch = "Choose a branch you have access to.";

        foreach (var branchId in new Guid?[] { world.BranchB, foreign.BranchA, inactiveBranch.Id, Guid.NewGuid(), null })
        {
            var response = await host.SendAsync(HttpMethod.Put, Url(branchless.Id, "branch"), limited, Body("branchId", branchId));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(noBranch, Error(await RequestsHost.ReadAsync(response), "branchId"));
        }

        var set = await RequestsHost.ReadAsync(await host.SendAsync(HttpMethod.Put, Url(branchless.Id, "branch"), limited, Body("branchId", world.BranchA)));
        Assert.Equal("Alpha Branch", set["branch"]!["name"]!.GetValue<string>());
        Assert.Equal(1, await database.AuditCountAsync(branchless.Id, "service_request.branch_changed"));
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(HttpMethod.Put, Url(branchless.Id, "branch"), limited, Body("branchId", world.BranchA))).StatusCode);
        Assert.Equal(1, await database.AuditCountAsync(branchless.Id, "service_request.branch_changed"));

        var withAssignee = await database.SeedRequestAsync(world, status: "needs_review", branch: world.BranchA, assignee: dispatcherA.UserId);
        var lacks = await host.SendAsync(HttpMethod.Put, Url(withAssignee.Id, "branch"), owner, Body("branchId", world.BranchB));
        Assert.Equal(HttpStatusCode.BadRequest, lacks.StatusCode);
        Assert.Equal("The assignee doesn't have access to this branch.", Error(await RequestsHost.ReadAsync(lacks), "branchId"));
        Assert.Equal(world.BranchA, await database.ScalarAsync<Guid>("SELECT branch_id FROM service_requests WHERE id = @r", ("r", withAssignee.Id)));

        var activity = (await RequestsHost.ReadAsync(await host.SendAsync(HttpMethod.Get, Url(request.Id, "").TrimEnd('/'), owner)))["activity"]!
            .AsArray().Select(entry => entry!["label"]!.GetValue<string>()).ToArray();
        Assert.Contains("Priority changed to Urgent", activity);
        Assert.Contains("Assigned to Dina Dispatcher", activity);
        Assert.Contains("Unassigned", activity);
    }

    // AC-13, AC-14, AC-15, AC-23: notes, information requests (email after commit, failure tolerance) and logged responses.
    [Fact]
    public async Task NotesInformationRequestsAndResponses_StoreMessagesAuditWithoutContentAndSendEmailAfterCommit()
    {
        var world = await database.SeedWorldAsync();
        var orgName = await database.ScalarAsync<string>("SELECT name FROM organizations WHERE id = @o", ("o", world.Org));
        await using var host = RequestsHost.Create(database);
        var (owner, ownerMember) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId, "Ola", "Owner");
        var (viewer, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.ViewerRoleId);

        var request = await database.SeedRequestAsync(world);
        const string noteText = "Check the <b>shutoff valve</b> first";

        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(HttpMethod.Post, Url(request.Id, "notes"), owner, Body("body", "   "))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(HttpMethod.Post, Url(request.Id, "notes"), owner, Body("body", new string('n', 2001)))).StatusCode);
        var note = await host.SendAsync(HttpMethod.Post, Url(request.Id, "notes"), owner, Body("body", $"  {noteText}  "));
        Assert.Equal(HttpStatusCode.OK, note.StatusCode);
        Assert.Equal(noteText, (await RequestsHost.ReadAsync(note))["notes"]![0]!["body"]!.GetValue<string>());
        Assert.Equal("new", await database.StatusOfAsync(request.Id));
        Assert.Empty(host.Sender.Messages);
        var viewerNotes = (await RequestsHost.ReadAsync(await host.SendAsync(HttpMethod.Get, $"/service-requests/{request.Id}", viewer)))["notes"]!.AsArray();
        Assert.Equal("Ola Owner", viewerNotes[0]!["authorName"]!.GetValue<string>());
        var (_, _, noteMetadata) = await database.GetLatestAuditAsync(world.Org, "service_request.internal_note_added");
        Assert.Equal(["messageId"], JsonNode.Parse(noteMetadata!)!.AsObject().Select(pair => pair.Key).ToArray());
        Assert.DoesNotContain("shutoff", await database.AuditTextAsync(request.Id));

        // Request information: customer message, implied transition, one email to the contact after the commit.
        const string question = "Please send a photo of <script>alert(1)</script> the leak";
        var information = await host.SendAsync(HttpMethod.Post, Url(request.Id, "information-requests"), owner, Body("body", question));
        Assert.Equal(HttpStatusCode.OK, information.StatusCode);
        var informationBody = await RequestsHost.ReadAsync(information);
        Assert.Equal("needs_review", informationBody["status"]!.GetValue<string>());
        Assert.True(informationBody["awaitingResponse"]!.GetValue<bool>());
        var email = Assert.Single(host.Sender.Messages);
        Assert.Equal("carla@example.com", email.To);
        Assert.Equal($"{orgName} needs more information about REQ-{request.Number}", email.Subject);
        Assert.Contains("Hi Pat,", email.TextBody);
        Assert.Contains(question, email.TextBody);
        Assert.Contains("Questions? Call us at +1 555 010 0100.", email.TextBody);
        Assert.Contains("&lt;script&gt;", email.HtmlBody);
        Assert.DoesNotContain("<script>", email.HtmlBody);
        Assert.Equal(2, await database.CountAsync("request_status_history", request.Id));

        var card = Column(await RequestsHost.ReadAsync(await host.SendAsync(HttpMethod.Get, "/service-requests/pipeline", viewer)), "needs_review")["items"]![0]!;
        Assert.True(card["awaitingResponse"]!.GetValue<bool>());
        Assert.Equal(
            1,
            (await RequestsHost.ReadAsync(await host.SendAsync(HttpMethod.Get, "/service-requests/metrics", viewer)))["awaitingResponse"]!["value"]!.GetValue<int>());
        Assert.Contains(
            informationBody["activity"]!.AsArray(),
            entry => entry!["label"]!.GetValue<string>() == "Information requested" && entry["detail"]!.GetValue<string>() == question);

        // BR-20: ids and status only, never names, contact data or message bodies.
        var auditText = await database.AuditTextAsync(request.Id);
        Assert.Contains("information_requested", auditText);
        Assert.DoesNotContain("photo", auditText);
        Assert.DoesNotContain("Carla", auditText);
        Assert.DoesNotContain("carla@example.com", auditText);
        Assert.DoesNotContain("Pat", auditText);

        // Logging the customer's answer ends "Awaiting response" and records the contact as author.
        var logged = await host.SendAsync(HttpMethod.Post, Url(request.Id, "customer-responses"), owner, Body("body", "Called back, will send tomorrow"));
        Assert.Equal(HttpStatusCode.OK, logged.StatusCode);
        var loggedBody = await RequestsHost.ReadAsync(logged);
        Assert.False(loggedBody["awaitingResponse"]!.GetValue<bool>());
        Assert.Contains(loggedBody["activity"]!.AsArray(), entry => entry!["label"]!.GetValue<string>() == "Customer response logged");
        Assert.Equal(world.Contact, await database.ScalarAsync<Guid>(
            "SELECT author_contact_id FROM request_messages WHERE request_id = @r AND author_contact_id IS NOT NULL", ("r", request.Id)));
        Assert.Equal(ownerMember.UserId, await database.ScalarAsync<Guid>(
            "SELECT author_user_id FROM request_messages WHERE request_id = @r AND author_contact_id IS NOT NULL", ("r", request.Id)));
        Assert.Equal(1, await database.AuditCountAsync(request.Id, "service_request.customer_response_logged"));
        Assert.Single(host.Sender.Messages);

        // Email failure keeps the message and the transition and logs no recipient or body.
        host.Sender.Fail = true;
        var failing = await database.SeedRequestAsync(world);
        var failed = await host.SendAsync(HttpMethod.Post, Url(failing.Id, "information-requests"), owner, Body("body", "Secret question text"));
        host.Sender.Fail = false;
        Assert.Equal(HttpStatusCode.OK, failed.StatusCode);
        Assert.Equal("needs_review", await database.StatusOfAsync(failing.Id));
        Assert.Equal(1, await database.CountAsync("request_messages", failing.Id));
        var logs = new CapturingLogs(host.Logs).AllText();
        Assert.Contains(failing.Id.ToString(), logs);
        Assert.DoesNotContain("carla@example.com", logs);
        Assert.DoesNotContain("Secret question", logs);
        Assert.Single(host.Sender.Messages);

        // No email address / no contact: 400 on body, nothing stored and nothing sent.
        var guestOnly = await database.SeedRequestAsync(world, linkCustomer: false, guestEmail: null);
        var noEmail = await host.SendAsync(HttpMethod.Post, Url(guestOnly.Id, "information-requests"), owner, Body("body", "Hello"));
        Assert.Equal(HttpStatusCode.BadRequest, noEmail.StatusCode);
        Assert.Equal("This customer has no email address.", Error(await RequestsHost.ReadAsync(noEmail), "body"));
        var noContact = await host.SendAsync(HttpMethod.Post, Url(guestOnly.Id, "customer-responses"), owner, Body("body", "Hello"));
        Assert.Equal(HttpStatusCode.BadRequest, noContact.StatusCode);
        Assert.Equal("This request has no customer contact.", Error(await RequestsHost.ReadAsync(noContact), "body"));
        Assert.Equal("new", await database.StatusOfAsync(guestOnly.Id));
        Assert.Equal(0, await database.CountAsync("request_messages", guestOnly.Id));
        Assert.Equal(0, await database.CountAsync("audit_logs", guestOnly.Id));
        Assert.Single(host.Sender.Messages);
    }

    private static JsonNode Column(JsonNode pipeline, string status) =>
        pipeline["columns"]!.AsArray().Single(column => column!["status"]!.GetValue<string>() == status)!;
}
