using System.Net;
using FieldOps.Api.Controllers;
using Microsoft.Extensions.Logging;

namespace FieldOps.IntegrationTests.Sessions;

[Collection(SessionsDatabaseCollection.Name)]
public class SignInLoggingTests(SessionsDatabaseFixture database)
{
    [Fact]
    public async Task PostSessions_CorrectWrongAndThrottledAttempts_NeverLogSensitiveValues()
    {
        var account = await database.SeedAccountAsync();
        var unknownEmail = SessionsDatabaseFixture.NewEmail();
        await using var host = SessionTestHost.Create(database.ConnectionString, captureLogs: true);
        const string wrongPassword = "Wrong password 9";

        var success = await SessionApi.SignInAsync(host.Client, account.Email, SessionsDatabaseFixture.Password, rememberMe: true);
        Assert.Equal(HttpStatusCode.OK, success.StatusCode);
        var cookie = SessionApi.GetIssuedCookie(success);
        Assert.Equal(HttpStatusCode.OK, (await SessionApi.GetCurrentAsync(host.Client, cookie)).StatusCode);

        for (var i = 0; i < 5; i++)
        {
            await SessionApi.SignInAsync(host.Client, account.Email, wrongPassword);
            await SessionApi.SignInAsync(host.Client, unknownEmail, wrongPassword);
        }

        var throttled = await SessionApi.SignInAsync(host.Client, account.Email, SessionsDatabaseFixture.Password);
        Assert.Equal(HttpStatusCode.TooManyRequests, throttled.StatusCode);

        var cookieValue = cookie["fieldops_session=".Length..];
        var forbidden = new[]
        {
            account.Email,
            unknownEmail,
            "example.com",
            SessionsDatabaseFixture.Password,
            wrongPassword,
            database.PasswordHash,
            "pbkdf2-sha256",
            cookieValue,
        };

        var entries = host.Logs!.Entries;
        Assert.NotEmpty(entries);

        foreach (var entry in entries)
        {
            var text = string.Join(
                "\n",
                new[] { entry.Message, entry.Exception?.ToString() }
                    .Concat(entry.Properties.Values.Select(value => value?.ToString())));

            foreach (var value in forbidden)
            {
                Assert.DoesNotContain(value, text, StringComparison.OrdinalIgnoreCase);
            }
        }

        // Failures are logged with the category and, when known, the user id only.
        var failures = entries
            .Where(entry => entry.Category == typeof(SessionsController).FullName && entry.Level == LogLevel.Warning)
            .ToList();
        Assert.Equal(10, failures.Count);
        Assert.Contains(failures, entry => Equals(entry.Properties["UserId"], account.UserId));
        Assert.Contains(failures, entry => entry.Properties["UserId"] is null);
        Assert.All(failures, entry => Assert.StartsWith("Sign-in failed: ", entry.Message));
    }
}
