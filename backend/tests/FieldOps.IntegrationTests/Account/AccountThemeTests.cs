using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.Api;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.OnlinePayments;
using FieldOps.IntegrationTests.Portal;
using FieldOps.IntegrationTests.ServiceRequests;
using FieldOps.IntegrationTests.Sessions;
using FieldOps.IntegrationTests.Users;

namespace FieldOps.IntegrationTests.Account;

/// <summary>
/// PUT /account/theme and PUT /portal/account/theme and the theme in every session body
/// (app dark mode BR-06 to BR-08; AC-02, AC-15 to AC-19).
/// </summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class AccountThemeTests(CompanySettingsDatabaseFixture database)
{
    private const string InternalPath = "/account/theme";
    private const string PortalPath = "/portal/account/theme";
    private const string Action = "user.theme_updated";
    private const string ThemeMessage = "Choose a supported appearance.";
    private const string SourceMessage = "Choose a supported source.";

    // AC-15, AC-16: store, audit {previousTheme,newTheme,source}, no-op without write or audit, next session.
    [Fact]
    public async Task PutAccountTheme_ChangeAndSameValue_StoresAuditsOnChangeOnlyAndSessionReturnsIt()
    {
        var account = await database.SeedAccountAsync();
        await using var host = SessionTestHost.Create(database.ConnectionString);
        var cookie = await CompanySettingsApi.SignInCookieAsync(host.Client, account.Email);
        Assert.Equal("system", await StoredAsync(account.UserId));
        var before = await UpdatedAtAsync(account.UserId);

        var changed = await CompanySettingsApi.PutAsync(host.Client, InternalPath, Theme("dark", "user_menu"), cookie);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.True(changed.Headers.CacheControl?.NoStore);
        using (var body = await CompanySettingsApi.ReadJsonAsync(changed))
        {
            Assert.Equal(["preferredTheme"], body.RootElement.EnumerateObject().Select(p => p.Name));
            Assert.Equal("dark", body.RootElement.GetProperty("preferredTheme").GetString());
        }

        Assert.Equal("dark", await StoredAsync(account.UserId));
        var afterChange = await UpdatedAtAsync(account.UserId);
        Assert.True(afterChange > before);
        Assert.Equal(1, await database.CountAuditLogsAsync(account.OrganizationId, Action));
        var (_, _, metadata) = await database.GetLatestAuditAsync(account.OrganizationId, Action);
        using (var meta = JsonDocument.Parse(metadata!))
        {
            Assert.Equal(["newTheme", "previousTheme", "source"], meta.RootElement.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
            Assert.Equal("system", meta.RootElement.GetProperty("previousTheme").GetString());
            Assert.Equal("dark", meta.RootElement.GetProperty("newTheme").GetString());
            Assert.Equal("user_menu", meta.RootElement.GetProperty("source").GetString());
        }

        Assert.Equal(account.UserId, await database.ScalarAsync<Guid>(
            "SELECT entity_id FROM audit_logs WHERE organization_id = @o AND action = @a", ("o", account.OrganizationId), ("a", Action)));
        Assert.True(await database.ScalarAsync<bool>(
            "SELECT ip_address IS NOT NULL FROM audit_logs WHERE organization_id = @o AND action = @a", ("o", account.OrganizationId), ("a", Action)));

        var same = await CompanySettingsApi.PutAsync(host.Client, InternalPath, Theme("dark", "preferences_page"), cookie);
        Assert.Equal(HttpStatusCode.OK, same.StatusCode);
        Assert.Equal(afterChange, await UpdatedAtAsync(account.UserId));
        Assert.Equal(1, await database.CountAuditLogsAsync(account.OrganizationId, Action));

        var current = await CompanySettingsApi.GetAsync(host.Client, "/sessions/current", cookie);
        Assert.Equal("dark", (await PortalApi.ReadAsync(current))["user"]!["preferredTheme"]!.GetValue<string>());
    }

    // AC-17: invalid bodies on both endpoints change nothing and write no audit.
    [Fact]
    public async Task ThemeEndpoints_InvalidBodies_ReturnKeyedOrKeyless400And415AndChangeNothing()
    {
        var world = await database.SeedWorldAsync();
        var member = await database.SeedMemberAsync(world.Org, CompanySettingsDatabaseFixture.OwnerRoleId, "Olivia", "Owner");
        await database.LinkPortalAsync(world.Org, world.Contact, existingUser: member.UserId);
        await using var host = OnlineHost.Create(database);
        var internalCookie = await CompanySettingsApi.SignInCookieAsync(host.Client, member.Email);
        var portalCookie = await PortalApi.CookieAsync(host.Client, member.Email);

        foreach (var (path, cookie, validSource, otherSource) in new[]
        {
            (InternalPath, internalCookie, "user_menu", "portal_menu"),
            (PortalPath, portalCookie, "portal_menu", "user_menu"),
        })
        {
            var cases = new (string Raw, bool Theme, bool Source)[]
            {
                ($$"""{"theme":"Dark","source":"{{validSource}}"}""", true, false),
                ($$"""{"theme":" dark","source":"{{validSource}}"}""", true, false),
                ($$"""{"theme":"","source":"{{validSource}}"}""", true, false),
                ($$"""{"theme":null,"source":"{{validSource}}"}""", true, false),
                ($$"""{"theme":5,"source":"{{validSource}}"}""", true, false),
                ($$"""{"theme":"sepia","source":"{{validSource}}"}""", true, false),
                ($$"""{"source":"{{validSource}}"}""", true, false),
                ("""{"theme":"dark"}""", false, true),
                ("""{"theme":"dark","source":"unknown"}""", false, true),
                ($$"""{"theme":"dark","source":"{{otherSource}}"}""", false, true),
                ("""{"theme":"sepia","source":"unknown"}""", true, true),
            };

            foreach (var (raw, theme, source) in cases)
            {
                var response = await CompanySettingsApi.SendRawAsync(host.Client, HttpMethod.Put, path, raw, cookie);
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
                using var problem = await CompanySettingsApi.ReadJsonAsync(response);
                var errors = problem.RootElement.GetProperty("errors");
                Assert.Equal(theme, errors.TryGetProperty("theme", out var t) && t.EnumerateArray().Single().GetString() == ThemeMessage);
                Assert.Equal(source, errors.TryGetProperty("source", out var s) && s.EnumerateArray().Single().GetString() == SourceMessage);
            }

            foreach (var raw in new[] { "{", "null", string.Empty })
            {
                var response = await CompanySettingsApi.SendRawAsync(host.Client, HttpMethod.Put, path, raw, cookie);
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
                using var problem = await CompanySettingsApi.ReadJsonAsync(response);
                Assert.False(problem.RootElement.TryGetProperty("errors", out _));
            }

            var wrongType = await CompanySettingsApi.SendRawAsync(host.Client, HttpMethod.Put, path, "theme=dark", cookie, "text/plain");
            Assert.Equal(HttpStatusCode.UnsupportedMediaType, wrongType.StatusCode);
        }

        Assert.Equal("system", await StoredAsync(member.UserId));
        Assert.Equal(0, await database.CountAuditLogsAsync(world.Org, Action));
    }

    // AC-18: identifiers in the body are ignored; anonymous and wrong-session-type calls are 401; 413 on Kestrel.
    [Fact]
    public async Task ThemeEndpoints_IgnoreBodyIdentifiersRejectUnauthorizedCallersAndOversizedBodies()
    {
        var a = await database.SeedAccountAsync();
        var b = await database.SeedAccountAsync();
        await using var host = OnlineHost.Create(database);
        var cookieA = await CompanySettingsApi.SignInCookieAsync(host.Client, a.Email);

        var body = Theme("dark", "preferences_page");
        body["userId"] = b.UserId;
        body["organizationId"] = b.OrganizationId;
        Assert.Equal(HttpStatusCode.OK, (await CompanySettingsApi.PutAsync(host.Client, InternalPath, body, cookieA)).StatusCode);
        Assert.Equal("dark", await StoredAsync(a.UserId));
        Assert.Equal("system", await StoredAsync(b.UserId));
        Assert.Equal(1, await database.CountAuditLogsAsync(a.OrganizationId, Action));
        Assert.Equal(0, await database.CountAuditLogsAsync(b.OrganizationId, Action));

        var world = await database.SeedWorldAsync();
        var member = await database.SeedMemberAsync(world.Org, CompanySettingsDatabaseFixture.OwnerRoleId, "Olivia", "Owner");
        await database.LinkPortalAsync(world.Org, world.Contact, existingUser: member.UserId);
        var internalCookie = await CompanySettingsApi.SignInCookieAsync(host.Client, member.Email);
        var portalCookie = await PortalApi.CookieAsync(host.Client, member.Email);

        var denied = new[]
        {
            await CompanySettingsApi.PutAsync(host.Client, InternalPath, Theme("dark", "user_menu"), null),
            await PortalApi.SendAsync(host.Client, HttpMethod.Put, PortalPath, null, Theme("dark", "portal_menu")),
            await PortalApi.SendAsync(host.Client, HttpMethod.Put, PortalPath, internalCookie, Theme("dark", "portal_menu")),
            await PortalApi.SendAsync(host.Client, HttpMethod.Put, InternalPath, portalCookie, Theme("dark", "user_menu")),
        };

        foreach (var response in denied)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        Assert.Equal("system", await StoredAsync(member.UserId));
        Assert.Equal(0, await database.CountAuditLogsAsync(world.Org, Action));

        await using var factory = FieldOpsApiFactory.Create(connectionString: database.ConnectionString);
        factory.UseKestrel(0);
        factory.StartServer();
        factory.ClientOptions.HandleCookies = false;
        factory.ClientOptions.AllowAutoRedirect = false;
        using var client = factory.CreateClient();
        var kestrelInternal = await CompanySettingsApi.SignInCookieAsync(client, member.Email);
        var kestrelPortal = await PortalApi.CookieAsync(client, member.Email);
        var oversized = Theme("dark", "user_menu");
        oversized["padding"] = new string('x', 5000);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await CompanySettingsApi.PutAsync(client, InternalPath, oversized, kestrelInternal)).StatusCode);
        oversized["source"] = "portal_menu";
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await PortalApi.SendAsync(client, HttpMethod.Put, PortalPath, kestrelPortal, oversized)).StatusCode);
        Assert.Equal("system", await StoredAsync(member.UserId));
    }

    // AC-02, AC-19: the audit organization is the current portal account's; every session body carries the theme.
    [Fact]
    public async Task PortalTheme_TwoLinkedAccounts_AuditsCurrentOrganizationOnlyAndSessionBodiesCarryTheme()
    {
        await using var host = OnlineHost.Create(database);
        var a = await database.SeedWorldAsync();
        var b = await database.SeedWorldAsync();
        var user = await database.LinkPortalAsync(a.Org, a.Contact, linkedAt: DateTimeOffset.UtcNow.AddDays(-10));
        await database.LinkPortalAsync(b.Org, b.Contact, existingUser: user.UserId, linkedAt: DateTimeOffset.UtcNow.AddDays(-5));
        var signIn = await PortalApi.SignInAsync(host.Client, user.Email);
        Assert.Equal("system", (await PortalApi.ReadAsync(signIn))["user"]!["preferredTheme"]!.GetValue<string>());
        var cookie = PortalApi.IssuedCookie(signIn);

        var switched = await PortalApi.PostAsync(host.Client, "/portal/sessions/current/account", cookie, new JsonObject { ["contactId"] = b.Contact.ToString() });
        var reissued = PortalApi.IssuedCookie(switched);

        var changed = await PortalApi.SendAsync(host.Client, HttpMethod.Put, PortalPath, reissued, Theme("dark", "portal_menu"));
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal("no-store", changed.Headers.CacheControl?.ToString());
        Assert.Equal("dark", (await PortalApi.ReadAsync(changed))["preferredTheme"]!.GetValue<string>());
        Assert.Equal(0, await database.CountAuditLogsAsync(a.Org, Action));
        Assert.Equal(1, await database.CountAuditLogsAsync(b.Org, Action));
        var (_, _, metadata) = await database.GetLatestAuditAsync(b.Org, Action);
        using (var meta = JsonDocument.Parse(metadata!))
        {
            Assert.Equal("portal_menu", meta.RootElement.GetProperty("source").GetString());
            Assert.Equal("system", meta.RootElement.GetProperty("previousTheme").GetString());
        }

        var current = await PortalApi.GetAsync(host.Client, "/portal/sessions/current", reissued);
        Assert.Equal("dark", (await PortalApi.ReadAsync(current))["user"]!["preferredTheme"]!.GetValue<string>());
        var back = await PortalApi.PostAsync(host.Client, "/portal/sessions/current/account", reissued, new JsonObject { ["contactId"] = a.Contact.ToString() });
        Assert.Equal("dark", (await PortalApi.ReadAsync(back))["user"]!["preferredTheme"]!.GetValue<string>());
        var portalSignIn = await PortalApi.SignInAsync(host.Client, user.Email);
        Assert.Equal("dark", (await PortalApi.ReadAsync(portalSignIn))["user"]!["preferredTheme"]!.GetValue<string>());
    }

    private static JsonObject Theme(string theme, string source) => new() { ["theme"] = theme, ["source"] = source };

    private Task<string> StoredAsync(Guid userId) =>
        database.ScalarAsync<string>("SELECT preferred_theme FROM users WHERE id = @u", ("u", userId));

    private Task<DateTimeOffset> UpdatedAtAsync(Guid userId) =>
        database.ScalarAsync<DateTimeOffset>("SELECT updated_at FROM users WHERE id = @u", ("u", userId));
}
