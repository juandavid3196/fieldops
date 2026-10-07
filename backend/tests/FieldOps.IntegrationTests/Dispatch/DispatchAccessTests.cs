using System.Net;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.ServiceRequests;
using FieldOps.IntegrationTests.Team;

namespace FieldOps.IntegrationTests.Dispatch;

/// <summary>Roles, branch scope and tenant isolation of the dispatch endpoints (dispatch-calendar AC-01, AC-11, AC-21, AC-22).</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class DispatchAccessTests(CompanySettingsDatabaseFixture database)
{
    private static async Task<string> WithoutTraceAsync(HttpResponseMessage response)
    {
        var problem = (System.Text.Json.Nodes.JsonObject)(await RequestsHost.ReadAsync(response));
        problem.Remove("traceId");

        return problem.ToJsonString();
    }

    [Fact]
    public async Task Access_RolesScopeAndTenantIsolation_Hold()
    {
        var world = await database.SeedWorldAsync();
        var foreign = await database.SeedWorldAsync();
        await using var host = RequestsHost.Create(database);
        var (owner, ownerMember) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var (dispatcher, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.DispatcherRoleId, "Dee", "Spatch", world.BranchA);
        var (viewer, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.ViewerRoleId);
        var (technician, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.TechnicianRoleId);
        var (accounting, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.AccountingRoleId);
        var (foreignOwner, foreignMember) = await host.SignInAsync(database, foreign.Org, CompanySettingsDatabaseFixture.OwnerRoleId);

        var techA = await database.SeedAvailableTechAsync(world.Org, world.BranchA, "Ann", "Alpha");
        var techB = await database.SeedAvailableTechAsync(world.Org, world.BranchB, "Bob", "Bravo");
        var foreignTech = await database.SeedAvailableTechAsync(foreign.Org, foreign.BranchA, "Fay", "Foreign");
        var orderA = await database.SeedOrderAsync(world, ownerMember.UserId, title: "Alpha job");
        var orderB = await database.SeedOrderAsync(world, ownerMember.UserId, branch: world.BranchB, title: "Bravo job");
        var foreignOrder = await database.SeedOrderAsync(foreign, foreignMember.UserId, title: "Foreign job");
        var slot = DispatchSeed.Future(5, 9);
        var today = DispatchSeed.Date(DateTimeOffset.UtcNow);

        string Calendar(Guid branch, string extra = "") => $"/dispatch/calendar?branchId={branch}&view=week&date={today}&status=all{extra}";
        string Unscheduled(Guid branch) => $"/dispatch/unscheduled?branchId={branch}&filter=all&page=1&pageSize=25";

        // Viewer reads everything in scope but cannot evaluate or dispatch (403).
        foreach (var path in new[] { "/dispatch/options", Calendar(world.BranchA), Unscheduled(world.BranchA), $"/dispatch/visits/{orderA.Visit}" })
        {
            var read = await host.SendAsync(HttpMethod.Get, path, viewer);

            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            Assert.Equal("no-store", read.Headers.CacheControl?.ToString());
        }

        Assert.False((await DispatchSeed.ReadAsync(await DispatchSeed.GetVisitAsync(host, viewer, orderA.Visit)))["canManage"]!.GetValue<bool>());
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await DispatchSeed.EvaluateAsync(host, viewer, orderA.Visit, DispatchSeed.EvaluationBody(slot, 60, techA))).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await DispatchSeed.PutAsync(host, viewer, orderA.Visit, DispatchSeed.Body(slot, DateTimeOffset.UtcNow.ToString("O"), technicians: [techA]))).StatusCode);

        // Technician and Accounting have no access at all.
        foreach (var cookie in new[] { technician, accounting })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(HttpMethod.Get, "/dispatch/options", cookie)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(HttpMethod.Get, Calendar(world.BranchA), cookie)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await DispatchSeed.GetVisitAsync(host, cookie, orderA.Visit)).StatusCode);
            Assert.Equal(
                HttpStatusCode.Forbidden,
                (await DispatchSeed.EvaluateAsync(host, cookie, orderA.Visit, DispatchSeed.EvaluationBody(slot, 60, techA))).StatusCode);
        }

        // A Dispatcher scoped to branch A sees only branch A: options, lists, calendars and technicians.
        var options = await DispatchSeed.ReadAsync(await host.SendAsync(HttpMethod.Get, "/dispatch/options", dispatcher));
        Assert.Equal([world.BranchA], options["branches"]!.AsArray().Select(branch => branch!["id"]!.GetValue<Guid>()).ToArray());
        Assert.Equal(world.BranchA, options["defaultBranchId"]!.GetValue<Guid>());
        Assert.Equal(2, (await DispatchSeed.ReadAsync(await host.SendAsync(HttpMethod.Get, "/dispatch/options", owner)))["branches"]!.AsArray().Count);

        Assert.Equal(HttpStatusCode.NotFound, (await DispatchSeed.GetVisitAsync(host, dispatcher, orderB.Visit)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Get, Calendar(world.BranchB), dispatcher)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Get, Unscheduled(world.BranchB), dispatcher)).StatusCode);

        var listed = await DispatchSeed.ReadAsync(await host.SendAsync(HttpMethod.Get, Unscheduled(world.BranchA), dispatcher));
        Assert.Equal(["Alpha job"], listed["items"]!.AsArray().Select(item => item!["title"]!.GetValue<string>()).ToArray());
        Assert.Equal(
            [orderB.Visit],
            (await DispatchSeed.ReadAsync(await host.SendAsync(HttpMethod.Get, Unscheduled(world.BranchB), owner)))["items"]!.AsArray()
                .Select(item => item!["visitId"]!.GetValue<Guid>())
                .ToArray());

        // A technician outside the Dispatcher scope is a 404, an eligible one evaluates; out-of-scope Team ids are ignored.
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await DispatchSeed.EvaluateAsync(host, dispatcher, orderA.Visit, DispatchSeed.EvaluationBody(slot, 60, techB))).StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await DispatchSeed.EvaluateAsync(host, dispatcher, orderA.Visit, DispatchSeed.EvaluationBody(slot, 60, techA))).StatusCode);

        var calendar = await DispatchSeed.ReadAsync(
            await host.SendAsync(HttpMethod.Get, Calendar(world.BranchA, $"&technicianIds={techB}&technicianIds={foreignTech}"), dispatcher));
        Assert.Equal([techA], calendar["technicians"]!.AsArray().Select(lane => lane!["id"]!.GetValue<Guid>()).ToArray());

        // Another organization: the same 404 as a nonexistent id, for the visit, the branch and every technician.
        var missing = await DispatchSeed.GetVisitAsync(host, owner, Guid.NewGuid());
        var crossVisit = await DispatchSeed.GetVisitAsync(host, foreignOwner, orderA.Visit);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, crossVisit.StatusCode);
        Assert.Equal(await WithoutTraceAsync(missing), await WithoutTraceAsync(crossVisit));
        Assert.Equal(HttpStatusCode.NotFound, (await DispatchSeed.GetVisitAsync(host, owner, foreignOrder.Visit)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Get, Calendar(world.BranchA), foreignOwner)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Get, Unscheduled(foreign.BranchA), owner)).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await DispatchSeed.EvaluateAsync(host, owner, orderA.Visit, DispatchSeed.EvaluationBody(slot, 60, foreignTech))).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await DispatchSeed.PutAsync(host, foreignOwner, orderA.Visit, DispatchSeed.Body(slot, DateTimeOffset.UtcNow.ToString("O"), technicians: [techA]))).StatusCode);
        Assert.DoesNotContain("Alpha job", await crossVisit.Content.ReadAsStringAsync());
        Assert.Equal(
            "unscheduled|-|0|0|0",
            await database.VisitStateAsync(orderA.Visit));
    }
}
