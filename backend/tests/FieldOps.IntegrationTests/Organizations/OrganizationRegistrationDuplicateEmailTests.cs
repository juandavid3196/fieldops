using System.Net;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.Sessions;

namespace FieldOps.IntegrationTests.Organizations;

[Collection(SessionsDatabaseCollection.Name)]
public class OrganizationRegistrationDuplicateEmailTests(SessionsDatabaseFixture database)
{
    // AC-12: pre-insert check path.
    [Fact]
    public async Task PostOrganizationRegistrations_NormalizedDuplicateEmail_Returns409AndCreatesNoRows()
    {
        var email = OrganizationRegistrationApi.NewEmail();
        await database.SeedUserAsync(email);
        await using var host = OrganizationRegistrationTestHost.Create(database.ConnectionString);
        var before = await database.ScalarAsync<long>("SELECT count(*) FROM organizations");

        var mixedCaseEmail = " " + email.ToUpperInvariant() + " ";
        var body = OrganizationRegistrationApi.ValidBody(ownerEmail: mixedCaseEmail);

        var response = await OrganizationRegistrationApi.PostAsync(host.Client, body);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        var errors = json["errors"]!.AsObject();
        Assert.Equal(
            "An account with this email already exists. Sign in instead.",
            errors["owner.email"]![0]!.GetValue<string>());
        Assert.Equal(before, await database.ScalarAsync<long>("SELECT count(*) FROM organizations"));
    }

    // AC-13: race path, caught by the unique-constraint exception mapping
    // rather than the pre-insert check.
    [Fact]
    public async Task PostOrganizationRegistrations_TwoConcurrentRequestsSameEmail_OneSucceedsOneConflictsAndOnlyOneOrganizationExists()
    {
        var email = OrganizationRegistrationApi.NewEmail();
        await using var host = OrganizationRegistrationTestHost.Create(database.ConnectionString);
        var body = OrganizationRegistrationApi.ValidBody(ownerEmail: email);

        var responses = await Task.WhenAll(
            OrganizationRegistrationApi.PostAsync(host.Client, body, clientIp: "203.0.113.60"),
            OrganizationRegistrationApi.PostAsync(host.Client, body, clientIp: "203.0.113.61"));

        Assert.Contains(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Contains(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(1L, await database.ScalarAsync<long>(
            "SELECT count(*) FROM users WHERE email = @email", ("email", email)));
        Assert.Equal(1L, await database.ScalarAsync<long>(
            """
            SELECT count(*) FROM organizations o
            JOIN organization_users ou ON ou.organization_id = o.id
            JOIN users u ON u.id = ou.user_id
            WHERE u.email = @email
            """,
            ("email", email)));
    }
}
