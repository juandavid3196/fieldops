using System.Globalization;
using System.Net;
using System.Text;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.Users;

namespace FieldOps.IntegrationTests.Catalog;

/// <summary>Export, template and import (FR-12, FR-13, FR-14, FR-15): AC-13, AC-14, AC-15, AC-16.</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class CatalogCsvEndpointsTests(CompanySettingsDatabaseFixture database)
{
    private const short Owner = CompanySettingsDatabaseFixture.OwnerRoleId;

    // AC-13 and AC-14: filters/sort, RFC 4180 quoting, formula neutralization, tenant scope, file names and the template.
    [Fact]
    public async Task ExportAndTemplate_ReturnNeutralizedFilteredCsvAndHeaderOnlyTemplate()
    {
        var org = await database.SeedOrganizationAsync();
        var other = await database.SeedOrganizationAsync();
        await database.ExecuteAsync("UPDATE organizations SET timezone = 'Pacific/Kiritimati' WHERE id = @o", ("o", org));

        await database.SeedCatalogItemAsync(org, "service", "=SUM(1,1)", price: 2m, cost: 1m, description: "+cmd");
        await database.SeedCatalogItemAsync(org, "product", "-2+3", price: 3m, description: "@user", taxable: false);
        await database.SeedCatalogItemAsync(org, "service", "\tTabbed", price: 4m);
        await database.SeedCatalogItemAsync(org, "product", "Comma, \"Quote\"", price: 5.5m, description: "line one\nline two", active: false);
        await database.SeedCatalogItemAsync(org, "service", "Plain", price: 6m);
        await database.SeedCatalogItemAsync(other, "service", "Other organization secret");

        await using var host = CatalogHost.Create(database);
        var viewer = await host.CookieAsync(database, org, CompanySettingsDatabaseFixture.ViewerRoleId);
        var ownerCookie = await host.CookieAsync(database, org, Owner);

        var response = await host.SendAsync(HttpMethod.Get, "/catalog-items/export?sort=unitPrice", viewer);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.Ordinal);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);

        var zone = TimeZoneInfo.FindSystemTimeZoneById("Pacific/Kiritimati");
        var fileName = response.Content.Headers.ContentDisposition?.FileName?.Trim('"');
        var expectedNames = new[] { DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1) }
            .Select(moment => $"products-services-{TimeZoneInfo.ConvertTime(moment, zone).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.csv");
        Assert.Contains(fileName, expectedNames);

        var text = await response.Content.ReadAsStringAsync();
        Assert.Equal(
            CatalogSeed.CsvOf(
                CatalogSeed.CsvHeader,
                "service,\"'=SUM(1,1)\",'+cmd,1.00,2.00,true,true",
                "product,'-2+3,'@user,0.00,3.00,false,true",
                "service,'\tTabbed,,0.00,4.00,true,true",
                "product,\"Comma, \"\"Quote\"\"\",\"line one\nline two\",0.00,5.50,true,false",
                "service,Plain,,0.00,6.00,true,true"),
            text);
        Assert.DoesNotContain("Other organization secret", text, StringComparison.Ordinal);

        // Filters and sort apply without paging; no match is header-only.
        var filtered = await (await host.SendAsync(HttpMethod.Get, "/catalog-items/export?type=product&status=active&sort=-name", viewer)).Content.ReadAsStringAsync();
        Assert.Equal(CatalogSeed.CsvOf(CatalogSeed.CsvHeader, "product,'-2+3,'@user,0.00,3.00,false,true"), filtered);
        var empty = await (await host.SendAsync(HttpMethod.Get, "/catalog-items/export?search=zzzz", viewer)).Content.ReadAsStringAsync();
        Assert.Equal(CatalogSeed.CsvOf(CatalogSeed.CsvHeader), empty);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(HttpMethod.Get, "/catalog-items/export?sort=nope", viewer)).StatusCode);

        // AC-14: the template is the header row only.
        var template = await host.SendAsync(HttpMethod.Get, "/catalog-items/import-template", ownerCookie);
        Assert.Equal(HttpStatusCode.OK, template.StatusCode);
        Assert.Equal("products-services-template.csv", template.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        Assert.Equal(CatalogSeed.CsvOf(CatalogSeed.CsvHeader), await template.Content.ReadAsStringAsync());
    }

    // AC-15: case variants, reordered columns, empty flags, multi-line names, one transaction and one audit row.
    [Fact]
    public async Task Import_MixedValidRows_CreatesAllInOneTransactionWithOneAuditRow()
    {
        var org = await database.SeedOrganizationAsync();
        await using var host = CatalogHost.Create(database);
        var cookie = await host.CookieAsync(database, org, Owner);

        var csv = CatalogSeed.CsvOf(
            "Name,TYPE,Description,Unit_Price,Unit_Cost,Active,Taxable",
            "  Pipe   Wrench ,Product,\"Heavy, steel\",12.50,5,,",
            "Leak Repair,SERVICE,,80,20.5,No,YES",
            "\" Multi\nLine \",service,,0,0,false,FALSE");

        var response = await host.SendFileAsync(HttpMethod.Post, "/catalog-items/import", cookie, CatalogSeed.Utf8(csv), "text/csv");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, (await CatalogHost.ReadAsync(response))["importedCount"]!.GetValue<int>());
        Assert.Equal(3, await database.CountItemsAsync(org));
        Assert.Equal(
            "product|Pipe Wrench|Heavy, steel|5.00|12.50|true|true",
            await database.TextAsync("SELECT concat_ws('|', type::text, name, description, unit_cost::text, unit_price::text, is_taxable::text, is_active::text) FROM catalog_items WHERE organization_id = @o AND normalized_name = 'pipe wrench'", ("o", org)));
        Assert.Equal(
            "service|Leak Repair|20.50|80.00|true|false",
            await database.TextAsync("SELECT concat_ws('|', type::text, name, unit_cost::text, unit_price::text, is_taxable::text, is_active::text) FROM catalog_items WHERE organization_id = @o AND normalized_name = 'leak repair'", ("o", org)));
        Assert.Equal(1, await database.CountAsync("SELECT COUNT(*) FROM catalog_items WHERE organization_id = @o AND name = 'Multi Line' AND NOT is_taxable AND NOT is_active", ("o", org)));

        Assert.Equal(1, await database.CountCatalogAuditAsync(org));
        Assert.Equal(1, await database.CountAsync("SELECT COUNT(*) FROM audit_logs WHERE organization_id = @o AND action = 'catalog_item.imported' AND entity_id IS NULL AND metadata->>'importedCount' = '3'", ("o", org)));
    }

    // AC-16: file-level and row-level failures create nothing; 413/415 and the unique-index conflict.
    [Fact]
    public async Task Import_InvalidFilesAndRows_ReturnErrorsAndCreateNothing()
    {
        var org = await database.SeedOrganizationAsync();
        await database.SeedCatalogItemAsync(org, "service", "Existing Item");
        await using var host = CatalogHost.Create(database);
        var cookie = await host.CookieAsync(database, org, Owner);
        var auditBefore = await database.CountCatalogAuditAsync(org);

        string Rows(int count, Func<int, string> row) =>
            CatalogSeed.CsvOf([CatalogSeed.CsvHeader, .. Enumerable.Range(1, count).Select(row)]);

        var fileErrors = new (string Name, byte[] Bytes, string Message)[]
        {
            ("oversized", CatalogSeed.Utf8(CatalogSeed.CsvHeader + "\r\n" + new string('x', 1_048_577)), "Choose a CSV file of 1 MB or smaller."),
            ("not-utf8", [0xFF, 0xFE, 0xFA, 0x80, 0x81, 0xC0], "Choose a CSV file."),
            ("missing-column", CatalogSeed.Utf8(CatalogSeed.CsvOf("type,name,description,unit_cost,unit_price,taxable", "service,A,,1,1,true")), "The file doesn't match the template."),
            ("extra-column", CatalogSeed.Utf8(CatalogSeed.CsvOf(CatalogSeed.CsvHeader + ",sku", "service,A,,1,1,true,true,x")), "The file doesn't match the template."),
            ("unknown-column", CatalogSeed.Utf8(CatalogSeed.CsvOf("type,name,description,unit_cost,unit_price,taxable,enabled", "service,A,,1,1,true,true")), "The file doesn't match the template."),
            ("no-rows", CatalogSeed.Utf8(CatalogSeed.CsvOf(CatalogSeed.CsvHeader)), "The file has no items."),
            ("too-many", CatalogSeed.Utf8(Rows(501, i => $"service,Bulk {i},,1,1,true,true")), "Import up to 500 items at a time."),
        };

        foreach (var (name, bytes, message) in fileErrors)
        {
            var response = await host.SendFileAsync(HttpMethod.Post, "/catalog-items/import", cookie, bytes, "text/csv");
            Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{name}: {response.StatusCode}");
            Assert.Equal(message, CatalogSeed.Error(await CatalogHost.ReadAsync(response), "file"));
        }

        var rowCsv = CatalogSeed.CsvOf(
            CatalogSeed.CsvHeader,
            "gadget,Bad type,,1,1,true,true",
            "service,Bad cost,,-1,1,true,true",
            "service,Bad price,,1,1.234,true,true",
            "service,Bad flag,,1,1,maybe,true",
            "service,,,1,1,true,true",
            $"service,{new string('n', 161)},,1,1,true,true",
            "service,Good one,,1,1,true,true",
            "SERVICE,  good   ONE ,,2,2,true,true",
            "service,existing item,,1,1,true,true",
            "product,Existing Item,,1,1,true,true");

        var rowResponse = await host.SendFileAsync(HttpMethod.Post, "/catalog-items/import", cookie, CatalogSeed.Utf8(rowCsv), "text/csv");
        Assert.Equal(HttpStatusCode.BadRequest, rowResponse.StatusCode);
        var rowErrors = (await CatalogHost.ReadAsync(rowResponse))["rowErrors"]!.AsArray();
        Assert.Equal(
            [(2, "type"), (3, "unit_cost"), (4, "unit_price"), (5, "taxable"), (6, "name"), (7, "name"), (9, "name"), (10, "name")],
            rowErrors.Select(e => (e!["row"]!.GetValue<int>(), e["column"]!.GetValue<string>())));
        Assert.All(rowErrors, e => Assert.False(string.IsNullOrWhiteSpace(e!["message"]!.GetValue<string>())));

        // Only the last of many valid rows is invalid: still nothing is created. More than 100 errors are capped.
        var lastInvalid = Rows(6, i => i == 6 ? "service,Last,,oops,1,true,true" : $"service,Fine {i},,1,1,true,true");
        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendFileAsync(HttpMethod.Post, "/catalog-items/import", cookie, CatalogSeed.Utf8(lastInvalid), "text/csv")).StatusCode);

        var many = await host.SendFileAsync(HttpMethod.Post, "/catalog-items/import", cookie, CatalogSeed.Utf8(Rows(150, i => $"gadget,Row {i},,1,1,true,true")), "text/csv");
        var capped = (await CatalogHost.ReadAsync(many))["rowErrors"]!.AsArray();
        Assert.Equal(100, capped.Count);
        Assert.Equal(Enumerable.Range(2, 100), capped.Select(e => e!["row"]!.GetValue<int>()));

        var notMultipart = await host.SendAsync(HttpMethod.Post, "/catalog-items/import", cookie, new System.Text.Json.Nodes.JsonObject());
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, notMultipart.StatusCode);

        Assert.Equal(1, await database.CountItemsAsync(org));
        Assert.Equal(auditBefore, await database.CountCatalogAuditAsync(org));

        // A duplicate that only the unique index can see: 409, nothing created, no audit, no name leaked.
        await using var blind = CatalogHost.Create(database, skipNameChecks: true);
        var blindCookie = await blind.CookieAsync(database, org, Owner);
        var conflictCsv = CatalogSeed.CsvOf(CatalogSeed.CsvHeader, "service,Brand new,,1,1,true,true", "service,EXISTING item,,1,1,true,true");
        var conflict = await blind.SendFileAsync(HttpMethod.Post, "/catalog-items/import", blindCookie, CatalogSeed.Utf8(conflictCsv), "text/csv");
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        var conflictBody = await conflict.Content.ReadAsStringAsync();
        Assert.Contains("Some items already exist. Review the file and try again.", conflictBody, StringComparison.Ordinal);
        Assert.DoesNotContain("EXISTING", conflictBody, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, await database.CountItemsAsync(org));
        Assert.Equal(auditBefore, await database.CountCatalogAuditAsync(org));
    }

    // AC-16 (413): an import body over the 2 MB envelope is rejected by the real server.
    [Fact]
    public async Task Import_BodyOverEnvelope_Returns413()
    {
        var owner = await database.SeedAccountAsync(Owner);

        await using var factory = FieldOps.IntegrationTests.Api.FieldOpsApiFactory.Create(connectionString: database.ConnectionString);
        factory.UseKestrel(0);
        factory.StartServer();
        factory.ClientOptions.HandleCookies = false;
        factory.ClientOptions.AllowAutoRedirect = false;
        using var client = factory.CreateClient();

        var cookie = await CompanySettingsApi.SignInCookieAsync(client, owner.Email);
        var raw = await CatalogImageEndpointsTests.SendHeadersOnlyAsync(
            client.BaseAddress!, "/catalog-items/import", cookie, (2 * 1024 * 1024) + 1);

        Assert.StartsWith("HTTP/1.1 413", raw, StringComparison.Ordinal);
        Assert.Equal(0, await database.CountItemsAsync(owner.OrganizationId));
    }
}
