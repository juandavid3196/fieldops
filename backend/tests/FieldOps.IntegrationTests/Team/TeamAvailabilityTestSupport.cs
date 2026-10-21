using System.Net;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.Customers;

namespace FieldOps.IntegrationTests.Team;

/// <summary>Seed and request helpers of the availability and skills tab tests (team-availability-skills).</summary>
public static class AvailabilitySeed
{
    public const string Hidden = "This technician profile isn't available.";

    public static readonly string[] DayNames = ["monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday"];

    public static string Hours(string start = "08:00", string end = "17:00", params string[] days) =>
        "{" + string.Join(",", days.Select(day => $"\"{day}\":{{\"start\":\"{start}\",\"end\":\"{end}\"}}")) + "}";

    public static Task SetBranchHoursAsync(this CompanySettingsDatabaseFixture db, Guid branchId, string json, string? timezone = null) =>
        db.ExecuteAsync(
            "UPDATE branches SET business_hours = CAST(@h AS jsonb), timezone = COALESCE(@tz, timezone) WHERE id = @b",
            ("h", json),
            ("tz", timezone),
            ("b", branchId));

    /// <summary>The UTC instant of the local midnight that starts the date in the zone.</summary>
    public static DateTimeOffset Midnight(DateOnly date, string zoneId = "UTC")
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);

        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue), zone), TimeSpan.Zero);
    }

    public static DateOnly TodayUtc(int offsetDays = 0) => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(offsetDays);

    public static string Text(this DateOnly date) => date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The first local date after <paramref name="after"/> whose midnight-to-midnight span is not 24 hours.</summary>
    public static DateOnly NextDstDay(string zoneId, DateOnly after)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);

        for (var date = after; date < after.AddDays(500); date = date.AddDays(1))
        {
            if (Midnight(date.AddDays(1), zoneId) - Midnight(date, zoneId) != TimeSpan.FromHours(24))
            {
                return date;
            }
        }

        throw new InvalidOperationException($"No DST change found for {zone.Id}.");
    }

    public static JsonObject DayBody(string name, string? start = null, string? end = null, bool? available = null) =>
        new()
        {
            ["dayOfWeek"] = name,
            ["isAvailable"] = available ?? start is not null,
            ["start"] = start,
            ["end"] = end,
        };

    /// <summary>Monday to Friday 09:00-17:00, weekend unavailable; <paramref name="change"/> replaces a day.</summary>
    public static JsonArray Week(Func<string, JsonObject?>? change = null) =>
        new([.. DayNames.Select(name => (JsonNode?)(change?.Invoke(name)
            ?? (name is "saturday" or "sunday" ? DayBody(name) : DayBody(name, "09:00", "17:00"))))]);

    public static JsonObject SaveBody(string updatedAt, JsonArray days) => new() { ["updatedAt"] = updatedAt, ["days"] = days };

    public static JsonObject Token(string updatedAt) => new() { ["updatedAt"] = updatedAt };

    public static async Task<JsonNode> GetDrawerAsync(CustomerHost host, TeamActor actor, Guid technicianId) =>
        await CustomerHost.ReadAsync(await host.SendAsync(HttpMethod.Get, $"/team/technicians/{technicianId}/availability", actor.Cookie));

    public static async Task<JsonNode> OkAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await CustomerHost.ReadAsync(response);
    }

    public static async Task<JsonNode> BadRequestAsync(HttpResponseMessage response, string key, string message)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await CustomerHost.ReadAsync(response);
        Assert.Equal(message, TeamSeed.Error(problem, key));

        return problem;
    }

    public static async Task AssertConflictAsync(HttpResponseMessage response, string message, string code)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var problem = await CustomerHost.ReadAsync(response);
        Assert.Equal((message, code), (problem["title"]!.GetValue<string>(), problem["code"]!.GetValue<string>()));
    }

    public static string[] States(JsonNode row) =>
        [.. row["days"]!.AsArray().Select(cell => cell!["state"]!.GetValue<string>())];

    public static JsonNode Row(JsonNode page, Guid technicianId) =>
        page["items"]!.AsArray().Single(item => item!["technicianId"]!.GetValue<Guid>() == technicianId)!;

    public static Guid[] Ids(JsonNode page) =>
        [.. page["items"]!.AsArray().Select(item => item!["technicianId"]!.GetValue<Guid>())];

    public static Task<string> AuditAsync(this CompanySettingsDatabaseFixture db, Guid organizationId, string action, string column = "metadata") =>
        // column is a test-controlled constant, never user input.
        db.ScalarAsync<string>(
            $"SELECT COALESCE(string_agg({column}::text, ' | ' ORDER BY id), '') FROM audit_logs WHERE organization_id = @o AND action = @a",
            ("o", organizationId),
            ("a", action));

    public static Task<string> SlotsAsync(this CompanySettingsDatabaseFixture db, Guid technicianId) =>
        db.ScalarAsync<string>(
            """
            SELECT COALESCE(string_agg(w.day_of_week || '@' || w.start_time || '-' || w.end_time || '#' || w.capacity_percent
                || COALESCE('[' || (SELECT string_agg(b.start_time || '-' || b.end_time, ';' ORDER BY b.start_time) FROM technician_breaks b WHERE b.availability_id = w.id) || ']', ''),
                ',' ORDER BY w.day_of_week, w.start_time), '')
            FROM technician_weekly_availability w WHERE w.technician_id = @t
            """,
            ("t", technicianId));
}
