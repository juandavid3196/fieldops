using System.Net;

namespace FieldOps.IntegrationTests.Sessions;

[Collection(SessionsDatabaseCollection.Name)]
public class SessionEndpointTests(SessionsDatabaseFixture database)
{
    [Fact]
    public async Task DeleteCurrentSession_SignedIn_Returns204AndExpiresCookie()
    {
        var account = await database.SeedAccountAsync();
        await using var host = SessionTestHost.Create(database.ConnectionString);
        var signIn = await SessionApi.SignInAsync(host.Client, account.Email, SessionsDatabaseFixture.Password, rememberMe: true);
        var cookie = SessionApi.GetIssuedCookie(signIn);

        var response = await SessionApi.DeleteCurrentAsync(host.Client, cookie);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        SessionApi.AssertCookieDeleted(response);
    }

    [Fact]
    public async Task GetCurrentSession_SignedIn_Returns200WithNoStore()
    {
        var account = await database.SeedAccountAsync();
        await using var host = SessionTestHost.Create(database.ConnectionString);
        var signIn = await SessionApi.SignInAsync(host.Client, account.Email, SessionsDatabaseFixture.Password);

        var response = await SessionApi.GetCurrentAsync(host.Client, SessionApi.GetIssuedCookie(signIn));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var session = await SessionApi.ReadSessionAsync(response);
        Assert.Equal(account.UserId, session.User.Id);
        Assert.Equal(account.OrganizationId, session.Organization.Id);
    }
}

/// <summary>
/// Anonymous calls that never reach the database.
/// </summary>
public class AnonymousSessionEndpointTests
{
    [Fact]
    public async Task DeleteCurrentSession_WithoutSession_Returns204()
    {
        await using var host = SessionTestHost.Create();

        var response = await SessionApi.DeleteCurrentAsync(host.Client);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        SessionApi.AssertCookieDeleted(response);
    }

    [Fact]
    public async Task GetCurrentSession_WithoutSession_Returns401ProblemWithoutRedirect()
    {
        await using var host = SessionTestHost.Create();

        var response = await SessionApi.GetCurrentAsync(host.Client);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Null(response.Headers.Location);
        SessionApi.AssertNoSessionCookie(response);

        using var problem = await SessionApi.ReadJsonAsync(response);
        Assert.Equal(401, problem.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(problem.RootElement.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task GetCurrentSession_WithoutSession_Returns401WithNoStore()
    {
        await using var host = SessionTestHost.Create();

        var response = await SessionApi.GetCurrentAsync(host.Client);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task GetCurrentSession_GarbageCookie_Returns401AndDeletesCookie()
    {
        await using var host = SessionTestHost.Create();

        var response = await SessionApi.GetCurrentAsync(host.Client, "fieldops_session=not-a-ticket");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        SessionApi.AssertCookieDeleted(response);
    }
}
