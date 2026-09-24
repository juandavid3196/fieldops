using System.Net;
using System.Text.Json.Nodes;

namespace FieldOps.IntegrationTests.Sessions;

[Collection(SessionsDatabaseCollection.Name)]
public class OrganizationResolutionTests(SessionsDatabaseFixture database)
{
    private const string Password = SessionsDatabaseFixture.Password;

    [Fact]
    public async Task PostSessions_TwoActiveMemberships_ResolvesEarliestJoinedOrganization()
    {
        var email = SessionsDatabaseFixture.NewEmail();
        var userId = await database.SeedUserAsync(email);
        var organizationA = await database.SeedOrganizationAsync();
        var organizationB = await database.SeedOrganizationAsync();
        await database.SeedMembershipAsync(organizationA, userId, joinedAt: Utc(2026, 1, 1));
        await database.SeedMembershipAsync(organizationB, userId, roleId: 2, joinedAt: Utc(2025, 1, 1));
        await using var host = SessionTestHost.Create(database.ConnectionString);

        var response = await SessionApi.SignInAsync(host.Client, email, Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var session = await SessionApi.ReadSessionAsync(response);
        Assert.Equal(organizationB, session.Organization.Id);
        Assert.Equal(new SessionBodyRole("dispatcher", "Dispatcher"), session.Role);
    }

    [Fact]
    public async Task PostSessions_EarlierMembershipInInactiveOrganization_ResolvesActiveOrganization()
    {
        var email = SessionsDatabaseFixture.NewEmail();
        var userId = await database.SeedUserAsync(email);
        var activeA = await database.SeedOrganizationAsync();
        var inactiveB = await database.SeedOrganizationAsync(isActive: false);
        await database.SeedMembershipAsync(inactiveB, userId, joinedAt: Utc(2025, 1, 1));
        await database.SeedMembershipAsync(activeA, userId, joinedAt: Utc(2026, 1, 1));
        await using var host = SessionTestHost.Create(database.ConnectionString);

        var response = await SessionApi.SignInAsync(host.Client, email, Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(activeA, (await SessionApi.ReadSessionAsync(response)).Organization.Id);
    }

    [Fact]
    public async Task PostSessions_ClientSuppliedIdentifiers_AreIgnored()
    {
        var account = await database.SeedAccountAsync();
        var otherAccount = await database.SeedAccountAsync();
        await using var host = SessionTestHost.Create(database.ConnectionString);

        var body = new JsonObject
        {
            ["email"] = account.Email,
            ["password"] = Password,
            ["organizationId"] = otherAccount.OrganizationId,
            ["membershipId"] = otherAccount.MembershipId,
            ["userId"] = otherAccount.UserId,
            ["roleId"] = 6,
        };

        var response = await SessionApi.PostSessionAsync(
            host.Client,
            body.ToJsonString(),
            headers: [new("X-Organization-Id", otherAccount.OrganizationId.ToString())]);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var session = await SessionApi.ReadSessionAsync(response);
        Assert.Equal(account.OrganizationId, session.Organization.Id);
        Assert.Equal(account.UserId, session.User.Id);
        Assert.Equal("owner", session.Role.Code);

        // The session keeps resolving to the BR-08 organization afterwards.
        var current = await SessionApi.GetCurrentAsync(host.Client, SessionApi.GetIssuedCookie(response));
        Assert.Equal(account.OrganizationId, (await SessionApi.ReadSessionAsync(current)).Organization.Id);
    }

    private static DateTimeOffset Utc(int year, int month, int day) =>
        new(year, month, day, 0, 0, 0, TimeSpan.Zero);
}
