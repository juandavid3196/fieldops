using System.Net;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.Users;

namespace FieldOps.IntegrationTests.Catalog;

/// <summary>POST/PUT /catalog-items and activate/deactivate (FR-05, FR-07, FR-09, FR-15): AC-05, AC-06, AC-07, AC-09.</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class CatalogWriteEndpointsTests(CompanySettingsDatabaseFixture database)
{
    private const short OperationsManager = CompanySettingsDatabaseFixture.OperationsManagerRoleId;

    // AC-05: normalization, defaults, session organization and the created audit row.
    [Fact]
    public async Task Create_ValidBody_PersistsNormalizedItemWithDefaultsAndOneAuditRow()
    {
        var org = await database.SeedOrganizationAsync();
        await using var host = CatalogHost.Create(database);
        var cookie = await host.CookieAsync(database, org, OperationsManager);

        var body = CatalogSeed.ItemBody(name: "  Drain   Cleaning ", cost: 20m, price: 100.5m);
        body["description"] = "   ";
        body["isTaxable"] = false;
        body["organizationId"] = Guid.NewGuid().ToString();

        var response = await host.SendAsync(HttpMethod.Post, "/catalog-items", cookie, body);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var detail = await CatalogHost.ReadAsync(response);
        var id = detail["id"]!.GetValue<Guid>();
        Assert.Equal($"/catalog-items/{id}", response.Headers.Location?.ToString());
        Assert.Equal("Drain Cleaning", detail["name"]!.GetValue<string>());
        Assert.Null(detail["description"]);
        Assert.Equal(80.1m, detail["estimatedMarginPercent"]!.GetValue<decimal>());
        Assert.False(detail["isTaxable"]!.GetValue<bool>());
        Assert.True(detail["isActive"]!.GetValue<bool>());

        Assert.Equal(
            "service|Drain Cleaning|drain cleaning|unit|0.0000|false|true",
            await database.TextAsync(
                "SELECT concat_ws('|', type::text, name, normalized_name, unit, tax_rate::text, is_taxable::text, is_active::text) FROM catalog_items WHERE id = @i",
                ("i", id)));
        Assert.Equal(0, await database.CountAsync("SELECT COUNT(*) FROM catalog_items WHERE id = @i AND (sku IS NOT NULL OR category_id IS NOT NULL OR organization_id <> @o)", ("i", id), ("o", org)));
        Assert.Equal(1, await database.CountCatalogAuditAsync(org, "catalog_item.created"));
        var (_, after, _) = await database.GetLatestAuditAsync(org, "catalog_item.created");
        Assert.Contains("\"name\": \"Drain Cleaning\"", after, StringComparison.Ordinal);

        // isTaxable / isActive default to true when omitted.
        var minimal = CatalogSeed.ItemBody(name: "Minimal");
        minimal.Remove("isTaxable");
        minimal.Remove("isActive");
        var created = await CatalogHost.ReadAsync(await host.SendAsync(HttpMethod.Post, "/catalog-items", cookie, minimal));
        Assert.True(created["isTaxable"]!.GetValue<bool>());
        Assert.True(created["isActive"]!.GetValue<bool>());
    }

    // AC-06: one rule per body, duplicates (case/spacing, other type, other organization), concurrency and the unique index.
    [Fact]
    public async Task CreateAndUpdate_InvalidBodiesAndDuplicates_ReturnFieldErrorsAndChangeNothing()
    {
        var org = await database.SeedOrganizationAsync();
        var other = await database.SeedOrganizationAsync();
        var existing = await database.SeedCatalogItemAsync(org, "service", "Drain Cleaning");
        var second = await database.SeedCatalogItemAsync(org, "service", "Second item");

        await using var host = CatalogHost.Create(database);
        var cookie = await host.CookieAsync(database, org, OperationsManager);
        var otherCookie = await host.CookieAsync(database, other, OperationsManager);

        var invalid = new (string Key, JsonNode? Value, string Message)[]
        {
            ("type", null, "Select a type."),
            ("type", "gadget", "Select a type."),
            ("name", "   ", "Enter a name."),
            ("name", new string('n', 161), "Name must be 160 characters or fewer."),
            ("name", 5, "Enter a name."),
            ("description", new string('d', 1001), "Description must be 1,000 characters or fewer."),
            ("unitCost", null, "Enter a unit cost of 0 or more."),
            ("unitCost", -1, "Enter a unit cost of 0 or more."),
            ("unitCost", 1.234m, "Enter a unit cost of 0 or more."),
            ("unitCost", "abc", "Enter a unit cost of 0 or more."),
            ("unitCost", 1_000_000_000_000m, "Enter a unit cost of 0 or more."),
            ("unitPrice", -0.01m, "Enter a unit price of 0 or more."),
            ("unitPrice", null, "Enter a unit price of 0 or more."),
            ("isTaxable", "yes", "Enter true or false."),
        };

        var itemsBefore = await database.CountItemsAsync(org);

        foreach (var (key, value, message) in invalid)
        {
            var body = CatalogSeed.ItemBody(name: "Valid name");
            body[key] = value;

            foreach (var (method, path) in new[] { (HttpMethod.Post, "/catalog-items"), (HttpMethod.Put, $"/catalog-items/{existing}") })
            {
                var response = await host.SendAsync(method, path, cookie, body);
                Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{method} {key}: {response.StatusCode}");
                Assert.Equal(message, CatalogSeed.Error(await CatalogHost.ReadAsync(response), key));
            }
        }

        Assert.Equal(itemsBefore, await database.CountItemsAsync(org));
        Assert.Equal("Drain Cleaning", await database.TextAsync("SELECT name FROM catalog_items WHERE id = @i", ("i", existing)));
        Assert.Equal(0, await database.CountCatalogAuditAsync(org));

        // Duplicates: same type differing only by case/spacing is 409 on create and update.
        const string duplicateMessage = "An item with this name already exists for this type.";
        var create = await host.SendAsync(HttpMethod.Post, "/catalog-items", cookie, CatalogSeed.ItemBody(name: "  drain   CLEANING "));
        Assert.Equal(HttpStatusCode.Conflict, create.StatusCode);
        var conflict = await CatalogHost.ReadAsync(create);
        Assert.Equal(duplicateMessage, CatalogSeed.Error(conflict, "name"));
        Assert.DoesNotContain("Drain Cleaning", conflict.ToJsonString(), StringComparison.Ordinal);

        var rename = await host.SendAsync(HttpMethod.Put, $"/catalog-items/{second}", cookie, CatalogSeed.ItemBody(name: "DRAIN cleaning"));
        Assert.Equal(HttpStatusCode.Conflict, rename.StatusCode);
        Assert.Equal(duplicateMessage, CatalogSeed.Error(await CatalogHost.ReadAsync(rename), "name"));
        Assert.Equal(itemsBefore, await database.CountItemsAsync(org));
        Assert.Equal(0, await database.CountCatalogAuditAsync(org));

        // The same name is allowed for the other type, in another organization and for the item itself.
        Assert.Equal(
            HttpStatusCode.Created,
            (await host.SendAsync(HttpMethod.Post, "/catalog-items", cookie, CatalogSeed.ItemBody(type: "product", name: "Drain Cleaning"))).StatusCode);
        Assert.Equal(
            HttpStatusCode.Created,
            (await host.SendAsync(HttpMethod.Post, "/catalog-items", otherCookie, CatalogSeed.ItemBody(name: "Drain Cleaning"))).StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await host.SendAsync(HttpMethod.Put, $"/catalog-items/{existing}", cookie, CatalogSeed.ItemBody(name: "DRAIN CLEANING"))).StatusCode);

        // Real concurrency: one winner, everyone else 409, never a 500.
        var racers = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            host.SendAsync(HttpMethod.Post, "/catalog-items", cookie, CatalogSeed.ItemBody(name: "Raced item"))));
        Assert.Equal(1, racers.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(7, racers.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(1, await database.CountAsync("SELECT COUNT(*) FROM catalog_items WHERE organization_id = @o AND name = 'Raced item'", ("o", org)));
        Assert.Equal(1, await database.CountAsync("SELECT COUNT(*) FROM audit_logs WHERE organization_id = @o AND action = 'catalog_item.created' AND after_data->>'name' = 'Raced item'", ("o", org)));

        // With the application pre-check disabled the unique index alone yields the 409 and rolls back.
        await using var blind = CatalogHost.Create(database, skipNameChecks: true);
        var blindCookie = await blind.CookieAsync(database, org, OperationsManager);
        var auditBefore = await database.CountCatalogAuditAsync(org);
        var itemsNow = await database.CountItemsAsync(org);

        var viaIndexCreate = await blind.SendAsync(HttpMethod.Post, "/catalog-items", blindCookie, CatalogSeed.ItemBody(name: "raced ITEM"));
        Assert.Equal(HttpStatusCode.Conflict, viaIndexCreate.StatusCode);
        Assert.Equal(duplicateMessage, CatalogSeed.Error(await CatalogHost.ReadAsync(viaIndexCreate), "name"));

        var viaIndexUpdate = await blind.SendAsync(HttpMethod.Put, $"/catalog-items/{second}", blindCookie, CatalogSeed.ItemBody(name: "Raced Item"));
        Assert.Equal(HttpStatusCode.Conflict, viaIndexUpdate.StatusCode);
        Assert.Equal("Second item", await database.TextAsync("SELECT name FROM catalog_items WHERE id = @i", ("i", second)));
        Assert.Equal(itemsNow, await database.CountItemsAsync(org));
        Assert.Equal(auditBefore, await database.CountCatalogAuditAsync(org));
    }

    // AC-07: persisted values, advanced updated_at, audit keys and the no-op.
    [Fact]
    public async Task Update_ChangedFieldsTypeActiveOnlyAndIdenticalBody_FollowAuditRules()
    {
        var org = await database.SeedOrganizationAsync();
        var id = await database.SeedCatalogItemAsync(org, "service", "Hose", price: 10m, cost: 5m);
        await using var host = CatalogHost.Create(database);
        var cookie = await host.CookieAsync(database, org, OperationsManager);
        var path = $"/catalog-items/{id}";

        // Changed fields.
        var t0 = await database.ItemUpdatedAtAsync(id);
        var body = CatalogSeed.ItemBody(name: "Hose Pro", cost: 6m, price: 12m);
        body["isTaxable"] = false;
        var changed = await host.SendAsync(HttpMethod.Put, path, cookie, body);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal("Hose Pro", (await CatalogHost.ReadAsync(changed))["name"]!.GetValue<string>());
        var t1 = await database.ItemUpdatedAtAsync(id);
        Assert.True(t1 > t0);
        Assert.Equal(1, await database.CountAuditAsync(org, "catalog_item.updated"));
        var (before, after, _) = await database.GetLatestAuditAsync(org, "catalog_item.updated");
        Assert.Contains("\"name\": \"Hose\"", before, StringComparison.Ordinal);
        Assert.Contains("\"name\": \"Hose Pro\"", after, StringComparison.Ordinal);
        Assert.Contains("\"isTaxable\": false", after, StringComparison.Ordinal);
        Assert.DoesNotContain("isActive", after, StringComparison.Ordinal);
        Assert.DoesNotContain("type", after, StringComparison.Ordinal);

        // Type change.
        body["type"] = "product";
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(HttpMethod.Put, path, cookie, body)).StatusCode);
        Assert.Equal("product", await database.TextAsync("SELECT type::text FROM catalog_items WHERE id = @i", ("i", id)));
        Assert.Equal(2, await database.CountAuditAsync(org, "catalog_item.updated"));

        // Only isActive changed: activated/deactivated audit, not updated.
        body["isActive"] = false;
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(HttpMethod.Put, path, cookie, body)).StatusCode);
        Assert.Equal(1, await database.CountAuditAsync(org, "catalog_item.deactivated"));
        body["isActive"] = true;
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(HttpMethod.Put, path, cookie, body)).StatusCode);
        Assert.Equal(1, await database.CountAuditAsync(org, "catalog_item.activated"));
        Assert.Equal(2, await database.CountAuditAsync(org, "catalog_item.updated"));

        // isActive plus another field: one updated row that includes isActive, no state row.
        body["isActive"] = false;
        body["unitPrice"] = 15m;
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(HttpMethod.Put, path, cookie, body)).StatusCode);
        Assert.Equal(3, await database.CountAuditAsync(org, "catalog_item.updated"));
        Assert.Equal(1, await database.CountAuditAsync(org, "catalog_item.deactivated"));
        var (_, combinedAfter, _) = await database.GetLatestAuditAsync(org, "catalog_item.updated");
        Assert.Contains("\"isActive\": false", combinedAfter, StringComparison.Ordinal);

        // Identical body: 200, nothing advances, no audit row.
        var stamp = await database.ItemUpdatedAtAsync(id);
        var auditCount = await database.CountCatalogAuditAsync(org);
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(HttpMethod.Put, path, cookie, body)).StatusCode);
        Assert.Equal(stamp, await database.ItemUpdatedAtAsync(id));
        Assert.Equal(auditCount, await database.CountCatalogAuditAsync(org));
    }

    // AC-09: deactivate/activate preserve history, repeat is a no-op without audit, unknown is 404.
    [Fact]
    public async Task ActivateAndDeactivate_PreserveReferencesImageAndUsageAndAuditOnlyRealTransitions()
    {
        var org = await database.SeedOrganizationAsync();
        var owner = await database.SeedMemberAsync(org, CompanySettingsDatabaseFixture.OwnerRoleId, "Ola", "Owner");
        var branch = await database.SeedBranchAsync(org);
        var id = await database.SeedCatalogItemAsync(org, "service", "Referenced");
        await database.SeedUsageChainAsync(org, owner.UserId, branch.Id, id);
        await database.ExecuteAsync(
            "INSERT INTO catalog_item_images (catalog_item_id, organization_id, content_type, content, size_bytes) VALUES (@i, @o, 'image/png', @c, 8)",
            ("i", id), ("o", org), ("c", CatalogSeed.Png(8)));

        await using var host = CatalogHost.Create(database);
        var cookie = await host.SignInAsync(owner.Email);
        var history = await database.CountAsync(
            "SELECT (SELECT COUNT(*) FROM quote_lines WHERE catalog_item_id = @i) + (SELECT COUNT(*) FROM visit_materials WHERE catalog_item_id = @i) + (SELECT COUNT(*) FROM invoice_lines)",
            ("i", id));

        Assert.Equal(HttpStatusCode.NoContent, (await host.SendAsync(HttpMethod.Post, $"/catalog-items/{id}/deactivate", cookie)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await host.SendAsync(HttpMethod.Post, $"/catalog-items/{id}/deactivate", cookie)).StatusCode);
        Assert.Equal(1, await database.CountAuditAsync(org, "catalog_item.deactivated"));
        Assert.Equal("false", await database.TextAsync("SELECT is_active::text FROM catalog_items WHERE id = @i", ("i", id)));

        var detail = await CatalogHost.ReadAsync(await host.SendAsync(HttpMethod.Get, $"/catalog-items/{id}", cookie));
        Assert.False(detail["isActive"]!.GetValue<bool>());
        Assert.True(detail["hasImage"]!.GetValue<bool>());
        Assert.Equal(1, detail["usage"]!["quotes"]!.GetValue<int>());
        Assert.Equal(1, detail["usage"]!["jobs"]!.GetValue<int>());
        Assert.Equal(1, detail["usage"]!["invoices"]!.GetValue<int>());
        Assert.Equal(
            ["Referenced"],
            CatalogSeed.Names(await CatalogHost.ReadAsync(await host.SendAsync(HttpMethod.Get, "/catalog-items?status=inactive", cookie))));
        Assert.Equal(1, (await CatalogHost.ReadAsync(await host.SendAsync(HttpMethod.Get, "/catalog-items/summary", cookie)))["inactiveItems"]!.GetValue<int>());

        Assert.Equal(HttpStatusCode.NoContent, (await host.SendAsync(HttpMethod.Post, $"/catalog-items/{id}/activate", cookie)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await host.SendAsync(HttpMethod.Post, $"/catalog-items/{id}/activate", cookie)).StatusCode);
        Assert.Equal(1, await database.CountAuditAsync(org, "catalog_item.activated"));
        Assert.Equal("true", await database.TextAsync("SELECT is_active::text FROM catalog_items WHERE id = @i", ("i", id)));

        Assert.Equal(
            history,
            await database.CountAsync(
                "SELECT (SELECT COUNT(*) FROM quote_lines WHERE catalog_item_id = @i) + (SELECT COUNT(*) FROM visit_materials WHERE catalog_item_id = @i) + (SELECT COUNT(*) FROM invoice_lines)",
                ("i", id)));
        Assert.Equal(1, await database.CountAsync("SELECT COUNT(*) FROM catalog_item_images WHERE catalog_item_id = @i", ("i", id)));

        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Post, $"/catalog-items/{Guid.NewGuid()}/deactivate", cookie)).StatusCode);
    }
}
