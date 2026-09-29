using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.Sessions;

namespace FieldOps.IntegrationTests.CompanySettings;

/// <summary>
/// Company setup completion for branches: main branch, team count, service
/// ZIP codes and company billing (FR-13 to FR-15, FR-19; AC-14 to AC-17).
/// </summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class BranchesCompletionTests(CompanySettingsDatabaseFixture database)
{
    // AC-14.
    [Fact]
    public async Task ListBranches_ReturnsIsMainAndCountsOnlyActiveSameOrganizationTechnicians()
    {
        var account = await database.SeedAccountAsync();
        var org = account.OrganizationId;
        var main = await database.SeedBranchAsync(org, name: "A Main", isMain: true);
        var other = await database.SeedBranchAsync(org, name: "B Other");
        await database.SeedTechnicianAsync(org, main.Id);
        await database.SeedTechnicianAsync(org, main.Id, status: "inactive");
        await database.SeedTechnicianAsync(org, other.Id);
        await database.SeedTechnicianAsync(org, other.Id);
        await database.SeedTechnicianAsync(org, other.Id);

        var foreignOrg = await database.SeedOrganizationAsync();
        await database.SeedTechnicianAsync(foreignOrg, main.Id);

        await using var host = SessionTestHost.Create(database.ConnectionString);
        var cookie = await CompanySettingsApi.SignInCookieAsync(host.Client, account.Email);

        var list = await CompanySettingsApi.ReadAsAsync<BranchListBody>(
            await CompanySettingsApi.GetAsync(host.Client, "/branches", cookie));

        var mainItem = Assert.Single(list.Items, item => item.Id == main.Id);
        var otherItem = Assert.Single(list.Items, item => item.Id == other.Id);
        Assert.True(mainItem.IsMain);
        Assert.Equal(1, mainItem.TechnicianCount);
        Assert.False(otherItem.IsMain);
        Assert.Equal(3, otherItem.TechnicianCount);
    }

    // AC-15 (valid part).
    [Fact]
    public async Task CreateThenUpdateBranch_ServiceCodesAndBilling_AreNormalizedPersistedAndAudited()
    {
        var account = await database.SeedAccountAsync();
        await using var host = SessionTestHost.Create(database.ConnectionString);
        var cookie = await CompanySettingsApi.SignInCookieAsync(host.Client, account.Email);

        var create = BranchesEndpointTests.ValidBranchBody(code: "ZIPS1");
        create["servicePostalCodes"] = new JsonArray(" 78701", "78702", "78701 ");
        create["usesCompanyBilling"] = false;

        var created = await CompanySettingsApi.PostAsync(host.Client, "/branches", create, cookie);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var detail = await CompanySettingsApi.ReadAsAsync<BranchDetailBody>(created);
        Assert.Equal(["78701", "78702"], detail.ServicePostalCodes);
        Assert.False(detail.UsesCompanyBilling);
        Assert.False(detail.IsMain);

        var (_, createdAfter, _) = await database.GetLatestAuditAsync(account.OrganizationId, "branch.created");
        using (var createdJson = JsonDocument.Parse(createdAfter!))
        {
            Assert.Equal(2, createdJson.RootElement.GetProperty("servicePostalCodes").GetArrayLength());
        }

        // Re-read: the 201 body carries the in-memory timestamp (sub-microsecond
        // ticks), while the stored concurrency token is microsecond-truncated.
        var stored = await CompanySettingsApi.ReadAsAsync<BranchDetailBody>(
            await CompanySettingsApi.GetAsync(host.Client, $"/branches/{detail.Id}", cookie));
        var update = BranchesEndpointTests.ValidBranchBody(code: "ZIPS1", updatedAt: stored.UpdatedAt.ToString("O"));
        update["servicePostalCodes"] = new JsonArray("90210");
        update["usesCompanyBilling"] = true;

        var updated = await CompanySettingsApi.PutAsync(host.Client, $"/branches/{detail.Id}", update, cookie);

        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var reread = await CompanySettingsApi.ReadAsAsync<BranchDetailBody>(
            await CompanySettingsApi.GetAsync(host.Client, $"/branches/{detail.Id}", cookie));
        Assert.Equal(["90210"], reread.ServicePostalCodes);
        Assert.True(reread.UsesCompanyBilling);

        var (_, updatedAfter, _) = await database.GetLatestAuditAsync(account.OrganizationId, "branch.updated");
        using var updatedJson = JsonDocument.Parse(updatedAfter!);
        Assert.True(updatedJson.RootElement.TryGetProperty("servicePostalCodes", out _));
        Assert.True(updatedJson.RootElement.GetProperty("usesCompanyBilling").GetBoolean());
        Assert.False(updatedJson.RootElement.TryGetProperty("name", out _));
    }

    // AC-15 (invalid part).
    [Theory]
    [InlineData("post-201-codes", "servicePostalCodes", "Enter up to 200 ZIP codes.")]
    [InlineData("post-invalid-code", "servicePostalCodes", "Enter valid ZIP codes separated by commas.")]
    [InlineData("post-wrong-type", "servicePostalCodes", "Enter a valid value.")]
    [InlineData("put-missing-billing", "usesCompanyBilling", "Enter a valid value.")]
    [InlineData("put-missing-codes", "servicePostalCodes", "Enter a valid value.")]
    public async Task CreateOrUpdateBranch_InvalidCodesOrBilling_Returns400WithKeyAndNoChange(
        string scenario, string key, string message)
    {
        var account = await database.SeedAccountAsync();
        var branch = await database.SeedBranchAsync(account.OrganizationId, code: "TARGET");
        await using var host = SessionTestHost.Create(database.ConnectionString);
        var cookie = await CompanySettingsApi.SignInCookieAsync(host.Client, account.Email);
        var before = await database.GetBranchUpdatedAtAsync(branch.Id);
        var branchesBefore = await database.ScalarAsync<long>(
            "SELECT count(*) FROM branches WHERE organization_id = @o", ("o", account.OrganizationId));

        HttpResponseMessage response;

        if (scenario.StartsWith("post", StringComparison.Ordinal))
        {
            var body = BranchesEndpointTests.ValidBranchBody(code: "NEWZIP");
            body["servicePostalCodes"] = scenario switch
            {
                "post-201-codes" => new JsonArray(Enumerable.Range(10000, 201)
                    .Select(n => (JsonNode)JsonValue.Create(n.ToString())!).ToArray()),
                "post-invalid-code" => new JsonArray("78701", "bad!code"),
                _ => JsonValue.Create("78701"),
            };
            response = await CompanySettingsApi.PostAsync(host.Client, "/branches", body, cookie);
        }
        else
        {
            var body = BranchesEndpointTests.ValidBranchBody(code: "TARGET", updatedAt: before.ToString("O"));
            body.Remove(key);
            response = await CompanySettingsApi.PutAsync(host.Client, $"/branches/{branch.Id}", body, cookie);
        }

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = await CompanySettingsApi.ReadJsonAsync(response);
        Assert.Equal(
            message,
            problem.RootElement.GetProperty("errors").GetProperty(key).EnumerateArray().Single().GetString());
        Assert.Equal(before, await database.GetBranchUpdatedAtAsync(branch.Id));
        Assert.Equal(
            branchesBefore,
            await database.ScalarAsync<long>(
                "SELECT count(*) FROM branches WHERE organization_id = @o", ("o", account.OrganizationId)));
    }

    // AC-16, AC-17 and the concurrency safety of BR-11.
    [Fact]
    public async Task SetMain_MovesMainIsIdempotentRejectsInactiveAndMainDeactivationAndSerializesConcurrentCalls()
    {
        var account = await database.SeedAccountAsync();
        var org = account.OrganizationId;
        var a = await database.SeedBranchAsync(org, name: "A", isMain: true);
        var b = await database.SeedBranchAsync(org, name: "B");
        var c = await database.SeedBranchAsync(org, name: "C", isActive: false);
        var d = await database.SeedBranchAsync(org, name: "D");
        var e = await database.SeedBranchAsync(org, name: "E");
        await using var host = SessionTestHost.Create(database.ConnectionString);
        var cookie = await CompanySettingsApi.SignInCookieAsync(host.Client, account.Email);

        var first = await CompanySettingsApi.PostAsync(host.Client, $"/branches/{b.Id}/set-main", null, cookie);
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(new[] { b.Id }, await MainBranchIdsAsync(org));
        Assert.Equal(1, await database.CountAuditLogsAsync(org, "branch.set_as_main"));

        var (before, after, metadata) = await database.GetLatestAuditAsync(org, "branch.set_as_main");
        Assert.False(JsonDocument.Parse(before!).RootElement.GetProperty("isMain").GetBoolean());
        Assert.True(JsonDocument.Parse(after!).RootElement.GetProperty("isMain").GetBoolean());
        Assert.Equal(
            a.Id.ToString(),
            JsonDocument.Parse(metadata!).RootElement.GetProperty("previousMainBranchId").GetString());

        var again = await CompanySettingsApi.PostAsync(host.Client, $"/branches/{b.Id}/set-main", null, cookie);
        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
        Assert.Equal(1, await database.CountAuditLogsAsync(org, "branch.set_as_main"));

        var inactive = await CompanySettingsApi.PostAsync(host.Client, $"/branches/{c.Id}/set-main", null, cookie);
        Assert.Equal(HttpStatusCode.Conflict, inactive.StatusCode);
        using (var problem = await CompanySettingsApi.ReadJsonAsync(inactive))
        {
            Assert.False(problem.RootElement.TryGetProperty("errors", out _));
            Assert.Equal("Only an active branch can be the main branch.", problem.RootElement.GetProperty("title").GetString());
        }

        Assert.Equal(new[] { b.Id }, await MainBranchIdsAsync(org));

        var deactivateMain = await CompanySettingsApi.PostAsync(host.Client, $"/branches/{b.Id}/deactivate", null, cookie);
        Assert.Equal(HttpStatusCode.Conflict, deactivateMain.StatusCode);
        using (var problem = await CompanySettingsApi.ReadJsonAsync(deactivateMain))
        {
            Assert.False(problem.RootElement.TryGetProperty("errors", out _));
            Assert.Equal("The main branch can't be deactivated.", problem.RootElement.GetProperty("title").GetString());
        }

        Assert.True(await database.ScalarAsync<bool>("SELECT is_active FROM branches WHERE id = @id", ("id", b.Id)));
        Assert.Equal(0, await database.CountAuditLogsAsync(org, "branch.deactivated"));

        var concurrent = await Task.WhenAll(
            CompanySettingsApi.PostAsync(host.Client, $"/branches/{d.Id}/set-main", null, cookie),
            CompanySettingsApi.PostAsync(host.Client, $"/branches/{e.Id}/set-main", null, cookie));
        Assert.All(concurrent, response => Assert.Equal(HttpStatusCode.NoContent, response.StatusCode));
        Assert.Single(await MainBranchIdsAsync(org));
        Assert.Equal(3, await database.CountAuditLogsAsync(org, "branch.set_as_main"));
    }

    private Task<Guid[]> MainBranchIdsAsync(Guid organizationId) =>
        database.QueryGuidsAsync(
            "SELECT id FROM branches WHERE organization_id = @o AND is_main", ("o", organizationId));
}
