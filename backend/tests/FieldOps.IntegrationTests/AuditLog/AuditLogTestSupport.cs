using System.Net;
using System.Text.Json.Nodes;
using FieldOps.Application.Features.ServiceRequests;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.ServiceRequests;

namespace FieldOps.IntegrationTests.AuditLog;

internal static class AuditSeed
{
    public const short Owner = CompanySettingsDatabaseFixture.OwnerRoleId;
    public const short Dispatcher = CompanySettingsDatabaseFixture.DispatcherRoleId;
    public const short Technician = CompanySettingsDatabaseFixture.TechnicianRoleId;
    public const short Accounting = CompanySettingsDatabaseFixture.AccountingRoleId;
    public const short Operations = CompanySettingsDatabaseFixture.OperationsManagerRoleId;
    public const short Viewer = CompanySettingsDatabaseFixture.ViewerRoleId;

    public const string Bogota = "America/Bogota";

    /// <summary>Inserts one audit row exactly as the application would store it; the text arguments are test constants.</summary>
    public static Task<long> InsertAsync(
        this CompanySettingsDatabaseFixture db,
        Guid org,
        string action,
        string entityType,
        DateTimeOffset at,
        Guid? actor = null,
        Guid? entity = null,
        Guid? branch = null,
        string? before = null,
        string? after = null,
        string metadata = "{}",
        string? ip = null) =>
        db.ScalarAsync<long>(
            """
            INSERT INTO audit_logs (organization_id, actor_user_id, action, entity_type, entity_id, branch_id, before_data, after_data, metadata, ip_address, occurred_at)
            VALUES (@org, @actor, @action, @type, @entity, @branch, CAST(@before AS jsonb), CAST(@after AS jsonb), CAST(@meta AS jsonb), CAST(@ip AS inet), @at)
            RETURNING id
            """,
            ("org", org),
            ("actor", actor),
            ("action", action),
            ("type", entityType),
            ("entity", entity),
            ("branch", branch),
            ("before", before),
            ("after", after),
            ("meta", metadata),
            ("ip", ip),
            ("at", at.ToUniversalTime()));

    /// <summary>Drops the rows the sign-ins of the arrangement wrote, so the test owns every row of its organization.</summary>
    public static Task ClearAuditAsync(this CompanySettingsDatabaseFixture db, Guid org) =>
        db.ExecuteAsync("DELETE FROM audit_logs WHERE organization_id = @o", ("o", org));

    public static Task<long> CountAuditAsync(this CompanySettingsDatabaseFixture db, Guid org, string action) =>
        db.ScalarAsync<long>("SELECT count(*) FROM audit_logs WHERE organization_id = @o AND action = @a", ("o", org), ("a", action));

    /// <summary>Local midnight (as UTC) of the organization-local day, <paramref name="days"/> days from today.</summary>
    public static DateTimeOffset LocalStart(string timezone, int days = 0)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timezone);

        return OrganizationTime.StartOfDayUtc(OrganizationTime.LocalDate(DateTimeOffset.UtcNow, zone).AddDays(days), zone);
    }

    public static string LocalDay(string timezone, int days = 0)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timezone);

        return OrganizationTime.LocalDate(DateTimeOffset.UtcNow, zone).AddDays(days).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    }

    public static async Task<JsonNode> GetAsync(
        RequestsHost host, string? cookie, string path, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var response = await host.SendAsync(HttpMethod.Get, path, cookie);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"{path}: {(int)response.StatusCode} {body} {Failures(host)}");

        return JsonNode.Parse(body)!;
    }

    private static string Failures(RequestsHost host) =>
        string.Join(" | ", host.Logs.Entries.Where(entry => entry.Exception is not null).Select(entry => entry.Exception!.GetType().Name + ": " + entry.Exception.Message));

    public static long[] Ids(JsonNode page) =>
        [.. page["items"]!.AsArray().Select(item => item!["id"]!.GetValue<long>())];

    public static JsonNode Item(JsonNode page, long id) =>
        page["items"]!.AsArray().Single(item => item!["id"]!.GetValue<long>() == id)!;

    public static string[] ErrorKeys(JsonNode problem) =>
        [.. problem["errors"]!.AsObject().Select(error => error.Key).Order(StringComparer.Ordinal)];

    public static async Task<string> TextAsync(RequestsHost host, string? cookie, string path, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var response = await host.SendAsync(HttpMethod.Get, path, cookie);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"{path}: {(int)response.StatusCode} {body} {Failures(host)}");

        return body;
    }
}
