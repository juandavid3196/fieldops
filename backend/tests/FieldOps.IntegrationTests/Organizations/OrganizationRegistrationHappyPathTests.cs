using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FieldOps.IntegrationTests.Sessions;

namespace FieldOps.IntegrationTests.Organizations;

[Collection(SessionsDatabaseCollection.Name)]
public class OrganizationRegistrationHappyPathTests(SessionsDatabaseFixture database)
{
    // Covers AC-06 (201 body, no cookie/auth header), AC-07 (one row per
    // table sharing the new organizationId), AC-08 (owner membership state),
    // AC-09 (user state) and AC-20 (BR-22 audit values) with one request.
    [Fact]
    public async Task PostOrganizationRegistrations_ValidRequest_CreatesOneRowPerTableWithExpectedState()
    {
        await using var host = OrganizationRegistrationTestHost.Create(database.ConnectionString);
        var email = OrganizationRegistrationApi.NewEmail();
        var body = OrganizationRegistrationApi.ValidBody(ownerEmail: email);

        var response = await OrganizationRegistrationApi.PostAsync(host.Client, body, clientIp: "203.0.113.50");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Null(response.Headers.WwwAuthenticate.FirstOrDefault());
        Assert.Null(response.Headers.Location);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var organizationId = json.GetProperty("organizationId").GetGuid();

        Assert.Equal(1L, await database.ScalarAsync<long>(
            "SELECT count(*) FROM organizations WHERE id = @id", ("id", organizationId)));
        Assert.Equal(1L, await database.ScalarAsync<long>(
            "SELECT count(*) FROM branches WHERE organization_id = @id", ("id", organizationId)));

        var userRow = Assert.Single(await database.QueryAsync(
            "SELECT id, status::text AS status, email_verified_at, email FROM users WHERE email = @email",
            ("email", email)));
        var userId = (Guid)userRow["id"]!;
        Assert.Equal("active", userRow["status"]);
        Assert.Null(userRow["email_verified_at"]);
        Assert.Equal(email, userRow["email"]);

        var membershipRow = Assert.Single(await database.QueryAsync(
            """
            SELECT ou.id, ou.status::text AS status, ou.is_all_branches, ou.joined_at, r.code AS role_code
            FROM organization_users ou JOIN roles r ON r.id = ou.role_id
            WHERE ou.organization_id = @orgId AND ou.user_id = @userId
            """,
            ("orgId", organizationId),
            ("userId", userId)));
        Assert.Equal("owner", membershipRow["role_code"]);
        Assert.Equal("active", membershipRow["status"]);
        Assert.Equal(true, membershipRow["is_all_branches"]);
        Assert.NotNull(membershipRow["joined_at"]);
        var membershipId = (Guid)membershipRow["id"]!;

        Assert.Equal(1L, await database.ScalarAsync<long>(
            "SELECT count(*) FROM organization_user_branches WHERE organization_user_id = @id",
            ("id", membershipId)));

        var auditRow = Assert.Single(await database.QueryAsync(
            """
            SELECT action, entity_type, entity_id, organization_id, actor_user_id, branch_id,
                   before_data::text AS before_data, after_data::text AS after_data,
                   metadata::text AS metadata, host(ip_address) AS ip_address
            FROM audit_logs WHERE organization_id = @id
            """,
            ("id", organizationId)));
        Assert.Equal("organization.registered", auditRow["action"]);
        Assert.Equal("organization", auditRow["entity_type"]);
        Assert.Equal(organizationId, auditRow["entity_id"]);
        Assert.Equal(userId, auditRow["actor_user_id"]);
        Assert.NotNull(auditRow["branch_id"]);
        Assert.Null(auditRow["before_data"]);
        Assert.Null(auditRow["after_data"]);
        Assert.Equal("{}", auditRow["metadata"]);
        Assert.Equal("203.0.113.50", auditRow["ip_address"]);
    }
}
