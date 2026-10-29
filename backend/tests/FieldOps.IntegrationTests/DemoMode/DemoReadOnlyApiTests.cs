using System.Net;
using System.Text.Json.Nodes;
using FieldOps.Application.Features.Email;
using FieldOps.IntegrationTests.Api;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.PasswordResets;
using FieldOps.IntegrationTests.Portal;
using FieldOps.IntegrationTests.ServiceRequests;
using FieldOps.IntegrationTests.Sessions;
using FieldOps.IntegrationTests.Team;
using FieldOps.IntegrationTests.Users;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FieldOps.IntegrationTests.DemoMode;

/// <summary>Read-only demo through the real pipeline: queries keep working per role, writes and credential changes are refused before any effect.</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class DemoReadOnlyApiTests(CompanySettingsDatabaseFixture database)
{
    private const string DemoCode = "\"code\":\"demo_read_only\"";

    [Fact]
    public async Task DemoOrganization_StaffAndTechnician_QueryFreelyAndWritesAreRefusedBeforeAnyEffect()
    {
        var demo = await database.SeedWorldAsync();
        var control = await database.SeedWorldAsync();
        var sender = new RecordingEmailSender();
        var client = CreateClient(demo.Org, sender);

        var owner = await database.SeedMemberAsync(demo.Org, CompanySettingsDatabaseFixture.OwnerRoleId, "Olive", "Owner");
        var viewer = await database.SeedMemberAsync(demo.Org, CompanySettingsDatabaseFixture.ViewerRoleId, "Vic", "Viewer");
        var technician = await database.SeedMemberAsync(demo.Org, CompanySettingsDatabaseFixture.TechnicianRoleId, "Tess", "Tech");
        await database.SeedTechAsync(demo.Org, demo.BranchA, "Tess", "Tech", membershipId: technician.MembershipId);
        var controlOwner = await database.SeedMemberAsync(control.Org, CompanySettingsDatabaseFixture.OwnerRoleId, "Carl", "Control");
        var ownerCookie = await CompanySettingsApi.SignInCookieAsync(client, owner.Email);
        var viewerCookie = await CompanySettingsApi.SignInCookieAsync(client, viewer.Email);
        var technicianCookie = await CompanySettingsApi.SignInCookieAsync(client, technician.Email);
        var controlCookie = await CompanySettingsApi.SignInCookieAsync(client, controlOwner.Email);
        var branchesBefore = await database.ScalarAsync<long>("SELECT COUNT(*) FROM branches WHERE organization_id = @o", ("o", demo.Org));
        var hashBefore = await database.ScalarAsync<string>("SELECT password_hash FROM users WHERE id = @u", ("u", owner.UserId));

        // The session announces the demo; each role keeps the queries its policies allow.
        var session = await CompanySettingsApi.GetAsync(client, "/sessions/current", ownerCookie);
        Assert.True(JsonNode.Parse(await session.Content.ReadAsStringAsync())!["demoReadOnly"]!.GetValue<bool>());
        Assert.Equal(HttpStatusCode.OK, (await CompanySettingsApi.GetAsync(client, "/branches", ownerCookie)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await CompanySettingsApi.GetAsync(client, "/customers", viewerCookie)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await CompanySettingsApi.GetAsync(client, "/messages/summary", technicianCookie)).StatusCode);

        // Writes, credentials and external effects: refused by the demo guard, not by the role.
        var refusals = new[]
        {
            await CompanySettingsApi.SendRawAsync(client, HttpMethod.Post, "/branches", """{"name":"Hacked","code":"HK1"}""", ownerCookie),
            await CompanySettingsApi.SendRawAsync(client, HttpMethod.Put, "/organization-settings", "{}", ownerCookie),
            await CompanySettingsApi.SendRawAsync(client, HttpMethod.Post, "/users/invitations", """{"email":"new@example.com"}""", ownerCookie),
            await CompanySettingsApi.SendRawAsync(
                client,
                HttpMethod.Post,
                "/account/password",
                """{"currentPassword":"Correct horse battery staple 1","newPassword":"Another long password 22"}""",
                ownerCookie),
            await CompanySettingsApi.SendRawAsync(client, HttpMethod.Post, $"/technician/visits/{Guid.NewGuid()}/start-travel", null, technicianCookie),
        };

        foreach (var refusal in refusals)
        {
            Assert.Equal(HttpStatusCode.Forbidden, refusal.StatusCode);
            Assert.Contains(DemoCode, await refusal.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        // A viewer is still a viewer: the role denial comes first and is not the demo code.
        var viewerWrite = await CompanySettingsApi.SendRawAsync(client, HttpMethod.Post, "/branches", """{"name":"X","code":"X1"}""", viewerCookie);
        Assert.Equal(HttpStatusCode.Forbidden, viewerWrite.StatusCode);
        Assert.DoesNotContain(DemoCode, await viewerWrite.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        Assert.Equal(branchesBefore, await database.ScalarAsync<long>("SELECT COUNT(*) FROM branches WHERE organization_id = @o", ("o", demo.Org)));
        Assert.Equal(hashBefore, await database.ScalarAsync<string>("SELECT password_hash FROM users WHERE id = @u", ("u", owner.UserId)));
        Assert.Equal(0, await database.ScalarAsync<long>("SELECT COUNT(*) FROM user_invitations WHERE organization_id = @o", ("o", demo.Org)));
        Assert.Equal(0, sender.Attempts);

        // View preferences stay usable, and an organization outside the demo behaves normally.
        var language = await CompanySettingsApi.SendRawAsync(client, HttpMethod.Put, "/account/language", """{"language":"es"}""", ownerCookie);
        Assert.Equal(HttpStatusCode.OK, language.StatusCode);
        var normal = await CompanySettingsApi.SendRawAsync(client, HttpMethod.Post, "/branches", "{}", controlCookie);
        Assert.NotEqual(HttpStatusCode.Forbidden, normal.StatusCode);
        Assert.DoesNotContain(DemoCode, await normal.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DemoPortalAndRecovery_CustomerQueriesWorkWritesAndCredentialResetsAreRefused()
    {
        var demo = await database.SeedWorldAsync();
        var control = await database.SeedWorldAsync();
        var sender = new RecordingEmailSender();
        var client = CreateClient(demo.Org, sender);
        var customer = await database.LinkPortalAsync(demo.Org, demo.Contact);
        var owner = await database.SeedMemberAsync(demo.Org, CompanySettingsDatabaseFixture.OwnerRoleId, "Olive", "Owner");
        var controlOwner = await database.SeedMemberAsync(control.Org, CompanySettingsDatabaseFixture.OwnerRoleId, "Carl", "Control");

        var signIn = await PortalApi.SignInAsync(client, customer.Email);
        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
        Assert.True(JsonNode.Parse(await signIn.Content.ReadAsStringAsync())!["demoReadOnly"]!.GetValue<bool>());
        var cookie = PortalApi.IssuedCookie(signIn);
        var requestsBefore = await database.ScalarAsync<long>("SELECT COUNT(*) FROM service_requests WHERE organization_id = @o", ("o", demo.Org));

        Assert.Equal(HttpStatusCode.OK, (await PortalApi.GetAsync(client, "/portal/dashboard", cookie)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PortalApi.GetAsync(client, "/portal/requests", cookie)).StatusCode);

        var write = await PortalApi.PostAsync(client, "/portal/service-requests", cookie, new JsonObject());
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
        Assert.Contains(DemoCode, await write.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(requestsBefore, await database.ScalarAsync<long>("SELECT COUNT(*) FROM service_requests WHERE organization_id = @o", ("o", demo.Org)));

        // Password recovery answers like any address, but issues no token and sends nothing for demo accounts.
        foreach (var (path, email) in new[] { ("/password-resets", owner.Email), ("/portal/password-resets", customer.Email) })
        {
            var reset = await CompanySettingsApi.SendRawAsync(client, HttpMethod.Post, path, new JsonObject { ["email"] = email }.ToJsonString(), null);
            Assert.Equal(HttpStatusCode.Accepted, reset.StatusCode);
        }

        Assert.Equal(
            0,
            await database.ScalarAsync<long>(
                "SELECT COUNT(*) FROM password_reset_tokens WHERE user_id = ANY(@u)", ("u", new[] { owner.UserId, customer.UserId })));
        Assert.Equal(0, sender.Attempts);

        // An account outside the demo still receives its token.
        var normal = await CompanySettingsApi.SendRawAsync(
            client, HttpMethod.Post, "/password-resets", new JsonObject { ["email"] = controlOwner.Email }.ToJsonString(), null);
        Assert.Equal(HttpStatusCode.Accepted, normal.StatusCode);
        Assert.Equal(1, await database.ScalarAsync<long>("SELECT COUNT(*) FROM password_reset_tokens WHERE user_id = @u", ("u", controlOwner.UserId)));
    }

    private HttpClient CreateClient(Guid demoOrganization, RecordingEmailSender sender)
    {
        var factory = FieldOpsApiFactory.Create(connectionString: database.ConnectionString)
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("DemoMode:Enabled", "true");
                builder.UseSetting("DemoMode:OrganizationSlugs:0", $"org-{demoOrganization:N}");
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IEmailSender>();
                    services.AddSingleton<IEmailSender>(sender);
                });
            });

        return SessionApi.CreateClient(factory);
    }
}
