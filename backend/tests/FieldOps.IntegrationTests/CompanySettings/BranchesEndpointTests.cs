using System.Net;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.Sessions;

namespace FieldOps.IntegrationTests.CompanySettings;

/// <summary>
/// GET/POST/PUT /branches and the deactivate/reactivate actions (FR-05 to
/// FR-09). Budget: ≤10 methods (AC-09, AC-10, AC-12 to AC-14, AC-16 to AC-18,
/// AC-41, AC-42, AC-49). The 9th method is a correction-pass regression for
/// a database-reviewer finding (DbUpdateConcurrencyException retry in
/// BranchStore.DeactivateAsync/ReactivateAsync). The 10th method is a
/// correction-pass regression for DB-01 (duplicate concurrent deactivate on
/// the same branch misread as BR-06 LastActiveConflict). Neither is new
/// scope.
/// </summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class BranchesEndpointTests(CompanySettingsDatabaseFixture database)
{
    // AC-09, AC-10 (list part).
    [Fact]
    public async Task ListBranches_OwnerSeesAllOrgBranchesSortedByName_ViewerSeesOnlyLinkedBranch()
    {
        var org = await database.SeedOrganizationAsync();
        var branchAlpha = await database.SeedBranchAsync(org, name: "Alpha");
        await database.SeedBranchAsync(org, name: "Beta");
        await database.SeedBranchAsync(org, name: "Charlie", isActive: false);

        var otherOrg = await database.SeedOrganizationAsync();
        await database.SeedBranchAsync(otherOrg, name: "Other Org Branch");

        var owner = await database.SeedAccountAsync(
            CompanySettingsDatabaseFixture.OwnerRoleId, organizationId: org);

        var viewerEmail = CompanySettingsDatabaseFixture.NewEmail();
        var viewerUserId = await database.SeedUserAsync(viewerEmail);
        var viewerMembershipId = await database.SeedMembershipAsync(
            org, viewerUserId, CompanySettingsDatabaseFixture.ViewerRoleId, isAllBranches: false);
        await database.LinkMembershipToBranchAsync(viewerMembershipId, branchAlpha.Id);

        await using var host = SessionTestHost.Create(database.ConnectionString);

        var ownerCookie = SessionApi.GetIssuedCookie(
            await SessionApi.SignInAsync(host.Client, owner.Email, CompanySettingsDatabaseFixture.Password));
        var ownerList = await CompanySettingsApi.ReadAsAsync<BranchListBody>(
            await CompanySettingsApi.GetAsync(host.Client, "/branches", ownerCookie));
        Assert.Equal(["Alpha", "Beta", "Charlie"], ownerList.Items.Select(item => item.Name).ToArray());

        var viewerCookie = SessionApi.GetIssuedCookie(
            await SessionApi.SignInAsync(host.Client, viewerEmail, CompanySettingsDatabaseFixture.Password));
        var viewerList = await CompanySettingsApi.ReadAsAsync<BranchListBody>(
            await CompanySettingsApi.GetAsync(host.Client, "/branches", viewerCookie));
        var viewerBranch = Assert.Single(viewerList.Items);
        Assert.Equal(branchAlpha.Id, viewerBranch.Id);
    }

    // AC-42.
    [Fact]
    public async Task GetBranchDetail_OwnAndAccessibleBranch_ReturnsFullDetail()
    {
        var account = await database.SeedAccountAsync();
        var branch = await database.SeedBranchAsync(account.OrganizationId, name: "Main Branch");
        await using var host = SessionTestHost.Create(database.ConnectionString);
        var cookie = SessionApi.GetIssuedCookie(
            await SessionApi.SignInAsync(host.Client, account.Email, CompanySettingsDatabaseFixture.Password));

        var response = await CompanySettingsApi.GetAsync(host.Client, $"/branches/{branch.Id}", cookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = await CompanySettingsApi.ReadAsAsync<BranchDetailBody>(response);
        Assert.Equal(branch.Id, detail.Id);
        Assert.Equal("Main Branch", detail.Name);
        Assert.True(detail.IsActive);
        Assert.Equal(branch.UpdatedAt, detail.UpdatedAt);
    }

    // AC-12.
    [Fact]
    public async Task CreateBranch_ValidBody_Returns201WithLocationDetailAndOneAuditRow()
    {
        var account = await database.SeedAccountAsync();
        await using var host = SessionTestHost.Create(database.ConnectionString);
        var cookie = SessionApi.GetIssuedCookie(
            await SessionApi.SignInAsync(host.Client, account.Email, CompanySettingsDatabaseFixture.Password));

        var body = ValidBranchBody(code: " main2 ");

        var response = await CompanySettingsApi.PostAsync(host.Client, "/branches", body, cookie);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var detail = await CompanySettingsApi.ReadAsAsync<BranchDetailBody>(response);
        Assert.Equal($"/branches/{detail.Id}", response.Headers.Location?.OriginalString);
        Assert.Equal("MAIN2", detail.Code);
        Assert.True(detail.IsActive);
        Assert.Equal(1, await database.CountAuditLogsAsync(account.OrganizationId, "branch.created"));
    }

    // AC-13.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CreateOrUpdateBranch_CodeAlreadyUsedInOrganization_Returns409WithCodeFieldError(bool isCreate)
    {
        var account = await database.SeedAccountAsync();
        await database.SeedBranchAsync(account.OrganizationId, code: "AUS-C");
        var target = await database.SeedBranchAsync(account.OrganizationId, code: "OTHER1");
        await using var host = SessionTestHost.Create(database.ConnectionString);
        var cookie = SessionApi.GetIssuedCookie(
            await SessionApi.SignInAsync(host.Client, account.Email, CompanySettingsDatabaseFixture.Password));

        var response = isCreate
            ? await CompanySettingsApi.PostAsync(
                host.Client, "/branches", ValidBranchBody(code: " aus-c "), cookie)
            : await CompanySettingsApi.PutAsync(
                host.Client,
                $"/branches/{target.Id}",
                ValidBranchBody(code: " aus-c ", updatedAt: target.UpdatedAt.ToString("O")),
                cookie);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var problem = await CompanySettingsApi.ReadJsonAsync(response);
        var codeError = problem.RootElement.GetProperty("errors").GetProperty("code");
        Assert.Equal("Another branch already uses this code.", codeError.EnumerateArray().Single().GetString());
    }

    // AC-14.
    [Fact]
    public async Task CreateBranch_OrganizationAt100Branches_Returns409WithoutFieldErrors()
    {
        var org = await database.SeedOrganizationAsync();
        await database.SeedManyBranchesAsync(org, 100);
        var account = await database.SeedAccountAsync(organizationId: org);
        await using var host = SessionTestHost.Create(database.ConnectionString);
        var cookie = SessionApi.GetIssuedCookie(
            await SessionApi.SignInAsync(host.Client, account.Email, CompanySettingsDatabaseFixture.Password));

        var response = await CompanySettingsApi.PostAsync(
            host.Client, "/branches", ValidBranchBody(code: "NEW101"), cookie);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var problem = await CompanySettingsApi.ReadJsonAsync(response);
        Assert.False(problem.RootElement.TryGetProperty("errors", out _));
    }

    // AC-16.
    [Fact]
    public async Task UpdateBranch_ChangesNameAndEmail_WritesAuditRowWithEmailMaskedAndNameReal()
    {
        var account = await database.SeedAccountAsync();
        var branch = await database.SeedBranchAsync(account.OrganizationId, name: "Old Name");
        await using var host = SessionTestHost.Create(database.ConnectionString);
        var cookie = SessionApi.GetIssuedCookie(
            await SessionApi.SignInAsync(host.Client, account.Email, CompanySettingsDatabaseFixture.Password));

        var body = ValidBranchBody(code: branch.Code, updatedAt: branch.UpdatedAt.ToString("O"));
        body["name"] = "New Name";
        body["email"] = "new-email@acme.com";

        var response = await CompanySettingsApi.PutAsync(host.Client, $"/branches/{branch.Id}", body, cookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var afterData = await database.GetLatestAuditAfterDataAsync(account.OrganizationId, "branch.updated");
        Assert.NotNull(afterData);

        // jsonb round-trips through Postgres with reordered keys and spaced
        // separators, so this parses rather than matching the raw text.
        using var afterJson = System.Text.Json.JsonDocument.Parse(afterData);
        Assert.Equal("New Name", afterJson.RootElement.GetProperty("name").GetString());
        Assert.Equal("[changed]", afterJson.RootElement.GetProperty("email").GetString());
        Assert.DoesNotContain("new-email@acme.com", afterData);
    }

    // AC-17, AC-18, AC-41.
    [Fact]
    public async Task DeactivateReactivateBranch_TogglesStateIsIdempotentAndBlocksLastActive()
    {
        var account = await database.SeedAccountAsync();
        var branchX = await database.SeedBranchAsync(account.OrganizationId, name: "Branch X");
        var branchY = await database.SeedBranchAsync(account.OrganizationId, name: "Branch Y");
        await using var host = SessionTestHost.Create(database.ConnectionString);
        var cookie = SessionApi.GetIssuedCookie(
            await SessionApi.SignInAsync(host.Client, account.Email, CompanySettingsDatabaseFixture.Password));

        // AC-17: deactivate then reactivate an active branch.
        var deactivate = await CompanySettingsApi.PostAsync(host.Client, $"/branches/{branchX.Id}/deactivate", null, cookie);
        Assert.Equal(HttpStatusCode.NoContent, deactivate.StatusCode);
        Assert.Equal(1, await database.CountAuditLogsAsync(account.OrganizationId, "branch.deactivated"));

        // AC-41: deactivating an already-inactive branch is a no-op, no extra audit row.
        var deactivateAgain = await CompanySettingsApi.PostAsync(host.Client, $"/branches/{branchX.Id}/deactivate", null, cookie);
        Assert.Equal(HttpStatusCode.NoContent, deactivateAgain.StatusCode);
        Assert.Equal(1, await database.CountAuditLogsAsync(account.OrganizationId, "branch.deactivated"));

        var reactivate = await CompanySettingsApi.PostAsync(host.Client, $"/branches/{branchX.Id}/reactivate", null, cookie);
        Assert.Equal(HttpStatusCode.NoContent, reactivate.StatusCode);
        Assert.Equal(1, await database.CountAuditLogsAsync(account.OrganizationId, "branch.reactivated"));

        // AC-41: reactivating an already-active branch is a no-op, no extra audit row.
        var reactivateAgain = await CompanySettingsApi.PostAsync(host.Client, $"/branches/{branchX.Id}/reactivate", null, cookie);
        Assert.Equal(HttpStatusCode.NoContent, reactivateAgain.StatusCode);
        Assert.Equal(1, await database.CountAuditLogsAsync(account.OrganizationId, "branch.reactivated"));

        // AC-18: deactivate the other branch, leaving branchX the only active one.
        var deactivateY = await CompanySettingsApi.PostAsync(host.Client, $"/branches/{branchY.Id}/deactivate", null, cookie);
        Assert.Equal(HttpStatusCode.NoContent, deactivateY.StatusCode);
        var deactivatedCountBeforeConflict = await database.CountAuditLogsAsync(
            account.OrganizationId, "branch.deactivated");

        var lastActiveAttempt = await CompanySettingsApi.PostAsync(host.Client, $"/branches/{branchX.Id}/deactivate", null, cookie);
        Assert.Equal(HttpStatusCode.Conflict, lastActiveAttempt.StatusCode);
        using var problem = await CompanySettingsApi.ReadJsonAsync(lastActiveAttempt);
        Assert.False(problem.RootElement.TryGetProperty("errors", out _));
        Assert.Equal(
            deactivatedCountBeforeConflict,
            await database.CountAuditLogsAsync(account.OrganizationId, "branch.deactivated"));
    }

    // AC-49 (mandatory: the real row-locking transaction under concurrency).
    [Fact]
    public async Task DeactivateBranch_TwoActiveBranchesConcurrentDeactivation_ExactlyOneSucceeds()
    {
        var account = await database.SeedAccountAsync();
        var branch1 = await database.SeedBranchAsync(account.OrganizationId, name: "Branch 1");
        var branch2 = await database.SeedBranchAsync(account.OrganizationId, name: "Branch 2");
        await using var host = SessionTestHost.Create(database.ConnectionString);
        var cookie = SessionApi.GetIssuedCookie(
            await SessionApi.SignInAsync(host.Client, account.Email, CompanySettingsDatabaseFixture.Password));

        var task1 = CompanySettingsApi.PostAsync(host.Client, $"/branches/{branch1.Id}/deactivate", null, cookie);
        var task2 = CompanySettingsApi.PostAsync(host.Client, $"/branches/{branch2.Id}/deactivate", null, cookie);
        var responses = await Task.WhenAll(task1, task2);

        var statusCodes = responses.Select(r => r.StatusCode).OrderBy(s => s).ToArray();
        Assert.Equal([HttpStatusCode.NoContent, HttpStatusCode.Conflict], statusCodes);

        var activeCount = await database.ScalarAsync<long>(
            "SELECT COUNT(*) FROM branches WHERE organization_id = @organizationId AND is_active = true",
            ("organizationId", account.OrganizationId));
        Assert.Equal(1, activeCount);
        Assert.Equal(1, await database.CountAuditLogsAsync(account.OrganizationId, "branch.deactivated"));
    }

    // Correction-pass regression (database-reviewer finding): a PUT racing a
    // deactivate on the same branch used to let DbUpdateConcurrencyException
    // bubble up as an unhandled 500, which isn't one of this endpoint's
    // documented statuses. branch2 stays active throughout so BR-06 never
    // blocks branch1's deactivation, which makes the end state deterministic
    // regardless of which request the database interleaves first: branch1
    // always ends up inactive, and neither response is ever a 500.
    [Fact]
    public async Task DeactivateBranch_ConcurrentWithPutOnSameBranch_NeverSurfacesUnhandled500()
    {
        var account = await database.SeedAccountAsync();
        var branch1 = await database.SeedBranchAsync(account.OrganizationId, name: "Branch 1");
        await database.SeedBranchAsync(account.OrganizationId, name: "Branch 2");
        await using var host = SessionTestHost.Create(database.ConnectionString);
        var cookie = SessionApi.GetIssuedCookie(
            await SessionApi.SignInAsync(host.Client, account.Email, CompanySettingsDatabaseFixture.Password));

        var putBody = ValidBranchBody(code: branch1.Code, updatedAt: branch1.UpdatedAt.ToString("O"));
        putBody["name"] = "Branch 1 Renamed";

        var putTask = CompanySettingsApi.PutAsync(host.Client, $"/branches/{branch1.Id}", putBody, cookie);
        var deactivateTask = CompanySettingsApi.PostAsync(host.Client, $"/branches/{branch1.Id}/deactivate", null, cookie);
        var responses = await Task.WhenAll(putTask, deactivateTask);

        Assert.DoesNotContain(HttpStatusCode.InternalServerError, responses.Select(r => r.StatusCode));
        Assert.Equal(HttpStatusCode.NoContent, responses[1].StatusCode);

        var isActive = await database.ScalarAsync<bool>(
            "SELECT is_active FROM branches WHERE id = @id", ("id", branch1.Id));
        Assert.False(isActive);
    }

    // Correction-pass regression (DB-01): two duplicate deactivate requests
    // racing on the SAME branch. The second call's FOR UPDATE blocks behind
    // the first's row lock; once unblocked, the target branch is no longer
    // in the locked active set purely because the first call already
    // deactivated it, not because it is the organization's last active
    // branch. Both calls must resolve to 204 (BR-08 idempotent NoOp), never
    // a 409. branch2 stays active throughout so BR-06 never legitimately
    // blocks either call.
    [Fact]
    public async Task DeactivateBranch_DuplicateRequestsOnSameBranchConcurrently_BothResolveToNoContent()
    {
        var account = await database.SeedAccountAsync();
        var branch1 = await database.SeedBranchAsync(account.OrganizationId, name: "Branch 1");
        await database.SeedBranchAsync(account.OrganizationId, name: "Branch 2");
        await using var host = SessionTestHost.Create(database.ConnectionString);
        var cookie = SessionApi.GetIssuedCookie(
            await SessionApi.SignInAsync(host.Client, account.Email, CompanySettingsDatabaseFixture.Password));

        var task1 = CompanySettingsApi.PostAsync(host.Client, $"/branches/{branch1.Id}/deactivate", null, cookie);
        var task2 = CompanySettingsApi.PostAsync(host.Client, $"/branches/{branch1.Id}/deactivate", null, cookie);
        var responses = await Task.WhenAll(task1, task2);

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.NoContent, response.StatusCode));

        var isActive = await database.ScalarAsync<bool>(
            "SELECT is_active FROM branches WHERE id = @id", ("id", branch1.Id));
        Assert.False(isActive);
        Assert.Equal(1, await database.CountAuditLogsAsync(account.OrganizationId, "branch.deactivated"));
    }

    internal static JsonObject ValidBranchBody(string? code = null, string? updatedAt = null)
    {
        var body = new JsonObject
        {
            ["name"] = "Main Branch",
            ["code"] = code ?? "MAIN",
            ["phone"] = "+1 555 987 6543",
            ["email"] = "branch@acme.com",
            ["timezone"] = "America/Chicago",
            ["addressLine1"] = "123 Main St",
            ["addressLine2"] = "Suite 200",
            ["city"] = "Chicago",
            ["stateRegion"] = "IL",
            ["postalCode"] = "60601",
            ["countryCode"] = "US",
            ["businessHours"] = new JsonObject
            {
                ["monday"] = new JsonObject { ["start"] = "08:00", ["end"] = "17:00" },
            },
        };

        if (updatedAt is not null)
        {
            body["updatedAt"] = updatedAt;
        }

        return body;
    }
}

public sealed record BranchListBody(IReadOnlyList<BranchListItemBody> Items);

public sealed record BranchListItemBody(Guid Id, string Name, string Code, bool IsActive);

public sealed record BranchDetailBody(
    Guid Id,
    string Name,
    string Code,
    string? Email,
    string? Phone,
    bool IsActive,
    DateTimeOffset UpdatedAt);
