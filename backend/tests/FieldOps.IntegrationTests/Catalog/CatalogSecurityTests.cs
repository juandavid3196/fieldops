using System.Net;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.Users;
using Npgsql;

namespace FieldOps.IntegrationTests.Catalog;

/// <summary>Role matrix, tenant isolation and migration shape (FR-16, FR-20): AC-18, AC-19, AC-25.</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class CatalogSecurityTests(CompanySettingsDatabaseFixture database)
{
    private enum Access
    {
        View,
        Manage,
        Import,
    }

    // Path templates use {id}; the success status is the one expected from an allowed caller.
    private static readonly (string Method, string Path, Access Access, HttpStatusCode Success)[] Endpoints =
    [
        ("GET", "/catalog-items", Access.View, HttpStatusCode.OK),
        ("GET", "/catalog-items/summary", Access.View, HttpStatusCode.OK),
        ("GET", "/catalog-items/export", Access.View, HttpStatusCode.OK),
        ("GET", "/catalog-items/{id}", Access.View, HttpStatusCode.OK),
        ("GET", "/catalog-items/{id}/image", Access.View, HttpStatusCode.NotFound),
        ("POST", "/catalog-items", Access.Manage, HttpStatusCode.Created),
        ("PUT", "/catalog-items/{id}", Access.Manage, HttpStatusCode.OK),
        ("POST", "/catalog-items/{id}/activate", Access.Manage, HttpStatusCode.NoContent),
        ("POST", "/catalog-items/{id}/deactivate", Access.Manage, HttpStatusCode.NoContent),
        ("PUT", "/catalog-items/{id}/image", Access.Manage, HttpStatusCode.OK),
        ("DELETE", "/catalog-items/{id}/image", Access.Manage, HttpStatusCode.NoContent),
        ("GET", "/catalog-items/import-template", Access.Import, HttpStatusCode.OK),
        ("POST", "/catalog-items/import", Access.Import, HttpStatusCode.OK),
    ];

    // AC-18: every endpoint for every role and for no session.
    [Theory]
    [InlineData("owner")]
    [InlineData("operations_manager")]
    [InlineData("dispatcher")]
    [InlineData("accounting")]
    [InlineData("viewer")]
    [InlineData("technician")]
    [InlineData("none")]
    public async Task Endpoints_RoleMatrix_FollowCatalogPoliciesAndChangeNothingWhenDenied(string role)
    {
        var org = await database.SeedOrganizationAsync();
        var id = await database.SeedCatalogItemAsync(org, "service", "Matrix item");
        await using var host = CatalogHost.Create(database);
        string? cookie = null;

        if (role != "none")
        {
            var roleId = role switch
            {
                "owner" => CompanySettingsDatabaseFixture.OwnerRoleId,
                "operations_manager" => CompanySettingsDatabaseFixture.OperationsManagerRoleId,
                "dispatcher" => CompanySettingsDatabaseFixture.DispatcherRoleId,
                "accounting" => CompanySettingsDatabaseFixture.AccountingRoleId,
                "viewer" => CompanySettingsDatabaseFixture.ViewerRoleId,
                _ => CompanySettingsDatabaseFixture.TechnicianRoleId,
            };
            cookie = await host.CookieAsync(database, org, roleId);
        }

        var items = await database.CountItemsAsync(org);
        var audit = await database.CountCatalogAuditAsync(org);
        var stamp = await database.ItemUpdatedAtAsync(id);
        var csv = CatalogSeed.Utf8(CatalogSeed.CsvOf(CatalogSeed.CsvHeader, "service,Imported by matrix,,1,1,true,true"));
        var allowedWrites = 0;

        foreach (var (method, template, access, success) in Endpoints)
        {
            var path = template.Replace("{id}", id.ToString(), StringComparison.Ordinal);
            var httpMethod = new HttpMethod(method);

            var response = template switch
            {
                "/catalog-items/{id}/image" when method == "PUT" =>
                    await host.SendFileAsync(httpMethod, path, cookie, CatalogSeed.Png(), "image/png"),
                "/catalog-items/import" =>
                    await host.SendFileAsync(httpMethod, path, cookie, csv, "text/csv"),
                "/catalog-items" when method == "POST" =>
                    await host.SendAsync(httpMethod, path, cookie, CatalogSeed.ItemBody(name: $"Created by {role}")),
                "/catalog-items/{id}" when method == "PUT" =>
                    await host.SendAsync(httpMethod, path, cookie, CatalogSeed.ItemBody(name: "Matrix item")),
                _ => await host.SendAsync(httpMethod, path, cookie),
            };

            var allowed = access switch
            {
                Access.View => role is "owner" or "operations_manager" or "dispatcher" or "accounting" or "viewer",
                Access.Manage => role is "owner" or "operations_manager",
                _ => role == "owner",
            };

            var expected = role == "none"
                ? HttpStatusCode.Unauthorized
                : allowed ? success : HttpStatusCode.Forbidden;

            Assert.True(expected == response.StatusCode, $"{role} {method} {template}: expected {expected} got {response.StatusCode}");

            if (allowed && access != Access.View)
            {
                allowedWrites++;
            }
        }

        if (allowedWrites == 0)
        {
            Assert.Equal(items, await database.CountItemsAsync(org));
            Assert.Equal(audit, await database.CountCatalogAuditAsync(org));
            Assert.Equal(stamp, await database.ItemUpdatedAtAsync(id));
            Assert.Equal(0, await database.CountAsync("SELECT COUNT(*) FROM catalog_item_images WHERE organization_id = @o", ("o", org)));
        }
    }

    // AC-19: A never sees B's rows, counts, usage or image and can change nothing of B; uniqueness is per organization.
    [Fact]
    public async Task Endpoints_OtherOrganization_Return404AndNeverLeakOrChange()
    {
        var orgA = await database.SeedOrganizationAsync();
        var orgB = await database.SeedOrganizationAsync();
        var ownerB = await database.SeedMemberAsync(orgB, CompanySettingsDatabaseFixture.OwnerRoleId, "Bo", "Owner");
        var branchB = await database.SeedBranchAsync(orgB);
        var itemA = await database.SeedCatalogItemAsync(orgA, "service", "Alpha only");
        var itemB = await database.SeedCatalogItemAsync(orgB, "service", "Bravo secret", price: 77m);
        await database.SeedCatalogItemAsync(orgB, "product", "Bravo only name");

        // B's records referencing A's item must not count as A's usage.
        await database.SeedUsageChainAsync(orgB, ownerB.UserId, branchB.Id, itemA);
        await database.ExecuteAsync(
            "INSERT INTO catalog_item_images (catalog_item_id, organization_id, content_type, content, size_bytes) VALUES (@i, @o, 'image/png', @c, 8)",
            ("i", itemB), ("o", orgB), ("c", CatalogSeed.Png(8)));

        await using var host = CatalogHost.Create(database);
        var manager = await host.CookieAsync(database, orgA, CompanySettingsDatabaseFixture.OperationsManagerRoleId);
        var owner = await host.CookieAsync(database, orgA, CompanySettingsDatabaseFixture.OwnerRoleId);

        Assert.Equal(["Alpha only"], CatalogSeed.Names(await CatalogHost.ReadAsync(await host.SendAsync(HttpMethod.Get, "/catalog-items", manager))));
        var summary = await CatalogHost.ReadAsync(await host.SendAsync(HttpMethod.Get, "/catalog-items/summary", manager));
        Assert.Equal(1, summary["allItems"]!.GetValue<int>());
        Assert.Equal(0, summary["products"]!.GetValue<int>());
        var export = await (await host.SendAsync(HttpMethod.Get, "/catalog-items/export", manager)).Content.ReadAsStringAsync();
        Assert.DoesNotContain("Bravo", export, StringComparison.Ordinal);

        var usage = (await CatalogHost.ReadAsync(await host.SendAsync(HttpMethod.Get, $"/catalog-items/{itemA}", manager)))["usage"]!;
        Assert.Equal(0, usage["quotes"]!.GetValue<int>() + usage["jobs"]!.GetValue<int>() + usage["invoices"]!.GetValue<int>());

        var stamp = await database.ItemUpdatedAtAsync(itemB);
        var auditB = await database.CountCatalogAuditAsync(orgB);

        var calls = new (HttpMethod Method, string Path)[]
        {
            (HttpMethod.Get, $"/catalog-items/{itemB}"),
            (HttpMethod.Put, $"/catalog-items/{itemB}"),
            (HttpMethod.Post, $"/catalog-items/{itemB}/activate"),
            (HttpMethod.Post, $"/catalog-items/{itemB}/deactivate"),
            (HttpMethod.Get, $"/catalog-items/{itemB}/image"),
            (HttpMethod.Put, $"/catalog-items/{itemB}/image"),
            (HttpMethod.Delete, $"/catalog-items/{itemB}/image"),
        };

        foreach (var (method, path) in calls)
        {
            var response = path.EndsWith("/image", StringComparison.Ordinal) && method == HttpMethod.Put
                ? await host.SendFileAsync(method, path, manager, CatalogSeed.Png(), "image/png")
                : await host.SendAsync(method, path, manager, method == HttpMethod.Put ? CatalogSeed.ItemBody(name: "Hijacked") : null);
            Assert.True(response.StatusCode == HttpStatusCode.NotFound, $"{method} {path}: {response.StatusCode}");
        }

        Assert.Equal("Bravo secret|true|77.00", await database.TextAsync("SELECT concat_ws('|', name, is_active::text, unit_price::text) FROM catalog_items WHERE id = @i", ("i", itemB)));
        Assert.Equal(stamp, await database.ItemUpdatedAtAsync(itemB));
        Assert.Equal(1, await database.CountAsync("SELECT COUNT(*) FROM catalog_item_images WHERE catalog_item_id = @i AND size_bytes = 8", ("i", itemB)));
        Assert.Equal(auditB, await database.CountCatalogAuditAsync(orgB));

        // Uniqueness is per organization: A may create and import names that exist only in B.
        Assert.Equal(
            HttpStatusCode.Created,
            (await host.SendAsync(HttpMethod.Post, "/catalog-items", manager, CatalogSeed.ItemBody(name: "Bravo secret"))).StatusCode);
        var csv = CatalogSeed.Utf8(CatalogSeed.CsvOf(CatalogSeed.CsvHeader, "product,Bravo only name,,1,1,true,true"));
        var imported = await host.SendFileAsync(HttpMethod.Post, "/catalog-items/import", owner, csv, "text/csv");
        Assert.Equal(HttpStatusCode.OK, imported.StatusCode);
        Assert.Equal(1, await database.CountAsync("SELECT COUNT(*) FROM catalog_items WHERE organization_id = @o AND name = 'Bravo only name'", ("o", orgA)));
        Assert.Equal(1, await database.CountAsync("SELECT COUNT(*) FROM catalog_items WHERE organization_id = @o AND name = 'Bravo only name'", ("o", orgB)));
    }

    // AC-25: the migration applied in the disposable container creates the amended schema.
    [Fact]
    public async Task Migration_AppliedToContainer_CreatesAmendedSchemaAndEnforcesUniqueIndex()
    {
        Assert.Equal(
            "boolean|NO|true",
            await database.TextAsync(
                "SELECT concat_ws('|', data_type, is_nullable, column_default) FROM information_schema.columns WHERE table_name = 'catalog_items' AND column_name = 'is_taxable'"));
        Assert.Equal(
            "ALWAYS|lower((name)::text)|character varying|160",
            await database.TextAsync(
                "SELECT concat_ws('|', is_generated, generation_expression, data_type, character_maximum_length::text) FROM information_schema.columns WHERE table_name = 'catalog_items' AND column_name = 'normalized_name'"));
        var index = await database.TextAsync("SELECT indexdef FROM pg_indexes WHERE indexname = 'ux_catalog_items_org_type_name'");
        Assert.Contains("UNIQUE INDEX", index, StringComparison.Ordinal);
        Assert.Contains("(organization_id, type, normalized_name)", index, StringComparison.Ordinal);

        Assert.Equal(
            "catalog_item_id|organization_id|content_type|content|size_bytes|created_at|updated_at",
            await database.TextAsync(
                "SELECT string_agg(column_name, '|' ORDER BY ordinal_position) FROM information_schema.columns WHERE table_name = 'catalog_item_images'"));
        Assert.Equal(
            "c",
            await database.TextAsync(
                "SELECT confdeltype::text FROM pg_constraint WHERE conrelid = 'catalog_item_images'::regclass AND contype = 'f' AND confrelid = 'catalog_items'::regclass"));
        Assert.Equal(
            2,
            await database.CountAsync("SELECT COUNT(*) FROM pg_constraint WHERE conrelid = 'catalog_item_images'::regclass AND contype = 'c'"));

        // A same-organization duplicate of type + lower(name) violates the index; the other type and organization do not.
        var org = await database.SeedOrganizationAsync();
        var other = await database.SeedOrganizationAsync();
        await database.SeedCatalogItemAsync(org, "service", "Unique Name");

        var violation = await Assert.ThrowsAsync<PostgresException>(
            () => database.SeedCatalogItemAsync(org, "service", "UNIQUE name"));
        Assert.Equal("ux_catalog_items_org_type_name", violation.ConstraintName);
        await database.SeedCatalogItemAsync(org, "product", "UNIQUE name");
        await database.SeedCatalogItemAsync(other, "service", "UNIQUE name");

        // Deleting an item cascades to its image.
        var id = await database.SeedCatalogItemAsync(org, "product", "Cascade item");
        await database.ExecuteAsync(
            "INSERT INTO catalog_item_images (catalog_item_id, organization_id, content_type, content, size_bytes) VALUES (@i, @o, 'image/png', @c, 8)",
            ("i", id), ("o", org), ("c", CatalogSeed.Png(8)));
        await database.ExecuteAsync("DELETE FROM catalog_items WHERE id = @i", ("i", id));
        Assert.Equal(0, await database.CountAsync("SELECT COUNT(*) FROM catalog_item_images WHERE catalog_item_id = @i", ("i", id)));
    }
}
