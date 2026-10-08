using System.Net;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.ServiceRequests;
using FieldOps.IntegrationTests.Team;

namespace FieldOps.IntegrationTests.BillingReview;

/// <summary>Role matrix, branch scope and tenant isolation of the completed jobs review (completed-jobs-review AC-01, AC-02).</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class BillingReviewAccessTests(CompanySettingsDatabaseFixture database)
{
    private static string[] Numbers(JsonNode queue) =>
        [.. queue["items"]!.AsArray().Select(item => item!["number"]!.GetValue<string>()).Order()];

    private static async Task<string> WithoutTraceAsync(HttpResponseMessage response)
    {
        var problem = (JsonObject)JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
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
        var (operations, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OperationsManagerRoleId);
        var (viewer, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.ViewerRoleId);
        var (technician, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.TechnicianRoleId);
        var (accountingAll, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.AccountingRoleId);
        var (accountingA, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.AccountingRoleId, "Ann", "Acct", world.BranchA);
        var (dispatcherA, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.DispatcherRoleId, "Dee", "Spatch", world.BranchA);
        var (foreignOwner, foreignMember) = await host.SignInAsync(database, foreign.Org, CompanySettingsDatabaseFixture.OwnerRoleId);

        var techA = await database.SeedTechAsync(world.Org, world.BranchA, "Ann", "Alpha");
        var techB = await database.SeedTechAsync(world.Org, world.BranchB, "Bob", "Bravo");
        var foreignTech = await database.SeedTechAsync(foreign.Org, foreign.BranchA, "Fay", "Foreign");
        var jobA = await database.SeedJobAsync(world, ownerMember.UserId, new JobSpec { Title = "Alpha job", Technician = techA });
        var jobB = await database.SeedJobAsync(world, ownerMember.UserId, new JobSpec { Title = "Bravo job", Branch = world.BranchB, Technician = techB });
        var foreignJob = await database.SeedJobAsync(foreign, foreignMember.UserId, new JobSpec { Title = "Foreign job" });

        var stateA = await database.OrderStateAsync(jobA.Order);
        var stateB = await database.OrderStateAsync(jobB.Order);
        var evidenceA = $"{BillingSeed.Detail(jobA.Order)}/evidence/{jobA.BeforePhoto}";
        var evidenceB = $"{BillingSeed.Detail(jobB.Order)}/evidence/{jobB.BeforePhoto}";
        var reads = new[] { "/billing-review/options", BillingSeed.Queue(), "/billing-review/export", BillingSeed.Detail(jobA.Order), evidenceA };
        var patch = new JsonObject { ["note"] = "hello", ["followUp"] = true };
        var post = BillingSeed.InvoiceBody(BillingSeed.Today("UTC"), acknowledge: true);

        // Read roles read every endpoint, no-store; the technician and anonymous callers are refused before any read.
        foreach (var cookie in new[] { owner, operations, viewer, dispatcherA, accountingA, accountingAll })
        {
            foreach (var path in reads)
            {
                var read = await host.SendAsync(HttpMethod.Get, path, cookie);

                Assert.Equal(HttpStatusCode.OK, read.StatusCode);
                Assert.Equal("no-store", read.Headers.CacheControl?.ToString());
            }
        }

        foreach (var path in reads)
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(HttpMethod.Get, path, technician)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await host.SendAsync(HttpMethod.Get, path, null)).StatusCode);
        }

        // Only owner and accounting act; everybody else gets 403 and nothing is written.
        foreach (var cookie in new[] { operations, viewer, dispatcherA, technician })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await BillingSeed.PatchReviewAsync(host, cookie, jobA.Order, patch)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await BillingSeed.PostInvoiceAsync(host, cookie, jobA.Order, post)).StatusCode);
        }

        Assert.Equal(stateA, await database.OrderStateAsync(jobA.Order));

        foreach (var (cookie, expected) in new[] { (owner, true), (accountingA, true), (viewer, false), (dispatcherA, false) })
        {
            Assert.Equal(expected, (await BillingSeed.ReadAsync(await host.SendAsync(HttpMethod.Get, "/billing-review/options", cookie)))["canAct"]!.GetValue<bool>());
            Assert.Equal(expected, (await BillingSeed.ReadAsync(await host.SendAsync(HttpMethod.Get, BillingSeed.Detail(jobA.Order), cookie)))["canAct"]!.GetValue<bool>());
        }

        // Branch scope: scoped Accounting and Dispatcher see branch A only; all-branch members see everything.
        var both = new[] { $"WO-{jobA.Number}", $"WO-{jobB.Number}" }.Order().ToArray();

        foreach (var cookie in new[] { owner, operations, viewer, accountingAll })
        {
            Assert.Equal(both, Numbers(await BillingSeed.ReadAsync(await host.SendAsync(HttpMethod.Get, BillingSeed.Queue(), cookie))));
        }

        foreach (var cookie in new[] { accountingA, dispatcherA })
        {
            Assert.Equal([$"WO-{jobA.Number}"], Numbers(await BillingSeed.ReadAsync(await host.SendAsync(HttpMethod.Get, BillingSeed.Queue(), cookie))));
            Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Get, BillingSeed.Detail(jobB.Order), cookie)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Get, evidenceB, cookie)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Get, BillingSeed.Queue($"branchId={world.BranchB}"), cookie)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Get, BillingSeed.Queue($"technicianId={techB}"), cookie)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Get, $"/billing-review/export?branchId={world.BranchB}", cookie)).StatusCode);
        }

        var scopedOptions = await BillingSeed.ReadAsync(await host.SendAsync(HttpMethod.Get, "/billing-review/options", accountingA));
        Assert.Equal([world.BranchA], scopedOptions["branches"]!.AsArray().Select(branch => branch!["id"]!.GetValue<Guid>()).ToArray());
        Assert.Equal([techA], scopedOptions["technicians"]!.AsArray().Select(item => item!["id"]!.GetValue<Guid>()).ToArray());
        Assert.Equal(
            2,
            (await BillingSeed.ReadAsync(await host.SendAsync(HttpMethod.Get, "/billing-review/options", owner)))["branches"]!.AsArray().Count);

        // Out-of-scope work orders cannot be changed by Accounting: identical 404 and no write.
        Assert.Equal(HttpStatusCode.NotFound, (await BillingSeed.PatchReviewAsync(host, accountingA, jobB.Order, patch)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await BillingSeed.PostInvoiceAsync(host, accountingA, jobB.Order, post)).StatusCode);

        // Another organization and random ids: the same 404, no foreign data, no write.
        var missing = await host.SendAsync(HttpMethod.Get, BillingSeed.Detail(Guid.NewGuid()), owner);
        var cross = await host.SendAsync(HttpMethod.Get, BillingSeed.Detail(jobA.Order), foreignOwner);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, cross.StatusCode);
        Assert.Equal(await WithoutTraceAsync(missing), await WithoutTraceAsync(cross));
        Assert.DoesNotContain("Alpha job", await cross.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Get, BillingSeed.Detail(foreignJob.Order), owner)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Get, evidenceA, foreignOwner)).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await host.SendAsync(HttpMethod.Get, $"{BillingSeed.Detail(foreignJob.Order)}/evidence/{foreignJob.BeforePhoto}", owner)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await BillingSeed.PatchReviewAsync(host, foreignOwner, jobA.Order, patch)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await BillingSeed.PostInvoiceAsync(host, foreignOwner, jobA.Order, post)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await BillingSeed.PatchReviewAsync(host, owner, foreignJob.Order, patch)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await BillingSeed.PostInvoiceAsync(host, owner, foreignJob.Order, post)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Get, BillingSeed.Queue($"branchId={foreign.BranchA}"), owner)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Get, BillingSeed.Queue($"technicianId={foreignTech}"), owner)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Get, BillingSeed.Queue($"branchId={Guid.NewGuid()}"), owner)).StatusCode);

        Assert.Equal(stateA, await database.OrderStateAsync(jobA.Order));
        Assert.Equal(stateB, await database.OrderStateAsync(jobB.Order));
        Assert.Equal(0, await database.InvoiceCountAsync(world.Org));
        Assert.Equal(0, await database.InvoiceCountAsync(foreign.Org));
    }
}
