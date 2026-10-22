using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using FieldOps.Application.Features.Users;
using FieldOps.IntegrationTests.Api;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.OnlinePayments;
using FieldOps.IntegrationTests.Organizations;
using FieldOps.IntegrationTests.Portal;
using FieldOps.IntegrationTests.ServiceRequests;
using FieldOps.IntegrationTests.Sessions;
using FieldOps.IntegrationTests.Users;

namespace FieldOps.IntegrationTests.Account;

/// <summary>
/// PUT /account/language and PUT /portal/account/language, the language in every session body and in account
/// creation (app i18n BR-01, BR-05 to BR-09; AC-02, AC-05 to AC-09, AC-12, AC-13).
/// </summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class AccountLanguageTests(CompanySettingsDatabaseFixture database)
{
    private const string InternalPath = "/account/language";
    private const string PortalPath = "/portal/account/language";
    private const string Action = "user.language_updated";
    private const string Message = "Choose a supported language.";
    private const string PortalPassword = "Brand new passphrase 1";

    // AC-05 to AC-08: store, same-value no-op, audit {from,to} in the session organization, and the invalid bodies.
    [Fact]
    public async Task PutAccountLanguage_ValidChangeNoOpAndInvalidBodies_StoresAuditsOnChangeAndRejectsUnsupportedValues()
    {
        var account = await database.SeedAccountAsync();
        await using var host = SessionTestHost.Create(database.ConnectionString);
        var cookie = await CompanySettingsApi.SignInCookieAsync(host.Client, account.Email);
        var before = await UpdatedAtAsync(account.UserId);

        var changed = await CompanySettingsApi.PutAsync(host.Client, InternalPath, Language("es"), cookie);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.True(changed.Headers.CacheControl?.NoStore);
        using (var body = await CompanySettingsApi.ReadJsonAsync(changed))
        {
            Assert.Equal(["preferredLanguage"], body.RootElement.EnumerateObject().Select(p => p.Name));
            Assert.Equal("es", body.RootElement.GetProperty("preferredLanguage").GetString());
        }

        Assert.Equal("es", await StoredAsync(account.UserId));
        var afterChange = await UpdatedAtAsync(account.UserId);
        Assert.True(afterChange > before);
        Assert.Equal(1, await database.CountAuditLogsAsync(account.OrganizationId, Action));
        var (_, _, metadata) = await database.GetLatestAuditAsync(account.OrganizationId, Action);
        using (var meta = JsonDocument.Parse(metadata!))
        {
            Assert.Equal(["from", "to"], meta.RootElement.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
            Assert.Equal("en", meta.RootElement.GetProperty("from").GetString());
            Assert.Equal("es", meta.RootElement.GetProperty("to").GetString());
        }

        Assert.Equal("user", await database.ScalarAsync<string>(
            "SELECT entity_type FROM audit_logs WHERE organization_id = @o AND action = @a", ("o", account.OrganizationId), ("a", Action)));
        Assert.Equal(account.UserId, await database.ScalarAsync<Guid>(
            "SELECT entity_id FROM audit_logs WHERE organization_id = @o AND action = @a", ("o", account.OrganizationId), ("a", Action)));
        Assert.True(await database.ScalarAsync<bool>(
            "SELECT ip_address IS NOT NULL FROM audit_logs WHERE organization_id = @o AND action = @a", ("o", account.OrganizationId), ("a", Action)));

        // The same value: 200, no write, no audit, updated_at untouched.
        var same = await CompanySettingsApi.PutAsync(host.Client, InternalPath, Language("es"), cookie);
        Assert.Equal(HttpStatusCode.OK, same.StatusCode);
        using (var body = await CompanySettingsApi.ReadJsonAsync(same))
        {
            Assert.Equal("es", body.RootElement.GetProperty("preferredLanguage").GetString());
        }

        Assert.Equal(afterChange, await UpdatedAtAsync(account.UserId));
        Assert.Equal(1, await database.CountAuditLogsAsync(account.OrganizationId, Action));

        Assert.Equal(HttpStatusCode.OK, (await CompanySettingsApi.PutAsync(host.Client, InternalPath, Language("en"), cookie)).StatusCode);
        Assert.Equal(2, await database.CountAuditLogsAsync(account.OrganizationId, Action));

        // Matching is exact: a keyed 400 for anything but "en" or "es"; keyless 400 for unreadable bodies.
        foreach (var raw in new[] { """{"language":"pt"}""", """{"language":"ES"}""", """{"language":" es"}""", """{"language":""}""", """{"language":null}""", """{"language":5}""", """{"language":true}""", "{}" })
        {
            var response = await CompanySettingsApi.SendRawAsync(host.Client, HttpMethod.Put, InternalPath, raw, cookie);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            using var problem = await CompanySettingsApi.ReadJsonAsync(response);
            Assert.Equal(Message, problem.RootElement.GetProperty("errors").GetProperty("language").EnumerateArray().Single().GetString());
        }

        foreach (var raw in new[] { "{", "null", string.Empty })
        {
            var response = await CompanySettingsApi.SendRawAsync(host.Client, HttpMethod.Put, InternalPath, raw, cookie);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            using var problem = await CompanySettingsApi.ReadJsonAsync(response);
            Assert.False(problem.RootElement.TryGetProperty("errors", out _));
        }

        var wrongType = await CompanySettingsApi.SendRawAsync(host.Client, HttpMethod.Put, InternalPath, "language=es", cookie, "text/plain");
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, wrongType.StatusCode);

        Assert.Equal("en", await StoredAsync(account.UserId));
        Assert.Equal(2, await database.CountAuditLogsAsync(account.OrganizationId, Action));
    }

    // AC-09 portal: the audit organization is the current portal account's, also after switching account.
    [Fact]
    public async Task PutPortalAccountLanguage_ChangeAndInvalid_AuditsTheCurrentPortalAccountOrganization()
    {
        await using var host = OnlineHost.Create(database);
        var a = await database.SeedWorldAsync();
        var b = await database.SeedWorldAsync();
        var user = await database.LinkPortalAsync(a.Org, a.Contact, linkedAt: DateTimeOffset.UtcNow.AddDays(-10));
        await database.LinkPortalAsync(b.Org, b.Contact, existingUser: user.UserId, linkedAt: DateTimeOffset.UtcNow.AddDays(-5));
        var signIn = await PortalApi.SignInAsync(host.Client, user.Email);
        var cookie = PortalApi.IssuedCookie(signIn);

        var changed = await PortalApi.SendAsync(host.Client, HttpMethod.Put, PortalPath, cookie, Language("es"));
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal("no-store", changed.Headers.CacheControl?.ToString());
        Assert.Equal("es", (await PortalApi.ReadAsync(changed))["preferredLanguage"]!.GetValue<string>());
        Assert.Equal(1, await database.CountAuditLogsAsync(a.Org, Action));
        Assert.Equal(0, await database.CountAuditLogsAsync(b.Org, Action));

        Assert.Equal(
            HttpStatusCode.OK,
            (await PortalApi.SendAsync(host.Client, HttpMethod.Put, PortalPath, cookie, Language("es"))).StatusCode);
        Assert.Equal(1, await database.CountAuditLogsAsync(a.Org, Action));

        var switched = await PortalApi.PostAsync(host.Client, "/portal/sessions/current/account", cookie, new JsonObject { ["contactId"] = b.Contact.ToString() });
        var reissued = PortalApi.IssuedCookie(switched);
        Assert.Equal("es", (await PortalApi.ReadAsync(switched))["user"]!["preferredLanguage"]!.GetValue<string>());

        Assert.Equal(
            HttpStatusCode.OK,
            (await PortalApi.SendAsync(host.Client, HttpMethod.Put, PortalPath, reissued, Language("en"))).StatusCode);
        Assert.Equal(1, await database.CountAuditLogsAsync(a.Org, Action));
        Assert.Equal(1, await database.CountAuditLogsAsync(b.Org, Action));
        var (_, _, metadata) = await database.GetLatestAuditAsync(b.Org, Action);
        using (var meta = JsonDocument.Parse(metadata!))
        {
            Assert.Equal("es", meta.RootElement.GetProperty("from").GetString());
            Assert.Equal("en", meta.RootElement.GetProperty("to").GetString());
        }

        var invalid = await PortalApi.SendAsync(host.Client, HttpMethod.Put, PortalPath, reissued, Language("pt"));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(Message, (await PortalApi.ReadAsync(invalid, HttpStatusCode.BadRequest))["errors"]!["language"]![0]!.GetValue<string>());
        var number = await PortalApi.SendAsync(host.Client, HttpMethod.Put, PortalPath, reissued, new JsonObject { ["language"] = 5 });
        Assert.Equal(HttpStatusCode.BadRequest, number.StatusCode);
        Assert.Equal("en", await StoredAsync(user.UserId));
    }

    // AC-02: preferredLanguage in every session body.
    [Fact]
    public async Task SessionBodies_InternalAndPortal_IncludeThePreferredLanguage()
    {
        await using var host = OnlineHost.Create(database);
        var a = await database.SeedWorldAsync();
        var b = await database.SeedWorldAsync();
        var member = await database.SeedMemberAsync(a.Org, CompanySettingsDatabaseFixture.OwnerRoleId, "Olivia", "Owner");
        await database.LinkPortalAsync(a.Org, a.Contact, existingUser: member.UserId, linkedAt: DateTimeOffset.UtcNow.AddDays(-10));
        await database.LinkPortalAsync(b.Org, b.Contact, existingUser: member.UserId, linkedAt: DateTimeOffset.UtcNow.AddDays(-5));

        var signIn = await SessionApi.SignInAsync(host.Client, member.Email, CompanySettingsDatabaseFixture.Password);
        var cookie = SessionApi.GetIssuedCookie(signIn);
        Assert.Equal("en", (await PortalApi.ReadAsync(signIn))["user"]!["preferredLanguage"]!.GetValue<string>());

        Assert.Equal(HttpStatusCode.OK, (await CompanySettingsApi.PutAsync(host.Client, InternalPath, Language("es"), cookie)).StatusCode);
        var current = await CompanySettingsApi.GetAsync(host.Client, "/sessions/current", cookie);
        Assert.Equal("es", (await PortalApi.ReadAsync(current))["user"]!["preferredLanguage"]!.GetValue<string>());

        var portalSignIn = await PortalApi.SignInAsync(host.Client, member.Email);
        var portalCookie = PortalApi.IssuedCookie(portalSignIn);
        Assert.Equal("es", (await PortalApi.ReadAsync(portalSignIn))["user"]!["preferredLanguage"]!.GetValue<string>());
        var portalCurrent = await PortalApi.GetAsync(host.Client, "/portal/sessions/current", portalCookie);
        Assert.Equal("es", (await PortalApi.ReadAsync(portalCurrent))["user"]!["preferredLanguage"]!.GetValue<string>());
        var switched = await PortalApi.PostAsync(host.Client, "/portal/sessions/current/account", portalCookie, new JsonObject { ["contactId"] = b.Contact.ToString() });
        Assert.Equal("es", (await PortalApi.ReadAsync(switched))["user"]!["preferredLanguage"]!.GetValue<string>());
    }

    // AC-12, AC-13: the optional preferredLanguage of the three account-creation flows; unsupported values fall back to en without an error.
    [Fact]
    public async Task AccountCreation_PreferredLanguage_StoresSupportedOtherwiseEnAndLeavesAcceptExistingUntouched()
    {
        await using var host = SessionTestHost.Create(database.ConnectionString);

        // Organization registration.
        foreach (var (include, value, expected) in new (bool, JsonNode?, string)[] { (true, "es", "es"), (true, " es", "en"), (true, 5, "en"), (true, null, "en"), (false, null, "en") })
        {
            var email = OrganizationRegistrationApi.NewEmail();
            var body = JsonNode.Parse(OrganizationRegistrationApi.ValidBody(ownerEmail: email))!.AsObject();

            if (include)
            {
                body["preferredLanguage"] = value;
            }

            var created = await OrganizationRegistrationApi.PostAsync(host.Client, body.ToJsonString());
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            Assert.Equal(expected, await database.ScalarAsync<string>("SELECT preferred_language FROM users WHERE email = @e", ("e", email)));
        }

        // Internal invitation: new user.
        var org = await database.SeedOrganizationAsync();
        var owner = await database.SeedMemberAsync(org, CompanySettingsDatabaseFixture.OwnerRoleId, "Olivia", "Owner");

        foreach (var (value, expected) in new (JsonNode?, string)[] { ("es", "es"), ("pt", "en"), (7, "en"), (null, "en") })
        {
            var email = CompanySettingsDatabaseFixture.NewEmail();
            var (token, hash) = InvitationTokens.Generate();
            await database.SeedInvitationAsync(org, owner.UserId, "Ivy", "Invitee", email, CompanySettingsDatabaseFixture.DispatcherRoleId, DateTimeOffset.UtcNow.AddDays(7), tokenHash: hash);
            var accepted = await CompanySettingsApi.PostAsync(
                host.Client,
                "/invitations/accept",
                new JsonObject { ["token"] = token, ["firstName"] = "Nina", ["lastName"] = "Nguyen", ["password"] = "Valid password 123", ["preferredLanguage"] = value },
                null);
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
            Assert.Equal(expected, (await PortalApi.ReadAsync(accepted))["user"]!["preferredLanguage"]!.GetValue<string>());
            Assert.Equal(expected, await database.ScalarAsync<string>("SELECT preferred_language FROM users WHERE email = @e", ("e", email)));
        }

        // Accept-existing never reads it.
        var existing = await database.SeedMemberAsync(await database.SeedOrganizationAsync(), CompanySettingsDatabaseFixture.OwnerRoleId, "Eve", "Existing");
        await database.ExecuteAsync("UPDATE users SET preferred_language = 'es' WHERE id = @u", ("u", existing.UserId));
        var existingCookie = await CompanySettingsApi.SignInCookieAsync(host.Client, existing.Email);
        var (existingToken, existingHash) = InvitationTokens.Generate();
        await database.SeedInvitationAsync(org, owner.UserId, "Eve", "Existing", existing.Email, CompanySettingsDatabaseFixture.DispatcherRoleId, DateTimeOffset.UtcNow.AddDays(7), tokenHash: existingHash);
        var linked = await CompanySettingsApi.PostAsync(
            host.Client, "/invitations/accept-existing", new JsonObject { ["token"] = existingToken, ["preferredLanguage"] = "en" }, existingCookie);
        Assert.Equal(HttpStatusCode.OK, linked.StatusCode);
        Assert.Equal("es", await StoredAsync(existing.UserId));

        // Portal activation.
        await using var portal = OnlineHost.Create(database);
        var staff = await database.SeedUserAsync();

        foreach (var (value, expected) in new (JsonNode?, string)[] { ("es", "es"), ("fr", "en"), (true, "en") })
        {
            var world = await database.SeedWorldAsync();
            var email = $"contact-{Guid.NewGuid():N}@example.com";
            await database.ExecuteAsync("UPDATE customer_contacts SET email = @e WHERE id = @c", ("e", email), ("c", world.Contact));
            var token = PortalSeed.NewToken();
            await database.SeedInvitationAsync(world.Org, world.Contact, staff, token, email);
            var accepted = await PortalApi.PostAsync(
                portal.Client,
                "/portal/invitations/accept",
                null,
                new JsonObject { ["token"] = token, ["password"] = PortalPassword, ["preferredLanguage"] = value });
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
            Assert.Equal(expected, (await PortalApi.ReadAsync(accepted))["user"]!["preferredLanguage"]!.GetValue<string>());
            Assert.Equal(expected, await database.ScalarAsync<string>("SELECT preferred_language FROM users WHERE email = @e", ("e", email)));
        }
    }

    // AC-09 security: identifiers in the body are ignored, anonymous and cross-area cookies are 401.
    [Fact]
    public async Task LanguageEndpoints_IgnoreBodyIdentifiersAndRejectAnonymousAndWrongAreaCookies()
    {
        var a = await database.SeedAccountAsync();
        var b = await database.SeedAccountAsync();
        await using var host = OnlineHost.Create(database);
        var cookieA = await CompanySettingsApi.SignInCookieAsync(host.Client, a.Email);

        var put = await CompanySettingsApi.PutAsync(
            host.Client,
            InternalPath,
            new JsonObject { ["language"] = "es", ["userId"] = b.UserId, ["organizationId"] = b.OrganizationId },
            cookieA);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        Assert.Equal("es", await StoredAsync(a.UserId));
        Assert.Equal("en", await StoredAsync(b.UserId));
        Assert.Equal(1, await database.CountAuditLogsAsync(a.OrganizationId, Action));
        Assert.Equal(0, await database.CountAuditLogsAsync(b.OrganizationId, Action));

        var world = await database.SeedWorldAsync();
        var member = await database.SeedMemberAsync(world.Org, CompanySettingsDatabaseFixture.OwnerRoleId, "Olivia", "Owner");
        await database.LinkPortalAsync(world.Org, world.Contact, existingUser: member.UserId);
        var internalCookie = await CompanySettingsApi.SignInCookieAsync(host.Client, member.Email);
        var portalCookie = await PortalApi.CookieAsync(host.Client, member.Email);

        var denied = new[]
        {
            await CompanySettingsApi.PutAsync(host.Client, InternalPath, Language("es"), null),
            await PortalApi.SendAsync(host.Client, HttpMethod.Put, PortalPath, null, Language("es")),
            await PortalApi.SendAsync(host.Client, HttpMethod.Put, PortalPath, internalCookie, Language("es")),
            await PortalApi.SendAsync(host.Client, HttpMethod.Put, InternalPath, portalCookie, Language("es")),
        };

        foreach (var response in denied)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Null(response.Headers.Location);
        }

        Assert.Equal("en", await StoredAsync(member.UserId));
        Assert.Equal(0, await database.CountAuditLogsAsync(world.Org, Action));
    }

    // 413 on both endpoints is enforced by the real server: run on Kestrel.
    [Fact]
    public async Task LanguageEndpoints_BodyLargerThan4Kilobytes_Return413AndChangeNothing()
    {
        var world = await database.SeedWorldAsync();
        var member = await database.SeedMemberAsync(world.Org, CompanySettingsDatabaseFixture.OwnerRoleId, "Olivia", "Owner");
        await database.LinkPortalAsync(world.Org, world.Contact, existingUser: member.UserId);
        await using var factory = FieldOpsApiFactory.Create(connectionString: database.ConnectionString);
        factory.UseKestrel(0);
        factory.StartServer();
        factory.ClientOptions.HandleCookies = false;
        factory.ClientOptions.AllowAutoRedirect = false;
        using var client = factory.CreateClient();
        var internalCookie = await CompanySettingsApi.SignInCookieAsync(client, member.Email);
        var portalCookie = await PortalApi.CookieAsync(client, member.Email);
        var oversized = new JsonObject { ["language"] = "es", ["padding"] = new string('x', 5000) };

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await CompanySettingsApi.PutAsync(client, InternalPath, oversized, internalCookie)).StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await PortalApi.SendAsync(client, HttpMethod.Put, PortalPath, portalCookie, oversized)).StatusCode);
        Assert.Equal("en", await StoredAsync(member.UserId));
        Assert.Equal(0, await database.CountAuditLogsAsync(world.Org, Action));
    }

    private static JsonObject Language(string value) => new() { ["language"] = value };

    private Task<string> StoredAsync(Guid userId) =>
        database.ScalarAsync<string>("SELECT preferred_language FROM users WHERE id = @u", ("u", userId));

    private Task<DateTimeOffset> UpdatedAtAsync(Guid userId) =>
        database.ScalarAsync<DateTimeOffset>("SELECT updated_at FROM users WHERE id = @u", ("u", userId));
}
