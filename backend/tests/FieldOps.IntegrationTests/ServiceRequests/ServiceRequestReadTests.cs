using System.Net;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.PublicRequests;
using FieldOps.IntegrationTests.Team;
using FieldOps.IntegrationTests.Users;

namespace FieldOps.IntegrationTests.ServiceRequests;

/// <summary>Pipeline, filters, metrics, branch scope, tenant isolation and role matrix: AC-01 to AC-07.</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class ServiceRequestReadTests(CompanySettingsDatabaseFixture database)
{
    private static JsonNode Column(JsonNode pipeline, string status) =>
        pipeline["columns"]!.AsArray().Single(column => column!["status"]!.GetValue<string>() == status)!;

    private static string[] Ids(JsonNode column) =>
        [.. column["items"]!.AsArray().Select(item => item!["id"]!.GetValue<string>())];

    private static string[] AllIds(JsonNode pipeline) =>
        [.. pipeline["columns"]!.AsArray().SelectMany(column => Ids(column!))];

    private static string Error(JsonNode problem, string key) =>
        problem["errors"]![key]!.AsArray().Single()!.GetValue<string>();

    // AC-01, AC-02, AC-06: four columns with true totals, BR-05 ordering, card derivation, paging and AND filters/search.
    [Fact]
    public async Task Pipeline_ReturnsOrderedPagedFilteredColumns()
    {
        var world = await database.SeedWorldAsync();
        var otherCategory = await database.SeedCategoryAsync(world.Org, "Electrical");
        var otherOrg = await database.SeedWorldAsync();
        var tech = await database.SeedTechAsync(world.Org, world.BranchA, "Tina", "Tech");

        await using var host = RequestsHost.Create(database);
        var (owner, ownerMember) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId, "Ola", "Owner");
        var (_, dispatcher) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.DispatcherRoleId, "Dina", "Dispatcher");
        var (_, opsManager) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OperationsManagerRoleId);

        var now = DateTimeOffset.UtcNow;
        var oldest = await database.SeedRequestAsync(world, createdAt: now.AddMinutes(-50));
        var plain = await database.SeedRequestAsync(world, createdAt: now.AddMinutes(-40));
        var urgent = await database.SeedRequestAsync(world, urgency: "urgent", createdAt: now.AddMinutes(-35));
        var emergency = await database.SeedRequestAsync(world, urgency: "emergency", createdAt: now.AddMinutes(-45));
        var internalRequest = await database.SeedRequestAsync(
            world,
            source: "internal",
            createdAt: now.AddMinutes(-30),
            linkCustomer: false,
            guestName: "Ivy Internal",
            guestEmail: "ivy@example.com",
            assignee: dispatcher.UserId,
            withService: false);
        var electrical = await database.SeedRequestAsync(world, createdAt: now.AddMinutes(-20), linkCustomer: false);
        await database.ExecuteAsync(
            "UPDATE service_requests SET category_id = @c, catalog_item_id = NULL WHERE id = @id",
            ("c", otherCategory),
            ("id", electrical.Id));
        var review = await database.SeedRequestAsync(world, status: "needs_review", createdAt: now.AddMinutes(-10));
        var scheduled = await database.SeedRequestAsync(world, status: "assessment_scheduled", branch: world.BranchA, createdAt: now.AddMinutes(-9));
        var start = now.AddDays(1).AddHours(1);
        await database.SeedAssessmentAsync(world.Org, scheduled.Id, start, start.AddHours(1), ownerMember.UserId, tech);
        var ready = await database.SeedRequestAsync(world, status: "ready_for_quote", createdAt: now.AddMinutes(-8));
        var quoted = await database.SeedRequestAsync(world, status: "quoted");
        var cancelled = await database.SeedRequestAsync(world, status: "cancelled");
        var old = await database.SeedRequestAsync(world, createdAt: now.AddDays(-20));

        var response = await host.SendAsync(HttpMethod.Get, "/service-requests/pipeline", owner);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var pipeline = await RequestsHost.ReadAsync(response);

        Assert.Equal("UTC", pipeline["timezone"]!.GetValue<string>());
        Assert.Equal(
            ["new", "needs_review", "assessment_scheduled", "ready_for_quote"],
            pipeline["columns"]!.AsArray().Select(column => column!["status"]!.GetValue<string>()).ToArray());
        Assert.DoesNotContain(quoted.Id.ToString(), AllIds(pipeline));
        Assert.DoesNotContain(cancelled.Id.ToString(), AllIds(pipeline));

        var newColumn = Column(pipeline, "new");
        Assert.Equal(7, newColumn["total"]!.GetValue<int>());
        Assert.Equal(
            new[] { emergency.Id, urgent.Id, old.Id, oldest.Id, plain.Id, internalRequest.Id, electrical.Id }.Select(id => id.ToString()),
            Ids(newColumn));
        Assert.Equal([review.Id.ToString()], Ids(Column(pipeline, "needs_review")));
        Assert.Equal([ready.Id.ToString()], Ids(Column(pipeline, "ready_for_quote")));

        var card = newColumn["items"]!.AsArray().Single(item => item!["id"]!.GetValue<string>() == plain.Id.ToString())!;
        Assert.Equal($"REQ-{plain.Number}", card["number"]!.GetValue<string>());
        Assert.Equal("Drain cleaning", card["title"]!.GetValue<string>());
        Assert.Equal("Carla Customer", card["customerName"]!.GetValue<string>());
        Assert.Equal("Plumbing", card["categoryName"]!.GetValue<string>());
        Assert.Equal("asap", card["dateKind"]!.GetValue<string>());
        Assert.Null(card["date"]);
        Assert.Equal("standard", card["urgency"]!.GetValue<string>());
        Assert.Null(card["avatar"]);
        Assert.False(card["awaitingResponse"]!.GetValue<bool>());

        var internalCard = newColumn["items"]!.AsArray().Single(item => item!["id"]!.GetValue<string>() == internalRequest.Id.ToString())!;
        Assert.Equal("Plumbing", internalCard["title"]!.GetValue<string>());
        Assert.Equal("Ivy Internal", internalCard["customerName"]!.GetValue<string>());
        Assert.Equal("assignee", internalCard["avatar"]!["role"]!.GetValue<string>());
        Assert.Equal("DD", internalCard["avatar"]!["initials"]!.GetValue<string>());

        var scheduledCard = Column(pipeline, "assessment_scheduled")["items"]![0]!;
        Assert.Equal("assessment", scheduledCard["dateKind"]!.GetValue<string>());
        Assert.Equal(start.ToUnixTimeSeconds(), scheduledCard["date"]!.GetValue<DateTimeOffset>().ToUnixTimeSeconds());
        Assert.Equal("technician", scheduledCard["avatar"]!["role"]!.GetValue<string>());
        Assert.Equal("Tina Tech", scheduledCard["avatar"]!["name"]!.GetValue<string>());

        // Filters are AND-combined, apply to columns and totals, and an empty result keeps four columns.
        async Task<JsonNode> Filtered(string query) =>
            await RequestsHost.ReadAsync(await host.SendAsync(HttpMethod.Get, $"/service-requests/pipeline?{query}", owner));

        Assert.DoesNotContain(internalRequest.Id.ToString(), Ids(Column(await Filtered("assigneeUserId=unassigned"), "new")));
        Assert.Equal([internalRequest.Id.ToString()], AllIds(await Filtered($"assigneeUserId={dispatcher.UserId}")));
        Assert.Equal([electrical.Id.ToString()], AllIds(await Filtered($"categoryId={otherCategory}")));
        Assert.Equal([emergency.Id.ToString()], AllIds(await Filtered("urgency=emergency")));
        Assert.Equal([internalRequest.Id.ToString()], AllIds(await Filtered("source=internal")));
        Assert.Contains(old.Id.ToString(), AllIds(await Filtered("created=30d")));
        Assert.DoesNotContain(old.Id.ToString(), AllIds(await Filtered("created=7d")));
        Assert.DoesNotContain(old.Id.ToString(), AllIds(await Filtered("created=today")));
        Assert.Equal([internalRequest.Id.ToString()], AllIds(await Filtered("search=IVY")));
        Assert.Equal([internalRequest.Id.ToString()], AllIds(await Filtered("search=ivy@example")));
        Assert.Equal([electrical.Id.ToString()], AllIds(await Filtered("search=electric")));
        Assert.Contains(plain.Id.ToString(), AllIds(await Filtered("search=carla")));
        Assert.Equal([plain.Id.ToString()], AllIds(await Filtered($"search=REQ-{plain.Number}")));
        Assert.Equal([plain.Id.ToString()], AllIds(await Filtered($"search={plain.Number}")));
        Assert.Empty(AllIds(await Filtered("search=%25")));
        Assert.Empty(AllIds(await Filtered("urgency=emergency&source=internal")));
        var none = await Filtered("search=zzzz-no-match");
        Assert.Equal(0, Column(none, "new")["total"]!.GetValue<int>());
        Assert.Equal(4, none["columns"]!.AsArray().Count);

        foreach (var (query, key) in new[]
        {
            ("assigneeUserId=garbage", "assigneeUserId"),
            ($"assigneeUserId={Guid.NewGuid()}", "assigneeUserId"),
            ($"assigneeUserId={opsManager.UserId}", "assigneeUserId"),
            ($"categoryId={otherOrg.Category}", "categoryId"),
            ("urgency=high", "urgency"),
            ("source=email", "source"),
            ("created=1y", "created"),
            ($"search={new string('a', 101)}", "search"),
            ("status=quoted", "status"),
            ("offset=-1", "offset"),
            ("offset=5", "offset"),
        })
        {
            var invalid = await host.SendAsync(HttpMethod.Get, $"/service-requests/pipeline?{query}", owner);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.NotNull(Error(await RequestsHost.ReadAsync(invalid), key));
        }

        // Paging: 55 requests in one column, the true total and the next page with the same order.
        var paged = await database.SeedWorldAsync();
        var baseline = now.AddDays(-3);
        var pagedIds = new List<string>();

        for (var index = 0; index < 55; index++)
        {
            pagedIds.Add((await database.SeedRequestAsync(paged, createdAt: baseline.AddSeconds(index), withHistory: false)).Id.ToString());
        }

        var (pagedOwner, _) = await host.SignInAsync(database, paged.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var first = await RequestsHost.ReadAsync(await host.SendAsync(HttpMethod.Get, "/service-requests/pipeline", pagedOwner));
        Assert.Equal(55, Column(first, "new")["total"]!.GetValue<int>());
        Assert.Equal(pagedIds.Take(50), Ids(Column(first, "new")));

        var second = await RequestsHost.ReadAsync(
            await host.SendAsync(HttpMethod.Get, "/service-requests/pipeline?status=new&offset=50", pagedOwner));
        Assert.Single(second["columns"]!.AsArray());
        Assert.Equal(55, Column(second, "new")["total"]!.GetValue<int>());
        Assert.Equal(pagedIds.Skip(50), Ids(Column(second, "new")));
    }

    // AC-07: values and deltas of BR-07, including a null delta for a zero previous value and percentage points.
    [Fact]
    public async Task Metrics_ComputeValuesAndDeltasInOrganizationTime()
    {
        var world = await database.SeedWorldAsync();
        await using var host = RequestsHost.Create(database);
        var (owner, ownerMember) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);

        var now = DateTimeOffset.UtcNow;
        var todayStart = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);

        await database.SeedRequestAsync(world, createdAt: todayStart.AddSeconds(1));
        var readyRequest = await database.SeedRequestAsync(world, status: "ready_for_quote", createdAt: todayStart.AddSeconds(2));
        await database.SeedHistoryAsync(world.Org, readyRequest.Id, "new", "ready_for_quote", todayStart.AddSeconds(3));
        await database.SeedRequestAsync(world, createdAt: todayStart.AddDays(-7).AddHours(1));

        var awaitingThen = await database.SeedRequestAsync(world, status: "needs_review", createdAt: now.AddDays(-10));
        await database.SeedMessageAsync(world.Org, awaitingThen.Id, "customer", "Please confirm", now.AddDays(-9), ownerMember.UserId);
        var awaitingNow = await database.SeedRequestAsync(world, createdAt: now.AddDays(-2));
        await database.SeedMessageAsync(world.Org, awaitingNow.Id, "customer", "Please confirm", now.AddHours(-1), ownerMember.UserId);
        var answered = await database.SeedRequestAsync(world, createdAt: now.AddDays(-2));
        await database.SeedMessageAsync(world.Org, answered.Id, "customer", "Please confirm", now.AddHours(-3), ownerMember.UserId);
        await database.SeedMessageAsync(world.Org, answered.Id, "customer", "Done", now.AddHours(-2), ownerMember.UserId, world.Contact);
        await database.SeedMessageAsync(world.Org, answered.Id, "internal", "Internal only", now.AddHours(-1), ownerMember.UserId);

        var quotedBefore = await database.SeedRequestAsync(world, status: "quoted", createdAt: now.AddDays(-40));
        await database.SeedHistoryAsync(world.Org, quotedBefore.Id, "new", "ready_for_quote", now.AddDays(-39));
        await database.SeedHistoryAsync(world.Org, quotedBefore.Id, "ready_for_quote", "quoted", now.AddDays(-38));
        await database.SeedRequestAsync(world, createdAt: now.AddDays(-40));

        var assessed = await database.SeedRequestAsync(world, status: "assessment_scheduled", createdAt: now.AddDays(-1));
        await database.SeedAssessmentAsync(world.Org, assessed.Id, todayStart.AddHours(12), todayStart.AddHours(13), ownerMember.UserId);
        await database.SeedAssessmentAsync(world.Org, assessed.Id, todayStart.AddHours(14), todayStart.AddHours(15), ownerMember.UserId, status: "completed");

        var response = await host.SendAsync(HttpMethod.Get, "/service-requests/metrics", owner);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var metrics = await RequestsHost.ReadAsync(response);

        Assert.Equal(2, metrics["newToday"]!["value"]!.GetValue<int>());
        Assert.Equal(100, metrics["newToday"]!["deltaPercent"]!.GetValue<int>());
        Assert.Equal(2, metrics["awaitingResponse"]!["value"]!.GetValue<int>());
        Assert.Equal(100, metrics["awaitingResponse"]!["deltaPercent"]!.GetValue<int>());
        Assert.Equal(1, metrics["assessmentsToday"]!["value"]!.GetValue<int>());
        Assert.Null(metrics["assessmentsToday"]!["deltaPercent"]);

        // Current period: 1 of 7 requests reached ready for quote (14%); previous period: 1 of 2 (50%).
        var current = metrics["conversionRate"]!["value"]!.GetValue<int>();
        Assert.Equal(14, current);
        Assert.Equal(-36, metrics["conversionRate"]!["deltaPoints"]!.GetValue<int>());
    }

    // AC-03, AC-04, AC-05: branch scope by role (null branches visible), cross-organization 404, 403 for read roles and technicians.
    [Fact]
    public async Task ScopeIsolationAndRoles_HideOutOfScopeForeignAndForbiddenData()
    {
        var world = await database.SeedWorldAsync();
        var foreign = await database.SeedWorldAsync();
        var inA = await database.SeedRequestAsync(world, branch: world.BranchA);
        var inB = await database.SeedRequestAsync(world, branch: world.BranchB);
        var unassigned = await database.SeedRequestAsync(world);
        var attachment = await database.SeedAttachmentAsync(world.Org, inB.Id, "photo.jpg", "image/jpeg", [0xFF, 0xD8, 0xFF, 0xE0, 1]);

        await using var host = RequestsHost.Create(database);
        var (owner, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var (viewer, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.ViewerRoleId);
        var (opsManager, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OperationsManagerRoleId);
        var (dispatcherA, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.DispatcherRoleId, "Dina", "Dispatcher", world.BranchA);
        var (dispatcherB, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.DispatcherRoleId, "Dora", "Dispatcher", world.BranchB);
        var (accountingB, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.AccountingRoleId, "Ann", "Accounting", world.BranchB);
        var (technician, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.TechnicianRoleId);
        var (foreignOwner, _) = await host.SignInAsync(database, foreign.Org, CompanySettingsDatabaseFixture.OwnerRoleId);

        async Task<string[]> NewIds(string cookie) =>
            Ids(Column(await RequestsHost.ReadAsync(await host.SendAsync(HttpMethod.Get, "/service-requests/pipeline", cookie)), "new"));

        string[] Expected(params (Guid Id, long Number)[] requests) => [.. requests.Select(r => r.Id.ToString()).Order()];

        Assert.Equal(Expected(inA, inB, unassigned), (await NewIds(owner)).Order());
        Assert.Equal(Expected(inA, inB, unassigned), (await NewIds(viewer)).Order());
        Assert.Equal(Expected(inA, inB, unassigned), (await NewIds(opsManager)).Order());
        Assert.Equal(Expected(inA, unassigned), (await NewIds(dispatcherA)).Order());
        Assert.Equal(Expected(inB, unassigned), (await NewIds(accountingB)).Order());

        var scopedColumn = Column(
            await RequestsHost.ReadAsync(await host.SendAsync(HttpMethod.Get, "/service-requests/pipeline", dispatcherA)), "new");
        Assert.Equal(2, scopedColumn["total"]!.GetValue<int>());
        Assert.Equal(
            2,
            (await RequestsHost.ReadAsync(await host.SendAsync(HttpMethod.Get, "/service-requests/metrics", dispatcherA)))["newToday"]!["value"]!.GetValue<int>());
        Assert.Equal(
            3,
            (await RequestsHost.ReadAsync(await host.SendAsync(HttpMethod.Get, "/service-requests/metrics", owner)))["newToday"]!["value"]!.GetValue<int>());

        // Out of scope or foreign: detail, attachment and mutation are the same 404 as a missing id.
        var missing = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(HttpMethod.Get, $"/service-requests/{unassigned.Id}", dispatcherA)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(HttpMethod.Get, $"/service-requests/{inB.Id}/attachments/{attachment}", accountingB)).StatusCode);

        foreach (var (cookie, id) in new[] { (dispatcherA, inB.Id), (foreignOwner, inB.Id), (foreignOwner, inA.Id), (owner, missing) })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Get, $"/service-requests/{id}", cookie)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Get, $"/service-requests/{id}/attachments/{attachment}", cookie)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Post, $"/service-requests/{id}/start-review", cookie)).StatusCode);
            Assert.Equal(
                HttpStatusCode.NotFound,
                (await host.SendAsync(HttpMethod.Post, $"/service-requests/{id}/notes", cookie, new JsonObject { ["body"] = "x" })).StatusCode);
        }

        Assert.Equal("new", await database.StatusOfAsync(inB.Id));
        Assert.Equal("new", await database.StatusOfAsync(inA.Id));
        Assert.Equal(0, await database.CountAsync("request_messages", inB.Id));
        Assert.Equal(0, await database.CountAsync("audit_logs", inB.Id));

        // Branch-limited options and customer options.
        var options = await RequestsHost.ReadAsync(await host.SendAsync(HttpMethod.Get, "/service-requests/options", dispatcherA));
        Assert.Equal("REQ", options["requestPrefix"]!.GetValue<string>());
        Assert.Equal(["Alpha Branch"], options["branches"]!.AsArray().Select(branch => branch!["name"]!.GetValue<string>()).ToArray());
        Assert.Equal("Plumbing", options["categories"]![0]!["name"]!.GetValue<string>());
        Assert.Contains(
            options["assignees"]!.AsArray(),
            assignee => assignee!["name"]!.GetValue<string>() == "Dora Dispatcher" && assignee["branchIds"] is JsonArray);

        var customers = await RequestsHost.ReadAsync(await host.SendAsync(HttpMethod.Get, "/service-requests/customer-options?search=carla", owner));
        Assert.Equal("Carla Customer", customers["items"]![0]!["displayName"]!.GetValue<string>());
        Assert.Single(customers["items"]![0]!["contacts"]!.AsArray());
        Assert.Single(customers["items"]![0]!["properties"]!.AsArray());
        Assert.Empty((await RequestsHost.ReadAsync(await host.SendAsync(HttpMethod.Get, "/service-requests/customer-options?search=carla", dispatcherB)))["items"]!.AsArray());
        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(HttpMethod.Get, $"/service-requests/customer-options?search={new string('a', 101)}", owner)).StatusCode);

        // Read roles read everything but cannot mutate; technicians and anonymous callers are denied everywhere.
        foreach (var cookie in new[] { opsManager, accountingB, viewer })
        {
            Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(HttpMethod.Get, "/service-requests/options", cookie)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(HttpMethod.Post, $"/service-requests/{unassigned.Id}/start-review", cookie)).StatusCode);
            Assert.Equal(
                HttpStatusCode.Forbidden,
                (await host.SendAsync(HttpMethod.Post, $"/service-requests/{unassigned.Id}/notes", cookie, new JsonObject { ["body"] = "x" })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(HttpMethod.Post, "/service-requests", cookie, new JsonObject())).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(HttpMethod.Get, "/service-requests/customer-options", cookie)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await host.UploadAsync($"/service-requests/{unassigned.Id}/attachments", cookie, ("a.jpg", [0xFF, 0xD8, 0xFF, 0xE0]))).StatusCode);
        }

        foreach (var path in new[]
        {
            "/service-requests/pipeline",
            "/service-requests/metrics",
            "/service-requests/options",
            "/service-requests/customer-options",
            $"/service-requests/{unassigned.Id}",
            $"/service-requests/{inB.Id}/attachments/{attachment}",
        })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(HttpMethod.Get, path, technician)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await host.SendAsync(HttpMethod.Get, path, null)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(HttpMethod.Post, $"/service-requests/{unassigned.Id}/start-review", technician)).StatusCode);
        Assert.Equal("new", await database.StatusOfAsync(unassigned.Id));
    }
}
