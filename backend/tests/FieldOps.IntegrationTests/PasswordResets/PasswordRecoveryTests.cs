using System.Net;
using System.Text.Json.Nodes;
using FieldOps.Application.Features.Users;
using FieldOps.Infrastructure.Authentication;
using FieldOps.IntegrationTests.Api;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.Sessions;
using FieldOps.IntegrationTests.Users;

namespace FieldOps.IntegrationTests.PasswordResets;

/// <summary>
/// Password recovery: request, validate, confirm, global session revocation
/// and the email port (AC-01 to AC-13, AC-16). Each test groups one behavior
/// and risk; no response reveals whether an account exists.
/// </summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class PasswordRecoveryTests(CompanySettingsDatabaseFixture database)
{
    private const string NewPassword = "A brand new password 1";

    [Fact]
    public async Task Request_EligibleUserStoresHashedTokenAndEmailsOnceAndEveryOtherOutcomeIsTheSameNeutralResponse()
    {
        await using var host = PasswordResetHost.Create(database);
        var (userId, email) = await database.SeedRecoveryUserAsync(email: $"alex-{Guid.NewGuid():N}@example.com");

        // AC-01: normalized lookup, one message, hash-only row.
        var created = await host.RequestAsync($"  {email.ToUpperInvariant()} ");
        Assert.Equal(HttpStatusCode.Accepted, created.StatusCode);
        Assert.Equal(string.Empty, await created.Content.ReadAsStringAsync());
        PasswordResetSeed.AssertNoStore(created);
        var neutral = await PasswordResetHost.SignatureAsync(created);

        await host.Sender.WaitForAttemptsAsync(1);
        var message = Assert.Single(host.Sender.Messages);
        var token = RecordingEmailSender.TokenOf(message);
        Assert.Equal(email, message.To);
        Assert.Equal("Reset your FieldOps password", message.Subject);
        Assert.True(PasswordResetHost.IsWellFormedToken(token));
        Assert.Contains($"{FieldOpsApiFactory.AllowedOrigin}/auth/reset-password#token={token}", message.TextBody, StringComparison.Ordinal);
        Assert.Contains($"{FieldOpsApiFactory.AllowedOrigin}/auth/reset-password#token={token}", message.HtmlBody, StringComparison.Ordinal);
        Assert.DoesNotContain('?', message.TextBody[message.TextBody.IndexOf("Reset password:", StringComparison.Ordinal)..].Split('\n')[0]);
        Assert.Contains("Hi Ada,", message.TextBody, StringComparison.Ordinal);
        Assert.Contains("This link expires in 30 minutes and can only be used once.", message.TextBody, StringComparison.Ordinal);
        Assert.Contains("If you didn't request a password reset, you can ignore this email. Your password won't change.", message.TextBody, StringComparison.Ordinal);

        Assert.Equal(PasswordResetSeed.HashOf(token), await database.TextAsync(
            "SELECT token_hash FROM password_reset_tokens WHERE user_id = @u", ("u", userId)));
        Assert.True(await database.ScalarAsync<bool>(
            "SELECT expires_at - created_at = interval '30 minutes' AND used_at IS NULL FROM password_reset_tokens WHERE user_id = @u",
            ("u", userId)));
        Assert.Equal(0, await database.CountAsync("SELECT COUNT(*) FROM password_reset_tokens WHERE token_hash = @t", ("t", token)));

        // AC-02: unknown and ineligible accounts, identical response, no token, no message.
        var ineligible = new List<(Guid? UserId, string Email, string Status)>
        {
            (null, $"unknown-{Guid.NewGuid():N}@example.com", string.Empty),
        };

        foreach (var status in new[] { "pending", "suspended", "disabled" })
        {
            var (id, address) = await database.SeedRecoveryUserAsync(status);
            ineligible.Add((id, address, status));
        }

        foreach (var (id, address, status) in ineligible)
        {
            Assert.Equal(neutral, await PasswordResetHost.SignatureAsync(await host.RequestAsync(address)));

            if (id is { } existing)
            {
                Assert.Equal(0, await database.TokensAsync(existing));
                Assert.Equal(status, await database.TextAsync("SELECT status::text FROM users WHERE id = @u", ("u", existing)));
            }
        }

        // The per-email limit (3 per hour) is silent: the fourth request is neutral and changes nothing.
        var (limitedId, limitedEmail) = await database.SeedRecoveryUserAsync();

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(neutral, await PasswordResetHost.SignatureAsync(await host.RequestAsync(limitedEmail)));
        }

        await host.Sender.WaitForAttemptsAsync(4);
        var lastHash = PasswordResetSeed.HashOf(RecordingEmailSender.TokenOf(host.Sender.Messages[^1]));

        Assert.Equal(neutral, await PasswordResetHost.SignatureAsync(await host.RequestAsync(limitedEmail.ToUpperInvariant())));
        await Task.Delay(300);
        Assert.Equal(4, host.Sender.Attempts);
        Assert.Equal(lastHash, await database.TextAsync(
            "SELECT token_hash FROM password_reset_tokens WHERE user_id = @u AND used_at IS NULL", ("u", limitedId)));

        // A failing provider never changes the response; the token still exists.
        var (failingId, failingEmail) = await database.SeedRecoveryUserAsync();
        host.Sender.Fail = true;
        Assert.Equal(neutral, await PasswordResetHost.SignatureAsync(await host.RequestAsync(failingEmail)));
        await host.Sender.WaitForAttemptsAsync(5);
        host.Sender.Fail = false;
        Assert.Equal(1, await database.OpenTokensAsync(failingId));
    }

    [Fact]
    public async Task Request_InvalidInputAndIpLimitsAreRejectedWithoutChangesAndNothingSensitiveIsLogged()
    {
        await using var host = PasswordResetHost.Create(database, captureLogs: true);
        var (eligibleId, eligibleEmail) = await database.SeedRecoveryUserAsync();
        var bodies = new List<string>();

        // AC-03: one failure per rule; nothing changes.
        var emailRule = new (string Body, string Message)[]
        {
            ("""{"email":""}""", "Enter your email address."),
            ("""{"email":"   "}""", "Enter your email address."),
            ("""{}""", "Enter your email address."),
            ($$"""{"email":"{{new string('a', 250)}}@b.com"}""", "Enter a valid email address, for example name@company.com."),
            ("""{"email":"not-an-email"}""", "Enter a valid email address, for example name@company.com."),
            ("""{"email":"a@b"}""", "Enter a valid email address, for example name@company.com."),
            ("""{"email":"a b@c.com"}""", "Enter a valid email address, for example name@company.com."),
        };

        foreach (var (body, message) in emailRule)
        {
            var response = await host.PostRawAsync("/password-resets", body);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            PasswordResetSeed.AssertNoStore(response);
            var errors = (await PasswordResetSeed.ReadObjectAsync(response))["errors"]!.AsObject();
            Assert.Equal(["email"], errors.Select(pair => pair.Key).ToArray());
            Assert.Equal(message, errors["email"]![0]!.GetValue<string>());
        }

        foreach (var keyless in new[] { """{"email":""", string.Empty, "null" })
        {
            var response = await host.PostRawAsync("/password-resets", keyless);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Null((await PasswordResetSeed.ReadObjectAsync(response))["errors"]);
        }

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await host.PostRawAsync("/password-resets", """{"email":"a@b.com"}""", "text/plain")).StatusCode);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await host.PostRawAsync("/password-resets", """{"email":"a@b.com"}""", null)).StatusCode);
        Assert.Equal(0, await database.TokensAsync(eligibleId));
        Assert.Equal(0, host.Sender.Attempts);

        // AC-12: the sixth request of one IP in the window is a 429 that creates no token.
        var requestIp = SessionApi.NewClientIp();

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(
                HttpStatusCode.Accepted,
                (await host.RequestAsync($"nobody-{Guid.NewGuid():N}@example.com", requestIp)).StatusCode);
        }

        var limited = await host.PostRawAsync(
            "/password-resets", new JsonObject { ["email"] = eligibleEmail }.ToJsonString(), ip: requestIp, origin: FieldOpsApiFactory.AllowedOrigin);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(int.Parse(limited.Headers.GetValues("Retry-After").Single()) >= 1);
        Assert.Equal(FieldOpsApiFactory.AllowedOrigin, limited.Headers.GetValues("Access-Control-Allow-Origin").Single());
        PasswordResetSeed.AssertNoStore(limited);
        bodies.Add(await limited.Content.ReadAsStringAsync());
        Assert.Equal(0, await database.TokensAsync(eligibleId));

        // Validate and confirm share one per-IP window of 20; the 21st confirm does not consume the token.
        var (tokenUserId, tokenEmail) = await database.SeedRecoveryUserAsync();
        await host.RequestAsync(tokenEmail);
        await host.Sender.WaitForAttemptsAsync(1);
        var liveToken = RecordingEmailSender.TokenOf(host.Sender.Messages[0]);
        var tokenIp = SessionApi.NewClientIp();

        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(HttpStatusCode.Gone, (await host.ValidateAsync(InvitationTokens.Generate().Raw, tokenIp)).StatusCode);
            Assert.Equal(HttpStatusCode.Gone, (await host.ConfirmAsync(InvitationTokens.Generate().Raw, NewPassword, tokenIp)).StatusCode);
        }

        var throttled = await host.PostRawAsync(
            "/password-resets/confirm",
            new JsonObject { ["token"] = liveToken, ["password"] = NewPassword }.ToJsonString(),
            ip: tokenIp,
            origin: FieldOpsApiFactory.AllowedOrigin);
        Assert.Equal(HttpStatusCode.TooManyRequests, throttled.StatusCode);
        Assert.NotEmpty(throttled.Headers.GetValues("Retry-After"));
        Assert.NotEmpty(throttled.Headers.GetValues("Access-Control-Allow-Origin"));
        bodies.Add(await throttled.Content.ReadAsStringAsync());
        Assert.Equal(1, await database.OpenTokensAsync(tokenUserId));
        Assert.Equal(database.PasswordHash, await database.PasswordHashAsync(tokenUserId));

        // AC-13: a full flow, including a failing provider, leaves no secret in logs or error bodies.
        var (_, flowEmail) = await database.SeedRecoveryUserAsync();
        host.Sender.Fail = true;
        await host.RequestAsync(flowEmail);
        await host.Sender.WaitForAttemptsAsync(2);
        host.Sender.Fail = false;
        await host.RequestAsync(flowEmail);
        await host.Sender.WaitForAttemptsAsync(3);
        var flowToken = RecordingEmailSender.TokenOf(host.Sender.Messages[^1]);
        bodies.Add(await (await host.ConfirmAsync(flowToken, "tooshort-pw")).Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NoContent, (await host.ConfirmAsync(flowToken, NewPassword)).StatusCode);
        bodies.Add(await (await host.ConfirmAsync(flowToken, NewPassword)).Content.ReadAsStringAsync());

        var logs = string.Join('\n', host.Logs!.Entries.Select(entry =>
            $"{entry.Message}\n{entry.Exception}\n{string.Join('\n', entry.Properties.Select(pair => $"{pair.Key}={pair.Value}"))}"));
        Assert.Contains("Password reset email dispatch failed", logs, StringComparison.Ordinal);

        foreach (var secret in new[]
        {
            flowToken, liveToken, PasswordResetSeed.HashOf(flowToken), PasswordResetSeed.HashOf(liveToken),
            flowEmail, eligibleEmail, tokenEmail, NewPassword, "tooshort-pw",
        })
        {
            Assert.DoesNotContain(secret, logs, StringComparison.OrdinalIgnoreCase);
            Assert.All(bodies, body => Assert.DoesNotContain(secret, body, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public async Task ValidateAndConfirm_ReplacementKeepsOneUsableTokenAndEveryUnusableTokenIsTheSameGone()
    {
        await using var host = PasswordResetHost.Create(database);
        var (userId, email) = await database.SeedRecoveryUserAsync();

        // AC-04: the second request replaces the first token.
        await host.RequestAsync(email);
        await host.RequestAsync(email);
        await host.Sender.WaitForAttemptsAsync(2);
        var first = RecordingEmailSender.TokenOf(host.Sender.Messages[0]);
        var second = RecordingEmailSender.TokenOf(host.Sender.Messages[1]);
        Assert.NotEqual(first, second);
        Assert.Equal(HttpStatusCode.Gone, (await host.ValidateAsync(first)).StatusCode);
        Assert.Equal(HttpStatusCode.Gone, (await host.ConfirmAsync(first, NewPassword)).StatusCode);

        // AC-05: exactly the account email, and the token stays usable.
        for (var i = 0; i < 2; i++)
        {
            var valid = await host.ValidateAsync(second);
            Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
            PasswordResetSeed.AssertNoStore(valid);
            var body = await PasswordResetSeed.ReadObjectAsync(valid);
            Assert.Equal(["email"], body.Select(pair => pair.Key).ToArray());
            Assert.Equal(email, body["email"]!.GetValue<string>());
        }

        Assert.Equal(1, await database.OpenTokensAsync(userId));
        Assert.Equal(1, await database.TokensAsync(userId));

        // AC-06: unknown, replaced, consumed, suspended-user and expired tokens.
        var (consumedId, consumedEmail) = await database.SeedRecoveryUserAsync();
        await host.RequestAsync(consumedEmail);
        var (suspendedId, suspendedEmail) = await database.SeedRecoveryUserAsync();
        await host.RequestAsync(suspendedEmail);
        var (expiredId, expiredEmail) = await database.SeedRecoveryUserAsync();
        await host.RequestAsync(expiredEmail);
        await host.Sender.WaitForAttemptsAsync(5);
        var consumed = RecordingEmailSender.TokenOf(host.Sender.Messages.Single(m => m.To == consumedEmail));
        var suspended = RecordingEmailSender.TokenOf(host.Sender.Messages.Single(m => m.To == suspendedEmail));
        var expired = RecordingEmailSender.TokenOf(host.Sender.Messages.Single(m => m.To == expiredEmail));

        Assert.Equal(HttpStatusCode.NoContent, (await host.ConfirmAsync(consumed, NewPassword)).StatusCode);
        var consumedHash = await database.PasswordHashAsync(consumedId);
        await database.ExecuteAsync("UPDATE users SET status = 'suspended' WHERE id = @u", ("u", suspendedId));

        string? reference = null;

        async Task AssertGoneAsync(string token)
        {
            foreach (var response in new[]
            {
                await host.ValidateAsync(token),
                await host.ConfirmAsync(token, "Another valid password 1"),
            })
            {
                Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
                PasswordResetSeed.AssertNoStore(response);
                var problem = await SessionApi.ReadProblemWithoutTraceIdAsync(response);
                reference ??= problem;
                Assert.Equal(reference, problem);
                Assert.DoesNotContain("detail", problem, StringComparison.Ordinal);
                Assert.DoesNotContain("errors", problem, StringComparison.Ordinal);
                Assert.DoesNotContain(token, problem, StringComparison.Ordinal);
                Assert.DoesNotContain(email, problem, StringComparison.OrdinalIgnoreCase);
            }
        }

        foreach (var token in new[] { InvitationTokens.Generate().Raw, first, consumed, suspended })
        {
            await AssertGoneAsync(token);
        }

        Assert.Equal(consumedHash, await database.PasswordHashAsync(consumedId));
        Assert.Equal(database.PasswordHash, await database.PasswordHashAsync(suspendedId));
        Assert.Equal("suspended", await database.TextAsync("SELECT status::text FROM users WHERE id = @u", ("u", suspendedId)));
        Assert.Equal(1, await database.OpenTokensAsync(suspendedId));

        // Exactly at expires_at the token is no longer usable.
        host.Time.Advance(TimeSpan.FromMinutes(30));
        await AssertGoneAsync(expired);
        Assert.Equal(database.PasswordHash, await database.PasswordHashAsync(expiredId));
        Assert.Equal(1, await database.OpenTokensAsync(expiredId));

        // Malformed tokens: a 400 whose only key is token, never echoed.
        foreach (var malformed in new[] { "abc", new string('a', 44), new string('a', 42) + "+", string.Empty })
        {
            foreach (var response in new[] { await host.ValidateAsync(malformed), await host.ConfirmAsync(malformed, NewPassword) })
            {
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
                var errors = (await PasswordResetSeed.ReadObjectAsync(response))["errors"]!.AsObject();
                Assert.Equal(["token"], errors.Select(pair => pair.Key).ToArray());
                Assert.Equal("Enter a valid value.", errors["token"]![0]!.GetValue<string>());
            }
        }

        Assert.Equal(HttpStatusCode.BadRequest, (await host.PostAsync("/password-resets/validate", new JsonObject())).StatusCode);
    }

    [Fact]
    public async Task Confirm_ValidTokenChangesOnlyThePasswordAndSessionMarkerAndLeavesStatusesUnchanged()
    {
        await using var host = PasswordResetHost.Create(database);
        var account = await database.SeedAccountAsync();
        var secondOrg = await database.SeedOrganizationAsync();
        var secondMembership = await database.SeedMembershipAsync(secondOrg, account.UserId, CompanySettingsDatabaseFixture.DispatcherRoleId);
        await database.ExecuteAsync("UPDATE organization_users SET status = 'suspended' WHERE id = @m", ("m", secondMembership));
        var emailBefore = await database.TextAsync("SELECT email FROM users WHERE id = @u", ("u", account.UserId));

        await host.RequestAsync(account.Email);
        await host.Sender.WaitForAttemptsAsync(1);
        var token = RecordingEmailSender.TokenOf(host.Sender.Messages[0]);

        var confirmed = await host.ConfirmAsync(token, NewPassword);

        Assert.Equal(HttpStatusCode.NoContent, confirmed.StatusCode);
        Assert.Equal(string.Empty, await confirmed.Content.ReadAsStringAsync());
        PasswordResetSeed.AssertNoStore(confirmed);

        var hash = await database.PasswordHashAsync(account.UserId);
        Assert.StartsWith("pbkdf2-sha256$", hash, StringComparison.Ordinal);
        Assert.NotEqual(NewPassword, hash);
        var hasher = new Pbkdf2PasswordHasher();
        Assert.True(hasher.Verify(NewPassword, hash));
        Assert.False(hasher.Verify(CompanySettingsDatabaseFixture.Password, hash));

        Assert.True(await database.ScalarAsync<bool>(
            "SELECT used_at IS NOT NULL FROM password_reset_tokens WHERE user_id = @u", ("u", account.UserId)));
        Assert.True(await database.ScalarAsync<bool>(
            "SELECT password_changed_at IS NOT NULL AND updated_at = password_changed_at FROM users WHERE id = @u", ("u", account.UserId)));
        Assert.Equal("active", await database.TextAsync("SELECT status::text FROM users WHERE id = @u", ("u", account.UserId)));
        Assert.Equal(emailBefore, await database.TextAsync("SELECT email FROM users WHERE id = @u", ("u", account.UserId)));
        Assert.Equal("active", await database.TextAsync("SELECT status::text FROM organization_users WHERE id = @m", ("m", account.MembershipId)));
        Assert.Equal("suspended", await database.TextAsync("SELECT status::text FROM organization_users WHERE id = @m", ("m", secondMembership)));
        Assert.Equal(HttpStatusCode.Gone, (await host.ValidateAsync(token)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await host.SignInAsync(account.Email, CompanySettingsDatabaseFixture.Password)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.SignInAsync(account.Email, NewPassword)).StatusCode);
    }

    [Fact]
    public async Task Confirm_RevokesEverySessionOfTheUserInAllOrganizationsAndNoOneElses()
    {
        await using var host = PasswordResetHost.Create(database);
        var orgA = await database.SeedOrganizationAsync();
        var orgB = await database.SeedOrganizationAsync();
        var user = await database.SeedAccountAsync(organizationId: orgA);
        var membershipB = await database.SeedMembershipAsync(orgB, user.UserId, CompanySettingsDatabaseFixture.DispatcherRoleId);
        var other = await database.SeedAccountAsync(organizationId: orgA);

        // The earliest joined membership wins sign-in, so B needs A suspended for a moment.
        var cookieA = await host.SignInCookieAsync(user.Email);
        await database.ExecuteAsync("UPDATE organization_users SET status = 'suspended' WHERE id = @m", ("m", user.MembershipId));
        var cookieB = await host.SignInCookieAsync(user.Email);
        await database.ExecuteAsync("UPDATE organization_users SET status = 'active' WHERE id = @m", ("m", user.MembershipId));
        var cookieOther = await host.SignInCookieAsync(other.Email);

        Assert.Equal(orgA, (await SessionApi.ReadSessionAsync(await SessionApi.GetCurrentAsync(host.Client, cookieA))).Organization.Id);
        Assert.Equal(orgB, (await SessionApi.ReadSessionAsync(await SessionApi.GetCurrentAsync(host.Client, cookieB))).Organization.Id);
        Assert.NotEqual(Guid.Empty, membershipB);

        host.Time.Advance(TimeSpan.FromMinutes(1));
        await host.RequestAsync(user.Email);
        await host.Sender.WaitForAttemptsAsync(1);
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await host.ConfirmAsync(RecordingEmailSender.TokenOf(host.Sender.Messages[0]), NewPassword)).StatusCode);

        foreach (var cookie in new[] { cookieA, cookieB })
        {
            var revoked = await SessionApi.GetCurrentAsync(host.Client, cookie);
            Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
            SessionApi.AssertCookieDeleted(revoked);
        }

        Assert.Equal(HttpStatusCode.OK, (await SessionApi.GetCurrentAsync(host.Client, cookieOther)).StatusCode);

        var fresh = await host.SignInCookieAsync(user.Email, NewPassword);
        Assert.Equal(HttpStatusCode.OK, (await SessionApi.GetCurrentAsync(host.Client, fresh)).StatusCode);
    }

    [Fact]
    public async Task Confirm_PasswordRulesRejectWithoutChangesAndBoundariesAreAccepted()
    {
        await using var host = PasswordResetHost.Create(database);
        var account = await database.SeedAccountAsync();
        var cookie = await host.SignInCookieAsync(account.Email);

        await host.RequestAsync(account.Email);
        await host.Sender.WaitForAttemptsAsync(1);
        var token = RecordingEmailSender.TokenOf(host.Sender.Messages[0]);

        var rules = new (string? Password, string Message)[]
        {
            (new string('a', 11), "Use 12 to 128 characters."),
            (new string('a', 129), "Use 12 to 128 characters."),
            (string.Empty, "This field is required."),
            (null, "This field is required."),
            (account.Email.ToUpperInvariant(), "Choose a password that is different from your email."),
            (account.Email, "Choose a password that is different from your email."),
        };

        foreach (var (password, message) in rules)
        {
            var response = await host.PostAsync(
                "/password-resets/confirm", new JsonObject { ["token"] = token, ["password"] = password });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var errors = (await PasswordResetSeed.ReadObjectAsync(response))["errors"]!.AsObject();
            Assert.Equal(["password"], errors.Select(pair => pair.Key).ToArray());
            Assert.Equal(message, errors["password"]![0]!.GetValue<string>());
        }

        Assert.Equal(database.PasswordHash, await database.PasswordHashAsync(account.UserId));
        Assert.Equal(1, await database.OpenTokensAsync(account.UserId));
        Assert.Equal(HttpStatusCode.OK, (await SessionApi.GetCurrentAsync(host.Client, cookie)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.ValidateAsync(token)).StatusCode);

        // BR-13: field shape (400) precedes the token; an unusable token never reaches the email rule.
        var unknown = InvitationTokens.Generate().Raw;
        Assert.Equal(HttpStatusCode.BadRequest, (await host.ConfirmAsync(unknown, "short")).StatusCode);
        Assert.Equal(HttpStatusCode.Gone, (await host.ConfirmAsync(unknown, account.Email)).StatusCode);

        // The 12 and 128 character boundaries are accepted.
        var boundary = new string('b', 12);
        Assert.Equal(HttpStatusCode.NoContent, (await host.ConfirmAsync(token, boundary)).StatusCode);
        Assert.True(new Pbkdf2PasswordHasher().Verify(boundary, await database.PasswordHashAsync(account.UserId)));

        await host.RequestAsync(account.Email);
        await host.Sender.WaitForAttemptsAsync(2);
        var longPassword = new string('c', 128);
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await host.ConfirmAsync(RecordingEmailSender.TokenOf(host.Sender.Messages[^1]), longPassword)).StatusCode);
        Assert.True(new Pbkdf2PasswordHasher().Verify(longPassword, await database.PasswordHashAsync(account.UserId)));
    }

    [Fact]
    public async Task Confirm_ConcurrentConsumptionSucceedsOnceAndAFailureInsideTheTransactionRollsEverythingBack()
    {
        await using var host = PasswordResetHost.Create(database);
        var (userId, email) = await database.SeedRecoveryUserAsync();

        // AC-10: two concurrent confirms, one winner; the loser and a later attempt get 410.
        await host.RequestAsync(email);
        await host.Sender.WaitForAttemptsAsync(1);
        var token = RecordingEmailSender.TokenOf(host.Sender.Messages[0]);
        const string first = "First concurrent password 1";
        const string secondPassword = "Second concurrent password 2";

        var responses = await Task.WhenAll(host.ConfirmAsync(token, first), host.ConfirmAsync(token, secondPassword));

        Assert.Equal(
            [HttpStatusCode.NoContent, HttpStatusCode.Gone],
            responses.Select(response => response.StatusCode).Order().ToArray());
        var winner = responses[0].StatusCode == HttpStatusCode.NoContent ? first : secondPassword;
        var loser = winner == first ? secondPassword : first;
        var hasher = new Pbkdf2PasswordHasher();
        var hash = await database.PasswordHashAsync(userId);
        Assert.True(hasher.Verify(winner, hash));
        Assert.False(hasher.Verify(loser, hash));
        Assert.Equal(HttpStatusCode.Gone, (await host.ConfirmAsync(token, "Third attempt password 3")).StatusCode);
        Assert.Equal(hash, await database.PasswordHashAsync(userId));
        Assert.Equal(1, await database.CountAsync(
            "SELECT COUNT(*) FROM password_reset_tokens WHERE user_id = @u AND used_at IS NOT NULL", ("u", userId)));

        // AC-11: a failure while the user row is updated rolls the token consumption back too.
        var account = await database.SeedAccountAsync();
        var cookie = await host.SignInCookieAsync(account.Email);
        await host.RequestAsync(account.Email);
        await host.Sender.WaitForAttemptsAsync(2);
        var rollbackToken = RecordingEmailSender.TokenOf(host.Sender.Messages[^1]);
        var name = $"force_fail_{account.UserId:N}";

        await database.ExecuteAsync(
            $"CREATE FUNCTION {name}() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'forced failure'; END $$");
        await database.ExecuteAsync(
            $"CREATE TRIGGER {name} BEFORE UPDATE ON users FOR EACH ROW WHEN (NEW.id = '{account.UserId}') EXECUTE FUNCTION {name}()");

        try
        {
            var failed = await host.ConfirmAsync(rollbackToken, NewPassword);

            Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
            Assert.Equal("application/problem+json", failed.Content.Headers.ContentType?.MediaType);
            var text = await failed.Content.ReadAsStringAsync();
            Assert.DoesNotContain("forced failure", text, StringComparison.Ordinal);
            Assert.DoesNotContain(rollbackToken, text, StringComparison.Ordinal);
        }
        finally
        {
            await database.ExecuteAsync($"DROP TRIGGER {name} ON users");
            await database.ExecuteAsync($"DROP FUNCTION {name}()");
        }

        Assert.Equal(database.PasswordHash, await database.PasswordHashAsync(account.UserId));
        Assert.True(await database.ScalarAsync<bool>(
            "SELECT password_changed_at IS NULL FROM users WHERE id = @u", ("u", account.UserId)));
        Assert.Equal(1, await database.OpenTokensAsync(account.UserId));
        Assert.Equal(HttpStatusCode.OK, (await SessionApi.GetCurrentAsync(host.Client, cookie)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.ValidateAsync(rollbackToken)).StatusCode);
    }

    [Fact]
    public async Task InvitationDelivery_SendsTheUnchangedMessageThroughTheEmailPortAndAFailureIsA502WithRollback()
    {
        var org = await database.SeedOrganizationAsync($"Acme Org {Guid.NewGuid():N}"[..28]);
        var owner = await database.SeedMemberAsync(org, CompanySettingsDatabaseFixture.OwnerRoleId, "Olivia", "Owner");
        await using var host = PasswordResetHost.Create(database);
        var cookie = await host.SignInCookieAsync(owner.Email);
        var inviteeEmail = CompanySettingsDatabaseFixture.NewEmail();

        var created = await host.PostWithCookieAsync("/users/invitations", cookie, UsersSeed.Body(
            ("email", inviteeEmail), ("firstName", "Ivy"), ("lastName", "Invitee"), ("roleCode", "dispatcher"), ("isAllBranches", true)));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var invitationId = (await PasswordResetSeed.ReadObjectAsync(created))["id"]!.GetValue<Guid>();

        var sent = Assert.Single(host.Sender.Messages);
        var orgName = await database.TextAsync("SELECT name FROM organizations WHERE id = @o", ("o", org));
        var link = System.Text.RegularExpressions.Regex.Match(
            sent.TextBody, @"Accept invitation: (\S+)").Groups[1].Value;
        Assert.Equal(inviteeEmail, sent.To);
        Assert.Equal($"Olivia Owner invited you to join {orgName} on FieldOps", sent.Subject);
        Assert.Matches($"^{System.Text.RegularExpressions.Regex.Escape(FieldOpsApiFactory.AllowedOrigin)}/auth/invitation#token=[A-Za-z0-9_-]{{43}}$", link);
        Assert.Contains($"Hi Ivy,", sent.TextBody, StringComparison.Ordinal);
        Assert.Contains($"Olivia Owner invited you to join {orgName} on FieldOps as Dispatcher.", sent.TextBody, StringComparison.Ordinal);
        Assert.Contains("This invitation can only be used once.", sent.TextBody, StringComparison.Ordinal);
        Assert.Contains($"href=\"{link}\"", sent.HtmlBody, StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.OK, (await host.PostWithCookieAsync($"/users/invitations/{invitationId}/resend", cookie)).StatusCode);
        Assert.Equal(2, host.Sender.Messages.Count);
        Assert.NotEqual(sent.TextBody, host.Sender.Messages[1].TextBody);
        Assert.Equal(sent.Subject, host.Sender.Messages[1].Subject);

        // A throwing sender: 502, nothing stored, the previous token stays.
        var countBefore = await database.CountAsync("SELECT COUNT(*) FROM user_invitations WHERE organization_id = @o", ("o", org));
        var hashBefore = await database.TextAsync("SELECT token_hash FROM user_invitations WHERE id = @i", ("i", invitationId));
        host.Sender.Fail = true;

        var failedInvite = await host.PostWithCookieAsync("/users/invitations", cookie, UsersSeed.Body(
            ("email", CompanySettingsDatabaseFixture.NewEmail()), ("firstName", "Lou"), ("lastName", "Lost"),
            ("roleCode", "dispatcher"), ("isAllBranches", true)));
        Assert.Equal(HttpStatusCode.BadGateway, failedInvite.StatusCode);
        Assert.Equal(
            "We couldn't send the invitation. Try again.",
            (await PasswordResetSeed.ReadObjectAsync(failedInvite))["title"]!.GetValue<string>());
        Assert.Equal(
            HttpStatusCode.BadGateway,
            (await host.PostWithCookieAsync($"/users/invitations/{invitationId}/resend", cookie)).StatusCode);
        host.Sender.Fail = false;

        Assert.Equal(countBefore, await database.CountAsync("SELECT COUNT(*) FROM user_invitations WHERE organization_id = @o", ("o", org)));
        Assert.Equal(hashBefore, await database.TextAsync("SELECT token_hash FROM user_invitations WHERE id = @i", ("i", invitationId)));
    }
}
