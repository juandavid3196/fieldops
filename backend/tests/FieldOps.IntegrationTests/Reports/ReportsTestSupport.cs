using System.Net;
using System.Text.Json.Nodes;
using FieldOps.Application.Features.AuditTrail;
using FieldOps.Application.Features.Reports;
using FieldOps.IntegrationTests.BillingReview;
using FieldOps.IntegrationTests.BusinessOverview;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.InvoicePayments;
using FieldOps.IntegrationTests.ServiceRequests;
using FieldOps.IntegrationTests.Sessions;
using FieldOps.IntegrationTests.Users;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace FieldOps.IntegrationTests.Reports;

/// <summary>One API host whose export audit writer can be told to fail and whose export limits are small, for the BR-14 to BR-16 rules.</summary>
internal sealed class ReportsHost : IAsyncDisposable
{
    private readonly SessionTestHost _host;

    private ReportsHost(SessionTestHost host, HttpClient client, FailureState state)
    {
        _host = host;
        Client = client;
        State = state;
    }

    public HttpClient Client { get; }

    public FailureState State { get; }

    public static ReportsHost Create(CompanySettingsDatabaseFixture db, ReportExportLimits? limits = null)
    {
        var host = SessionTestHost.Create(db.ConnectionString);
        var state = new FailureState();
        var factory = host.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            var original = services.Last(descriptor => descriptor.ServiceType == typeof(IExportAuditWriter));
            services.Remove(original);
            services.AddScoped<IExportAuditWriter>(provider =>
                new ToggleWriter(state, (IExportAuditWriter)original.ImplementationFactory!(provider)));

            if (limits is not null)
            {
                services.AddSingleton(limits);
            }
        }));

        return new ReportsHost(host, SessionApi.CreateClient(factory), state);
    }

    public Task<HttpResponseMessage> GetAsync(string path, string? cookie) =>
        CompanySettingsApi.SendRawAsync(Client, HttpMethod.Get, path, null, cookie);

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _host.DisposeAsync();
    }

    internal sealed class FailureState
    {
        public bool Fail { get; set; }
    }

    private sealed class ToggleWriter(FailureState state, IExportAuditWriter inner) : IExportAuditWriter
    {
        public Task RecordAsync(FieldOps.Application.Features.ServiceRequests.MembershipCall call, ExportAuditEntry entry, CancellationToken cancellationToken) =>
            state.Fail
                ? throw new InvalidOperationException("The audit insert failed.")
                : inner.RecordAsync(call, entry, cancellationToken);
    }
}

internal static class ReportsSeed
{
    public static readonly string[] Keys =
        ["job_profitability", "invoice_aging", "quote_conversion", "jobs_on_time", "technician_utilization"];

    public static string Window => BusinessOverviewSeed.Window;

    public static string Path(string key, string extra = "") => $"/reports/{key}?{Window}{extra}";

    public static string ExportPath(string key, string format, string extra = "") => $"/reports/{key}/export?{Window}&format={format}{extra}";

    /// <summary>The business-overview history of the BusinessOverview seed plus a signed-in member of any role (optionally branch-limited).</summary>
    public static async Task<BusinessSeed> SeedAsync(CompanySettingsDatabaseFixture db)
    {
        await using var seeder = RequestsHost.Create(db);

        return await BusinessOverviewSeed.SeedAsync(db, seeder);
    }

    public static async Task<(string Cookie, SeededMember Member)> SignInAsync(
        this CompanySettingsDatabaseFixture db, ReportsHost host, Guid org, short role, params Guid[] limitedToBranches)
    {
        var member = await db.SeedMemberAsync(org, role, "Sam", "Staff", isAllBranches: limitedToBranches.Length == 0);

        foreach (var branch in limitedToBranches)
        {
            await db.LinkMembershipToBranchAsync(member.MembershipId, branch);
        }

        return (await CompanySettingsApi.SignInCookieAsync(host.Client, member.Email), member);
    }

    public static async Task<JsonNode> OkAsync(this ReportsHost host, string path, string cookie) =>
        await BillingSeed.ReadAsync(await host.GetAsync(path, cookie));

    public static async Task<string> BodyWithoutTraceAsync(HttpResponseMessage response)
    {
        var problem = (JsonObject)JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        problem.Remove("traceId");

        return $"{(int)response.StatusCode} {problem.ToJsonString()}";
    }

    public static decimal Dec(JsonNode? node) => node!.GetValue<decimal>();

    public static string Text(JsonNode? node) => node!.GetValue<string>();

    public static JsonNode Metric(JsonNode report, string key) =>
        report["summary"]!.AsArray().Single(metric => Text(metric!["key"]) == key)!;

    public static decimal? Value(JsonNode report, string key) => Metric(report, key)["value"]?.GetValue<decimal>();

    public static decimal? Previous(JsonNode report, string key) => Metric(report, key)["previous"]?.GetValue<decimal>();

    public static List<JsonNode> Items(JsonNode report) => [.. report["items"]!.AsArray().Select(item => item!)];

    public static Task<long> ExportCountAsync(this CompanySettingsDatabaseFixture db, Guid org) =>
        db.ScalarAsync<long>("SELECT COUNT(*) FROM audit_logs WHERE organization_id = @o AND action = 'export.generated'", ("o", org));

    public static HttpStatusCode Status(this HttpResponseMessage response) => response.StatusCode;
}
