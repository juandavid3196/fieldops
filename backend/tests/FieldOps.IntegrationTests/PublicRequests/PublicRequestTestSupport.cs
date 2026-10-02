using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FieldOps.Application.Features.Email;
using FieldOps.IntegrationTests.Api;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.PasswordResets;
using FieldOps.IntegrationTests.Sessions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FieldOps.IntegrationTests.PublicRequests;

public sealed record PublicOrg(Guid Id, string Slug, Guid BranchId, Guid CategoryId, Guid ServiceId);

/// <summary>One API host over the shared container with a recording email port and optional log capture.</summary>
public sealed class PublicRequestHost : IAsyncDisposable
{
    private readonly SessionTestHost _host;

    private PublicRequestHost(SessionTestHost host, HttpClient client, RecordingEmailSender sender)
    {
        _host = host;
        Client = client;
        Sender = sender;
    }

    public HttpClient Client { get; }

    public RecordingEmailSender Sender { get; }

    public CapturingLoggerProvider? Logs => _host.Logs;

    public static PublicRequestHost Create(CompanySettingsDatabaseFixture database, bool captureLogs = false)
    {
        var host = SessionTestHost.Create(database.ConnectionString, captureLogs: captureLogs);
        var sender = new RecordingEmailSender();

        var factory = host.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(sender);
        }));

        return new PublicRequestHost(host, SessionApi.CreateClient(factory), sender);
    }

    public Task<HttpResponseMessage> GetFormAsync(string slug, string? ip = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/public/organizations/{slug}/service-request-form");
        request.Headers.Add(TestClientIpStartupFilter.HeaderName, ip ?? SessionApi.NewClientIp());

        return Client.SendAsync(request);
    }

    /// <summary>Posts the multipart submission; <paramref name="requestAsFile"/> sends the JSON part as a Blob would.</summary>
    public Task<HttpResponseMessage> PostAsync(
        string slug,
        JsonNode? body,
        IReadOnlyList<(string FileName, byte[] Content)>? files = null,
        string? ip = null,
        bool requestAsFile = false)
    {
        var content = new MultipartFormDataContent();

        if (body is not null)
        {
            var json = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

            if (requestAsFile)
            {
                content.Add(json, "request", "blob");
            }
            else
            {
                content.Add(json, "request");
            }
        }

        foreach (var (fileName, bytes) in files ?? [])
        {
            // The declared content type is deliberately wrong: it is never trusted.
            var part = new ByteArrayContent(bytes);
            part.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            content.Add(part, "attachments", fileName);
        }

        var request = new HttpRequestMessage(HttpMethod.Post, $"/public/organizations/{slug}/service-requests")
        {
            Content = content,
        };
        request.Headers.Add(TestClientIpStartupFilter.HeaderName, ip ?? SessionApi.NewClientIp());

        return Client.SendAsync(request);
    }

    public ValueTask DisposeAsync()
    {
        Client.Dispose();

        return _host.DisposeAsync();
    }
}

public static class PublicRequestSeed
{
    public static byte[] Jpeg(int totalBytes = 64) =>
        [0xFF, 0xD8, 0xFF, 0xE0, .. new byte[Math.Max(0, totalBytes - 4)]];

    public static byte[] Png(int totalBytes = 64) =>
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. new byte[Math.Max(0, totalBytes - 8)]];

    public static byte[] Pdf(int totalBytes = 64) =>
        [0x25, 0x50, 0x44, 0x46, 0x2D, .. new byte[Math.Max(0, totalBytes - 5)]];

    public static string NewSlug() => $"acme-{Guid.NewGuid():N}"[..24];

    /// <summary>An active organization with a main branch and one category with one active service.</summary>
    public static async Task<PublicOrg> SeedPublicOrgAsync(
        this CompanySettingsDatabaseFixture db, string? slug = null, string timezone = "UTC", string? name = null)
    {
        var orgSlug = slug ?? NewSlug();
        var org = await db.SeedOrganizationAsync(name ?? $"Acme {orgSlug}");

        await db.ExecuteAsync(
            "UPDATE organizations SET public_slug = @slug, phone = '+1 555 010 0100', website = 'https://acme.example', timezone = @tz WHERE id = @id",
            ("slug", orgSlug),
            ("tz", timezone),
            ("id", org));

        var branch = await db.SeedBranchAsync(org, isMain: true);
        var category = await db.SeedCategoryAsync(org, "Plumbing");
        var service = await db.SeedServiceAsync(org, category, "Drain cleaning");

        return new PublicOrg(org, orgSlug, branch.Id, category, service);
    }

    public static async Task<Guid> SeedCategoryAsync(
        this CompanySettingsDatabaseFixture db, Guid organizationId, string name, bool active = true)
    {
        var id = Guid.NewGuid();

        await db.ExecuteAsync(
            "INSERT INTO service_categories (id, organization_id, name, is_active) VALUES (@id, @org, @name, @active)",
            ("id", id),
            ("org", organizationId),
            ("name", name),
            ("active", active));

        return id;
    }

    public static async Task<Guid> SeedServiceAsync(
        this CompanySettingsDatabaseFixture db,
        Guid organizationId,
        Guid categoryId,
        string name,
        bool active = true,
        string type = "service")
    {
        var id = Guid.NewGuid();

        // type is a test-controlled constant, never user input.
        await db.ExecuteAsync(
            $"""
            INSERT INTO catalog_items (id, organization_id, category_id, type, name, unit_cost, unit_price, is_active)
            VALUES (@id, @org, @category, '{type}', @name, 20, 99, @active)
            """,
            ("id", id),
            ("org", organizationId),
            ("category", categoryId),
            ("name", name),
            ("active", active));

        return id;
    }

    public static JsonObject ValidBody(PublicOrg org, string email = "visitor@example.com") =>
        new()
        {
            ["contact"] = new JsonObject
            {
                ["firstName"] = "Ada",
                ["lastName"] = "Lovelace",
                ["email"] = email,
                ["phone"] = "(555) 123-4567",
                ["prefersEmail"] = true,
                ["prefersSms"] = false,
            },
            ["property"] = new JsonObject
            {
                ["propertyType"] = "home",
                ["addressLine1"] = "12 Analytical St",
                ["addressLine2"] = "Apt 3",
                ["city"] = "Austin",
                ["state"] = "TX",
                ["postalCode"] = "78701",
                ["accessInstructions"] = "Gate code 1234",
            },
            ["service"] = new JsonObject
            {
                ["categoryId"] = org.CategoryId.ToString(),
                ["serviceId"] = org.ServiceId.ToString(),
                ["notSure"] = false,
                ["description"] = "Kitchen drain is clogged",
                ["urgency"] = "urgent",
                ["hasActiveDamage"] = true,
            },
            ["availability"] = new JsonObject
            {
                ["dateMode"] = "asap",
                ["preferredDate"] = null,
                ["timeWindow"] = "morning",
                ["schedulingNotes"] = "Call first",
            },
            ["consent"] = true,
            ["website"] = string.Empty,
            // Ignored by contract (BR-02).
            ["organizationId"] = Guid.NewGuid().ToString(),
            ["branchId"] = Guid.NewGuid().ToString(),
        };

    public static JsonNode Section(this JsonObject body, string name) => body[name]!;

    public static async Task<JsonNode> ReadAsync(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!;

    /// <summary>The first message under the given validation error key, or null.</summary>
    public static string? ErrorOf(JsonNode problem, string key) =>
        problem["errors"]?[key]?.AsArray().FirstOrDefault()?.GetValue<string>();

    public static Task<long> CountAsync(this CompanySettingsDatabaseFixture db, string table, Guid organizationId) =>
        db.ScalarAsync<long>($"SELECT COUNT(*) FROM {table} WHERE organization_id = @o", ("o", organizationId));

    public static Task<long> NextNumberAsync(this CompanySettingsDatabaseFixture db, Guid organizationId) =>
        db.ScalarAsync<long>("SELECT next_request_number FROM organizations WHERE id = @o", ("o", organizationId));

    /// <summary>Counts of every table a submission writes, to prove nothing was persisted.</summary>
    public static async Task<string> SnapshotAsync(this CompanySettingsDatabaseFixture db, Guid organizationId)
    {
        var parts = new List<string>();

        foreach (var table in new[]
        {
            "service_requests", "customers", "customer_contacts", "properties",
            "request_attachments", "request_status_history", "audit_logs",
        })
        {
            parts.Add($"{table}={await db.CountAsync(table, organizationId)}");
        }

        parts.Add($"next={await db.NextNumberAsync(organizationId)}");

        return string.Join(';', parts);
    }

    public static string Compact(JsonNode node) =>
        JsonSerializer.Serialize(node, new JsonSerializerOptions { WriteIndented = false });

    public static void AssertNoStore(HttpResponseMessage response) =>
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.Ordinal);

    public static void AssertStatus(HttpStatusCode expected, HttpResponseMessage response, string because = "") =>
        Assert.True(expected == response.StatusCode, $"{because} expected {expected} but was {response.StatusCode}.");
}
