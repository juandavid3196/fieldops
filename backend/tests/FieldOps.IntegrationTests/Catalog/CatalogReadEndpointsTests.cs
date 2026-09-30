using System.Net;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.Users;

namespace FieldOps.IntegrationTests.Catalog;

/// <summary>GET /catalog-items, /summary and /{id} (FR-02, FR-03, FR-06, FR-10): AC-02, AC-03, AC-10.</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class CatalogReadEndpointsTests(CompanySettingsDatabaseFixture database)
{
    // AC-02 and AC-03: filters, sorts, paging, margins, organization isolation, summary and currency.
    [Fact]
    public async Task ListAndSummary_FilterSortPageAndCountPerOrganization()
    {
        var org = await database.SeedOrganizationAsync();
        var other = await database.SeedOrganizationAsync();
        await database.ExecuteAsync("UPDATE organizations SET currency = 'EUR' WHERE id = @o", ("o", org));

        await database.SeedCatalogItemAsync(org, "service", "Alpha Drain", price: 100m, cost: 20m, description: "Clears clogs");
        await database.SeedCatalogItemAsync(org, "product", "beta pipe", price: 0m, cost: 10m, taxable: false);
        await database.SeedCatalogItemAsync(org, "product", "Gamma Valve", price: 40m, cost: 50m, active: false);
        await database.SeedCatalogItemAsync(org, "service", "delta repair", price: 60m, cost: 20m, taxable: false, active: false);

        foreach (var index in Enumerable.Range(1, 10))
        {
            await database.SeedCatalogItemAsync(org, "service", $"Item {index:00}", price: index);
        }

        await database.SeedCatalogItemAsync(other, "service", "Alpha Drain");
        await database.SeedCatalogItemAsync(other, "product", "Zeta Other");

        await using var host = CatalogHost.Create(database);
        var cookie = await host.CookieAsync(database, org, CompanySettingsDatabaseFixture.ViewerRoleId);

        var cases = new (string Query, string[] Expected, int Total)[]
        {
            ("", ["Alpha Drain", "beta pipe", "delta repair", "Gamma Valve", "Item 01", "Item 02", "Item 03", "Item 04", "Item 05", "Item 06"], 14),
            ("?page=2", ["Item 07", "Item 08", "Item 09", "Item 10"], 14),
            ("?type=product&sort=-name", ["Gamma Valve", "beta pipe"], 2),
            ("?status=inactive&sort=unitPrice", ["Gamma Valve", "delta repair"], 2),
            ("?taxStatus=non_taxable&sort=-unitCost", ["delta repair", "beta pipe"], 2),
            ("?search=CLOG", ["Alpha Drain"], 1),
            ("?search=pipe&type=service", [], 0),
            ("?search=100%25", [], 0),
            ("?sort=type", ["beta pipe", "Gamma Valve", "Alpha Drain", "delta repair", "Item 01", "Item 02", "Item 03", "Item 04", "Item 05", "Item 06"], 14),
            ("?sort=-status", ["delta repair", "Gamma Valve", "Alpha Drain", "beta pipe", "Item 01", "Item 02", "Item 03", "Item 04", "Item 05", "Item 06"], 14),
            ("?sort=taxable", ["beta pipe", "delta repair", "Alpha Drain", "Gamma Valve", "Item 01", "Item 02", "Item 03", "Item 04", "Item 05", "Item 06"], 14),
            ("?sort=-unitPrice&pageSize=10", ["Alpha Drain", "delta repair", "Gamma Valve", "Item 10", "Item 09", "Item 08", "Item 07", "Item 06", "Item 05", "Item 04"], 14),
        };

        foreach (var (query, expected, total) in cases)
        {
            var response = await host.SendAsync(HttpMethod.Get, $"/catalog-items{query}", cookie);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{query}: {response.StatusCode}");
            Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.Ordinal);

            var list = await CatalogHost.ReadAsync(response);
            Assert.Equal(expected, CatalogSeed.Names(list));
            Assert.Equal(total, list["totalCount"]!.GetValue<int>());
            Assert.Equal(10, list["pageSize"]!.GetValue<int>());
        }

        // BR-04 / BR-09 row fields: margin rule incl. price 0 (null) and cost above price (negative).
        var rows = (await CatalogHost.ReadAsync(await host.SendAsync(HttpMethod.Get, "/catalog-items?sort=name", cookie)))["items"]!.AsArray();
        var alpha = rows[0]!;
        Assert.Equal("service", alpha["type"]!.GetValue<string>());
        Assert.Equal(80.0m, alpha["estimatedMarginPercent"]!.GetValue<decimal>());
        Assert.Equal("Clears clogs", alpha["description"]!.GetValue<string>());
        Assert.True(alpha["isTaxable"]!.GetValue<bool>());
        Assert.False(alpha["hasImage"]!.GetValue<bool>());
        Assert.Null(rows[1]!["estimatedMarginPercent"]);
        Assert.Equal(66.7m, rows[2]!["estimatedMarginPercent"]!.GetValue<decimal>());
        Assert.Equal(-25.0m, rows[3]!["estimatedMarginPercent"]!.GetValue<decimal>());

        foreach (var invalid in new[]
        {
            "type=widget", "taxStatus=other", "status=archived", "sort=-margin", "page=0", "page=abc", "pageSize=20",
            $"search={new string('x', 101)}",
        })
        {
            var response = await host.SendAsync(HttpMethod.Get, $"/catalog-items?{invalid}", cookie);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var key = invalid[..invalid.IndexOf('=', StringComparison.Ordinal)];
            Assert.NotNull((await CatalogHost.ReadAsync(response))["errors"]![key == "sort" ? "sort" : key]);
        }

        // Summary ignores list filters and the other organization; currency is the organization's.
        foreach (var query in new[] { string.Empty, "?type=product&status=inactive" })
        {
            var summary = await CatalogHost.ReadAsync(await host.SendAsync(HttpMethod.Get, $"/catalog-items/summary{query}", cookie));
            Assert.Equal(12, summary["activeItems"]!.GetValue<int>());
            Assert.Equal(11, summary["activeServices"]!.GetValue<int>());
            Assert.Equal(1, summary["activeProducts"]!.GetValue<int>());
            Assert.Equal(2, summary["inactiveItems"]!.GetValue<int>());
            Assert.Equal(14, summary["allItems"]!.GetValue<int>());
            Assert.Equal(12, summary["services"]!.GetValue<int>());
            Assert.Equal(2, summary["products"]!.GetValue<int>());
            Assert.Equal("EUR", summary["currency"]!.GetValue<string>());
        }
    }

    // AC-10: distinct usage across quote versions, work orders and invoices; statuses all count; other organizations ignored.
    [Fact]
    public async Task Detail_UsageCountsDistinctRecordsOfTheOrganizationOnly()
    {
        var org = await database.SeedOrganizationAsync();
        var other = await database.SeedOrganizationAsync();
        var owner = await database.SeedMemberAsync(org, CompanySettingsDatabaseFixture.OwnerRoleId, "Ola", "Owner");
        var otherOwner = await database.SeedMemberAsync(other, CompanySettingsDatabaseFixture.OwnerRoleId, "Bo", "Owner");
        var branch = await database.SeedBranchAsync(org);
        var otherBranch = await database.SeedBranchAsync(other);
        var item = await database.SeedCatalogItemAsync(org, "service", "Used item");
        var unused = await database.SeedCatalogItemAsync(org, "product", "Unused item");

        await database.SeedUsageChainAsync(org, owner.UserId, branch.Id, item);
        await database.SeedUsageChainAsync(other, otherOwner.UserId, otherBranch.Id, item);

        await using var host = CatalogHost.Create(database);
        var cookie = await host.SignInAsync(owner.Email);

        foreach (var (id, quotes, jobs, invoices) in new[] { (item, 1, 1, 1), (unused, 0, 0, 0) })
        {
            var response = await host.SendAsync(HttpMethod.Get, $"/catalog-items/{id}", cookie);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var detail = await CatalogHost.ReadAsync(response);
            Assert.Equal(quotes, detail["usage"]!["quotes"]!.GetValue<int>());
            Assert.Equal(jobs, detail["usage"]!["jobs"]!.GetValue<int>());
            Assert.Equal(invoices, detail["usage"]!["invoices"]!.GetValue<int>());
            Assert.Null(detail["image"]);
        }
    }
}

internal static class CatalogHostExtensions
{
    public static Task<string> SignInAsync(this CatalogHost host, string email) =>
        CompanySettingsApi.SignInCookieAsync(host.Client, email);
}
