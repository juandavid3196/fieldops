using System.Net;

namespace FieldOps.IntegrationTests.Sessions;

[Collection(SessionsDatabaseCollection.Name)]
public class SignInCredentialTests(SessionsDatabaseFixture database)
{
    private const string Password = SessionsDatabaseFixture.Password;

    [Fact]
    public async Task PostSessions_ValidCredentialsWithSpacedUppercaseEmail_Returns200SessionBody()
    {
        var account = await database.SeedAccountAsync("Acme Field Services");
        await using var host = SessionTestHost.Create(database.ConnectionString);
        var typedEmail = $"  {account.Email.ToUpperInvariant()} ";

        var response = await SessionApi.SignInAsync(host.Client, typedEmail, Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        SessionApi.GetIssuedCookie(response);

        var session = await SessionApi.ReadSessionAsync(response);
        Assert.Equal(new SessionBodyUser(account.UserId, "Ada", "Lovelace", account.Email), session.User);
        Assert.Equal(new SessionBodyOrganization(account.OrganizationId, "Acme Field Services"), session.Organization);
        Assert.Equal(new SessionBodyRole("owner", "Owner"), session.Role);

        using var json = await SessionApi.ReadJsonAsync(response);
        Assert.Equal(
            ["organization", "role", "user"],
            json.RootElement.EnumerateObject().Select(property => property.Name).Order());
    }

    [Fact]
    public async Task PostSessions_EveryCredentialFailure_Returns401WithIdenticalBodyAndNoCookie()
    {
        await using var host = SessionTestHost.Create(database.ConnectionString);
        var attempts = new List<(string Case, string Email, string Password)>
        {
            ("unknown email", SessionsDatabaseFixture.NewEmail(), Password),
            ("wrong password", (await database.SeedAccountAsync()).Email, Password + "x"),
            ("pending user", await SeedUserWithMembershipAsync("pending"), Password),
            ("suspended user", await SeedUserWithMembershipAsync("suspended"), Password),
            ("disabled user", await SeedUserWithMembershipAsync("disabled"), Password),
            ("inactive membership only", await SeedUserWithMembershipAsync(membershipStatus: "suspended"), Password),
            ("inactive organization only", await SeedUserWithMembershipAsync(organizationIsActive: false), Password),
            ("no membership", await SeedUserWithoutMembershipAsync(), Password),
            ("malformed hash", await SeedUserWithMembershipAsync(passwordHash: "pbkdf2-sha256$1$bad$hash"), Password),
        };

        var bodies = new List<string>();

        foreach (var (name, email, password) in attempts)
        {
            var response = await SessionApi.SignInAsync(host.Client, email, password);

            Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"{name}: {response.StatusCode}");
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            SessionApi.AssertNoSessionCookie(response);
            bodies.Add(await SessionApi.ReadProblemWithoutTraceIdAsync(response));
        }

        Assert.Single(bodies.Distinct());
        using var problem = System.Text.Json.JsonDocument.Parse(bodies[0]);
        Assert.Equal(401, problem.RootElement.GetProperty("status").GetInt32());
        Assert.True(problem.RootElement.TryGetProperty("type", out _));
        Assert.True(problem.RootElement.TryGetProperty("title", out _));
        Assert.False(problem.RootElement.TryGetProperty("detail", out _));
        Assert.False(problem.RootElement.TryGetProperty("errors", out _));
    }

    [Fact]
    public async Task PostSessions_SuspendedUserWithCorrectPassword_Returns401NotForbidden()
    {
        var email = await SeedUserWithMembershipAsync("suspended");
        await using var host = SessionTestHost.Create(database.ConnectionString);

        var response = await SessionApi.SignInAsync(host.Client, email, Password);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        SessionApi.AssertNoSessionCookie(response);
    }

    [Fact]
    public async Task PostSessions_UnverifiedEmailOtherwiseEligible_Returns200()
    {
        var email = SessionsDatabaseFixture.NewEmail();
        var organizationId = await database.SeedOrganizationAsync();
        var userId = await database.SeedUserAsync(email, emailVerified: false);
        await database.SeedMembershipAsync(organizationId, userId);
        await using var host = SessionTestHost.Create(database.ConnectionString);

        var response = await SessionApi.SignInAsync(host.Client, email, Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<string> SeedUserWithMembershipAsync(
        string userStatus = "active",
        string membershipStatus = "active",
        bool organizationIsActive = true,
        string? passwordHash = null)
    {
        var email = SessionsDatabaseFixture.NewEmail();
        var organizationId = await database.SeedOrganizationAsync(isActive: organizationIsActive);
        var userId = await database.SeedUserAsync(email, userStatus, passwordHash);
        await database.SeedMembershipAsync(organizationId, userId, status: membershipStatus);
        return email;
    }

    private async Task<string> SeedUserWithoutMembershipAsync()
    {
        var email = SessionsDatabaseFixture.NewEmail();
        await database.SeedUserAsync(email);
        return email;
    }
}
