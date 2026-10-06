using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using FieldOps.Application.Features.Catalog;
using FieldOps.Domain.Catalog;
using FieldOps.IntegrationTests.Api;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.Sessions;
using FieldOps.IntegrationTests.Users;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace FieldOps.IntegrationTests.Catalog;

/// <summary>
/// Test-only proxy that disables the application-level duplicate-name checks so a duplicate reaches
/// the unique index, the deterministic stand-in for losing a concurrent race (BR-08, BR-13).
/// </summary>
public class SkipNameChecksProxy : DispatchProxy
{
    public ICatalogStore Inner { get; set; } = null!;

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        switch (targetMethod!.Name)
        {
            case nameof(ICatalogStore.NameExistsAsync):
                return Task.FromResult(false);
            case nameof(ICatalogStore.FindExistingNamesAsync):
                return Task.FromResult(new HashSet<(CatalogItemType Type, string NormalizedName)>());
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

/// <summary>One API host over the shared container, optionally with the duplicate pre-checks disabled.</summary>
public sealed class CatalogHost : IAsyncDisposable
{
    private readonly SessionTestHost _host;

    private CatalogHost(SessionTestHost host, HttpClient client)
    {
        _host = host;
        Client = client;
    }

    public HttpClient Client { get; }

    public static CatalogHost Create(CompanySettingsDatabaseFixture database, bool skipNameChecks = false)
    {
        var host = SessionTestHost.Create(database.ConnectionString);

        if (!skipNameChecks)
        {
            return new CatalogHost(host, host.Client);
        }

        var factory = host.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            var implementation = services.Single(d => d.ServiceType == typeof(ICatalogStore)).ImplementationType!;
            services.AddScoped<ICatalogStore>(provider =>
            {
                var proxy = DispatchProxy.Create<ICatalogStore, SkipNameChecksProxy>();
                ((SkipNameChecksProxy)(object)proxy).Inner =
                    (ICatalogStore)ActivatorUtilities.CreateInstance(provider, implementation);

                return proxy;
            });
        }));

        return new CatalogHost(host, SessionApi.CreateClient(factory));
    }

    /// <summary>Seeds a member of the role in the organization and returns a signed-in cookie.</summary>
    public async Task<string> CookieAsync(CompanySettingsDatabaseFixture database, Guid organizationId, short roleId)
    {
        var member = await database.SeedMemberAsync(organizationId, roleId, "Cat", "Alog");

        return await CompanySettingsApi.SignInCookieAsync(Client, member.Email);
    }

    public Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? cookie, JsonObject? body = null) =>
        CompanySettingsApi.SendRawAsync(Client, method, path, body?.ToJsonString(), cookie);

    public static async Task<JsonNode> ReadAsync(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!;

    public Task<HttpResponseMessage> SendFileAsync(
        HttpMethod method, string path, string? cookie, byte[] bytes, string declaredType, string fileName = "file.bin")
    {
        var part = new ByteArrayContent(bytes);
        part.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(declaredType);

        var request = new HttpRequestMessage(method, path)
        {
            Content = new MultipartFormDataContent { { part, "file", fileName } },
        };

        if (cookie is not null)
        {
            request.Headers.Add("Cookie", cookie);
        }

        request.Headers.Add(TestClientIpStartupFilter.HeaderName, SessionApi.NewClientIp());

        return Client.SendAsync(request);
    }

    public async ValueTask DisposeAsync()
    {
        if (!ReferenceEquals(Client, _host.Client))
        {
            Client.Dispose();
        }

        await _host.DisposeAsync();
    }
}

public static class CatalogSeed
{
    public static byte[] Png(int totalBytes = 64) =>
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. new byte[Math.Max(0, totalBytes - 8)]];

    public static byte[] Jpeg(int totalBytes = 64) =>
        [0xFF, 0xD8, 0xFF, 0xE0, .. new byte[Math.Max(0, totalBytes - 4)]];

    public static JsonObject ItemBody(
        string type = "service", string name = "Drain cleaning", decimal cost = 20m, decimal price = 100m) =>
        new()
        {
            ["type"] = type,
            ["name"] = name,
            ["description"] = "Clears clogs",
            ["unitCost"] = cost,
            ["unitPrice"] = price,
            ["isTaxable"] = true,
            ["isActive"] = true,
        };

    public static async Task<Guid> SeedCatalogItemAsync(
        this CompanySettingsDatabaseFixture db,
        Guid organizationId,
        string type,
        string name,
        decimal price = 10m,
        decimal cost = 0m,
        bool taxable = true,
        bool active = true,
        string? description = null)
    {
        var id = Guid.NewGuid();

        // type is a test-controlled constant, never user input.
        await db.ExecuteAsync(
            $"""
            INSERT INTO catalog_items (id, organization_id, type, name, description, unit_cost, unit_price, is_taxable, is_active)
            VALUES (@id, @org, '{type}', @name, @description, @cost, @price, @taxable, @active)
            """,
            ("id", id),
            ("org", organizationId),
            ("name", name),
            ("description", description),
            ("cost", cost),
            ("price", price),
            ("taxable", taxable),
            ("active", active));

        return id;
    }

    /// <summary>
    /// Seeds quote (two versions, a line in each) -> work order (version 1) -> visit with a material ->
    /// invoice with one line from the quote line and one from the material, all for the item, in
    /// non-default statuses. Foreign keys to unrelated parents are bypassed for this throwaway
    /// container only (session_replication_role = replica), as in the document-number seeds.
    /// </summary>
    public static Task SeedUsageChainAsync(
        this CompanySettingsDatabaseFixture db, Guid organizationId, Guid userId, Guid branchId, Guid itemId)
    {
        var number = Random.Shared.NextInt64(1, 1_000_000_000);

        return db.ExecuteAsync(
            """
            SET session_replication_role = replica;
            INSERT INTO quotes (id, organization_id, request_id, quote_number, status, current_version_no, created_by_user_id)
            VALUES (@q, @org, gen_random_uuid(), @n, 'cancelled', 2, @user);
            INSERT INTO quote_versions (id, organization_id, quote_id, version_no, scope, subtotal, tax_total, total, currency, created_by_user_id, is_immutable)
            VALUES (@v1, @org, @q, 1, 's', 0, 0, 0, 'USD', @user, true), (@v2, @org, @q, 2, 's', 0, 0, 0, 'USD', @user, true);
            INSERT INTO quote_lines (id, organization_id, quote_version_id, catalog_item_id, line_type, name, description, quantity, unit, unit_price, line_subtotal, line_tax, line_total)
            VALUES (@l1, @org, @v1, @item, 'service', 'n', 'd', 1, 'unit', 10, 10, 0, 10), (@l2, @org, @v2, @item, 'service', 'n', 'd', 1, 'unit', 10, 10, 0, 10);
            INSERT INTO work_orders (id, organization_id, branch_id, work_order_number, quote_version_id, customer_id, property_id, status, scope_snapshot, created_by_user_id)
            VALUES (@wo, @org, @branch, @n, @v1, gen_random_uuid(), gen_random_uuid(), 'cancelled', 's', @user);
            INSERT INTO visits (id, organization_id, work_order_id, visit_number, status) VALUES (@vis, @org, @wo, 1, 'unscheduled');
            INSERT INTO visit_materials (id, visit_id, catalog_item_id, description, quantity, unit) VALUES (@m, @vis, @item, 'd', 1, 'unit');
            INSERT INTO invoices (id, organization_id, branch_id, invoice_number, work_order_id, customer_id, status, currency, subtotal, tax_total, total, amount_paid, balance_due, created_by_user_id)
            VALUES (@inv, @org, @branch, @n, @wo, gen_random_uuid(), 'void', 'USD', 0, 0, 0, 0, 0, @user);
            INSERT INTO invoice_lines (id, invoice_id, source_quote_line_id, source_visit_material_id, description, quantity, unit, unit_price, line_subtotal, line_tax, line_total)
            VALUES (gen_random_uuid(), @inv, @l1, NULL, 'd', 1, 'unit', 10, 10, 0, 10), (gen_random_uuid(), @inv, NULL, @m, 'd', 1, 'unit', 10, 10, 0, 10);
            """,
            ("org", organizationId),
            ("user", userId),
            ("branch", branchId),
            ("item", itemId),
            ("n", number),
            ("q", Guid.NewGuid()),
            ("v1", Guid.NewGuid()),
            ("v2", Guid.NewGuid()),
            ("l1", Guid.NewGuid()),
            ("l2", Guid.NewGuid()),
            ("wo", Guid.NewGuid()),
            ("vis", Guid.NewGuid()),
            ("m", Guid.NewGuid()),
            ("inv", Guid.NewGuid()));
    }

    public static Task<long> CountItemsAsync(this CompanySettingsDatabaseFixture db, Guid organizationId) =>
        db.CountAsync("SELECT COUNT(*) FROM catalog_items WHERE organization_id = @o", ("o", organizationId));

    public static Task<long> CountCatalogAuditAsync(this CompanySettingsDatabaseFixture db, Guid organizationId, string? action = null) =>
        db.CountAsync(
            "SELECT COUNT(*) FROM audit_logs WHERE organization_id = @o AND entity_type = 'catalog_item' AND (@a::text IS NULL OR action = @a)",
            ("o", organizationId),
            ("a", action));

    public static Task<DateTimeOffset> ItemUpdatedAtAsync(this CompanySettingsDatabaseFixture db, Guid itemId) =>
        db.ScalarAsync<DateTimeOffset>("SELECT updated_at FROM catalog_items WHERE id = @i", ("i", itemId));

    public static string[] Names(JsonNode list) => UsersSeed.Items(list, "name");

    public static string Error(JsonNode problem, string key) =>
        problem["errors"]![key]!.AsArray().Single()!.GetValue<string>();

    public static string CsvOf(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    public static byte[] Utf8(string text) => new UTF8Encoding(false).GetBytes(text);

    public const string CsvHeader = "type,name,description,unit_cost,unit_price,taxable,active";
}
