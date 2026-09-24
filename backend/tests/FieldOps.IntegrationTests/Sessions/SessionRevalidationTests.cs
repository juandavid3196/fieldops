using System.Net;

namespace FieldOps.IntegrationTests.Sessions;

[Collection(SessionsDatabaseCollection.Name)]
public class SessionRevalidationTests(SessionsDatabaseFixture database)
{
    [Fact]
    public async Task GetCurrentSession_MembershipSuspendedAfterSignIn_Returns401AndDeletesCookie()
    {
        var (account, host, cookie) = await SignInAsync();
        await using var _ = host;

        await database.ExecuteAsync(
            "UPDATE organization_users SET status = 'suspended' WHERE id = @id",
            ("id", account.MembershipId));

        var response = await SessionApi.GetCurrentAsync(host.Client, cookie);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        SessionApi.AssertCookieDeleted(response);
    }

    [Theory]
    [InlineData("UPDATE organizations SET is_active = false WHERE id = @organizationId")]
    [InlineData("UPDATE users SET status = 'disabled' WHERE id = @userId")]
    [InlineData("UPDATE users SET status = 'suspended' WHERE id = @userId")]
    public async Task GetCurrentSession_OrganizationOrUserNoLongerActive_Returns401AndDeletesCookie(string sql)
    {
        var (account, host, cookie) = await SignInAsync();
        await using var _ = host;

        await database.ExecuteAsync(
            sql,
            ("organizationId", account.OrganizationId),
            ("userId", account.UserId));

        var response = await SessionApi.GetCurrentAsync(host.Client, cookie);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        SessionApi.AssertCookieDeleted(response);
    }

    [Fact]
    public async Task GetCurrentSession_MembershipRoleChanged_ReturnsNewRole()
    {
        var (account, host, cookie) = await SignInAsync();
        await using var _ = host;

        await database.ExecuteAsync(
            "UPDATE organization_users SET role_id = 3 WHERE id = @id",
            ("id", account.MembershipId));

        var response = await SessionApi.GetCurrentAsync(host.Client, cookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new SessionBodyRole("technician", "Technician"), (await SessionApi.ReadSessionAsync(response)).Role);
    }

    [Fact]
    public async Task GetCurrentSession_TamperedCookie_Returns401AndDeletesCookie()
    {
        var (_, host, cookie) = await SignInAsync();
        await using var _host = host;

        var response = await SessionApi.GetCurrentAsync(host.Client, Tamper(cookie));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        SessionApi.AssertCookieDeleted(response);
    }

    [Fact]
    public async Task GetCurrentSession_CookieFromAnotherKeyRing_Returns401AndDeletesCookie()
    {
        var (_, host, cookie) = await SignInAsync();
        await using var _host = host;
        await using var otherHost = SessionTestHost.Create(database.ConnectionString);

        var response = await SessionApi.GetCurrentAsync(otherHost.Client, cookie);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        SessionApi.AssertCookieDeleted(response);

        // The issuing host still accepts it.
        Assert.Equal(HttpStatusCode.OK, (await SessionApi.GetCurrentAsync(host.Client, cookie)).StatusCode);
    }

    [Fact]
    public async Task GetCurrentSession_OrganizationHeaderForAnotherOrganization_ReturnsOwnOrganizationOnly()
    {
        var (account, host, cookie) = await SignInAsync();
        await using var _ = host;
        var otherAccount = await database.SeedAccountAsync();

        var response = await SessionApi.GetCurrentAsync(
            host.Client,
            cookie,
            headers: [new("X-Organization-Id", otherAccount.OrganizationId.ToString())]);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(account.OrganizationId.ToString(), body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(otherAccount.OrganizationId.ToString(), body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PostSessions_FailedSignInWithTamperedCookie_DeletesCookie()
    {
        var (account, host, cookie) = await SignInAsync();
        await using var _ = host;

        var response = await SessionApi.SignInAsync(
            host.Client,
            account.Email,
            "wrong password",
            cookie: Tamper(cookie));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        SessionApi.AssertCookieDeleted(response);
    }

    [Fact]
    public async Task PostSessions_SuccessfulSignInWithTamperedCookie_SetsOnlyNewCookie()
    {
        var (account, host, cookie) = await SignInAsync();
        await using var _ = host;

        var response = await SessionApi.SignInAsync(
            host.Client,
            account.Email,
            SessionsDatabaseFixture.Password,
            cookie: Tamper(cookie));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var newCookie = SessionApi.GetIssuedCookie(response);
        Assert.Equal(HttpStatusCode.OK, (await SessionApi.GetCurrentAsync(host.Client, newCookie)).StatusCode);
    }

    [Fact]
    public async Task AnonymousEndpoint_WithInvalidatedSessionCookie_DeletesCookie()
    {
        var (account, host, cookie) = await SignInAsync();
        await using var _ = host;

        await database.ExecuteAsync(
            "UPDATE users SET status = 'disabled' WHERE id = @id",
            ("id", account.UserId));

        var health = await SessionApi.SendAsync(
            host.Client,
            new HttpRequestMessage(HttpMethod.Get, "/health/live"),
            clientIp: null,
            cookie,
            origin: null,
            headers: null);

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        SessionApi.AssertCookieDeleted(health);
    }

    [Fact]
    public async Task AnonymousEndpoint_WithValidSessionCookie_KeepsCookie()
    {
        var (_, host, cookie) = await SignInAsync();
        await using var _host = host;

        var health = await SessionApi.SendAsync(
            host.Client,
            new HttpRequestMessage(HttpMethod.Get, "/health/live"),
            clientIp: null,
            cookie,
            origin: null,
            headers: null);

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        SessionApi.AssertNoSessionCookie(health);
    }

    private static string Tamper(string cookie)
    {
        // Changes one character in the middle of the protected payload.
        var index = cookie.Length / 2;
        var replacement = cookie[index] == 'A' ? 'B' : 'A';
        return string.Concat(cookie.AsSpan(0, index), replacement.ToString(), cookie.AsSpan(index + 1));
    }

    private async Task<(SeededAccount Account, SessionTestHost Host, string Cookie)> SignInAsync()
    {
        var account = await database.SeedAccountAsync();
        var host = SessionTestHost.Create(database.ConnectionString);

        var response = await SessionApi.SignInAsync(host.Client, account.Email, SessionsDatabaseFixture.Password);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (account, host, SessionApi.GetIssuedCookie(response));
    }
}
