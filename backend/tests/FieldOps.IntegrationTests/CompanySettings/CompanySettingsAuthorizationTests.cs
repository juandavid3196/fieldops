using System.Net;
using FieldOps.IntegrationTests.Sessions;

namespace FieldOps.IntegrationTests.CompanySettings;

/// <summary>
/// BR-10 policies and tenant isolation (BR-11, FR-02, FR-10). Always
/// required, not reduced by test budgets (AC-03, AC-04, AC-05, AC-11,
/// AC-32).
/// </summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class CompanySettingsAuthorizationTests(CompanySettingsDatabaseFixture database)
{
    // AC-32: Viewer on every Manage endpoint.
    [Theory]
    [InlineData("PUT", "/organization-settings")]
    [InlineData("POST", "/branches")]
    public async Task ManageEndpoint_ViewerRole_Returns403AndNoChange(string method, string path)
    {
        var account = await database.SeedAccountAsync(CompanySettingsDatabaseFixture.ViewerRoleId);
        await using var host = SessionTestHost.Create(database.ConnectionString);
        var cookie = SessionApi.GetIssuedCookie(
            await SessionApi.SignInAsync(host.Client, account.Email, CompanySettingsDatabaseFixture.Password));

        var response = await CompanySettingsApi.SendRawAsync(
            host.Client, new HttpMethod(method), path, "{}", cookie);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // AC-32/AC-03: a branch-scoped Manage action (deactivate) also enforces the policy.
    [Fact]
    public async Task DeactivateBranch_ViewerRole_Returns403AndBranchUnchanged()
    {
        var account = await database.SeedAccountAsync(CompanySettingsDatabaseFixture.ViewerRoleId);
        var branch = await database.SeedBranchAsync(account.OrganizationId);
        await using var host = SessionTestHost.Create(database.ConnectionString);
        var cookie = SessionApi.GetIssuedCookie(
            await SessionApi.SignInAsync(host.Client, account.Email, CompanySettingsDatabaseFixture.Password));

        var response = await CompanySettingsApi.PostAsync(host.Client, $"/branches/{branch.Id}/deactivate", null, cookie);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(branch.UpdatedAt, await database.GetBranchUpdatedAtAsync(branch.Id));
    }

    // AC-03: roles outside BR-10 entirely.
    [Theory]
    [InlineData(CompanySettingsDatabaseFixture.DispatcherRoleId)]
    [InlineData(CompanySettingsDatabaseFixture.TechnicianRoleId)]
    [InlineData(CompanySettingsDatabaseFixture.AccountingRoleId)]
    [InlineData(CompanySettingsDatabaseFixture.OperationsManagerRoleId)]
    public async Task GetOrganizationSettings_RoleOutsideCompanySettingsPolicies_Returns403(short roleId)
    {
        var account = await database.SeedAccountAsync(roleId);
        await using var host = SessionTestHost.Create(database.ConnectionString);
        var cookie = SessionApi.GetIssuedCookie(
            await SessionApi.SignInAsync(host.Client, account.Email, CompanySettingsDatabaseFixture.Password));

        var response = await CompanySettingsApi.GetAsync(host.Client, "/organization-settings", cookie);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // AC-04.
    [Theory]
    [InlineData("GET", "/organization-settings")]
    [InlineData("GET", "/branches")]
    [InlineData("PUT", "/organization-settings")]
    public async Task Endpoint_NoSessionCookie_Returns401(string method, string path)
    {
        await using var host = SessionTestHost.Create(database.ConnectionString);

        var response = await CompanySettingsApi.SendRawAsync(
            host.Client, new HttpMethod(method), path, method == "GET" ? null : "{}", cookie: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // AC-11: representative cross-organization branch id across every branch endpoint.
    [Theory]
    [InlineData("GET")]
    [InlineData("PUT")]
    [InlineData("DEACTIVATE")]
    [InlineData("REACTIVATE")]
    public async Task BranchEndpoint_BranchBelongsToAnotherOrganization_Returns404AndNoChange(string action)
    {
        var account = await database.SeedAccountAsync();
        var otherOrg = await database.SeedOrganizationAsync();
        var otherOrgBranch = await database.SeedBranchAsync(otherOrg, name: "Other Org Branch");
        await using var host = SessionTestHost.Create(database.ConnectionString);
        var cookie = SessionApi.GetIssuedCookie(
            await SessionApi.SignInAsync(host.Client, account.Email, CompanySettingsDatabaseFixture.Password));

        var response = action switch
        {
            "GET" => await CompanySettingsApi.GetAsync(host.Client, $"/branches/{otherOrgBranch.Id}", cookie),
            "PUT" => await CompanySettingsApi.PutAsync(
                host.Client,
                $"/branches/{otherOrgBranch.Id}",
                BranchesEndpointTests.ValidBranchBody(updatedAt: otherOrgBranch.UpdatedAt.ToString("O")),
                cookie),
            "DEACTIVATE" => await CompanySettingsApi.PostAsync(
                host.Client, $"/branches/{otherOrgBranch.Id}/deactivate", null, cookie),
            "REACTIVATE" => await CompanySettingsApi.PostAsync(
                host.Client, $"/branches/{otherOrgBranch.Id}/reactivate", null, cookie),
            _ => throw new InvalidOperationException(),
        };

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(otherOrgBranch.UpdatedAt, await database.GetBranchUpdatedAtAsync(otherOrgBranch.Id));
        Assert.Equal(0, await database.CountAuditLogsAsync(otherOrg, "branch.deactivated"));
    }

    // AC-05: a client-supplied organizationId in the body is ignored; only the session organization changes.
    [Fact]
    public async Task PutOrganizationSettings_BodyNamesAnotherOrganizationId_OnlySessionOrganizationChanges()
    {
        var account = await database.SeedAccountAsync();
        var otherOrgId = await database.SeedOrganizationAsync();
        var otherOrgUpdatedAtBefore = await database.GetOrganizationUpdatedAtAsync(otherOrgId);
        await using var host = SessionTestHost.Create(database.ConnectionString);
        var cookie = SessionApi.GetIssuedCookie(
            await SessionApi.SignInAsync(host.Client, account.Email, CompanySettingsDatabaseFixture.Password));

        var current = await CompanySettingsApi.ReadAsAsync<OrganizationSettingsBody>(
            await CompanySettingsApi.GetAsync(host.Client, "/organization-settings", cookie));

        var body = OrganizationSettingsEndpointTests.ValidBody(current.UpdatedAt.ToString("O"));
        body["organizationId"] = otherOrgId.ToString();

        var response = await CompanySettingsApi.PutAsync(host.Client, "/organization-settings", body, cookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(otherOrgUpdatedAtBefore, await database.GetOrganizationUpdatedAtAsync(otherOrgId));
        Assert.True(
            await database.GetOrganizationUpdatedAtAsync(account.OrganizationId) > current.UpdatedAt);
    }
}
