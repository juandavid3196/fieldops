using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.Organizations;
using FieldOps.IntegrationTests.Sessions;

namespace FieldOps.IntegrationTests.PublicRequests;

[Collection(CompanySettingsDatabaseCollection.Name)]
public class PublicSlugRegistrationTests(CompanySettingsDatabaseFixture database)
{
    // AC-24: slug rules, collision suffixes, concurrent registrations and rename.
    [Fact]
    public async Task Register_AssignsSlugPerRulesWithCollisionSuffixes_AndRenameKeepsTheSlug()
    {
        await using var host = OrganizationRegistrationTestHost.Create(database.ConnectionString);
        var token = Guid.NewGuid().ToString("N")[..8];

        // Accents and symbols (BR-21 steps 1-4), then the lowest free suffix.
        var accented = await RegisterAsync(host, $"Café & Co {token}.");
        Assert.Equal($"cafe-co-{token}", await SlugAsync(accented));
        Assert.Equal($"cafe-co-{token}-2", await SlugAsync(await RegisterAsync(host, $"CAFE   CO {token}")));
        Assert.Equal($"cafe-co-{token}-3", await SlugAsync(await RegisterAsync(host, $"Café & Co {token}!")));

        // Symbols only fall back to "organization"; the next one gets a suffix.
        Assert.Equal("organization", await SlugAsync(await RegisterAsync(host, "&&& ---")));
        Assert.Equal("organization-2", await SlugAsync(await RegisterAsync(host, "***")));

        // Total length including the suffix stays within 60.
        var longName = new string('x', 70);
        Assert.Equal(new string('x', 60), await SlugAsync(await RegisterAsync(host, longName)));
        Assert.Equal(new string('x', 58) + "-2", await SlugAsync(await RegisterAsync(host, longName)));

        // Two concurrent registrations with the same name both succeed with distinct slugs.
        var concurrentName = $"Racing Plumbers {token}";
        var racing = await Task.WhenAll(
            RegisterAsync(host, concurrentName),
            RegisterAsync(host, concurrentName),
            RegisterAsync(host, concurrentName));
        var slugs = new List<string>();

        foreach (var id in racing)
        {
            slugs.Add(await SlugAsync(id));
        }

        Assert.Equal(
            [$"racing-plumbers-{token}", $"racing-plumbers-{token}-2", $"racing-plumbers-{token}-3"],
            slugs.Order(StringComparer.Ordinal));

        // Renaming through Company settings leaves the slug unchanged (FR-16).
        var owner = await database.SeedAccountAsync();
        var slugBefore = await SlugAsync(owner.OrganizationId);
        await using var session = SessionTestHost.Create(database.ConnectionString);
        var signIn = await SessionApi.SignInAsync(session.Client, owner.Email, CompanySettingsDatabaseFixture.Password);
        var cookie = SessionApi.GetIssuedCookie(signIn);
        var current = await CompanySettingsApi.ReadAsAsync<OrganizationSettingsBody>(
            await CompanySettingsApi.GetAsync(session.Client, "/organization-settings", cookie));

        var body = OrganizationSettingsEndpointTests.ValidBody(current.UpdatedAt.ToString("O"));
        body["name"] = "Totally Different Name";
        var renamed = await CompanySettingsApi.PutAsync(session.Client, "/organization-settings", body, cookie);

        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal(
            "Totally Different Name",
            await database.ScalarAsync<string>("SELECT name FROM organizations WHERE id = @o", ("o", owner.OrganizationId)));
        Assert.Equal(slugBefore, await SlugAsync(owner.OrganizationId));
    }

    private static async Task<Guid> RegisterAsync(OrganizationRegistrationTestHost host, string organizationName)
    {
        var body = OrganizationRegistrationApi.ValidBody(
            ownerEmail: OrganizationRegistrationApi.NewEmail(), organizationName: organizationName);
        var response = await OrganizationRegistrationApi.PostAsync(host.Client, body);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        return json.GetProperty("organizationId").GetGuid();
    }

    private Task<string> SlugAsync(Guid organizationId) =>
        database.ScalarAsync<string>("SELECT public_slug FROM organizations WHERE id = @o", ("o", organizationId));
}
