using System.Net;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.Team;

namespace FieldOps.IntegrationTests.ServiceRequests;

/// <summary>Assessment schedule, reschedule and cancel with branch requirement and overlap (including concurrency): AC-16, AC-17, AC-23.</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class ServiceRequestAssessmentTests(CompanySettingsDatabaseFixture database)
{
    private static string Url(Guid id, string suffix) => $"/service-requests/{id}/{suffix}";

    private static string Error(JsonNode problem, string key) =>
        problem["errors"]![key]!.AsArray().Single()!.GetValue<string>();

    private static JsonObject Slot(DateTimeOffset day, int fromHour, int toHour, Guid? technician = null, Guid? branch = null, int fromMinute = 0) =>
        new()
        {
            ["start"] = $"{day:yyyy-MM-dd}T{fromHour:00}:{fromMinute:00}",
            ["end"] = $"{day:yyyy-MM-dd}T{toHour:00}:00",
            ["technicianId"] = technician,
            ["branchId"] = branch,
        };

    [Fact]
    public async Task Assessments_ValidateScheduleAndKeepOverlapAndRowsConsistent()
    {
        var world = await database.SeedWorldAsync();
        var techA = await database.SeedTechAsync(world.Org, world.BranchA, "Tina", "Tech");
        var techB = await database.SeedTechAsync(world.Org, world.BranchB, "Bob", "Builder");
        var tomorrow = DateTimeOffset.UtcNow.AddDays(1);
        var yesterday = DateTimeOffset.UtcNow.AddDays(-1);

        await using var host = RequestsHost.Create(database);
        var (owner, ownerMember) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);

        var request = await database.SeedRequestAsync(world, status: "needs_review");
        var busy = await database.SeedRequestAsync(world, status: "assessment_scheduled", branch: world.BranchA);
        await database.SeedAssessmentAsync(
            world.Org,
            busy.Id,
            new DateTimeOffset(tomorrow.Year, tomorrow.Month, tomorrow.Day, 9, 30, 0, TimeSpan.Zero),
            new DateTimeOffset(tomorrow.Year, tomorrow.Month, tomorrow.Day, 10, 30, 0, TimeSpan.Zero),
            ownerMember.UserId,
            techA);

        async Task<JsonNode> Rejected(JsonObject body)
        {
            var response = await host.SendAsync(HttpMethod.Post, Url(request.Id, "assessment"), owner, body);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            return await RequestsHost.ReadAsync(response);
        }

        // Invalid inputs: branch required, past start, end rules, technician branch and overlap. Nothing is persisted.
        Assert.NotNull(Error(await Rejected(Slot(tomorrow, 11, 12)), "branchId"));
        Assert.NotNull(Error(await Rejected(Slot(yesterday, 11, 12, branch: world.BranchA)), "start"));
        Assert.NotNull(Error(await Rejected(Slot(tomorrow, 12, 11, branch: world.BranchA)), "end"));
        Assert.NotNull(Error(await Rejected(Slot(tomorrow, 8, 17, branch: world.BranchA)), "end"));
        var overnight = Slot(tomorrow, 20, 21, branch: world.BranchA);
        overnight["end"] = $"{tomorrow.AddDays(1):yyyy-MM-dd}T01:00";
        Assert.NotNull(Error(await Rejected(overnight), "end"));
        Assert.NotNull(Error(await Rejected(Slot(tomorrow, 11, 12, techB, world.BranchA)), "technicianId"));
        Assert.Equal(
            "This technician already has an assessment at that time.",
            Error(await Rejected(Slot(tomorrow, 9, 10, techA, world.BranchA)), "technicianId"));
        Assert.Equal("needs_review", await database.StatusOfAsync(request.Id));
        Assert.Equal(0, await database.CountAsync("assessments", request.Id));
        Assert.Equal(0, await database.CountAsync("audit_logs", request.Id));
        Assert.False(await database.ScalarAsync<bool>("SELECT branch_id IS NOT NULL FROM service_requests WHERE id = @r", ("r", request.Id)));

        // Valid: sets the branch, creates one scheduled assessment and moves to assessment_scheduled.
        var scheduled = await host.SendAsync(HttpMethod.Post, Url(request.Id, "assessment"), owner, Slot(tomorrow, 11, 12, techA, world.BranchA));
        Assert.Equal(HttpStatusCode.OK, scheduled.StatusCode);
        var detail = await RequestsHost.ReadAsync(scheduled);
        Assert.Equal("assessment_scheduled", detail["status"]!.GetValue<string>());
        Assert.Equal("Alpha Branch", detail["branch"]!["name"]!.GetValue<string>());
        Assert.Equal("Tina Tech", detail["assessment"]!["technician"]!["name"]!.GetValue<string>());
        var assessmentId = detail["assessment"]!["id"]!.GetValue<string>();
        Assert.Equal(1, await database.CountAsync("assessments", request.Id));
        Assert.Equal(1, await database.AuditCountAsync(request.Id, "service_request.assessment_scheduled"));
        var (before, after, metadata) = await database.GetLatestAuditAsync(world.Org, "service_request.assessment_scheduled");
        Assert.Equal("needs_review", JsonNode.Parse(before!)!["status"]!.GetValue<string>());
        Assert.Equal("assessment_scheduled", JsonNode.Parse(after!)!["status"]!.GetValue<string>());
        Assert.Equal(assessmentId, JsonNode.Parse(metadata!)!["assessmentId"]!.GetValue<string>());
        Assert.Equal(techA.ToString(), JsonNode.Parse(metadata!)!["technicianId"]!.GetValue<string>());
        Assert.DoesNotContain("Tina", await database.AuditTextAsync(request.Id));
        Assert.Equal(HttpStatusCode.Conflict, (await host.SendAsync(HttpMethod.Post, Url(request.Id, "assessment"), owner, Slot(tomorrow, 14, 15, techA, world.BranchA))).StatusCode);

        var card = (await RequestsHost.ReadAsync(await host.SendAsync(HttpMethod.Get, "/service-requests/pipeline", owner)))["columns"]![2]!["items"]!.AsArray()
            .Single(item => item!["id"]!.GetValue<string>() == request.Id.ToString())!;
        Assert.Equal("assessment", card["dateKind"]!.GetValue<string>());
        Assert.Equal("TT", card["avatar"]!["initials"]!.GetValue<string>());

        // Reschedule: same row, status unchanged, own old slot excluded from the overlap check, other slots still checked.
        var historyBefore = await database.CountAsync("request_status_history", request.Id);
        var overlapping = await host.SendAsync(HttpMethod.Put, Url(request.Id, "assessment"), owner, Slot(tomorrow, 9, 10, techA));
        Assert.Equal(HttpStatusCode.BadRequest, overlapping.StatusCode);
        var rescheduled = await host.SendAsync(HttpMethod.Put, Url(request.Id, "assessment"), owner, Slot(tomorrow, 11, 13, techA, fromMinute: 30));
        Assert.Equal(HttpStatusCode.OK, rescheduled.StatusCode);
        var rescheduledBody = await RequestsHost.ReadAsync(rescheduled);
        Assert.Equal(assessmentId, rescheduledBody["assessment"]!["id"]!.GetValue<string>());
        Assert.Equal("assessment_scheduled", rescheduledBody["status"]!.GetValue<string>());
        Assert.Equal(1, await database.CountAsync("assessments", request.Id));
        Assert.Equal(1, await database.AuditCountAsync(request.Id, "service_request.assessment_rescheduled"));
        Assert.Equal(historyBefore, await database.CountAsync("request_status_history", request.Id));

        // Cancel assessment: assessment cancelled, request back to needs_review, one audit row; a second cancel is 409.
        var cancelled = await host.SendAsync(HttpMethod.Post, Url(request.Id, "assessment/cancel"), owner);
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        Assert.Equal("needs_review", (await RequestsHost.ReadAsync(cancelled))["status"]!.GetValue<string>());
        Assert.Equal("cancelled", await database.ScalarAsync<string>("SELECT status::text FROM assessments WHERE id = @a", ("a", Guid.Parse(assessmentId))));
        Assert.Equal(1, await database.AuditCountAsync(request.Id, "service_request.assessment_cancelled"));
        Assert.Equal(HttpStatusCode.Conflict, (await host.SendAsync(HttpMethod.Post, Url(request.Id, "assessment/cancel"), owner)).StatusCode);

        // Concurrent schedules of the same technician and slot: exactly one wins, the other is a 400 overlap.
        var first = await database.SeedRequestAsync(world, status: "needs_review", branch: world.BranchA);
        var second = await database.SeedRequestAsync(world, status: "needs_review", branch: world.BranchA);
        var day = tomorrow.AddDays(2);
        var results = await Task.WhenAll(
            host.SendAsync(HttpMethod.Post, Url(first.Id, "assessment"), owner, Slot(day, 14, 15, techA)),
            host.SendAsync(HttpMethod.Post, Url(second.Id, "assessment"), owner, Slot(day, 14, 15, techA)));
        Assert.Equal(
            [HttpStatusCode.OK, HttpStatusCode.BadRequest],
            results.Select(result => result.StatusCode).Order().ToArray());
        Assert.Equal(
            1,
            await database.ScalarAsync<long>(
                "SELECT COUNT(*) FROM assessments WHERE technician_id = @t AND status = 'scheduled' AND scheduled_start = @s",
                ("t", techA),
                ("s", new DateTimeOffset(day.Year, day.Month, day.Day, 14, 0, 0, TimeSpan.Zero))));
    }
}
