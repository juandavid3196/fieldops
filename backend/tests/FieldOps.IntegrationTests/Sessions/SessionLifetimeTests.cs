using System.Net;

namespace FieldOps.IntegrationTests.Sessions;

[Collection(SessionsDatabaseCollection.Name)]
public class SessionLifetimeTests(SessionsDatabaseFixture database)
{
    private static readonly DateTimeOffset SignInTime = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetCurrentSession_BrowserSessionJustBefore8Hours_Returns200WithoutRenewal()
    {
        var (host, cookie) = await SignInAsync(rememberMe: false);
        await using var _ = host;

        host.Time!.Advance(TimeSpan.FromHours(8) - TimeSpan.FromMinutes(1));
        var response = await SessionApi.GetCurrentAsync(host.Client, cookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        SessionApi.AssertNoSessionCookie(response);
    }

    [Fact]
    public async Task GetCurrentSession_BrowserSession8HoursAfterSignIn_Returns401()
    {
        var (host, cookie) = await SignInAsync(rememberMe: false);
        await using var _ = host;

        host.Time!.Advance(TimeSpan.FromHours(8));
        var response = await SessionApi.GetCurrentAsync(host.Client, cookie);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        SessionApi.AssertCookieDeleted(response);
    }

    [Fact]
    public async Task GetCurrentSession_RememberMeAfter14IdleDays_Returns401()
    {
        var (host, cookie) = await SignInAsync(rememberMe: true);
        await using var _ = host;

        host.Time!.Advance(TimeSpan.FromDays(14) + TimeSpan.FromMinutes(1));
        var response = await SessionApi.GetCurrentAsync(host.Client, cookie);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        SessionApi.AssertCookieDeleted(response);
    }

    [Fact]
    public async Task GetCurrentSession_RememberMeUsedDaily_RenewsUntil30DaysThenReturns401()
    {
        var (host, cookie) = await SignInAsync(rememberMe: true);
        await using var _ = host;

        for (var day = 1; day < 30; day++)
        {
            host.Time!.Advance(TimeSpan.FromDays(1));
            var response = await SessionApi.GetCurrentAsync(host.Client, cookie);

            Assert.True(response.StatusCode == HttpStatusCode.OK, $"Day {day}: {response.StatusCode}");

            // Every authenticated request slides the 14-day window.
            var renewed = Assert.Single(SessionApi.GetSessionSetCookies(response));
            Assert.Equal(host.Time.GetUtcNow().AddDays(14), renewed.Expires);
            cookie = SessionApi.GetIssuedCookie(response);
        }

        host.Time!.Advance(TimeSpan.FromDays(1));
        var expired = await SessionApi.GetCurrentAsync(host.Client, cookie);

        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
        SessionApi.AssertCookieDeleted(expired);
    }

    private async Task<(SessionTestHost Host, string Cookie)> SignInAsync(bool rememberMe)
    {
        var account = await database.SeedAccountAsync();
        var host = SessionTestHost.Create(database.ConnectionString, new MutableTimeProvider(SignInTime));

        var response = await SessionApi.SignInAsync(
            host.Client,
            account.Email,
            SessionsDatabaseFixture.Password,
            rememberMe);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (host, SessionApi.GetIssuedCookie(response));
    }
}
