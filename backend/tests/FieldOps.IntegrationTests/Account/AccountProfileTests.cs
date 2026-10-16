using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.Sessions;

namespace FieldOps.IntegrationTests.Account;

/// <summary>
/// GET/PUT /account/profile (account settings FR-05, BR-01, BR-07; AC-08 to AC-10):
/// trimming, ignored identifiers, tenant isolation, audit only on change, the
/// validation table and the 401 boundary of every /account/* endpoint.
/// </summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class AccountProfileTests(CompanySettingsDatabaseFixture database)
{
    private const string ProfilePath = "/account/profile";

    // AC-08, AC-10: representative cross-tenant case, profile audit rules.
    [Fact]
    public async Task PutProfile_TrimsStoresNullPhoneIgnoresIdentifiersAndAuditsOnlyChanges()
    {
        var accountA = await database.SeedAccountAsync(organizationName: "Acme Field Services");
        var accountB = await database.SeedAccountAsync();
        await using var host = SessionTestHost.Create(database.ConnectionString);
        var cookie = await CompanySettingsApi.SignInCookieAsync(host.Client, accountA.Email);
        var roleName = await database.ScalarAsync<string>(
            "SELECT name FROM roles WHERE id = @id", ("id", CompanySettingsDatabaseFixture.OwnerRoleId));

        var get = await CompanySettingsApi.GetAsync(host.Client, ProfilePath, cookie);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.True(get.Headers.CacheControl?.NoStore);
        using (var initial = await CompanySettingsApi.ReadJsonAsync(get))
        {
            var root = initial.RootElement;
            Assert.Equal("Ada", root.GetProperty("firstName").GetString());
            Assert.Equal(accountA.Email, root.GetProperty("email").GetString());
            Assert.Equal(JsonValueKind.Null, root.GetProperty("phone").ValueKind);
            Assert.Equal("Acme Field Services", root.GetProperty("organization").GetProperty("name").GetString());
            Assert.Equal(roleName, root.GetProperty("role").GetProperty("name").GetString());
            Assert.Equal(JsonValueKind.Null, root.GetProperty("photo").ValueKind);
        }

        var put = await CompanySettingsApi.PutAsync(
            host.Client,
            ProfilePath,
            new JsonObject
            {
                ["firstName"] = " Ana ",
                ["lastName"] = "Lopez ",
                ["phone"] = " +1 (555) 123-4567 ",
                ["email"] = "attacker@example.com",
                ["userId"] = accountB.UserId,
                ["organizationId"] = accountB.OrganizationId,
                ["role"] = "viewer",
            },
            cookie);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        using (var saved = await CompanySettingsApi.ReadJsonAsync(put))
        {
            Assert.Equal("Ana", saved.RootElement.GetProperty("firstName").GetString());
            Assert.Equal("Lopez", saved.RootElement.GetProperty("lastName").GetString());
            Assert.Equal("+1 (555) 123-4567", saved.RootElement.GetProperty("phone").GetString());
            Assert.Equal(accountA.Email, saved.RootElement.GetProperty("email").GetString());
        }

        // Another user and organization are untouched; the audit row is only in the session organization.
        Assert.Equal("Ada", await database.ScalarAsync<string>(
            "SELECT first_name FROM users WHERE id = @id", ("id", accountB.UserId)));
        Assert.Equal(accountB.Email, await database.ScalarAsync<string>(
            "SELECT email FROM users WHERE id = @id", ("id", accountB.UserId)));
        Assert.Equal(accountA.Email, await database.ScalarAsync<string>(
            "SELECT email FROM users WHERE id = @id", ("id", accountA.UserId)));
        Assert.Equal(CompanySettingsDatabaseFixture.OwnerRoleId, await database.ScalarAsync<short>(
            "SELECT role_id FROM organization_users WHERE id = @id", ("id", accountA.MembershipId)));
        Assert.Equal(1, await database.CountAuditLogsAsync(accountA.OrganizationId, "user.profile_updated"));
        Assert.Equal(0, await database.CountAuditLogsAsync(accountB.OrganizationId, "user.profile_updated"));

        var (_, _, metadata) = await database.GetLatestAuditAsync(accountA.OrganizationId, "user.profile_updated");
        using (var metadataJson = JsonDocument.Parse(metadata!))
        {
            Assert.Equal(
                ["firstName", "lastName", "phone"],
                metadataJson.RootElement.GetProperty("changedFields").EnumerateArray().Select(e => e.GetString()));
        }

        Assert.DoesNotContain("Ana", metadata!);
        Assert.True(await database.ScalarAsync<bool>(
            "SELECT ip_address IS NOT NULL FROM audit_logs WHERE organization_id = @o AND action = 'user.profile_updated'",
            ("o", accountA.OrganizationId)));
        Assert.Equal("user", await database.ScalarAsync<string>(
            "SELECT entity_type FROM audit_logs WHERE organization_id = @o AND action = 'user.profile_updated'",
            ("o", accountA.OrganizationId)));

        // An empty phone is stored as NULL and audited as a phone change; identical values write nothing.
        var clearPhone = new JsonObject { ["firstName"] = "Ana", ["lastName"] = "Lopez", ["phone"] = "  " };
        Assert.Equal(HttpStatusCode.OK, (await CompanySettingsApi.PutAsync(host.Client, ProfilePath, clearPhone, cookie)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await CompanySettingsApi.PutAsync(host.Client, ProfilePath, clearPhone, cookie)).StatusCode);
        Assert.True(await database.ScalarAsync<bool>(
            "SELECT phone IS NULL FROM users WHERE id = @id", ("id", accountA.UserId)));
        Assert.Equal(2, await database.CountAuditLogsAsync(accountA.OrganizationId, "user.profile_updated"));
    }

    // AC-09 and the rejection statuses of PUT /account/profile.
    [Fact]
    public async Task PutProfile_InvalidInput_ReturnsFieldErrorsOr4xxAndChangesNothing()
    {
        var account = await database.SeedAccountAsync();
        await using var host = SessionTestHost.Create(database.ConnectionString);
        var cookie = await CompanySettingsApi.SignInCookieAsync(host.Client, account.Email);

        (string Key, string Message, JsonObject Body)[] cases =
        [
            ("firstName", "This field is required.", Profile("  ", "Lopez", null)),
            ("lastName", "This field is required.", Profile("Ana", null, null)),
            ("firstName", "Use 100 characters or fewer.", Profile(new string('a', 101), "Lopez", null)),
            ("lastName", "Use 100 characters or fewer.", Profile("Ana", new string('b', 101), null)),
            ("phone", "Use 40 characters or fewer.", Profile("Ana", "Lopez", new string('1', 41))),
            ("phone", "Enter a valid phone number.", Profile("Ana", "Lopez", "call me")),
            ("phone", "Enter a valid phone number.", Profile("Ana", "Lopez", "123")),
        ];

        foreach (var (key, message, body) in cases)
        {
            var response = await CompanySettingsApi.PutAsync(host.Client, ProfilePath, body, cookie);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            using var problem = await CompanySettingsApi.ReadJsonAsync(response);
            var errors = problem.RootElement.GetProperty("errors");
            Assert.Equal(message, errors.GetProperty(key).EnumerateArray().Single().GetString());
        }

        var malformed = await CompanySettingsApi.SendRawAsync(host.Client, HttpMethod.Put, ProfilePath, "{", cookie);
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        using (var keyless = await CompanySettingsApi.ReadJsonAsync(malformed))
        {
            Assert.False(keyless.RootElement.TryGetProperty("errors", out _));
        }

        var wrongType = await CompanySettingsApi.SendRawAsync(
            host.Client, HttpMethod.Put, ProfilePath, "firstName=Ana", cookie, "text/plain");
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, wrongType.StatusCode);

        Assert.Equal("Ada", await database.ScalarAsync<string>(
            "SELECT first_name FROM users WHERE id = @id", ("id", account.UserId)));
        Assert.Equal(0, await database.CountAuditLogsAsync(account.OrganizationId, "user.profile_updated"));
    }

    // AC-05: every /account/* endpoint answers 401 without a valid session.
    [Fact]
    public async Task AccountEndpoints_WithoutSession_Return401NoStore()
    {
        await using var host = SessionTestHost.Create(database.ConnectionString);
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1 };

        var responses = new[]
        {
            await CompanySettingsApi.GetAsync(host.Client, ProfilePath, null),
            await CompanySettingsApi.PutAsync(host.Client, ProfilePath, Profile("Ana", "Lopez", null), null),
            await CompanySettingsApi.GetAsync(host.Client, "/account/photo", null),
            await CompanySettingsApi.PutFileAsync(host.Client, "/account/photo", png, "image/png", null),
            await CompanySettingsApi.SendRawAsync(host.Client, HttpMethod.Delete, "/account/photo", null, null),
            await CompanySettingsApi.PostAsync(
                host.Client,
                "/account/password",
                new JsonObject { ["currentPassword"] = "x", ["newPassword"] = "y" },
                null),
        };

        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
        }
    }

    private static JsonObject Profile(string? firstName, string? lastName, string? phone) =>
        new() { ["firstName"] = firstName, ["lastName"] = lastName, ["phone"] = phone };
}
