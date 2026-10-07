using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using FieldOps.Application.Features.Email;
using FieldOps.IntegrationTests.Api;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.Dispatch;
using FieldOps.IntegrationTests.PasswordResets;
using FieldOps.IntegrationTests.ServiceRequests;
using FieldOps.IntegrationTests.Sessions;
using FieldOps.IntegrationTests.Team;
using FieldOps.IntegrationTests.Users;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FieldOps.IntegrationTests.TechnicianVisits;

/// <summary>One API host over the shared container with a controllable clock, a recording email port and log capture.</summary>
internal sealed class TechnicianHost : IAsyncDisposable
{
    private readonly SessionTestHost _host;

    private readonly HttpClient _client;

    private TechnicianHost(SessionTestHost host, HttpClient client, RecordingEmailSender sender)
    {
        _host = host;
        _client = client;
        Sender = sender;
    }

    public MutableTimeProvider Time => _host.Time!;

    /// <summary>Every outgoing message; <see cref="RecordingEmailSender.Fail"/> makes the provider throw.</summary>
    public RecordingEmailSender Sender { get; }

    public CapturingLoggerProvider Logs => _host.Logs!;

    public static TechnicianHost Create(CompanySettingsDatabaseFixture database)
    {
        var host = SessionTestHost.Create(database.ConnectionString, new MutableTimeProvider(DateTimeOffset.UtcNow), captureLogs: true);
        var sender = new RecordingEmailSender();
        var factory = host.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(sender);
        }));

        return new TechnicianHost(host, SessionApi.CreateClient(factory), sender);
    }

    /// <summary>Moves the clock to <paramref name="instant"/> (sessions are issued at the clock of the sign-in).</summary>
    public void SetNow(DateTimeOffset instant) => Time.Advance(instant - Time.GetUtcNow());

    public async Task<(string Cookie, SeededMember Member)> SignInAsync(
        CompanySettingsDatabaseFixture database, Guid organizationId, short roleId, string first = "Tess", string last = "Tech")
    {
        var member = await database.SeedMemberAsync(organizationId, roleId, first, last);

        return (await CompanySettingsApi.SignInCookieAsync(_client, member.Email), member);
    }

    public Task<HttpResponseMessage> GetAsync(string path, string? cookie) =>
        CompanySettingsApi.SendRawAsync(_client, HttpMethod.Get, path, null, cookie);

    /// <summary>A body-less POST, like the start-travel and arrive requests of the app.</summary>
    public Task<HttpResponseMessage> PostAsync(string path, string? cookie) =>
        CompanySettingsApi.SendRawAsync(_client, HttpMethod.Post, path, null, cookie);

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _host.DisposeAsync();
    }
}

/// <summary>A signed-in technician with a linked active profile in branch A.</summary>
internal sealed record SeededTechnician(string Cookie, SeededMember Member, Guid Profile);

internal static class TechnicianVisitSeed
{
    public static async Task<SeededTechnician> SeedTechnicianAsync(
        this CompanySettingsDatabaseFixture db,
        TechnicianHost host,
        RequestWorld world,
        string status = "active",
        string first = "Tess")
    {
        var (cookie, member) = await host.SignInAsync(db, world.Org, CompanySettingsDatabaseFixture.TechnicianRoleId, first);
        var profile = await db.SeedTechAsync(world.Org, world.BranchA, first, "Tech", status, membershipId: member.MembershipId);

        return new SeededTechnician(cookie, member, profile);
    }

    /// <summary>A work order with one scheduled visit, optionally assigned to a technician.</summary>
    public static async Task<SeededOrder> SeedJobAsync(
        this CompanySettingsDatabaseFixture db,
        RequestWorld world,
        Guid user,
        Guid? technician,
        string status,
        DateTimeOffset start,
        int minutes = 60,
        string title = "Fix drain",
        short priority = 3)
    {
        var order = await db.SeedOrderAsync(
            world,
            user,
            status: "scheduled",
            title: title,
            priority: priority,
            visitStatus: status,
            start: start,
            end: start.AddMinutes(minutes));

        if (technician is { } assignee)
        {
            await db.AssignAsync(order.Visit, assignee, user);
        }

        return order;
    }

    public static Task UnassignAsync(this CompanySettingsDatabaseFixture db, Guid visit) =>
        db.ExecuteAsync("UPDATE visit_assignments SET unassigned_at = now() WHERE visit_id = @v", ("v", visit));

    /// <summary>The write-relevant state of a visit as one text: status, history, travel entries (closed), travel audit rows and concurrency value.</summary>
    public static Task<string> TravelStateAsync(this CompanySettingsDatabaseFixture db, Guid visit) =>
        db.ScalarAsync<string>(
            """
            SELECT v.status::text
                || '|h=' || (SELECT COUNT(*) FROM visit_status_history WHERE visit_id = @v)
                || '|t=' || (SELECT COUNT(*) FROM visit_time_entries WHERE visit_id = @v)
                || '|c=' || (SELECT COUNT(*) FROM visit_time_entries WHERE visit_id = @v AND ended_at IS NOT NULL)
                || '|a=' || (SELECT COUNT(*) FROM audit_logs WHERE entity_id = @v AND action IN ('visit.travel_started', 'visit.arrived'))
                || '|u=' || v.updated_at::text
            FROM visits v WHERE v.id = @v
            """,
            ("v", visit));

    public static Task<long> AuditCountAsync(this CompanySettingsDatabaseFixture db, Guid visit, string action) =>
        db.ScalarAsync<long>(
            "SELECT COUNT(*) FROM audit_logs WHERE entity_id = @v AND action = @a", ("v", visit), ("a", action));

    public static Task SeedTravelEntryAsync(
        this CompanySettingsDatabaseFixture db, Guid visit, Guid technician, DateTimeOffset started, DateTimeOffset? ended = null) =>
        db.ExecuteAsync(
            "INSERT INTO visit_time_entries (visit_id, technician_id, started_at, ended_at, entry_type) VALUES (@v, @t, @s, @e, 'travel')",
            ("v", visit),
            ("t", technician),
            ("s", started),
            ("e", ended));

    public static Task SetSendFlagsAsync(
        this CompanySettingsDatabaseFixture db, Guid order, bool reminder, bool technicianDetails) =>
        db.ExecuteAsync(
            "UPDATE work_orders SET send_arrival_reminder = @r, send_technician_details = @d WHERE id = @w",
            ("r", reminder),
            ("d", technicianDetails),
            ("w", order));

    public static Task SetBranchTimezoneAsync(this CompanySettingsDatabaseFixture db, Guid branch, string? timezone) =>
        db.ExecuteAsync("UPDATE branches SET timezone = @tz WHERE id = @b", ("tz", timezone), ("b", branch));

    public static Task<string> PrefixAsync(this CompanySettingsDatabaseFixture db, Guid org) =>
        db.ScalarAsync<string>("SELECT work_order_prefix FROM organizations WHERE id = @o", ("o", org));

    public static async Task<JsonNode> ReadAsync(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        Assert.True(
            response.StatusCode == expected,
            $"{response.RequestMessage?.RequestUri?.AbsolutePath}: expected {expected} but was {response.StatusCode} {await response.Content.ReadAsStringAsync()}");

        return await RequestsHost.ReadAsync(response);
    }

    public static async Task<string> WithoutTraceAsync(HttpResponseMessage response)
    {
        var problem = (JsonObject)await RequestsHost.ReadAsync(response);
        problem.Remove("traceId");

        return problem.ToJsonString();
    }

    public static DateTimeOffset Utc(string iso) => DateTimeOffset.Parse(iso, CultureInfo.InvariantCulture);

    public static Guid[] VisitIds(JsonNode today) =>
        [.. today["visits"]!.AsArray().Select(visit => visit!["visitId"]!.GetValue<Guid>())];
}
