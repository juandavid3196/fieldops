using System.Net;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.Api;
using FieldOps.IntegrationTests.CompanySettings;

namespace FieldOps.IntegrationTests.Customers;

/// <summary>CSV template, preview and all-or-nothing import (FR-10): AC-22, AC-23.</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class CustomerImportTests(CompanySettingsDatabaseFixture database)
{
    // AC-22: the template header is exact; preview validates and warns without writing; confirm creates every row,
    // missing tags and one audit row without personal data.
    [Fact]
    public async Task TemplatePreviewAndConfirm_CreateAllRowsWithTagsAndOneAuditRow()
    {
        var org = await database.SeedOrganizationAsync();
        var branch = await database.SeedBranchAsync(org, isMain: true);
        await database.SeedCustomerAsync(org, branch.Id, "Existing Dup", email: "dup@example.com");
        await database.SeedTagAsync(org, "VIP");

        await using var host = CustomerHost.Create(database);
        var cookie = await host.CookieAsync(database, org, CompanySettingsDatabaseFixture.OwnerRoleId);

        var template = await host.SendAsync(HttpMethod.Get, "/customers/import/template", cookie);
        Assert.Equal(HttpStatusCode.OK, template.StatusCode);
        Assert.Equal("customers-template.csv", template.Content.Headers.ContentDisposition?.FileName);
        Assert.Equal(CustomerSeed.CsvHeader + "\r\n", await template.Content.ReadAsStringAsync());

        var csv = CustomerSeed.Utf8(CustomerSeed.CsvOf(
            CustomerSeed.CsvHeader.ToUpperInvariant(),
            $"residential,,Ann,One,,DUP@example.com,(512) 555-0001,yes,no,1 Main St,Austin,tx,78701,{branch.Code.ToLowerInvariant()},vip;Gold;gold,Gate code 1,Likes mail",
            $"commercial,Acme Co,Bob,Two,Boss,bob@example.com,,,,2 Main St,Austin,,,{branch.Code},Gold,,",
            $"residential,,Cat,Three,,bob@example.com,,true,false,3 Main St,Austin,TX,,{branch.Code},,,"));

        var customers = await database.CountCustomersAsync(org);
        var preview = await host.SendCsvAsync("/customers/import/preview", cookie, csv);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var summary = await CustomerHost.ReadAsync(preview);
        Assert.Equal(3, summary["validRowCount"]!.GetValue<int>());
        Assert.Empty(summary["rowErrors"]!.AsArray());
        var warnings = summary["duplicateWarnings"]!.AsArray();
        Assert.Equal(2, warnings.Count);
        Assert.Equal((2, "email", "Existing Dup"), (warnings[0]!["row"]!.GetValue<int>(), warnings[0]!["matchedField"]!.GetValue<string>(), warnings[0]!["existingDisplayName"]!.GetValue<string>()));
        Assert.Equal((4, "email", "Acme Co"), (warnings[1]!["row"]!.GetValue<int>(), warnings[1]!["matchedField"]!.GetValue<string>(), warnings[1]!["existingDisplayName"]!.GetValue<string>()));
        Assert.Equal(customers, await database.CountCustomersAsync(org));
        Assert.Equal(1L, await database.CountRowsAsync("customer_tags", org));

        var confirmed = await host.SendCsvAsync("/customers/import", cookie, csv);
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        Assert.Equal(3, (await CustomerHost.ReadAsync(confirmed))["importedCount"]!.GetValue<int>());
        Assert.Equal(customers + 3, await database.CountCustomersAsync(org));
        Assert.Equal(2L, await database.CountRowsAsync("customer_tags", org));
        Assert.Equal(3L, await database.CountRowsAsync("customer_tag_assignments", org));
        Assert.Equal(3L, await database.CountRowsAsync("properties", org) - 1);

        // FR-08: every imported customer's first property is its primary one.
        Assert.Equal(4L, await database.ScalarAsync<long>(
            "SELECT COUNT(*) FROM properties WHERE organization_id = @o AND is_primary", ("o", org)));
        Assert.Equal("5125550001|true|false|TX|Gate code 1", await database.ScalarAsync<string>(
            """
            SELECT k.phone || '|' || k.prefers_email::text || '|' || k.prefers_sms::text || '|' || p.state_region || '|' || p.service_notes
            FROM customer_contacts k JOIN properties p ON p.customer_id = k.customer_id
            WHERE k.organization_id = @o AND k.first_name = 'Ann'
            """,
            ("o", org)));

        Assert.Equal(1L, await database.CountCustomerAuditAsync(org, "customer.imported"));
        var (_, after, metadata) = await database.GetLatestAuditAsync(org, "customer.imported");
        Assert.Equal(3, JsonNode.Parse(metadata!)!["importedCount"]!.GetValue<int>());
        Assert.Null(after);
        Assert.Equal(0L, await database.CountCustomerAuditAsync(org, "customer.created"));
        Assert.DoesNotContain("bob@", metadata, StringComparison.Ordinal);
    }

    // AC-23: file errors, row errors (ordered, file line numbers), size limits and media type; confirm with errors creates nothing.
    [Fact]
    public async Task PreviewAndConfirm_InvalidFilesReturnErrorsAndWriteNothing()
    {
        var org = await database.SeedOrganizationAsync();
        var other = await database.SeedOrganizationAsync();
        var branch = await database.SeedBranchAsync(org, isMain: true);
        var inactive = await database.SeedBranchAsync(org, isActive: false);
        var foreignBranch = await database.SeedBranchAsync(other, isMain: true);

        await using var host = CustomerHost.Create(database);
        var cookie = await host.CookieAsync(database, org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var row = (string email, string code) =>
            $"residential,,Ann,One,,{email},,,,1 Main St,Austin,TX,78701,{code},,,";

        var fileCases = new (string Description, byte[] Bytes, string Message)[]
        {
            ("header", CustomerSeed.Utf8(CustomerSeed.CsvOf("type,name", "residential,Ann")), "The file doesn't match the template."),
            ("empty", CustomerSeed.Utf8(CustomerSeed.CsvOf(CustomerSeed.CsvHeader)), "The file has no customers."),
            ("utf8", [0xFF, 0xFE, 0xFA], "Choose a CSV file."),
            ("rows", CustomerSeed.Utf8(CustomerSeed.CsvOf([CustomerSeed.CsvHeader, .. Enumerable.Repeat(row("a@b.co", branch.Code), 501)])), "Import up to 500 customers at a time."),
            ("size", CustomerSeed.Utf8(CustomerSeed.CsvOf(CustomerSeed.CsvHeader, row("a@b.co", branch.Code)) + new string(' ', 1_048_577)), "Choose a CSV file of 1 MB or smaller."),
        };

        foreach (var path in new[] { "/customers/import/preview", "/customers/import" })
        {
            foreach (var (description, bytes, message) in fileCases)
            {
                var response = await host.SendCsvAsync(path, cookie, bytes);
                Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{path} {description}: {response.StatusCode}");
                Assert.Equal(message, CustomerSeed.Error(await CustomerHost.ReadAsync(response), "file"));
            }
        }

        var bad = CustomerSeed.Utf8(CustomerSeed.CsvOf(
            CustomerSeed.CsvHeader,
            row("good@example.com", branch.Code),
            row("nope", branch.Code),
            row("b@example.com", "NOPE"),
            row("c@example.com", inactive.Code),
            row("d@example.com", foreignBranch.Code),
            "residential,,Ann,One,,e@example.com,,maybe,,1 Main St,Austin,TX,78701," + branch.Code + ",,,",
            "residential,,Ann,One,,f@example.com,,,,1 Main St,Austin,ZZ,78701," + branch.Code + ",,,",
            "residential,only,three"));

        var preview = await CustomerHost.ReadAsync(await host.SendCsvAsync("/customers/import/preview", cookie, bad));
        Assert.Equal(1, preview["validRowCount"]!.GetValue<int>());
        var errors = preview["rowErrors"]!.AsArray().Select(error => (error!["row"]!.GetValue<int>(), error["column"]!.GetValue<string>())).ToArray();
        Assert.Equal(
            [(3, "email"), (4, "branch_code"), (5, "branch_code"), (6, "branch_code"), (7, "prefers_email"), (8, "state"), (9, "row")],
            errors);

        var confirm = await host.SendCsvAsync("/customers/import", cookie, bad);
        Assert.Equal(HttpStatusCode.BadRequest, confirm.StatusCode);
        Assert.Equal(7, (await CustomerHost.ReadAsync(confirm))["rowErrors"]!.AsArray().Count);

        // Only 100 errors are returned, in file order.
        var many = CustomerSeed.Utf8(CustomerSeed.CsvOf([CustomerSeed.CsvHeader, .. Enumerable.Repeat(row("nope", branch.Code), 150)]));
        var capped = (await CustomerHost.ReadAsync(await host.SendCsvAsync("/customers/import/preview", cookie, many)))["rowErrors"]!.AsArray();
        Assert.Equal(100, capped.Count);
        Assert.Equal(101, capped[99]!["row"]!.GetValue<int>());

        Assert.Equal(0L, await database.CountCustomersAsync(org));
        Assert.Equal(0L, await database.CountCustomerAuditAsync(org));

        // A non-multipart body is 415 and writes nothing.
        var json = await host.SendAsync(HttpMethod.Post, "/customers/import/preview", cookie, new JsonObject());
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, json.StatusCode);
        Assert.Equal(0L, await database.CountCustomersAsync(org));
    }

    // AC-23 (413): an import body over the 2 MB envelope is rejected by the real server before the form is read.
    [Fact]
    public async Task Import_BodyOverEnvelope_Returns413AndWritesNothing()
    {
        var owner = await database.SeedAccountAsync(CompanySettingsDatabaseFixture.OwnerRoleId);

        await using var factory = FieldOpsApiFactory.Create(connectionString: database.ConnectionString);
        factory.UseKestrel(0);
        factory.StartServer();
        factory.ClientOptions.HandleCookies = false;
        factory.ClientOptions.AllowAutoRedirect = false;
        using var client = factory.CreateClient();

        var cookie = await CompanySettingsApi.SignInCookieAsync(client, owner.Email);

        var raw = await Catalog.CatalogImageEndpointsTests.SendHeadersOnlyAsync(
            client.BaseAddress!, "/customers/import", cookie, (2 * 1024 * 1024) + 1);

        Assert.StartsWith("HTTP/1.1 413", raw, StringComparison.Ordinal);
        Assert.Equal(0L, await database.CountCustomersAsync(owner.OrganizationId));
    }
}
