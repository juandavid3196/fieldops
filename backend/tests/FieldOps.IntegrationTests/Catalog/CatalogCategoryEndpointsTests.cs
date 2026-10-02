using System.Net;
using System.Reflection;
using System.Text.Json.Nodes;
using FieldOps.Application.Features.Catalog;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.Sessions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using FieldOps.IntegrationTests.PublicRequests;
using FieldOps.IntegrationTests.Users;

namespace FieldOps.IntegrationTests.Catalog;

/// <summary>Disables the application-level duplicate check so a duplicate reaches the unique index.</summary>
public class SkipCategoryNameCheckProxy : DispatchProxy
{
    public ICatalogCategoryStore Inner { get; set; } = null!;

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod!.Name == nameof(ICatalogCategoryStore.NameExistsAsync))
        {
            return Task.FromResult(false);
        }

        try
        {
            return targetMethod.Invoke(Inner, args);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }
}

/// <summary>Catalog categories and item category assignment (FR-01..FR-07, FR-11, FR-12).</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class CatalogCategoryEndpointsTests(CompanySettingsDatabaseFixture database)
{
    private static readonly string TooLong = new('x', 121);

    private Task<long> CategoryAuditAsync(Guid org, string? action = null) =>
        database.CountAsync(
            "SELECT COUNT(*) FROM audit_logs WHERE organization_id = @o AND entity_type = 'catalog_category' AND (@a::text IS NULL OR action = @a)",
            ("o", org),
            ("a", action));

    private async Task<string> AuditAsync(Guid entity, string action, string column) =>
        await database.TextAsync(
            $"SELECT COALESCE({column}::text, 'null') FROM audit_logs WHERE entity_id = @i AND action = @a",
            ("i", entity),
            ("a", action));

    private async Task AssertAuditAsync(Guid entity, string action, string? before, string? after)
    {
        Assert.True(
            JsonNode.DeepEquals(JsonNode.Parse(before ?? "null"), JsonNode.Parse(await AuditAsync(entity, action, "before_data"))),
            "before_data");
        Assert.True(
            JsonNode.DeepEquals(JsonNode.Parse(after ?? "null"), JsonNode.Parse(await AuditAsync(entity, action, "after_data"))),
            "after_data");
    }

    private static JsonObject Name(string? name) => new() { ["name"] = name };

    private static async Task<JsonNode> CreateAsync(CatalogHost host, string cookie, string name)
    {
        var response = await host.SendAsync(HttpMethod.Post, "/catalog-categories", cookie, Name(name));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return await CatalogHost.ReadAsync(response);
    }

    [Fact]
    public async Task Create_TrimsValidatesAndRejectsCaseInsensitiveDuplicates()
    {
        var org = await database.SeedOrganizationAsync();
        var other = await database.SeedOrganizationAsync();
        await database.SeedCategoryAsync(other, "Shared name");
        var inactive = await database.SeedCategoryAsync(org, "Retired", active: false);
        await using var host = CatalogHost.Create(database);
        var cookie = await host.CookieAsync(database, org, CompanySettingsDatabaseFixture.OperationsManagerRoleId);

        var created = await host.SendAsync(HttpMethod.Post, "/catalog-categories", cookie, Name("  Plumbing  "));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("no-store", created.Headers.CacheControl?.ToString());
        var body = await CatalogHost.ReadAsync(created);
        Assert.Equal($"/catalog-categories/{body["id"]}", created.Headers.Location!.OriginalString);
        Assert.Equal("Plumbing", body["name"]!.GetValue<string>());
        Assert.True(body["isActive"]!.GetValue<bool>());
        Assert.Equal(0, body["itemCount"]!.GetValue<int>());
        Assert.Equal(0, body["activeServiceCount"]!.GetValue<int>());
        var createdId = Guid.Parse(body["id"]!.GetValue<string>());
        await AssertAuditAsync(createdId, "catalog_category.created", null, "{\"name\":\"Plumbing\",\"isActive\":true}");
        Assert.Equal(
            1,
            await database.CountAsync(
                "SELECT COUNT(*) FROM audit_logs WHERE entity_id = @i AND branch_id IS NULL AND actor_user_id IS NOT NULL",
                ("i", createdId)));

        foreach (var (name, message) in new[]
        {
            ("   ", "Enter a category name."),
            (TooLong, "Category name must be 120 characters or fewer."),
        })
        {
            var invalid = await host.SendAsync(HttpMethod.Post, "/catalog-categories", cookie, Name(name));
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.Equal(message, CatalogSeed.Error(await CatalogHost.ReadAsync(invalid), "name"));
        }

        foreach (var duplicate in new[] { "plumbing", "PLUMBING ", "retired" })
        {
            var conflict = await host.SendAsync(HttpMethod.Post, "/catalog-categories", cookie, Name(duplicate));
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
            Assert.Equal(
                "A category with this name already exists.",
                CatalogSeed.Error(await CatalogHost.ReadAsync(conflict), "name"));
        }

        // Another organization's name is free; only the one valid create audited.
        Assert.Equal(HttpStatusCode.Created, (await host.SendAsync(HttpMethod.Post, "/catalog-categories", cookie, Name("Shared name"))).StatusCode);
        Assert.Equal(2, await CategoryAuditAsync(org, "catalog_category.created"));
        Assert.NotEqual(Guid.Empty, inactive);

        // Exact-name race: with the application check bypassed the unique index maps to the same 409.
        await using var racingHost = SessionTestHost.Create(database.ConnectionString);
        var factory = racingHost.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            var implementation = services.Single(d => d.ServiceType == typeof(ICatalogCategoryStore)).ImplementationType!;
            services.AddScoped<ICatalogCategoryStore>(provider =>
            {
                var proxy = DispatchProxy.Create<ICatalogCategoryStore, SkipCategoryNameCheckProxy>();
                ((SkipCategoryNameCheckProxy)(object)proxy).Inner =
                    (ICatalogCategoryStore)ActivatorUtilities.CreateInstance(provider, implementation);

                return proxy;
            });
        }));
        using var racing = SessionApi.CreateClient(factory);
        var raceCookie = await CompanySettingsApi.SignInCookieAsync(
            racing, (await database.SeedMemberAsync(org, CompanySettingsDatabaseFixture.OwnerRoleId, "Ra", "Ce")).Email);
        var raced = await CompanySettingsApi.SendRawAsync(racing, HttpMethod.Post, "/catalog-categories", Name("Plumbing").ToJsonString(), raceCookie);
        Assert.Equal(HttpStatusCode.Conflict, raced.StatusCode);
        Assert.Equal("A category with this name already exists.", CatalogSeed.Error(await CatalogHost.ReadAsync(raced), "name"));
    }

    [Fact]
    public async Task Rename_AppliesRulesAndAuditsOnlyRealChanges()
    {
        var org = await database.SeedOrganizationAsync();
        var first = await database.SeedCategoryAsync(org, "Plumbing");
        var retired = await database.SeedCategoryAsync(org, "Retired", active: false);
        await database.SeedCategoryAsync(org, "Electrical");
        await using var host = CatalogHost.Create(database);
        var cookie = await host.CookieAsync(database, org, CompanySettingsDatabaseFixture.OwnerRoleId);

        // Same stored name: 200, no change, no audit.
        var same = await host.SendAsync(HttpMethod.Put, $"/catalog-categories/{first}", cookie, Name(" Plumbing "));
        Assert.Equal(HttpStatusCode.OK, same.StatusCode);
        Assert.Equal(0, await CategoryAuditAsync(org));

        // Case-only rename of its own name and renaming an inactive category are allowed.
        var recased = await host.SendAsync(HttpMethod.Put, $"/catalog-categories/{first}", cookie, Name("PLUMBING"));
        Assert.Equal(HttpStatusCode.OK, recased.StatusCode);
        Assert.Equal("PLUMBING", (await CatalogHost.ReadAsync(recased))["name"]!.GetValue<string>());
        await AssertAuditAsync(first, "catalog_category.renamed", "{\"name\":\"Plumbing\"}", "{\"name\":\"PLUMBING\"}");
        Assert.Equal(
            HttpStatusCode.OK,
            (await host.SendAsync(HttpMethod.Put, $"/catalog-categories/{retired}", cookie, Name("Retired 2"))).StatusCode);
        Assert.Equal(2, await CategoryAuditAsync(org, "catalog_category.renamed"));

        // Failures write nothing and change nothing.
        var duplicate = await host.SendAsync(HttpMethod.Put, $"/catalog-categories/{first}", cookie, Name("electrical"));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("A category with this name already exists.", CatalogSeed.Error(await CatalogHost.ReadAsync(duplicate), "name"));
        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(HttpMethod.Put, $"/catalog-categories/{first}", cookie, Name(""))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(HttpMethod.Put, $"/catalog-categories/{first}", cookie, Name(TooLong))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Put, $"/catalog-categories/{Guid.NewGuid()}", cookie, Name("Nope"))).StatusCode);
        Assert.Equal(2, await CategoryAuditAsync(org));
        Assert.Equal("PLUMBING", await database.TextAsync("SELECT name FROM service_categories WHERE id = @i", ("i", first)));
    }

    [Fact]
    public async Task Deactivate_IsIdempotentKeepsItemsAndRemovesCategoryFromPublicForm()
    {
        var org = await database.SeedPublicOrgAsync();
        await using var host = CatalogHost.Create(database);
        var cookie = await host.CookieAsync(database, org.Id, CompanySettingsDatabaseFixture.OperationsManagerRoleId);
        await using var publicHost = PublicRequestHost.Create(database);
        var items = await database.CountItemsAsync(org.Id);

        // The public form is unavailable (404) when no category is eligible.
        async Task<int> FormCategoriesAsync()
        {
            var response = await publicHost.GetFormAsync(org.Slug);

            return response.StatusCode == HttpStatusCode.NotFound
                ? 0
                : (await CatalogHost.ReadAsync(response))["categories"]!.AsArray().Count;
        }

        Assert.Equal(1, await FormCategoriesAsync());

        Assert.Equal(HttpStatusCode.NoContent, (await host.SendAsync(HttpMethod.Post, $"/catalog-categories/{org.CategoryId}/deactivate", cookie)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await host.SendAsync(HttpMethod.Post, $"/catalog-categories/{org.CategoryId}/deactivate", cookie)).StatusCode);
        Assert.Equal(1, await CategoryAuditAsync(org.Id, "catalog_category.deactivated"));
        await AssertAuditAsync(org.CategoryId, "catalog_category.deactivated", "{\"isActive\":true}", "{\"isActive\":false}");
        Assert.Equal(0, await FormCategoriesAsync());
        Assert.Equal(items, await database.CountItemsAsync(org.Id));
        Assert.Equal(
            1,
            await database.CountAsync("SELECT COUNT(*) FROM catalog_items WHERE id = @i AND is_active AND category_id = @c", ("i", org.ServiceId), ("c", org.CategoryId)));

        Assert.Equal(HttpStatusCode.NoContent, (await host.SendAsync(HttpMethod.Post, $"/catalog-categories/{org.CategoryId}/activate", cookie)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await host.SendAsync(HttpMethod.Post, $"/catalog-categories/{org.CategoryId}/activate", cookie)).StatusCode);
        Assert.Equal(1, await CategoryAuditAsync(org.Id, "catalog_category.reactivated"));
        Assert.Equal(1, await FormCategoriesAsync());

        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Post, $"/catalog-categories/{Guid.NewGuid()}/activate", cookie)).StatusCode);
        Assert.Equal(2, await CategoryAuditAsync(org.Id));
    }

    [Fact]
    public async Task ItemCategory_EnforcesActiveRuleKeepsInactiveCurrentAndExposesDetail()
    {
        var org = await database.SeedOrganizationAsync();
        var other = await database.SeedOrganizationAsync();
        var active = await database.SeedCategoryAsync(org, "Active cat");
        var retired = await database.SeedCategoryAsync(org, "Retired cat", active: false);
        var foreign = await database.SeedCategoryAsync(other, "Foreign cat");
        await using var host = CatalogHost.Create(database);
        var cookie = await host.CookieAsync(database, org, CompanySettingsDatabaseFixture.OwnerRoleId);

        JsonObject Body(string name, string type, object? categoryId)
        {
            var body = CatalogSeed.ItemBody(type: type, name: name);
            body["categoryId"] = categoryId?.ToString();

            return body;
        }

        // Both types accept an active category; absent categoryId means none.
        var service = await CatalogHost.ReadAsync(await host.SendAsync(HttpMethod.Post, "/catalog-items", cookie, Body("Cat service", "service", active)));
        var product = await CatalogHost.ReadAsync(await host.SendAsync(HttpMethod.Post, "/catalog-items", cookie, Body("Cat product", "product", active)));
        var none = await CatalogHost.ReadAsync(await host.SendAsync(HttpMethod.Post, "/catalog-items", cookie, CatalogSeed.ItemBody(name: "No category")));
        Assert.Equal(active.ToString(), service["categoryId"]!.GetValue<string>());
        Assert.Equal("Active cat", product["categoryName"]!.GetValue<string>());
        Assert.True(product["categoryIsActive"]!.GetValue<bool>());
        Assert.Null(none["categoryId"]);
        Assert.Null(none["categoryName"]);
        Assert.Null(none["categoryIsActive"]);

        var createdAudit = JsonNode.Parse(
            await AuditAsync(Guid.Parse(service["id"]!.GetValue<string>()), "catalog_item.created", "after_data"))!;
        Assert.Equal(active.ToString(), createdAudit["categoryId"]!.GetValue<string>());
        Assert.Null(JsonNode.Parse(
            await AuditAsync(Guid.Parse(none["id"]!.GetValue<string>()), "catalog_item.created", "after_data"))!.AsObject()["categoryId"]);

        // Inactive, foreign, unknown and malformed ids on create are 400 and create nothing.
        var items = await database.CountItemsAsync(org);

        foreach (var bad in new object[] { retired, foreign, Guid.NewGuid(), "not-a-guid" })
        {
            var response = await host.SendAsync(HttpMethod.Post, "/catalog-items", cookie, Body($"Bad {bad}", "service", bad));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("Choose an active category.", CatalogSeed.Error(await CatalogHost.ReadAsync(response), "categoryId"));
        }

        Assert.Equal(items, await database.CountItemsAsync(org));

        // Update: change category audits through catalog_item.updated with ids only.
        var serviceId = service["id"]!.GetValue<string>();
        var changed = await host.SendAsync(HttpMethod.Put, $"/catalog-items/{serviceId}", cookie, Body("Cat service", "service", null));
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Null((await CatalogHost.ReadAsync(changed))["categoryId"]);
        await AssertAuditAsync(
            Guid.Parse(serviceId), "catalog_item.updated", $"{{\"categoryId\":\"{active}\"}}", "{\"categoryId\":null}");

        // The current category is accepted while inactive; moving to a different inactive one is not.
        var legacy = await database.SeedCatalogItemAsync(org, "service", "Legacy item");
        await database.ExecuteAsync("UPDATE catalog_items SET category_id = @c WHERE id = @i", ("c", retired), ("i", legacy));
        var detail = await CatalogHost.ReadAsync(await host.SendAsync(HttpMethod.Get, $"/catalog-items/{legacy}", cookie));
        Assert.Equal("Retired cat", detail["categoryName"]!.GetValue<string>());
        Assert.False(detail["categoryIsActive"]!.GetValue<bool>());

        var keep = CatalogSeed.ItemBody(name: "Legacy item");
        keep["description"] = "Edited";
        keep["categoryId"] = retired.ToString();
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(HttpMethod.Put, $"/catalog-items/{legacy}", cookie, keep)).StatusCode);

        var otherRetired = await database.SeedCategoryAsync(org, "Other retired", active: false);
        keep["categoryId"] = otherRetired.ToString();
        var rejected = await host.SendAsync(HttpMethod.Put, $"/catalog-items/{legacy}", cookie, keep);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Equal(retired, await database.ScalarAsync<Guid>("SELECT category_id FROM catalog_items WHERE id = @i", ("i", legacy)));
    }

    [Fact]
    public async Task ListAndSummary_ReportCountsOrderingAndPublicRequestReadiness()
    {
        var org = await database.SeedOrganizationAsync();
        await using var host = CatalogHost.Create(database);
        var cookie = await host.CookieAsync(database, org, CompanySettingsDatabaseFixture.ViewerRoleId);

        async Task<string> ReadinessAsync() =>
            (await CatalogHost.ReadAsync(await host.SendAsync(HttpMethod.Get, "/catalog-items/summary?status=inactive&type=product", cookie)))
                ["publicRequestReadiness"]!.GetValue<string>();

        Assert.Equal("no_active_categories", await ReadinessAsync());

        var beta = await database.SeedCategoryAsync(org, "beta");
        var alpha = await database.SeedCategoryAsync(org, "Alpha");
        var retired = await database.SeedCategoryAsync(org, "Zulu", active: false);
        Assert.Equal("no_active_services", await ReadinessAsync());

        await database.SeedServiceAsync(org, beta, "Inactive service", active: false);
        await database.SeedServiceAsync(org, alpha, "Product only", type: "product");
        await database.SeedServiceAsync(org, retired, "Hidden service");
        Assert.Equal("no_active_services", await ReadinessAsync());

        await database.SeedServiceAsync(org, alpha, "Live service");
        Assert.Equal("ready", await ReadinessAsync());

        var response = await host.SendAsync(HttpMethod.Get, "/catalog-categories", cookie);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var items = (await CatalogHost.ReadAsync(response))["items"]!.AsArray();
        Assert.Equal(["Alpha", "beta", "Zulu"], items.Select(i => i!["name"]!.GetValue<string>()));
        Assert.Equal([2, 1, 1], items.Select(i => i!["itemCount"]!.GetValue<int>()));
        Assert.Equal([1, 0, 1], items.Select(i => i!["activeServiceCount"]!.GetValue<int>()));
        Assert.Equal([true, true, false], items.Select(i => i!["isActive"]!.GetValue<bool>()));
    }

    [Fact]
    public async Task Categories_EnforceRolesTenantIsolationAndForeignReferences()
    {
        var orgA = await database.SeedOrganizationAsync();
        var orgB = await database.SeedOrganizationAsync();
        var categoryB = await database.SeedCategoryAsync(orgB, "Bravo category");
        await database.SeedCategoryAsync(orgA, "Alpha category");
        await using var host = CatalogHost.Create(database);
        var manager = await host.CookieAsync(database, orgA, CompanySettingsDatabaseFixture.OperationsManagerRoleId);
        var viewer = await host.CookieAsync(database, orgA, CompanySettingsDatabaseFixture.ViewerRoleId);
        var technician = await host.CookieAsync(database, orgA, CompanySettingsDatabaseFixture.TechnicianRoleId);

        // Cross-organization ids are 404 and nothing of B changes or is listed for A.
        var auditB = await CategoryAuditAsync(orgB);

        foreach (var (method, path) in new[]
        {
            (HttpMethod.Put, $"/catalog-categories/{categoryB}"),
            (HttpMethod.Post, $"/catalog-categories/{categoryB}/activate"),
            (HttpMethod.Post, $"/catalog-categories/{categoryB}/deactivate"),
        })
        {
            var response = await host.SendAsync(method, path, manager, method == HttpMethod.Put ? Name("Hijacked") : null);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        Assert.Equal("Bravo category|true", await database.TextAsync("SELECT concat_ws('|', name, is_active::text) FROM service_categories WHERE id = @i", ("i", categoryB)));
        Assert.Equal(auditB, await CategoryAuditAsync(orgB));
        var listed = (await CatalogHost.ReadAsync(await host.SendAsync(HttpMethod.Get, "/catalog-categories", manager)))["items"]!.AsArray();
        Assert.Equal(["Alpha category"], listed.Select(i => i!["name"]!.GetValue<string>()));

        // A foreign categoryId is rejected exactly like an unknown one.
        var item = CatalogSeed.ItemBody(name: "Foreign ref");
        item["categoryId"] = categoryB.ToString();
        var foreign = await host.SendAsync(HttpMethod.Post, "/catalog-items", manager, item);
        Assert.Equal(HttpStatusCode.BadRequest, foreign.StatusCode);
        Assert.Equal("Choose an active category.", CatalogSeed.Error(await CatalogHost.ReadAsync(foreign), "categoryId"));

        // Role matrix: viewer reads only, technician and anonymous are denied everywhere.
        var writes = new (HttpMethod Method, string Path)[]
        {
            (HttpMethod.Post, "/catalog-categories"),
            (HttpMethod.Put, $"/catalog-categories/{categoryB}"),
            (HttpMethod.Post, $"/catalog-categories/{categoryB}/activate"),
            (HttpMethod.Post, $"/catalog-categories/{categoryB}/deactivate"),
        };

        foreach (var (method, path) in writes)
        {
            var body = method == HttpMethod.Get ? null : Name("Denied");
            Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(method, path, viewer, body)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(method, path, technician, body)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await host.SendAsync(method, path, null, body)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(HttpMethod.Get, "/catalog-categories", viewer)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(HttpMethod.Get, "/catalog-categories", technician)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.SendAsync(HttpMethod.Get, "/catalog-categories", null)).StatusCode);
        Assert.Equal(0, await database.CountAsync("SELECT COUNT(*) FROM service_categories WHERE organization_id = @o AND name = 'Denied'", ("o", orgA)));
    }
}
