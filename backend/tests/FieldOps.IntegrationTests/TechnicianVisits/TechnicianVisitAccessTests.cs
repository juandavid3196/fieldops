using System.Net;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.Dispatch;
using FieldOps.IntegrationTests.ServiceRequests;

namespace FieldOps.IntegrationTests.TechnicianVisits;

/// <summary>Roles, profile state, detail visibility and tenant isolation of the technician endpoints (AC-02, AC-03).</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class TechnicianVisitAccessTests(CompanySettingsDatabaseFixture database)
{
    private static readonly DateTimeOffset Noon = TechnicianVisitSeed.Utc("2026-06-10T12:00:00Z");

    [Fact]
    public async Task Detail_ReturnsOnlyTheCallersVisitAndIdentical404ForEverythingElse()
    {
        var world = await database.SeedWorldAsync();
        var foreign = await database.SeedWorldAsync();
        await using var host = TechnicianHost.Create(database);
        var me = await database.SeedTechnicianAsync(host, world);
        var other = await database.SeedTechnicianAsync(host, world, first: "Otto");
        var foreignTech = await database.SeedTechnicianAsync(host, foreign, first: "Fay");
        host.SetNow(Noon);
        var user = me.Member.UserId;

        var mine = await database.SeedJobAsync(world, user, me.Profile, "scheduled", Noon.AddDays(3), 45, "Mine job");
        await database.ExecuteAsync("UPDATE visits SET dispatch_note = 'Ring twice' WHERE id = @v", ("v", mine.Visit));
        var foreignVisit = await database.SeedJobAsync(world, user, other.Profile, "assigned", Noon, title: "Secret other job");
        var released = await database.SeedJobAsync(world, user, me.Profile, "assigned", Noon, title: "Secret released job");
        await database.UnassignAsync(released.Visit);
        var unscheduled = await database.SeedJobAsync(world, user, me.Profile, "unscheduled", Noon, title: "Secret unscheduled job");
        var cancelled = await database.SeedJobAsync(world, user, me.Profile, "cancelled", Noon, title: "Secret cancelled job");
        var otherOrg = await database.SeedJobAsync(foreign, foreignTech.Member.UserId, foreignTech.Profile, "assigned", Noon, title: "Secret foreign job");
        var before = await database.VisitStateAsync(mine.Visit);

        // Any date is allowed: the caller's visit three days ahead.
        var okResponse = await host.GetAsync($"/technician/visits/{mine.Visit}?technicianId={other.Profile}", me.Cookie);
        var detail = await TechnicianVisitSeed.ReadAsync(okResponse);
        Assert.Equal("no-store", okResponse.Headers.CacheControl?.ToString());
        Assert.Equal(mine.Visit, detail["visitId"]!.GetValue<Guid>());
        Assert.Equal("Mine job", detail["title"]!.GetValue<string>());
        Assert.Equal("scheduled", detail["status"]!.GetValue<string>());
        Assert.Equal("2026-06-13", detail["date"]!.GetValue<string>());
        Assert.Equal("UTC", detail["timezone"]!.GetValue<string>());
        Assert.Equal("Ring twice", detail["dispatchNote"]!.GetValue<string>());

        var rejected = new List<HttpResponseMessage>();

        foreach (var id in new[] { foreignVisit.Visit, released.Visit, unscheduled.Visit, cancelled.Visit, otherOrg.Visit, Guid.NewGuid() })
        {
            rejected.Add(await host.GetAsync($"/technician/visits/{id}", me.Cookie));
        }

        var bodies = new List<string>();

        foreach (var response in rejected)
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
            Assert.DoesNotContain("Secret", await response.Content.ReadAsStringAsync());
            bodies.Add(await TechnicianVisitSeed.WithoutTraceAsync(response));
        }

        Assert.Single(bodies.Distinct());
        Assert.DoesNotContain("\"code\"", bodies[0]);
        Assert.Equal(before, await database.VisitStateAsync(mine.Visit));
    }

    [Fact]
    public async Task Endpoints_RejectOtherRolesAndUnusableProfiles()
    {
        var world = await database.SeedWorldAsync();
        await using var host = TechnicianHost.Create(database);
        var technician = await database.SeedTechnicianAsync(host, world);
        var visit = await database.SeedJobAsync(world, technician.Member.UserId, technician.Profile, "assigned", DateTimeOffset.UtcNow, title: "Secret job");

        // Every role but technician is a 403 on both endpoints, with no visit data.
        foreach (var role in new[]
        {
            CompanySettingsDatabaseFixture.OwnerRoleId,
            CompanySettingsDatabaseFixture.OperationsManagerRoleId,
            CompanySettingsDatabaseFixture.DispatcherRoleId,
            CompanySettingsDatabaseFixture.ViewerRoleId,
            CompanySettingsDatabaseFixture.AccountingRoleId,
        })
        {
            var (cookie, _) = await host.SignInAsync(database, world.Org, role, "Role", "Holder");

            foreach (var path in new[] { "/technician/today", $"/technician/visits/{visit.Visit}" })
            {
                var response = await host.GetAsync(path, cookie);

                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
                Assert.DoesNotContain("Secret", await response.Content.ReadAsStringAsync());
            }
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await host.GetAsync("/technician/today", null)).StatusCode);

        // A technician account without a linked profile: 404 with a code, on both endpoints.
        var (unlinked, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.TechnicianRoleId, "Una", "Linked");

        foreach (var path in new[] { "/technician/today", $"/technician/visits/{visit.Visit}" })
        {
            var problem = await TechnicianVisitSeed.ReadAsync(await host.GetAsync(path, unlinked), HttpStatusCode.NotFound);
            Assert.Equal("technician_profile_not_linked", problem["code"]!.GetValue<string>());
        }

        // Inactive and suspended profiles: 403 technician_inactive, checked before any visit is read.
        foreach (var status in new[] { "inactive", "suspended" })
        {
            var blocked = await database.SeedTechnicianAsync(host, world, status, first: status);
            var job = await database.SeedJobAsync(world, blocked.Member.UserId, blocked.Profile, "assigned", DateTimeOffset.UtcNow, title: "Secret blocked job");

            foreach (var path in new[] { "/technician/today", $"/technician/visits/{job.Visit}" })
            {
                var response = await host.GetAsync(path, blocked.Cookie);
                var body = await response.Content.ReadAsStringAsync();

                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
                Assert.Contains("technician_inactive", body);
                Assert.DoesNotContain("Secret", body);
            }
        }
    }
}
