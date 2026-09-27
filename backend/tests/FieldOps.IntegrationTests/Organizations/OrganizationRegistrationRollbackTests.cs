using System.Net;
using FieldOps.IntegrationTests.Sessions;

namespace FieldOps.IntegrationTests.Organizations;

// Shares SessionsDatabaseCollection, whose tests run sequentially relative
// to each other (xUnit does not parallelize test classes within one
// collection), so the temporary 'owner' role rename below is safe.
[Collection(SessionsDatabaseCollection.Name)]
public class OrganizationRegistrationRollbackTests(SessionsDatabaseFixture database)
{
    // AC-19 (mandatory: rollback of a multi-record write). One representative
    // case: the seeded 'owner' role missing propagates the same
    // InvalidOperationException -> 500 -> implicit-transaction-rollback path
    // that a database failure during SaveChangesAsync would also take, so a
    // second contrived DB-failure test would exercise the same code path.
    [Fact]
    public async Task PostOrganizationRegistrations_OwnerRoleMissing_Returns500AndCreatesNoRowsInAnyOfTheSixTables()
    {
        await using var host = OrganizationRegistrationTestHost.Create(database.ConnectionString);
        var email = OrganizationRegistrationApi.NewEmail();
        var organizationName = $"Rollback Test {Guid.NewGuid():N}";
        var body = OrganizationRegistrationApi.ValidBody(ownerEmail: email, organizationName: organizationName);

        await database.ExecuteAsync("UPDATE roles SET code = 'owner_disabled_for_test' WHERE code = 'owner'");
        try
        {
            var response = await OrganizationRegistrationApi.PostAsync(host.Client, body);

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        }
        finally
        {
            await database.ExecuteAsync("UPDATE roles SET code = 'owner' WHERE code = 'owner_disabled_for_test'");
        }

        Assert.Equal(0L, await database.ScalarAsync<long>(
            "SELECT count(*) FROM organizations WHERE name = @name", ("name", organizationName)));
        Assert.Equal(0L, await database.ScalarAsync<long>(
            "SELECT count(*) FROM users WHERE email = @email", ("email", email)));
        Assert.Equal(0L, await database.ScalarAsync<long>(
            """
            SELECT count(*) FROM branches b
            JOIN organizations o ON o.id = b.organization_id
            WHERE o.name = @name
            """,
            ("name", organizationName)));
        Assert.Equal(0L, await database.ScalarAsync<long>(
            "SELECT count(*) FROM organization_users ou JOIN users u ON u.id = ou.user_id WHERE u.email = @email",
            ("email", email)));
        Assert.Equal(0L, await database.ScalarAsync<long>(
            """
            SELECT count(*) FROM organization_user_branches oub
            JOIN organization_users ou ON ou.id = oub.organization_user_id
            JOIN users u ON u.id = ou.user_id
            WHERE u.email = @email
            """,
            ("email", email)));
        Assert.Equal(0L, await database.ScalarAsync<long>(
            "SELECT count(*) FROM audit_logs a JOIN organizations o ON o.id = a.organization_id WHERE o.name = @name",
            ("name", organizationName)));
    }
}
