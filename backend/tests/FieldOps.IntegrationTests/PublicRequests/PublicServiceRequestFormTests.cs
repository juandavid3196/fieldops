using System.Net;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.CompanySettings;

namespace FieldOps.IntegrationTests.PublicRequests;

[Collection(CompanySettingsDatabaseCollection.Name)]
public class PublicServiceRequestFormTests(CompanySettingsDatabaseFixture database)
{
    // AC-01: only active categories with active services, service items only, no price data.
    [Fact]
    public async Task GetForm_FiltersCatalogAndExposesNoPricing()
    {
        var org = await database.SeedPublicOrgAsync();
        await database.SeedServiceAsync(org.Id, org.CategoryId, "Old service", active: false);
        await database.SeedServiceAsync(org.Id, org.CategoryId, "Copper pipe", type: "product");
        var electrical = await database.SeedCategoryAsync(org.Id, "Electrical");
        await database.SeedServiceAsync(org.Id, electrical, "Wiring");
        await database.SeedCategoryAsync(org.Id, "Empty category");
        var retired = await database.SeedCategoryAsync(org.Id, "Retired", active: false);
        await database.SeedServiceAsync(org.Id, retired, "Hidden service");

        await using var host = PublicRequestHost.Create(database);

        // The slug is compared case-insensitively.
        var response = await host.GetFormAsync(org.Slug.ToUpperInvariant());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        PublicRequestSeed.AssertNoStore(response);

        var raw = await response.Content.ReadAsStringAsync();
        var form = JsonNode.Parse(raw)!;

        Assert.Equal("https://acme.example", form["website"]!.GetValue<string>());
        Assert.Equal("+1 555 010 0100", form["phone"]!.GetValue<string>());
        Assert.Equal("REQ", form["requestPrefix"]!.GetValue<string>());
        Assert.Equal("UTC", form["timezone"]!.GetValue<string>());

        var categories = form["categories"]!.AsArray();
        Assert.Equal(["Electrical", "Plumbing"], categories.Select(c => c!["name"]!.GetValue<string>()));
        Assert.Equal(
            ["Drain cleaning"],
            categories[1]!["services"]!.AsArray().Select(s => s!["name"]!.GetValue<string>()));
        Assert.Equal(["Wiring"], categories[0]!["services"]!.AsArray().Select(s => s!["name"]!.GetValue<string>()));
        Assert.All(
            categories.SelectMany(c => c!["services"]!.AsArray()),
            service => Assert.Equal(["id", "name"], service!.AsObject().Select(p => p.Key).Order()));

        foreach (var forbidden in new[] { "price", "cost", "tax", "unit", "organizationId" })
        {
            Assert.DoesNotContain(forbidden, raw, StringComparison.OrdinalIgnoreCase);
        }
    }

    // AC-02: every BR-01 failure is the same 404 on both endpoints; nothing is persisted.
    [Fact]
    public async Task BothEndpoints_UnavailableOrganizations_ReturnOneIdentical404()
    {
        var valid = await database.SeedPublicOrgAsync();
        var inactive = await database.SeedPublicOrgAsync();
        await database.ExecuteAsync("UPDATE organizations SET is_active = false WHERE id = @o", ("o", inactive.Id));
        var noMain = await database.SeedPublicOrgAsync();
        await database.ExecuteAsync("UPDATE branches SET is_main = false WHERE id = @b", ("b", noMain.BranchId));
        var noServices = await database.SeedPublicOrgAsync();
        await database.ExecuteAsync(
            "UPDATE catalog_items SET is_active = false WHERE id = @s", ("s", noServices.ServiceId));

        var slugs = new[]
        {
            "no-such-organization", inactive.Slug, noMain.Slug, noServices.Slug,
        };

        await using var host = PublicRequestHost.Create(database);
        var bodies = new HashSet<string>(StringComparer.Ordinal);

        foreach (var slug in slugs)
        {
            var form = await host.GetFormAsync(slug);
            var submit = await host.PostAsync(slug, PublicRequestSeed.ValidBody(valid));

            foreach (var response in new[] { form, submit })
            {
                PublicRequestSeed.AssertStatus(HttpStatusCode.NotFound, response, slug);
                PublicRequestSeed.AssertNoStore(response);

                var problem = (await PublicRequestSeed.ReadAsync(response)).AsObject();
                problem.Remove("traceId");
                bodies.Add(problem.ToJsonString());
            }
        }

        Assert.Single(bodies);

        foreach (var org in new[] { inactive, noMain, noServices })
        {
            Assert.Equal(0L, await database.CountAsync("service_requests", org.Id));
        }

        // Positive control: the valid organization is still served.
        Assert.Equal(HttpStatusCode.OK, (await host.GetFormAsync(valid.Slug)).StatusCode);
    }
}
