using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.Sessions;

namespace FieldOps.IntegrationTests.Account;

/// <summary>
/// POST /account/password (account settings FR-11, BR-02, BR-05, BR-06, BR-07; AC-19 to AC-22).
/// </summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class AccountPasswordTests(CompanySettingsDatabaseFixture database)
{
    private const string PasswordPath = "/account/password";

    private const string NewPassword = "A brand new passphrase 42";

    private const string CurrentPassword = CompanySettingsDatabaseFixture.Password;

    // AC-19: the current session continues, every earlier one is revoked, audit with client IP, no secrets leak.
    [Fact]
    public async Task ChangePassword_ValidCurrent_ReissuesCurrentCookieRevokesEarlierSessionsAndAudits()
    {
        var account = await database.SeedAccountAsync();
        var time = new MutableTimeProvider(DateTimeOffset.UtcNow);
        await using var host = SessionTestHost.Create(database.ConnectionString, time, captureLogs: true);

        var earlierCookie = SessionApi.GetIssuedCookie(
            await SessionApi.SignInAsync(host.Client, account.Email, CurrentPassword));
        time.Advance(TimeSpan.FromSeconds(1));
        var currentCookie = SessionApi.GetIssuedCookie(
            await SessionApi.SignInAsync(host.Client, account.Email, CurrentPassword, rememberMe: true));
        time.Advance(TimeSpan.FromMinutes(1));

        var response = await Change(host.Client, currentCookie, CurrentPassword, NewPassword);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Empty(await response.Content.ReadAsStringAsync());

        // Remember-me persistence is kept: the re-issued cookie is persistent.
        var reissued = Assert.Single(SessionApi.GetSessionSetCookies(response));
        Assert.NotNull(reissued.Expires);
        var newCookie = $"{reissued.Name}={reissued.Value}";

        var current = await SessionApi.GetCurrentAsync(host.Client, newCookie);
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        Assert.Equal(account.OrganizationId, (await SessionApi.ReadSessionAsync(current)).Organization.Id);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SessionApi.GetCurrentAsync(host.Client, currentCookie)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SessionApi.GetCurrentAsync(host.Client, earlierCookie)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await SessionApi.SignInAsync(host.Client, account.Email, NewPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SessionApi.SignInAsync(host.Client, account.Email, CurrentPassword)).StatusCode);

        var changedAt = await database.ScalarAsync<DateTimeOffset>(
            "SELECT password_changed_at FROM users WHERE id = @id", ("id", account.UserId));
        var now = time.GetUtcNow();
        Assert.Equal(now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMillisecond)), changedAt);

        Assert.Equal(1, await database.CountAuditLogsAsync(account.OrganizationId, "auth.password_changed"));
        Assert.True(await database.ScalarAsync<bool>(
            """
            SELECT ip_address IS NOT NULL AND entity_type = 'user' AND entity_id = @u AND actor_user_id = @u
                AND before_data IS NULL AND after_data IS NULL AND metadata::text = '{}'
            FROM audit_logs WHERE organization_id = @o AND action = 'auth.password_changed'
            """,
            ("u", account.UserId),
            ("o", account.OrganizationId)));

        foreach (var entry in host.Logs!.Entries)
        {
            var text = string.Join(
                "\n",
                new[] { entry.Message, entry.Exception?.ToString() }
                    .Concat(entry.Properties.Values.Select(value => value?.ToString())));
            Assert.DoesNotContain(NewPassword, text);
            Assert.DoesNotContain(CurrentPassword, text);
            Assert.DoesNotContain("pbkdf2", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    // AC-20, AC-21, AC-22: rule order, field errors, the per-user failure window and its independence.
    [Fact]
    public async Task ChangePassword_RejectionsAndFailureWindow_FollowRuleOrderAndLimitOnlyThatUser()
    {
        var account = await database.SeedAccountAsync();
        var other = await database.SeedAccountAsync();
        var third = await database.SeedAccountAsync();
        var time = new MutableTimeProvider(DateTimeOffset.UtcNow);
        await using var host = SessionTestHost.Create(database.ConnectionString, time);
        var cookie = SessionApi.GetIssuedCookie(await SessionApi.SignInAsync(host.Client, account.Email, CurrentPassword));
        var otherCookie = SessionApi.GetIssuedCookie(await SessionApi.SignInAsync(host.Client, other.Email, CurrentPassword));
        var thirdCookie = SessionApi.GetIssuedCookie(await SessionApi.SignInAsync(host.Client, third.Email, CurrentPassword));
        time.Advance(TimeSpan.FromMinutes(1));

        // Step 2 and 4 failures count nothing and change nothing.
        (string Key, string Message, string? Current, string? New)[] policy =
        [
            ("currentPassword", "This field is required.", null, NewPassword),
            ("newPassword", "This field is required.", CurrentPassword, null),
            ("newPassword", "Use 12 to 128 characters.", CurrentPassword, "short-pass"),
            ("newPassword", "Use 12 to 128 characters.", CurrentPassword, new string('p', 129)),
            ("newPassword", "Choose a password that is different from your email.", CurrentPassword, account.Email.ToUpperInvariant()),
            ("newPassword", "Choose a password that is different from your current password.", CurrentPassword, CurrentPassword),
        ];

        foreach (var (key, message, currentValue, newValue) in policy)
        {
            var rejected = await Change(host.Client, cookie, currentValue, newValue);
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            using var problem = await SessionApi.ReadJsonAsync(rejected);
            Assert.Equal(message, problem.RootElement.GetProperty("errors").GetProperty(key).EnumerateArray().Single().GetString());
        }

        var malformed = await CompanySettingsApi.SendRawAsync(host.Client, HttpMethod.Post, PasswordPath, "{", cookie);
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        var wrongType = await CompanySettingsApi.SendRawAsync(
            host.Client, HttpMethod.Post, PasswordPath, "currentPassword=x", cookie, "text/plain");
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, wrongType.StatusCode);

        // Five wrong current passwords are counted; none of the above was.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var wrong = await Change(host.Client, cookie, "Not the current password", NewPassword);
            Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
            using var problem = await SessionApi.ReadJsonAsync(wrong);
            Assert.Equal(
                "Your current password is incorrect.",
                problem.RootElement.GetProperty("errors").GetProperty("currentPassword").EnumerateArray().Single().GetString());
        }

        // Limited: even the correct current password, or an unreadable body, gets 429 with Retry-After.
        var limited = await Change(host.Client, cookie, CurrentPassword, NewPassword);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        var retryAfter = int.Parse(Assert.Single(limited.Headers.GetValues("Retry-After")), System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(retryAfter, 1, 900);
        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            (await CompanySettingsApi.SendRawAsync(host.Client, HttpMethod.Post, PasswordPath, "{", cookie)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SessionApi.SignInAsync(host.Client, account.Email, CurrentPassword)).StatusCode);
        Assert.True(await database.ScalarAsync<bool>(
            "SELECT password_changed_at IS NULL FROM users WHERE id = @id", ("id", account.UserId)));

        // Another user is not limited.
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await Change(host.Client, otherCookie, "Not the current password", NewPassword)).StatusCode);

        // A success does not reset the count: 2 failures, success, 3 failures, then limited.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await Change(host.Client, thirdCookie, "Not the current password", NewPassword);
        }

        time.Advance(TimeSpan.FromSeconds(1));
        var success = await Change(host.Client, thirdCookie, CurrentPassword, NewPassword);
        Assert.Equal(HttpStatusCode.NoContent, success.StatusCode);
        var reissued = Assert.Single(SessionApi.GetSessionSetCookies(success));
        var thirdNewCookie = $"{reissued.Name}={reissued.Value}";

        for (var attempt = 0; attempt < 3; attempt++)
        {
            Assert.Equal(
                HttpStatusCode.BadRequest,
                (await Change(host.Client, thirdNewCookie, "Not the current password", "Another new passphrase 7")).StatusCode);
        }

        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            (await Change(host.Client, thirdNewCookie, NewPassword, "Another new passphrase 7")).StatusCode);

        // The window ends after 15 minutes.
        time.Advance(TimeSpan.FromMinutes(16));
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await Change(host.Client, thirdNewCookie, NewPassword, "Another new passphrase 7")).StatusCode);
    }

    private static Task<HttpResponseMessage> Change(HttpClient client, string cookie, string? current, string? replacement) =>
        CompanySettingsApi.PostAsync(
            client,
            PasswordPath,
            new JsonObject { ["currentPassword"] = current, ["newPassword"] = replacement },
            cookie);
}
