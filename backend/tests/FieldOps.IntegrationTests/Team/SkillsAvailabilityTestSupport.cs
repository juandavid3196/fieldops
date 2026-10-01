using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.CompanySettings;

namespace FieldOps.IntegrationTests.Team;

/// <summary>Seed and request helpers of the skills and availability tests.</summary>
public static class SkillsAvailabilitySeed
{
    public static string Today(int offsetDays = 0) =>
        DateOnly.FromDateTime(DateTime.UtcNow).AddDays(offsetDays).ToString("yyyy-MM-dd");

    public static DateTimeOffset At(int offsetDays, string time) =>
        DateTimeOffset.Parse($"{Today(offsetDays)}T{time}:00Z", System.Globalization.CultureInfo.InvariantCulture);

    public static Task SeedBreakAsync(
        this CompanySettingsDatabaseFixture db, Guid slotId, string start, string end) =>
        db.ExecuteAsync(
            "INSERT INTO technician_breaks (availability_id, start_time, end_time) VALUES (@s, @a::time, @b::time)",
            ("s", slotId),
            ("a", start),
            ("b", end));

    public static async Task<Guid> SeedExceptionAsync(
        this CompanySettingsDatabaseFixture db,
        Guid technicianId,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt,
        bool available = false,
        string reason = "Seeded reason",
        string status = "active")
    {
        var id = Guid.NewGuid();

        await db.ExecuteAsync(
            "INSERT INTO technician_exceptions (id, technician_id, starts_at, ends_at, is_available, reason, status) VALUES (@id, @t, @s, @e, @a, @r, @st)",
            ("id", id),
            ("t", technicianId),
            ("s", startsAt),
            ("e", endsAt),
            ("a", available),
            ("r", reason),
            ("st", status));

        return id;
    }

    public static Task<DateTimeOffset> ProfileVersionAsync(this CompanySettingsDatabaseFixture db, Guid technicianId) =>
        db.ScalarAsync<DateTimeOffset>("SELECT updated_at FROM technician_profiles WHERE id = @t", ("t", technicianId));

    public static Task<long> CountAsync(this CompanySettingsDatabaseFixture db, string table, string column, Guid id) =>
        // table and column are test-controlled constants, never user input.
        db.ScalarAsync<long>($"SELECT COUNT(*) FROM {table} WHERE {column} = @id", ("id", id));

    public static JsonObject Day(int dayOfWeek, string start = "09:00", string end = "17:00", string? breakStart = null, string? breakEnd = null) =>
        new()
        {
            ["dayOfWeek"] = dayOfWeek,
            ["start"] = start,
            ["end"] = end,
            ["breakStart"] = breakStart,
            ["breakEnd"] = breakEnd,
        };

    public static JsonObject Assign(Guid skillId, int? level, bool primary) =>
        new()
        {
            ["skillId"] = skillId.ToString(),
            ["proficiency"] = level,
            ["isPrimary"] = primary,
        };

    public static JsonObject SaveBody(string version, IEnumerable<JsonObject> weekly, IEnumerable<JsonObject> skills) =>
        new()
        {
            ["version"] = version,
            ["weeklyAvailability"] = new JsonArray([.. weekly]),
            ["skills"] = new JsonArray([.. skills]),
        };

    public static JsonObject ExceptionBody(
        string date, string kind, string? start, string? end, string reason = "Doctor appointment", string? version = null) =>
        new()
        {
            ["version"] = version,
            ["date"] = date,
            ["kind"] = kind,
            ["start"] = start,
            ["end"] = end,
            ["reason"] = reason,
        };

    /// <summary>Copies the weekly rows of a GET response into request rows (capacity is not sent).</summary>
    public static IEnumerable<JsonObject> WeeklyFrom(JsonNode page) =>
        page["weeklyAvailability"]!.AsArray().Select(row => Day(
            row!["dayOfWeek"]!.GetValue<int>(),
            row["start"]!.GetValue<string>(),
            row["end"]!.GetValue<string>(),
            row["breakStart"]?.GetValue<string>(),
            row["breakEnd"]?.GetValue<string>()));

    public static IEnumerable<JsonObject> SkillsFrom(JsonNode page) =>
        page["skills"]!.AsArray().Select(row => Assign(
            Guid.Parse(row!["skillId"]!.GetValue<string>()),
            row["proficiency"]!.GetValue<int>(),
            row["isPrimary"]!.GetValue<bool>()));
}
