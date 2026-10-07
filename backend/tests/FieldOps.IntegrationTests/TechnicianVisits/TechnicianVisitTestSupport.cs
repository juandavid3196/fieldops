using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.Dispatch;
using FieldOps.IntegrationTests.ServiceRequests;
using FieldOps.IntegrationTests.Sessions;
using FieldOps.IntegrationTests.Team;
using FieldOps.IntegrationTests.Users;

namespace FieldOps.IntegrationTests.TechnicianVisits;

/// <summary>One API host over the shared container with a controllable clock.</summary>
internal sealed class TechnicianHost : IAsyncDisposable
{
    private readonly SessionTestHost _host;

    private TechnicianHost(SessionTestHost host) => _host = host;

    public MutableTimeProvider Time => _host.Time!;

    public static TechnicianHost Create(CompanySettingsDatabaseFixture database) =>
        new(SessionTestHost.Create(database.ConnectionString, new MutableTimeProvider(DateTimeOffset.UtcNow)));

    /// <summary>Moves the clock to <paramref name="instant"/> (sessions are issued at the real time first).</summary>
    public void SetNow(DateTimeOffset instant) => Time.Advance(instant - Time.GetUtcNow());

    public async Task<(string Cookie, SeededMember Member)> SignInAsync(
        CompanySettingsDatabaseFixture database, Guid organizationId, short roleId, string first = "Tess", string last = "Tech")
    {
        var member = await database.SeedMemberAsync(organizationId, roleId, first, last);

        return (await CompanySettingsApi.SignInCookieAsync(_host.Client, member.Email), member);
    }

    public Task<HttpResponseMessage> GetAsync(string path, string? cookie) =>
        CompanySettingsApi.SendRawAsync(_host.Client, HttpMethod.Get, path, null, cookie);

    public ValueTask DisposeAsync() => _host.DisposeAsync();
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
