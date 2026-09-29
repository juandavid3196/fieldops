using System.Net;
using FieldOps.IntegrationTests.Sessions;

namespace FieldOps.IntegrationTests.CompanySettings;

/// <summary>
/// Authorization and tenant isolation of the endpoints added by company setup
/// completion (AC-20 API, AC-21). Always required, not reduced by test budgets.
/// </summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class CompanySetupAuthorizationTests(CompanySettingsDatabaseFixture database)
{
    public static IEnumerable<object[]> RoleMatrix()
    {
        short[] roles =
        [
            CompanySettingsDatabaseFixture.ViewerRoleId,
            CompanySettingsDatabaseFixture.DispatcherRoleId,
            CompanySettingsDatabaseFixture.TechnicianRoleId,
            CompanySettingsDatabaseFixture.AccountingRoleId,
            CompanySettingsDatabaseFixture.OperationsManagerRoleId,
            0,
        ];

        foreach (var endpoint in new[] { "GET-LOGO", "PUT-LOGO", "DELETE-LOGO", "SET-MAIN" })
        {
            foreach (var role in roles)
            {
                var expected = role == 0
                    ? HttpStatusCode.Unauthorized
                    : endpoint == "GET-LOGO" && role == CompanySettingsDatabaseFixture.ViewerRoleId
                        ? HttpStatusCode.OK
                        : HttpStatusCode.Forbidden;

                yield return [endpoint, role, expected];
            }
        }
    }

    [Theory]
    [MemberData(nameof(RoleMatrix))]
    public async Task NewEndpoint_RoleOrNoSession_ReturnsExpectedStatusAndChangesNothing(
        string endpoint, short roleId, HttpStatusCode expected)
    {
        var account = await database.SeedAccountAsync(roleId == 0 ? CompanySettingsDatabaseFixture.OwnerRoleId : roleId);
        var branch = await database.SeedBranchAsync(account.OrganizationId, name: "Target");
        var logo = OrganizationLogoEndpointTests.Png("ORIGINAL");
        await database.SeedLogoAsync(account.OrganizationId, logo);
        await using var host = SessionTestHost.Create(database.ConnectionString);
        var cookie = roleId == 0 ? null : await CompanySettingsApi.SignInCookieAsync(host.Client, account.Email);

        var response = endpoint switch
        {
            "GET-LOGO" => await CompanySettingsApi.GetAsync(host.Client, "/organization-settings/logo", cookie),
            "PUT-LOGO" => await CompanySettingsApi.PutFileAsync(
                host.Client,
                "/organization-settings/logo",
                OrganizationLogoEndpointTests.Png("REPLACEMENT"),
                "image/png",
                cookie),
            "DELETE-LOGO" => await CompanySettingsApi.SendRawAsync(
                host.Client, HttpMethod.Delete, "/organization-settings/logo", null, cookie),
            _ => await CompanySettingsApi.PostAsync(host.Client, $"/branches/{branch.Id}/set-main", null, cookie),
        };

        Assert.Equal(expected, response.StatusCode);

        var storedLogo = await database.ScalarAsync<byte[]>(
            "SELECT content FROM organization_logos WHERE organization_id = @o", ("o", account.OrganizationId));
        Assert.Equal(logo, storedLogo);
        Assert.False(await database.ScalarAsync<bool>("SELECT is_main FROM branches WHERE id = @id", ("id", branch.Id)));
        Assert.Equal(0, await database.CountAuditLogsAsync(account.OrganizationId, "organization.logo_updated"));
        Assert.Equal(0, await database.CountAuditLogsAsync(account.OrganizationId, "organization.logo_removed"));
        Assert.Equal(0, await database.CountAuditLogsAsync(account.OrganizationId, "branch.set_as_main"));
    }

    // AC-21: cross-organization set-main and logo isolation.
    [Fact]
    public async Task SetMainAndLogo_AnotherOrganization_Returns404AndNeverExposesOtherLogo()
    {
        var accountA = await database.SeedAccountAsync();
        var accountB = await database.SeedAccountAsync();
        var branchB = await database.SeedBranchAsync(accountB.OrganizationId, name: "B branch");
        var mainB = await database.SeedBranchAsync(accountB.OrganizationId, name: "B main", isMain: true);
        await database.SeedLogoAsync(accountA.OrganizationId, OrganizationLogoEndpointTests.Png("A-LOGO"));
        await using var host = SessionTestHost.Create(database.ConnectionString);
        var cookieA = await CompanySettingsApi.SignInCookieAsync(host.Client, accountA.Email);
        var cookieB = await CompanySettingsApi.SignInCookieAsync(host.Client, accountB.Email);

        var crossSetMain = await CompanySettingsApi.PostAsync(host.Client, $"/branches/{branchB.Id}/set-main", null, cookieA);

        Assert.Equal(HttpStatusCode.NotFound, crossSetMain.StatusCode);
        Assert.Equal(branchB.UpdatedAt, await database.GetBranchUpdatedAtAsync(branchB.Id));
        Assert.Equal(new[] { mainB.Id }, await database.QueryGuidsAsync(
            "SELECT id FROM branches WHERE organization_id = @o AND is_main", ("o", accountB.OrganizationId)));

        Assert.Equal(
            HttpStatusCode.OK,
            (await CompanySettingsApi.GetAsync(host.Client, "/organization-settings/logo", cookieA)).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await CompanySettingsApi.GetAsync(host.Client, "/organization-settings/logo", cookieB)).StatusCode);
    }
}
