using System.Net;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.BillingReview;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.OnlinePayments;
using FieldOps.IntegrationTests.PasswordResets;
using FieldOps.IntegrationTests.ServiceRequests;
using FieldOps.IntegrationTests.Sessions;
using FieldOps.IntegrationTests.Users;
using Microsoft.Net.Http.Headers;

namespace FieldOps.IntegrationTests.Portal;

/// <summary>Portal sign-in, cookie separation, revalidation and switch account (customer portal BR-01 … BR-08, AC-01 … AC-04, AC-10).</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class PortalAuthenticationTests(CompanySettingsDatabaseFixture database)
{
    [Fact]
    public async Task PostPortalSessions_CredentialsAndEveryFailure_IssuesThePortalCookieAndIdentical401s()
    {
        await using var host = OnlineHost.Create(database);
        var world = await database.SeedWorldAsync();
        var user = await database.LinkPortalAsync(world.Org, world.Contact);

        var success = await PortalApi.SignInAsync(host.Client, user.Email);

        Assert.Equal(HttpStatusCode.OK, success.StatusCode);
        Assert.Contains("no-store", success.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
        SessionApi.AssertNoSessionCookie(success);
        var cookie = Assert.Single(PortalApi.PortalCookies(success));
        Assert.True(cookie.HttpOnly);
        Assert.True(cookie.Secure);
        Assert.Equal(SameSiteMode.Strict, cookie.SameSite);
        Assert.Equal("/", cookie.Path.ToString());
        Assert.False(cookie.Domain.HasValue);
        Assert.Null(cookie.Expires);

        var text = await success.Content.ReadAsStringAsync();
        var session = JsonNode.Parse(text)!;
        Assert.Equal(user.Email, session["user"]!["email"]!.GetValue<string>());
        Assert.Equal("Carla Customer", session["account"]!["customerName"]!.GetValue<string>());
        Assert.Equal(world.Contact, session["account"]!["contactId"]!.GetValue<Guid>());
        Assert.Single(session["accounts"]!.AsArray());

        // No organization or customer identifier, no sign-in or audit leak of the email.
        Assert.DoesNotContain(world.Org.ToString(), text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(world.Customer.ToString(), text, StringComparison.OrdinalIgnoreCase);
        Assert.True(await database.ScalarAsync<bool>("SELECT last_login_at IS NOT NULL FROM users WHERE id = @u", ("u", user.UserId)));
        Assert.Equal(1, await database.CountAuditLogsAsync(world.Org, "portal.signed_in"));
        Assert.DoesNotContain(user.Email, await database.OrgAuditTextAsync(world.Org), StringComparison.OrdinalIgnoreCase);

        // Unknown email, wrong password, a user that is not active and a user with no active link: one identical 401.
        var disabled = await database.LinkPortalAsync(world.Org, (await database.SeedSiblingAsync(world)).Contact);
        await database.ExecuteAsync("UPDATE users SET status = 'disabled' WHERE id = @u", ("u", disabled.UserId));
        var staffOnly = await database.SeedMemberAsync(world.Org, CompanySettingsDatabaseFixture.OwnerRoleId, "Sam", "Staff");
        var failures = new[]
        {
            await PortalApi.SignInAsync(host.Client, $"nobody-{Guid.NewGuid():N}@example.com"),
            await PortalApi.SignInAsync(host.Client, user.Email, "Not the right password 1"),
            await PortalApi.SignInAsync(host.Client, disabled.Email),
            await PortalApi.SignInAsync(host.Client, staffOnly.Email),
        };
        var bodies = new HashSet<string>();

        foreach (var failure in failures)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, failure.StatusCode);
            Assert.Empty(PortalApi.PortalCookies(failure));
            bodies.Add(await PortalApi.BodyWithoutTraceAsync(failure));
        }

        Assert.Single(bodies);

        // A portal-only user gets the same generic 401 from the internal sign-in.
        var internalSignIn = await SessionApi.SignInAsync(host.Client, user.Email, CompanySettingsDatabaseFixture.Password);
        Assert.Equal(HttpStatusCode.Unauthorized, internalSignIn.StatusCode);
        Assert.Equal(bodies.Single(), await PortalApi.BodyWithoutTraceAsync(internalSignIn));
        Assert.Empty(SessionApi.GetSessionSetCookies(internalSignIn));

        // The per-email failure counter is the internal one: five failures, then even the right password is a 429.
        var throttled = await database.LinkPortalAsync(world.Org, (await database.SeedSiblingAsync(world)).Contact);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await PortalApi.SignInAsync(host.Client, throttled.Email, "Wrong password number 1")).StatusCode);
        }

        var limited = await PortalApi.SignInAsync(host.Client, throttled.Email);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.NotNull(limited.Headers.RetryAfter);
        Assert.Empty(PortalApi.PortalCookies(limited));

        // Logs carry categories only, never the email.
        Assert.DoesNotContain(user.Email, new CapturingLogs(host.Logs).AllText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PortalAndInternalCookies_UsedOnTheOtherArea_AreUnauthenticatedAndPortalSignOutKeepsTheInternalSession()
    {
        await using var host = OnlineHost.Create(database);
        var world = await database.SeedWorldAsync();
        var member = await database.SeedMemberAsync(world.Org, CompanySettingsDatabaseFixture.OwnerRoleId, "Olivia", "Owner");
        await database.LinkPortalAsync(world.Org, world.Contact, existingUser: member.UserId);
        var internalCookie = await CompanySettingsApi.SignInCookieAsync(host.Client, member.Email);
        var portalCookie = await PortalApi.CookieAsync(host.Client, member.Email);

        // The portal cookie never authenticates an internal endpoint, and the internal cookie never a portal one.
        var onInternal = await PortalApi.GetAsync(host.Client, "/sessions/current", portalCookie);
        var onInternalList = await PortalApi.GetAsync(host.Client, $"/customers/{world.Customer}/detail", portalCookie);
        var onPortal = await PortalApi.GetAsync(host.Client, "/portal/dashboard", internalCookie);
        var onPortalSession = await PortalApi.GetAsync(host.Client, "/portal/sessions/current", internalCookie);

        foreach (var response in new[] { onInternal, onInternalList, onPortal, onPortalSession })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Null(response.Headers.Location);
        }

        Assert.Equal("no-store", onPortal.Headers.CacheControl?.ToString());
        Assert.Equal("no-store", onPortalSession.Headers.CacheControl?.ToString());

        // Both cookies coexist in one browser.
        var both = $"{internalCookie}; {portalCookie}";
        Assert.Equal(HttpStatusCode.OK, (await PortalApi.GetAsync(host.Client, "/sessions/current", both)).StatusCode);
        var current = await PortalApi.GetAsync(host.Client, "/portal/sessions/current", both);
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        Assert.Equal(world.Contact, (await PortalApi.ReadAsync(current))["account"]!["contactId"]!.GetValue<Guid>());

        // Portal sign-out clears only the portal cookie.
        var signOut = await PortalApi.SendAsync(host.Client, HttpMethod.Delete, "/portal/sessions/current", both);
        Assert.Equal(HttpStatusCode.NoContent, signOut.StatusCode);
        PortalApi.AssertCookieDeleted(signOut);
        Assert.Empty(SessionApi.GetSessionSetCookies(signOut));
        Assert.Equal(HttpStatusCode.OK, (await PortalApi.GetAsync(host.Client, "/sessions/current", internalCookie)).StatusCode);
    }

    [Theory]
    [InlineData("customer_archived")]
    [InlineData("contact_inactive")]
    [InlineData("access_removed")]
    [InlineData("user_disabled")]
    [InlineData("organization_inactive")]
    [InlineData("password_reset")]
    public async Task PortalRequest_AfterTheLinkIsRevoked_Returns401AndDeletesTheCookie(string reason)
    {
        await using var host = OnlineHost.Create(database);
        var world = await database.SeedWorldAsync();
        var member = await database.SeedMemberAsync(world.Org, CompanySettingsDatabaseFixture.OwnerRoleId, "Olivia", "Owner");
        await database.LinkPortalAsync(world.Org, world.Contact, existingUser: member.UserId);
        var internalCookie = await CompanySettingsApi.SignInCookieAsync(host.Client, member.Email);
        var portalCookie = await PortalApi.CookieAsync(host.Client, member.Email);
        Assert.Equal(HttpStatusCode.OK, (await PortalApi.GetAsync(host.Client, "/portal/dashboard", portalCookie)).StatusCode);

        // Resolves the password reset of the invalid sign-in time: the change must be later than the sign-in.
        await Task.Delay(50);

        switch (reason)
        {
            case "customer_archived":
                await database.ExecuteAsync("UPDATE customers SET is_active = false WHERE id = @c", ("c", world.Customer));
                break;
            case "contact_inactive":
                await database.ExecuteAsync("UPDATE customer_contacts SET is_active = false WHERE id = @c", ("c", world.Contact));
                break;
            case "access_removed":
                var removed = await host.SendAsync(HttpMethod.Delete, $"/customers/{world.Customer}/portal-access", internalCookie);
                Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
                break;
            case "user_disabled":
                await database.ExecuteAsync("UPDATE users SET status = 'disabled' WHERE id = @u", ("u", member.UserId));
                break;
            case "organization_inactive":
                await database.ExecuteAsync("UPDATE organizations SET is_active = false WHERE id = @o", ("o", world.Org));
                break;
            default:
                await ResetPasswordAsync(host, world, member);
                break;
        }

        var revoked = await PortalApi.GetAsync(host.Client, "/portal/dashboard", portalCookie);

        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
        Assert.Equal("no-store", revoked.Headers.CacheControl?.ToString());
        Assert.Null(revoked.Headers.Location);
        PortalApi.AssertCookieDeleted(revoked);

        if (reason == "password_reset")
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await PortalApi.GetAsync(host.Client, "/sessions/current", internalCookie)).StatusCode);
        }
    }

    [Fact]
    public async Task PostPortalAccount_SwitchesToAnotherActiveLinkOfTheCaller_AndRefusesEveryOtherContact()
    {
        await using var host = OnlineHost.Create(database);
        var a = await database.SeedWorldAsync();
        var b = await database.SeedWorldAsync();
        var c = await database.SeedWorldAsync();
        var other = await database.SeedSiblingAsync(a);
        await database.ExecuteAsync("UPDATE customers SET display_name = 'Bob Builder' WHERE id = @c", ("c", b.Customer));
        var user = await database.LinkPortalAsync(a.Org, a.Contact, linkedAt: DateTimeOffset.UtcNow.AddDays(-10));
        await database.LinkPortalAsync(b.Org, b.Contact, existingUser: user.UserId, linkedAt: DateTimeOffset.UtcNow.AddDays(-5));
        await database.LinkPortalAsync(c.Org, c.Contact, existingUser: user.UserId, linkedAt: DateTimeOffset.UtcNow.AddDays(-2));
        await database.ExecuteAsync("UPDATE customer_contacts SET is_active = false WHERE id = @c", ("c", c.Contact));
        await database.LinkPortalAsync(other.Org, other.Contact);

        var signIn = await PortalApi.SignInAsync(host.Client, user.Email);
        var signedInAt = DateTimeOffset.UtcNow;
        var first = await PortalApi.ReadAsync(signIn);
        var cookie = PortalApi.IssuedCookie(signIn);

        // The oldest link opens; the inactive link is not listed.
        Assert.Equal(a.Contact, first["account"]!["contactId"]!.GetValue<Guid>());
        Assert.Equal([a.Contact, b.Contact], first["accounts"]!.AsArray().Select(account => account!["contactId"]!.GetValue<Guid>()).ToArray());

        await Task.Delay(150);
        var switched = await PortalApi.PostAsync(host.Client, "/portal/sessions/current/account", cookie, new JsonObject { ["contactId"] = b.Contact.ToString() });
        Assert.Equal(HttpStatusCode.OK, switched.StatusCode);
        var reissued = PortalApi.IssuedCookie(switched);
        Assert.Equal("Bob Builder", (await PortalApi.ReadAsync(switched))["account"]!["customerName"]!.GetValue<string>());

        var dashboard = await PortalApi.ReadAsync(await PortalApi.GetAsync(host.Client, "/portal/dashboard", reissued));
        var propertyIds = dashboard["properties"]!.AsArray().Select(property => property!["id"]!.GetValue<Guid>()).ToArray();
        Assert.Equal([b.Property], propertyIds);
        Assert.Equal(1, await database.CountAuditLogsAsync(b.Org, "portal.account_switched"));
        Assert.Equal(0, await database.CountAuditLogsAsync(a.Org, "portal.account_switched"));

        // Another user's contact, an inactive link and an unknown id are the same 404.
        foreach (var target in new[] { other.Contact, c.Contact, Guid.NewGuid() })
        {
            var refused = await PortalApi.PostAsync(host.Client, "/portal/sessions/current/account", reissued, new JsonObject { ["contactId"] = target.ToString() });
            Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
            Assert.Equal("portal_account_unavailable", (await PortalApi.ReadAsync(refused, HttpStatusCode.NotFound))["code"]!.GetValue<string>());
            Assert.Empty(PortalApi.PortalCookies(refused));
        }

        // The sign-in time is kept: a password change made after the sign-in still ends the switched session.
        await database.ExecuteAsync(
            "UPDATE users SET password_changed_at = @at WHERE id = @u", ("at", signedInAt.AddMilliseconds(20)), ("u", user.UserId));
        Assert.Equal(HttpStatusCode.Unauthorized, (await PortalApi.GetAsync(host.Client, "/portal/dashboard", reissued)).StatusCode);
    }

    // AC-10: the neutral 202 with the portal link, no email without an active link, then the confirmation of the existing endpoint.
    private async Task ResetPasswordAsync(OnlineHost host, RequestWorld world, SeededMember member)
    {
        var staffOnly = await database.SeedMemberAsync(world.Org, CompanySettingsDatabaseFixture.OwnerRoleId, "Sam", "Staff");
        var neutral = await PortalApi.PostAsync(host.Client, "/portal/password-resets", null, new JsonObject { ["email"] = staffOnly.Email });
        Assert.Equal(HttpStatusCode.Accepted, neutral.StatusCode);

        var requested = await PortalApi.PostAsync(host.Client, "/portal/password-resets", null, new JsonObject { ["email"] = member.Email });
        Assert.Equal(HttpStatusCode.Accepted, requested.StatusCode);
        await host.Sender.WaitForAttemptsAsync(1);

        var message = Assert.Single(host.Sender.Messages);
        Assert.Equal(member.Email, message.To);
        Assert.Contains("/portal/reset-password#token=", message.TextBody, StringComparison.Ordinal);

        var confirmed = await PortalApi.PostAsync(
            host.Client,
            "/password-resets/confirm",
            null,
            new JsonObject { ["token"] = RecordingEmailSender.TokenOf(message), ["password"] = "Another horse battery staple 2" });
        Assert.Equal(HttpStatusCode.NoContent, confirmed.StatusCode);
    }
}
