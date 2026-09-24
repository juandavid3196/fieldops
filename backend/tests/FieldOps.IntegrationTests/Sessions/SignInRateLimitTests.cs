using System.Net;
using FieldOps.IntegrationTests.Api;

namespace FieldOps.IntegrationTests.Sessions;

/// <summary>
/// Per-IP and global limits need no database: invalid bodies are rejected
/// with 400 but still count toward the limits.
/// </summary>
public class SignInRateLimitTests
{
    private const string InvalidBody = "{}";

    [Fact]
    public async Task PostSessions_11thRequestFromOneIpInWindow_Returns429WithRetryAfterAndCors()
    {
        await using var host = SessionTestHost.Create();
        var clientIp = SessionApi.NewClientIp();

        for (var i = 0; i < 10; i++)
        {
            var allowed = await SessionApi.PostSessionAsync(host.Client, InvalidBody, clientIp: clientIp);
            Assert.Equal(HttpStatusCode.BadRequest, allowed.StatusCode);
        }

        var response = await SessionApi.PostSessionAsync(
            host.Client,
            InvalidBody,
            clientIp: clientIp,
            origin: FieldOpsApiFactory.AllowedOrigin);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        AssertRetryAfter(response, maxSeconds: 300);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(FieldOpsApiFactory.AllowedOrigin, Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.Equal("true", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Credentials")));

        // Other clients are not affected by this client's limit.
        var other = await SessionApi.PostSessionAsync(host.Client, InvalidBody);
        Assert.Equal(HttpStatusCode.BadRequest, other.StatusCode);
    }

    [Fact]
    public async Task PostSessions_After300RequestsInOneMinute_Returns429ForAnyClient()
    {
        await using var host = SessionTestHost.Create();

        for (var client = 0; client < 30; client++)
        {
            var clientIp = SessionApi.NewClientIp();

            for (var i = 0; i < 10; i++)
            {
                var allowed = await SessionApi.PostSessionAsync(host.Client, InvalidBody, clientIp: clientIp);
                Assert.Equal(HttpStatusCode.BadRequest, allowed.StatusCode);
            }
        }

        var response = await SessionApi.PostSessionAsync(host.Client, InvalidBody, clientIp: SessionApi.NewClientIp());

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        AssertRetryAfter(response, maxSeconds: 60);

        // The global limit applies only to POST /sessions.
        var health = await host.Client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    internal static void AssertRetryAfter(HttpResponseMessage response, int maxSeconds)
    {
        var value = Assert.Single(response.Headers.GetValues("Retry-After"));
        var seconds = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(seconds, 1, maxSeconds);
    }
}

[Collection(SessionsDatabaseCollection.Name)]
public class SignInEmailThrottleTests(SessionsDatabaseFixture database)
{
    private static readonly DateTimeOffset Start = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task PostSessions_SixthAttemptAfterFiveFailures_Returns429WithoutSignIn()
    {
        var account = await database.SeedAccountAsync();
        await using var host = SessionTestHost.Create(database.ConnectionString, new MutableTimeProvider(Start));

        await FailAsync(host, account.Email, 5);

        var response = await SessionApi.SignInAsync(host.Client, account.Email, SessionsDatabaseFixture.Password);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        // Failures at 0..4 s; the oldest leaves the window at 15:00, 896 s from now.
        Assert.Equal("896", Assert.Single(response.Headers.GetValues("Retry-After")));
        SessionApi.AssertNoSessionCookie(response);
        Assert.Null(await database.ScalarAsync<DateTime?>(
            "SELECT last_login_at FROM users WHERE id = @id",
            ("id", account.UserId)));

        // Throttled per email, not per IP: the same email is refused from anywhere
        // until the oldest failure leaves the 15-minute window.
        host.Time!.Advance(TimeSpan.FromSeconds(895));
        var stillThrottled = await SessionApi.SignInAsync(host.Client, account.Email, SessionsDatabaseFixture.Password);
        Assert.Equal(HttpStatusCode.TooManyRequests, stillThrottled.StatusCode);

        host.Time.Advance(TimeSpan.FromSeconds(1));
        var allowed = await SessionApi.SignInAsync(host.Client, account.Email, SessionsDatabaseFixture.Password);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    [Fact]
    public async Task PostSessions_UnknownEmailAfterFiveFailures_Returns429LikeKnownEmail()
    {
        var account = await database.SeedAccountAsync();
        var unknownEmail = SessionsDatabaseFixture.NewEmail();
        await using var host = SessionTestHost.Create(database.ConnectionString, new MutableTimeProvider(Start));

        await FailAsync(host, account.Email, 5);
        await FailAsync(host, unknownEmail, 5);

        var known = await SessionApi.SignInAsync(host.Client, account.Email, SessionsDatabaseFixture.Password);
        var unknown = await SessionApi.SignInAsync(host.Client, unknownEmail, "any password");

        Assert.Equal(HttpStatusCode.TooManyRequests, known.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, unknown.StatusCode);
        Assert.True(unknown.Headers.Contains("Retry-After"));
        Assert.Equal(
            await SessionApi.ReadProblemWithoutTraceIdAsync(known),
            await SessionApi.ReadProblemWithoutTraceIdAsync(unknown));
    }

    [Fact]
    public async Task PostSessions_FiveFailuresAfterSuccessfulSignIn_FifthIs401()
    {
        var account = await database.SeedAccountAsync();
        await using var host = SessionTestHost.Create(database.ConnectionString, new MutableTimeProvider(Start));

        await FailAsync(host, account.Email, 4);
        var success = await SessionApi.SignInAsync(host.Client, account.Email, SessionsDatabaseFixture.Password);
        Assert.Equal(HttpStatusCode.OK, success.StatusCode);

        var statuses = new List<HttpStatusCode>();

        for (var i = 0; i < 5; i++)
        {
            statuses.Add((await SessionApi.SignInAsync(host.Client, account.Email, "wrong password")).StatusCode);
        }

        Assert.All(statuses, status => Assert.Equal(HttpStatusCode.Unauthorized, status));
    }

    [Fact]
    public async Task PostSessions_InvalidRequests_DoNotCountAsFailures()
    {
        var account = await database.SeedAccountAsync();
        await using var host = SessionTestHost.Create(database.ConnectionString, new MutableTimeProvider(Start));

        for (var i = 0; i < 6; i++)
        {
            var invalid = await SessionApi.SignInAsync(host.Client, account.Email, string.Empty);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }

        var response = await SessionApi.SignInAsync(host.Client, account.Email, SessionsDatabaseFixture.Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // Each attempt comes from a different IP and one second apart.
    private static async Task FailAsync(SessionTestHost host, string email, int count)
    {
        for (var i = 0; i < count; i++)
        {
            if (i > 0)
            {
                host.Time!.Advance(TimeSpan.FromSeconds(1));
            }

            var response = await SessionApi.SignInAsync(host.Client, email, "wrong password");
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }
}
