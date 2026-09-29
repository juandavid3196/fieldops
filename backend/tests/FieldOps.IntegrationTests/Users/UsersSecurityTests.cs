using System.Net;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.CompanySettings;

namespace FieldOps.IntegrationTests.Users;

/// <summary>Role matrix, permission catalog and tenant isolation (FR-05, FR-14): AC-05, AC-15, AC-16.</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class UsersSecurityTests(CompanySettingsDatabaseFixture database)
{
    private static readonly (string Method, string Path, bool IsRead)[] Endpoints =
    [
        ("GET", "/users", true),
        ("GET", "/users/summary", true),
        ("GET", "/permission-matrix", true),
        ("POST", "/users/invitations", false),
        ("POST", "/users/invitations/{invitation}/resend", false),
        ("POST", "/users/invitations/{invitation}/revoke", false),
        ("PUT", "/users/invitations/{invitation}/access", false),
        ("PUT", "/users/{member}/access", false),
        ("POST", "/users/{member}/suspend", false),
        ("POST", "/users/{member}/reactivate", false),
    ];

    // AC-15 (and the AC-05 catalog for the two roles that may read it).
    [Theory]
    [InlineData("owner")]
    [InlineData("viewer")]
    [InlineData("dispatcher")]
    [InlineData("technician")]
    [InlineData("accounting")]
    [InlineData("operations_manager")]
    [InlineData("none")]
    public async Task Endpoints_RoleMatrix_OwnerAllowedViewerReadsOnlyOthersForbiddenNoSessionUnauthorized(string role)
    {
        var org = await database.SeedOrganizationAsync();
        var seeder = await database.SeedMemberAsync(org, CompanySettingsDatabaseFixture.OwnerRoleId, "Sid", "Seeder");
        var target = await database.SeedMemberAsync(org, CompanySettingsDatabaseFixture.ViewerRoleId, "Tara", "Target");
        var matrixHash = $"matrix-{Guid.NewGuid():N}";
        var invitation = await database.SeedInvitationAsync(
            org, seeder.UserId, "Ivan", "Invitee", "ivan@example.com", CompanySettingsDatabaseFixture.ViewerRoleId,
            DateTimeOffset.UtcNow.AddDays(3), tokenHash: matrixHash);

        await using var host = UsersHost.Create(database);
        string? cookie = null;

        if (role != "none")
        {
            var roleId = role switch
            {
                "owner" => CompanySettingsDatabaseFixture.OwnerRoleId,
                "viewer" => CompanySettingsDatabaseFixture.ViewerRoleId,
                "dispatcher" => CompanySettingsDatabaseFixture.DispatcherRoleId,
                "technician" => CompanySettingsDatabaseFixture.TechnicianRoleId,
                "accounting" => CompanySettingsDatabaseFixture.AccountingRoleId,
                _ => CompanySettingsDatabaseFixture.OperationsManagerRoleId,
            };
            var account = await database.SeedMemberAsync(org, roleId, "Rae", "Role");
            cookie = await host.SignInAsync(account.Email);
        }

        foreach (var (method, template, isRead) in Endpoints)
        {
            // The owner uses unknown ids so its allowed calls change nothing.
            var path = template
                .Replace("{invitation}", role == "owner" ? Guid.NewGuid().ToString() : invitation.ToString(), StringComparison.Ordinal)
                .Replace("{member}", role == "owner" ? Guid.NewGuid().ToString() : target.MembershipId.ToString(), StringComparison.Ordinal);

            var response = await CompanySettingsApi.SendRawAsync(
                host.Client, new HttpMethod(method), path, method == "GET" ? null : "{}", cookie);

            var expected = role switch
            {
                "none" => HttpStatusCode.Unauthorized,
                "owner" when isRead => HttpStatusCode.OK,
                "owner" => path.EndsWith("/resend", StringComparison.Ordinal)
                    || path.EndsWith("/revoke", StringComparison.Ordinal)
                    || path.EndsWith("/suspend", StringComparison.Ordinal)
                    || path.EndsWith("/reactivate", StringComparison.Ordinal)
                        ? HttpStatusCode.NotFound
                        : HttpStatusCode.BadRequest,
                "viewer" when isRead => HttpStatusCode.OK,
                _ => HttpStatusCode.Forbidden,
            };

            Assert.True(expected == response.StatusCode, $"{role} {method} {template}: expected {expected} got {response.StatusCode}");

            if (response.StatusCode == HttpStatusCode.OK)
            {
                Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.Ordinal);
            }

            if (path == "/permission-matrix" && response.StatusCode == HttpStatusCode.OK)
            {
                AssertCatalog(await host.ReadAsync(response));
            }
        }

        // Rejected mutations changed nothing.
        Assert.Equal("active", await database.TextAsync("SELECT status::text FROM organization_users WHERE id = @i", ("i", target.MembershipId)));
        Assert.Equal(matrixHash, await database.TextAsync("SELECT token_hash FROM user_invitations WHERE id = @i", ("i", invitation)));
        Assert.Equal(0, await database.CountAsync("SELECT COUNT(*) FROM user_invitations WHERE id = @i AND revoked_at IS NOT NULL", ("i", invitation)));
        Assert.Equal(0, await database.CountAsync("SELECT COUNT(*) FROM audit_logs WHERE organization_id = @o AND action LIKE 'user.%'", ("o", org)));
        Assert.Empty(host.Delivery.Messages);
    }

    // AC-16: another organization's ids are 404, its branches are 400, and nothing there changes.
    [Fact]
    public async Task Endpoints_OtherOrganizationsIdsAndBranches_Return404And400WithoutChange()
    {
        var orgA = await database.SeedOrganizationAsync();
        var orgB = await database.SeedOrganizationAsync();
        var ownerA = await database.SeedMemberAsync(orgA, CompanySettingsDatabaseFixture.OwnerRoleId, "Alma", "Owner");
        var ownerB = await database.SeedMemberAsync(orgB, CompanySettingsDatabaseFixture.OwnerRoleId, "Boris", "Owner");
        var memberB = await database.SeedMemberAsync(orgB, CompanySettingsDatabaseFixture.DispatcherRoleId, "Bea", "Member", isAllBranches: false);
        var branchB = await database.SeedBranchAsync(orgB, name: "Bravo");
        var bHash = $"b-{Guid.NewGuid():N}";
        var invitationB = await database.SeedInvitationAsync(
            orgB, ownerB.UserId, "Bob", "Invitee", "bob@example.com", CompanySettingsDatabaseFixture.ViewerRoleId,
            DateTimeOffset.UtcNow.AddDays(3), tokenHash: bHash);
        var memberA = await database.SeedMemberAsync(orgA, CompanySettingsDatabaseFixture.DispatcherRoleId, "Ana", "Member", isAllBranches: false);

        await using var host = UsersHost.Create(database);
        var cookie = await host.SignInAsync(ownerA.Email);
        var access = UsersSeed.Body(("roleCode", "viewer"), ("isAllBranches", true));

        var calls = new (string Method, string Path, JsonObject? Body)[]
        {
            ("POST", $"/users/invitations/{invitationB}/resend", null),
            ("POST", $"/users/invitations/{invitationB}/revoke", null),
            ("PUT", $"/users/invitations/{invitationB}/access", access),
            ("PUT", $"/users/{memberB.MembershipId}/access", access),
            ("POST", $"/users/{memberB.MembershipId}/suspend", null),
            ("POST", $"/users/{memberB.MembershipId}/reactivate", null),
        };

        foreach (var (method, path, body) in calls)
        {
            var response = await CompanySettingsApi.SendRawAsync(
                host.Client, new HttpMethod(method), path, body?.ToJsonString() ?? (method == "PUT" ? "{}" : null), cookie);
            Assert.True(response.StatusCode == HttpStatusCode.NotFound, $"{method} {path} returned {response.StatusCode}");
        }

        // A's other organization's branch is rejected for invite and edit access.
        var invite = await host.PostAsync("/users/invitations", cookie, UsersSeed.Body(
            ("email", "x@example.com"), ("firstName", "X"), ("lastName", "Y"), ("roleCode", "dispatcher"),
            ("isAllBranches", false), ("branchIds", new[] { branchB.Id })));
        Assert.Equal(HttpStatusCode.BadRequest, invite.StatusCode);
        Assert.NotNull((await host.ReadAsync(invite))["errors"]!["branchIds"]);
        var edit = await host.PutAsync($"/users/{memberA.MembershipId}/access", cookie, UsersSeed.Body(
            ("roleCode", "dispatcher"), ("isAllBranches", false), ("branchIds", new[] { branchB.Id })));
        Assert.Equal(HttpStatusCode.BadRequest, edit.StatusCode);

        // Organization B is untouched.
        Assert.Equal("active", await database.TextAsync("SELECT status::text FROM organization_users WHERE id = @i", ("i", memberB.MembershipId)));
        Assert.Equal(CompanySettingsDatabaseFixture.DispatcherRoleId, await database.ScalarAsync<short>("SELECT role_id FROM organization_users WHERE id = @i", ("i", memberB.MembershipId)));
        Assert.Equal(bHash, await database.TextAsync("SELECT token_hash FROM user_invitations WHERE id = @i", ("i", invitationB)));
        Assert.Equal(0, await database.CountAsync("SELECT COUNT(*) FROM user_invitations WHERE id = @i AND revoked_at IS NOT NULL", ("i", invitationB)));
        Assert.Equal(0, await database.CountAsync("SELECT COUNT(*) FROM audit_logs WHERE organization_id IN (@a, @b) AND action LIKE 'user.%'", ("a", orgA), ("b", orgB)));
        Assert.Empty(host.Delivery.Messages);
    }

    private static void AssertCatalog(JsonNode matrix)
    {
        var roles = matrix["roles"]!.AsArray();
        Assert.Equal(
            ["owner", "operations_manager", "dispatcher", "technician", "accounting", "viewer"],
            [.. roles.Select(role => role!["code"]!.GetValue<string>())]);
        Assert.Equal(
            ["Owner", "Operations Manager", "Dispatcher", "Technician", "Accounting", "Viewer"],
            [.. roles.Select(role => role!["name"]!.GetValue<string>())]);
        Assert.All(roles, role => Assert.False(string.IsNullOrWhiteSpace(role!["summary"]!.GetValue<string>())));
        Assert.Equal(
            [true, true, false, false, false, false],
            [.. roles.Select(role => role!["forcesAllBranches"]!.GetValue<bool>())]);
        Assert.Equal(
            [false, true, true, true, false, false],
            [.. roles.Select(role => role!["hasTeamProfile"]!.GetValue<bool>())]);

        var modules = matrix["modules"]!.AsArray();
        Assert.Equal(9, modules.Count);
        Assert.Equal(
            ["Overview", "Customers", "Requests & quotes", "Work orders & schedule", "Invoices & payments", "Reports", "Team", "Company settings", "Audit log"],
            [.. modules.Select(module => module!["name"]!.GetValue<string>())]);

        string Level(string moduleName, string roleCode) =>
            modules.Single(module => module!["name"]!.GetValue<string>() == moduleName)!["levels"]![roleCode]!["level"]!.GetValue<string>();

        Assert.Equal("view_if_granted", Level("Overview", "viewer"));
        Assert.Equal("assigned_only", Level("Customers", "technician"));
        Assert.Equal("completed_only", Level("Work orders & schedule", "accounting"));
        Assert.Equal("edit", Level("Invoices & payments", "accounting"));
        Assert.Equal("financial_only", Level("Reports", "accounting"));
        Assert.Equal("own_profile", Level("Team", "technician"));
        Assert.Equal("none", Level("Company settings", "operations_manager"));
        Assert.Equal("financial_only", Level("Audit log", "accounting"));
        Assert.Equal("Own profile", modules.Single(m => m!["name"]!.GetValue<string>() == "Team")!["levels"]!["technician"]!["label"]!.GetValue<string>());
    }
}
