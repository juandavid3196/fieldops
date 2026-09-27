using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.Sessions;

namespace FieldOps.IntegrationTests.Organizations;

[Collection(SessionsDatabaseCollection.Name)]
public class OrganizationRegistrationTenantIsolationTests(SessionsDatabaseFixture database)
{
    // AC-10, the mandatory cross-tenant denial test for this anonymous,
    // pre-tenant endpoint: a client-supplied organizationId naming an
    // existing organization is ignored (FR-05); a new, separate organization
    // is created and the existing one's rows are unchanged.
    [Fact]
    public async Task PostOrganizationRegistrations_BodyNamesExistingOrganizationId_CreatesNewOrganizationAndLeavesExistingUnchanged()
    {
        var existingOrganizationId = await database.SeedOrganizationAsync();
        await using var host = OrganizationRegistrationTestHost.Create(database.ConnectionString);

        var bodyWithForeignId = JsonNode.Parse(OrganizationRegistrationApi.ValidBody())!.AsObject();
        bodyWithForeignId["organizationId"] = existingOrganizationId.ToString();

        var response = await OrganizationRegistrationApi.PostAsync(host.Client, bodyWithForeignId.ToJsonString());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var newOrganizationId = json.GetProperty("organizationId").GetGuid();

        Assert.NotEqual(existingOrganizationId, newOrganizationId);
        Assert.Equal(1L, await database.ScalarAsync<long>(
            "SELECT count(*) FROM organizations WHERE id = @id", ("id", existingOrganizationId)));
        Assert.Equal(0L, await database.ScalarAsync<long>(
            "SELECT count(*) FROM branches WHERE organization_id = @id", ("id", existingOrganizationId)));
        Assert.Equal(0L, await database.ScalarAsync<long>(
            "SELECT count(*) FROM organization_users WHERE organization_id = @id", ("id", existingOrganizationId)));
    }
}
