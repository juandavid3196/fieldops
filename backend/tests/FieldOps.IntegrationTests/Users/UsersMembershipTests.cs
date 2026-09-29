using System.Net;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.Sessions;

namespace FieldOps.IntegrationTests.Users;

/// <summary>Suspend, reactivate and the last-Owner rules (FR-11 to FR-13): AC-13, AC-14, BR-15, BR-18.</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class UsersMembershipTests(CompanySettingsDatabaseFixture database)
{
    private const short Owner = CompanySettingsDatabaseFixture.OwnerRoleId;
    private const short Dispatcher = CompanySettingsDatabaseFixture.DispatcherRoleId;

    [Fact]
    public async Task SuspendAndReactivate_EndTheSessionKeepDataAuditOnceAndAreIdempotent()
    {
        var org = await database.SeedOrganizationAsync();
        var otherOrg = await database.SeedOrganizationAsync();
        var alpha = await database.SeedBranchAsync(org, name: "Alpha");
        var actor = await database.SeedMemberAsync(org, Owner, "Olivia", "Owner");
        var target = await database.SeedMemberAsync(org, Dispatcher, "Dee", "Spatch", isAllBranches: false);
        await database.LinkMembershipToBranchAsync(target.MembershipId, alpha.Id);
        await database.SeedTechnicianProfileAsync(org, alpha.Id, target.MembershipId, "Dee", "Tech");
        var otherOrgMembership = await database.SeedMemberAsync(
            otherOrg, Dispatcher, "Dee", "Spatch", existingUserId: target.UserId);

        await using var host = UsersHost.Create(database);
        var ownerCookie = await host.SignInAsync(actor.Email);
        var targetCookie = await host.SignInAsync(target.Email);
        Assert.Equal(HttpStatusCode.OK, (await SessionApi.GetCurrentAsync(host.Client, targetCookie)).StatusCode);

        var statusOf = (Guid id) => database.TextAsync("SELECT status::text FROM organization_users WHERE id = @i", ("i", id));

        Assert.Equal(HttpStatusCode.NoContent, (await host.PostAsync($"/users/{target.MembershipId}/suspend", ownerCookie)).StatusCode);
        Assert.Equal("suspended", await statusOf(target.MembershipId));
        Assert.Equal(1, await database.CountAuditAsync(org, "user.suspended"));
        var (before, after, _) = await database.GetLatestAuditAsync(org, "user.suspended");
        Assert.Equal("active", System.Text.Json.Nodes.JsonNode.Parse(before!)!["status"]!.GetValue<string>());
        Assert.Equal("suspended", System.Text.Json.Nodes.JsonNode.Parse(after!)!["status"]!.GetValue<string>());

        // BR-18: the suspended member's next request is 401 and clears the cookie.
        var rejected = await SessionApi.GetCurrentAsync(host.Client, targetCookie);
        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        SessionApi.AssertCookieDeleted(rejected);

        // Only the session organization's membership changes; nothing is deleted.
        Assert.Equal("active", await statusOf(otherOrgMembership.MembershipId));
        Assert.Equal(1, await database.CountAsync("SELECT COUNT(*) FROM organization_user_branches WHERE organization_user_id = @m", ("m", target.MembershipId)));
        Assert.Equal(1, await database.CountAsync("SELECT COUNT(*) FROM technician_profiles WHERE organization_user_id = @m", ("m", target.MembershipId)));

        // Idempotent: no second audit row.
        Assert.Equal(HttpStatusCode.NoContent, (await host.PostAsync($"/users/{target.MembershipId}/suspend", ownerCookie)).StatusCode);
        Assert.Equal(1, await database.CountAuditAsync(org, "user.suspended"));

        var summary = await host.ReadAsync(await host.GetAsync("/users/summary", ownerCookie));
        Assert.Equal(1, summary["suspendedUsers"]!.GetValue<int>());
        Assert.Equal(1, summary["activeUsers"]!.GetValue<int>());

        Assert.Equal(HttpStatusCode.NoContent, (await host.PostAsync($"/users/{target.MembershipId}/reactivate", ownerCookie)).StatusCode);
        Assert.Equal("active", await statusOf(target.MembershipId));
        Assert.Equal(1, await database.CountAuditAsync(org, "user.reactivated"));
        Assert.Equal(HttpStatusCode.NoContent, (await host.PostAsync($"/users/{target.MembershipId}/reactivate", ownerCookie)).StatusCode);
        Assert.Equal(1, await database.CountAuditAsync(org, "user.reactivated"));

        var signedInAgain = await host.SignInAsync(target.Email);
        Assert.Equal(HttpStatusCode.OK, (await SessionApi.GetCurrentAsync(host.Client, signedInAgain)).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await host.PostAsync($"/users/{Guid.NewGuid()}/suspend", ownerCookie)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.PostAsync($"/users/{Guid.NewGuid()}/reactivate", ownerCookie)).StatusCode);
    }

    [Fact]
    public async Task LastOwnerRules_BlockSuspendAndDowngradeAndNeverLeaveZeroOwnersUnderConcurrency()
    {
        var org = await database.SeedOrganizationAsync();
        var first = await database.SeedMemberAsync(org, Owner, "Olga", "Alpha");
        await using var host = UsersHost.Create(database);
        var firstCookie = await host.SignInAsync(first.Email);
        var activeOwners = (Guid organization) => database.CountAsync(
            "SELECT COUNT(*) FROM organization_users ou JOIN roles r ON r.id = ou.role_id WHERE ou.organization_id = @o AND r.code = 'owner' AND ou.status = 'active'",
            ("o", organization));
        var access = (string role) => UsersSeed.Body(("roleCode", role), ("isAllBranches", true));

        // Sole Owner: self-suspend and self-downgrade are blocked with their own messages.
        var self = await host.PostAsync($"/users/{first.MembershipId}/suspend", firstCookie);
        Assert.Equal(HttpStatusCode.Conflict, self.StatusCode);
        Assert.Equal("You can't suspend your own access.", (await host.ReadAsync(self))["title"]!.GetValue<string>());
        var downgrade = await host.PutAsync($"/users/{first.MembershipId}/access", firstCookie, access("dispatcher"));
        Assert.Equal(HttpStatusCode.Conflict, downgrade.StatusCode);
        Assert.Equal("The last Owner can't be suspended or downgraded.", (await host.ReadAsync(downgrade))["title"]!.GetValue<string>());
        Assert.Equal(1, await activeOwners(org));
        Assert.Equal(0, await database.CountAuditAsync(org, "user.access_updated") + await database.CountAuditAsync(org, "user.suspended"));
        var list = await host.ReadAsync(await host.GetAsync("/users", firstCookie));
        Assert.True(list["items"]![0]!["isLastOwner"]!.GetValue<bool>());

        // Two Owners: one can be downgraded or suspended; the remaining one is then protected.
        var second = await database.SeedMemberAsync(org, Owner, "Owen", "Beta");
        var secondCookie = await host.SignInAsync(second.Email);
        Assert.Equal(HttpStatusCode.OK, (await host.PutAsync($"/users/{second.MembershipId}/access", firstCookie, access("dispatcher"))).StatusCode);
        Assert.Equal(1, await activeOwners(org));
        Assert.Equal(HttpStatusCode.Conflict, (await host.PutAsync($"/users/{first.MembershipId}/access", firstCookie, access("viewer"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.PutAsync($"/users/{second.MembershipId}/access", firstCookie, access("owner"))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await host.PostAsync($"/users/{second.MembershipId}/suspend", firstCookie)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await host.PostAsync($"/users/{second.MembershipId}/reactivate", firstCookie)).StatusCode);
        Assert.Equal(2, await activeOwners(org));

        // Concurrency: two Owners change each other at once; never zero active Owners.
        for (var round = 0; round < 4; round++)
        {
            var raceOrg = await database.SeedOrganizationAsync();
            var a = await database.SeedMemberAsync(raceOrg, Owner, "Ann", $"Race{round}");
            var b = await database.SeedMemberAsync(raceOrg, Owner, "Bea", $"Race{round}");
            var aCookie = await host.SignInAsync(a.Email);
            var bCookie = await host.SignInAsync(b.Email);
            var suspendRound = round % 2 == 1;

            var results = await Task.WhenAll(
                suspendRound
                    ? host.PostAsync($"/users/{b.MembershipId}/suspend", aCookie)
                    : host.PutAsync($"/users/{b.MembershipId}/access", aCookie, access("viewer")),
                suspendRound
                    ? host.PostAsync($"/users/{a.MembershipId}/suspend", bCookie)
                    : host.PutAsync($"/users/{a.MembershipId}/access", bCookie, access("viewer")));

            var statuses = results.Select(r => r.StatusCode).ToArray();
            Assert.Equal(1, statuses.Count(status => status is HttpStatusCode.OK or HttpStatusCode.NoContent));
            Assert.All(
                statuses.Where(status => status is not (HttpStatusCode.OK or HttpStatusCode.NoContent)),
                status => Assert.Contains(status, new[] { HttpStatusCode.Conflict, HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized }));
            Assert.Equal(1, await activeOwners(raceOrg));
        }
    }
}
