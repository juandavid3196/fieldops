using System.Net;
using Microsoft.Net.Http.Headers;

namespace FieldOps.IntegrationTests.Sessions;

[Collection(SessionsDatabaseCollection.Name)]
public class SessionCookieTests(SessionsDatabaseFixture database)
{
    private static readonly DateTimeOffset SignInTime = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task PostSessions_RememberMeFalse_SetsBrowserSessionCookie()
    {
        var account = await database.SeedAccountAsync();
        await using var host = SessionTestHost.Create(database.ConnectionString);

        var response = await SessionApi.SignInAsync(host.Client, account.Email, SessionsDatabaseFixture.Password, rememberMe: false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookie = Assert.Single(SessionApi.GetSessionSetCookies(response));
        Assert.Null(cookie.Expires);
        Assert.Null(cookie.MaxAge);
        AssertNoExpiryAttributes(response);
    }

    [Fact]
    public async Task PostSessions_RememberMeOmitted_SetsBrowserSessionCookie()
    {
        var account = await database.SeedAccountAsync();
        await using var host = SessionTestHost.Create(database.ConnectionString);

        var response = await SessionApi.SignInAsync(host.Client, account.Email, SessionsDatabaseFixture.Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertNoExpiryAttributes(response);
    }

    [Fact]
    public async Task PostSessions_RememberMeTrue_SetsPersistentCookieExpiringIn14Days()
    {
        var account = await database.SeedAccountAsync();
        await using var host = SessionTestHost.Create(database.ConnectionString, new MutableTimeProvider(SignInTime));

        var response = await SessionApi.SignInAsync(host.Client, account.Email, SessionsDatabaseFixture.Password, rememberMe: true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookie = Assert.Single(SessionApi.GetSessionSetCookies(response));
        Assert.Equal(SignInTime.AddDays(14), cookie.Expires);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PostSessions_ValidCredentials_SetsHttpOnlySecureStrictRootCookie(bool rememberMe)
    {
        var account = await database.SeedAccountAsync();
        await using var host = SessionTestHost.Create(database.ConnectionString);

        var response = await SessionApi.SignInAsync(host.Client, account.Email, SessionsDatabaseFixture.Password, rememberMe);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookie = Assert.Single(SessionApi.GetSessionSetCookies(response));
        Assert.Equal("fieldops_session", cookie.Name.ToString());
        Assert.False(string.IsNullOrEmpty(cookie.Value.ToString()));
        Assert.True(cookie.HttpOnly);
        Assert.True(cookie.Secure);
        Assert.Equal(SameSiteMode.Strict, cookie.SameSite);
        Assert.Equal("/", cookie.Path.ToString());
        Assert.True(cookie.Domain.Length == 0);
    }

    [Fact]
    public async Task PostSessions_ValidCredentials_CookieHoldsNoPlainIdentityData()
    {
        var account = await database.SeedAccountAsync();
        await using var host = SessionTestHost.Create(database.ConnectionString);

        var response = await SessionApi.SignInAsync(host.Client, account.Email, SessionsDatabaseFixture.Password);

        var value = SessionApi.GetIssuedCookie(response);
        Assert.DoesNotContain("example.com", value, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(account.UserId.ToString(), value, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("owner", value, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertNoExpiryAttributes(HttpResponseMessage response)
    {
        var header = Assert.Single(
            response.Headers.GetValues(HeaderNames.SetCookie),
            value => value.StartsWith("fieldops_session=", StringComparison.Ordinal));
        Assert.DoesNotContain("expires=", header, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("max-age=", header, StringComparison.OrdinalIgnoreCase);
    }
}
