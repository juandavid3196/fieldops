using System.Net;
using FieldOps.IntegrationTests.CompanySettings;

namespace FieldOps.IntegrationTests.Users;

/// <summary>GET /users and /users/summary (FR-02, FR-03): AC-02, AC-03 and the list/summary half of AC-16.</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class UsersListTests(CompanySettingsDatabaseFixture database)
{
    [Fact]
    public async Task ListAndSummary_FilterSortPageAndStayInsideTheSessionOrganization()
    {
        const short owner = CompanySettingsDatabaseFixture.OwnerRoleId;
        const short dispatcher = CompanySettingsDatabaseFixture.DispatcherRoleId;
        const short technician = CompanySettingsDatabaseFixture.TechnicianRoleId;
        const short accounting = CompanySettingsDatabaseFixture.AccountingRoleId;
        const short viewer = CompanySettingsDatabaseFixture.ViewerRoleId;

        var org = await database.SeedOrganizationAsync();
        var alpha = await database.SeedBranchAsync(org, name: "Alpha");
        var beta = await database.SeedBranchAsync(org, name: "Beta");

        var olivia = await database.SeedMemberAsync(
            org, owner, "Olivia", "Owner", lastLoginAt: DateTimeOffset.UtcNow.AddHours(-2));
        var amy = await database.SeedMemberAsync(org, dispatcher, "Amy", "Dispatcher", isAllBranches: false);
        await database.LinkMembershipToBranchAsync(amy.MembershipId, alpha.Id);
        await database.SeedTechnicianProfileAsync(org, alpha.Id, amy.MembershipId, "Amy", "Dee");
        var bob = await database.SeedMemberAsync(org, technician, "Bob", "Technician", isAllBranches: false);
        await database.LinkMembershipToBranchAsync(bob.MembershipId, beta.Id);
        await database.SeedMemberAsync(org, accounting, "Cara", "Accounting", status: "suspended");
        await database.SeedMemberAsync(org, viewer, "Dan", "Pending", status: "pending");
        await database.SeedMemberAsync(org, viewer, "Eve", "Disabled", status: "disabled");

        var future = DateTimeOffset.UtcNow.AddDays(5);
        await database.SeedInvitationAsync(
            org, olivia.UserId, "Fay", "Invitee", "fay@example.com", dispatcher, future,
            isAllBranches: false, branchIds: [alpha.Id]);
        await database.SeedInvitationAsync(
            org, olivia.UserId, "Gus", "Expired", "gus@example.com", accounting, DateTimeOffset.UtcNow.AddDays(-1));
        await database.SeedInvitationAsync(
            org, olivia.UserId, "Hal", "Accepted", "hal@example.com", viewer, future, accepted: true);
        await database.SeedInvitationAsync(
            org, olivia.UserId, "Ivy", "Revoked", "ivy@example.com", viewer, future, revoked: true);

        for (var i = 1; i <= 5; i++)
        {
            await database.SeedInvitationAsync(
                org, olivia.UserId, $"Pager0{i}", "Invitee", $"pager0{i}@example.com", viewer, future);
        }

        var otherOrg = await database.SeedOrganizationAsync();
        var otherOwner = await database.SeedMemberAsync(otherOrg, owner, "Zoe", "Other");
        var otherViewer = await database.SeedMemberAsync(otherOrg, viewer, "Vic", "Viewer");
        await database.SeedMemberAsync(otherOrg, dispatcher, "Amy", "Elsewhere", status: "suspended");
        await database.SeedInvitationAsync(otherOrg, otherOwner.UserId, "Oli", "Other", "oli@example.com", viewer, future);

        await using var host = UsersHost.Create(database);
        var cookie = await host.SignInAsync(olivia.Email);

        // Default: members and open invitations (expired included), sorted by name, ten per page.
        var response = await host.GetAsync("/users", cookie);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString(), StringComparison.Ordinal);
        var first = await host.ReadAsync(response);
        Assert.Equal(11, first["totalCount"]!.GetValue<int>());
        Assert.Equal(10, first["pageSize"]!.GetValue<int>());
        Assert.Equal(
            ["Amy Dispatcher", "Bob Technician", "Cara Accounting", "Fay Invitee", "Gus Expired", "Olivia Owner"],
            [.. first["items"]!.AsArray().Take(6).Select(item => $"{item!["firstName"]} {item["lastName"]}")]);
        Assert.Equal(10, first["items"]!.AsArray().Count);

        var second = await host.ReadAsync(await host.GetAsync("/users?page=2", cookie));
        Assert.Equal(["pager05@example.com"], UsersSeed.Items(second));

        var descending = await host.ReadAsync(await host.GetAsync("/users?sort=-name", cookie));
        Assert.Equal("pager05@example.com", UsersSeed.Items(descending)[0]);

        // Row shape (BR-04).
        var all = await host.ReadAsync(await host.GetAsync("/users?search=%20%20", cookie));
        var rows = all["items"]!.AsArray().ToDictionary(item => item!["email"]!.GetValue<string>(), item => item!);
        var amyRow = rows[amy.Email];
        Assert.Equal("member", amyRow["kind"]!.GetValue<string>());
        Assert.Equal("active", amyRow["status"]!.GetValue<string>());
        Assert.Equal("dispatcher", amyRow["roleCode"]!.GetValue<string>());
        Assert.False(amyRow["isAllBranches"]!.GetValue<bool>());
        Assert.Equal(["Alpha"], amyRow["branches"]!.AsArray().Select(b => b!["name"]!.GetValue<string>()).ToArray());
        Assert.True(amyRow["teamProfile"]!["applicable"]!.GetValue<bool>());
        Assert.Equal("Amy Dee", amyRow["teamProfile"]!["name"]!.GetValue<string>());
        Assert.Null(amyRow["lastActiveAt"]);
        Assert.False(amyRow["isCurrentUser"]!.GetValue<bool>());

        var bobRow = rows[bob.Email];
        Assert.True(bobRow["teamProfile"]!["applicable"]!.GetValue<bool>());
        Assert.Null(bobRow["teamProfile"]!["name"]);

        var oliviaRow = rows[olivia.Email];
        Assert.True(oliviaRow["isCurrentUser"]!.GetValue<bool>());
        Assert.True(oliviaRow["isLastOwner"]!.GetValue<bool>());
        Assert.True(oliviaRow["isAllBranches"]!.GetValue<bool>());
        Assert.False(oliviaRow["teamProfile"]!["applicable"]!.GetValue<bool>());
        Assert.NotNull(oliviaRow["lastActiveAt"]);

        var expiredRow = rows["gus@example.com"];
        Assert.Equal("invitation", expiredRow["kind"]!.GetValue<string>());
        Assert.Equal("pending_invitation", expiredRow["status"]!.GetValue<string>());
        Assert.True(expiredRow["isExpired"]!.GetValue<bool>());
        Assert.Null(expiredRow["lastActiveAt"]);
        Assert.False(rows["fay@example.com"]["isExpired"]!.GetValue<bool>());
        Assert.True(rows["fay@example.com"]["teamProfile"]!["applicable"]!.GetValue<bool>());

        // Filters (parameterized by table) combine with AND.
        var cases = new (string Query, string[] Expected)[]
        {
            ("status=suspended", ["Cara"]),
            ("status=pending_invitation&search=fay", ["Fay"]),
            ("status=active", ["Amy", "Bob", "Olivia"]),
            ("roleCode=dispatcher", ["Amy", "Fay"]),
            ("branchId=" + alpha.Id, ["Amy", "Cara", "Fay", "Gus", "Olivia", "Pager01", "Pager02", "Pager03", "Pager04", "Pager05"]),
            ("roleCode=dispatcher&branchId=" + beta.Id, []),
            ("search=%20BOB%20", ["Bob"]),
            ("search=amy%20disp", ["Amy"]),
            ("search=gus@EXAMPLE", ["Gus"]),
        };

        foreach (var (query, expected) in cases)
        {
            var body = await host.ReadAsync(await host.GetAsync($"/users?{query}", cookie));
            var names = body["items"]!.AsArray().Select(item => item!["firstName"]!.GetValue<string>()).ToArray();
            Assert.True(expected.SequenceEqual(names), $"{query}: expected [{string.Join(",", expected)}] got [{string.Join(",", names)}]");
            Assert.Equal(expected.Length, body["totalCount"]!.GetValue<int>());
        }

        // Invalid query values name their key.
        foreach (var (query, key) in new[]
        {
            ("roleCode=admin", "roleCode"),
            ("status=zzz", "status"),
            ("sort=email", "sort"),
            ("branchId=not-a-guid", "branchId"),
            ("pageSize=20", "pageSize"),
            ("page=0", "page"),
        })
        {
            var invalid = await host.GetAsync($"/users?{query}", cookie);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            var problem = (await host.ReadAsync(invalid)).ToJsonString();
            Assert.True(problem.Contains($"\"{key}\"", StringComparison.Ordinal), $"{query}: {problem}");
        }

        // Summary is organization-wide and ignores filters (BR-05).
        var summary = await host.ReadAsync(await host.GetAsync("/users/summary?status=suspended&roleCode=viewer", cookie));
        Assert.Equal(3, summary["activeUsers"]!.GetValue<int>());
        Assert.Equal(7, summary["pendingInvitations"]!.GetValue<int>());
        Assert.Equal(1, summary["suspendedUsers"]!.GetValue<int>());
        Assert.Equal(1, summary["owners"]!.GetValue<int>());

        // Organization B (Viewer) sees only its own rows and counts.
        var viewerCookie = await host.SignInAsync(otherViewer.Email);
        var otherList = await host.ReadAsync(await host.GetAsync("/users", viewerCookie));
        Assert.Equal(4, otherList["totalCount"]!.GetValue<int>());
        Assert.DoesNotContain(UsersSeed.Items(otherList), email => email == olivia.Email || email == amy.Email);
        var otherSummary = await host.ReadAsync(await host.GetAsync("/users/summary", viewerCookie));
        Assert.Equal(2, otherSummary["activeUsers"]!.GetValue<int>());
        Assert.Equal(1, otherSummary["pendingInvitations"]!.GetValue<int>());
        Assert.Equal(1, otherSummary["suspendedUsers"]!.GetValue<int>());
        Assert.Equal(1, otherSummary["owners"]!.GetValue<int>());
    }
}
