using System.Text.Json;
using System.Text.Json.Nodes;
using FieldOps.Application.Features.Branches;

namespace FieldOps.UnitTests.Branches;

/// <summary>business-hours BR-01 to BR-05 validation/canonicalization and BR-12 classification (AC-07, AC-16, AC-22).</summary>
public class BusinessHoursRulesTests
{
    private const string Canonical =
        """{"monday":{"start":"08:00","end":"17:00"},"tuesday":{"start":"08:00","end":"17:00"},"wednesday":{"start":"08:00","end":"17:00"},"thursday":{"start":"08:00","end":"17:00"},"friday":{"start":"08:00","end":"17:00"},"saturday":{"start":"08:15","end":"12:00"}}""";

    public static TheoryData<string, string, string> ValidationCases()
    {
        static JsonArray Week()
        {
            string[] names = ["monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday"];

            return new JsonArray([.. names.Select(name => (JsonNode?)Day(
                name,
                name != "sunday",
                name == "saturday" ? "08:15" : name == "sunday" ? null : "08:00",
                name == "saturday" ? "12:00" : name == "sunday" ? null : "17:00"))]);
        }

        static JsonObject Day(string name, bool open, string? opening, string? closing) =>
            new() { ["dayOfWeek"] = name, ["isOpen"] = open, ["openingTime"] = opening, ["closingTime"] = closing };

        static string Mutate(Action<JsonArray> change)
        {
            var week = Week();
            change(week);

            return week.ToJsonString();
        }

        return new TheoryData<string, string, string>
        {
            { "valid week keeps off-step 08:15 and omits closed days", Week().ToJsonString(), $"OK|{Canonical}" },
            { "six days", Mutate(w => w.RemoveAt(6)), "days|Enter a valid value." },
            { "duplicate day", Mutate(w => w[6]!["dayOfWeek"] = "monday"), "days|Enter a valid value." },
            { "unknown day", Mutate(w => w[0]!["dayOfWeek"] = "funday"), "days|Enter a valid value." },
            { "uppercase day", Mutate(w => w[0]!["dayOfWeek"] = "Monday"), "days|Enter a valid value." },
            { "extra property", Mutate(w => w[0]!["note"] = "x"), "days|Enter a valid value." },
            { "open without opening", Mutate(w => w[0]!["openingTime"] = null), "days.monday.openingTime|This field is required." },
            { "malformed closing", Mutate(w => w[0]!["closingTime"] = "5pm"), "days.monday.closingTime|Enter a valid value." },
            { "24:00 is out of range", Mutate(w => w[0]!["closingTime"] = "24:00"), "days.monday.closingTime|Enter a valid value." },
            { "closing equals opening", Mutate(w => w[0]!["closingTime"] = "08:00"), "days.monday.closingTime|Closing time must be after opening time." },
            { "closed day with a time", Mutate(w => w[6]!["openingTime"] = "08:00"), "days.sunday|Enter a valid value." },
        };
    }

    [Theory]
    [MemberData(nameof(ValidationCases))]
    public void TryValidate_SevenDayTable_ReportsKeyedErrorsOrCanonicalStorageThatRoundTrips(
        string name, string daysJson, string expected)
    {
        var errors = new List<string>();
        using var document = JsonDocument.Parse(daysJson);

        var valid = BusinessHoursRules.TryValidate(
            document.RootElement, (key, message) => errors.Add($"{key}|{message}"), out var days, out var canonical);

        if (expected.StartsWith("OK|", StringComparison.Ordinal))
        {
            Assert.True(valid, name);
            Assert.Equal(expected[3..], canonical);
            Assert.Equal(days, BusinessHoursRules.FromStored(canonical));
            Assert.Equal(["monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday"], days.Select(day => day.DayOfWeek));
            Assert.False(days[6].IsOpen);
            Assert.Null(days[6].OpeningTime);

            return;
        }

        Assert.False(valid, name);
        Assert.Equal([expected], errors);
    }

    [Theory]
    [InlineData("UTC", "2026-11-02T16:00:00Z", "2026-11-02T18:00:00Z", true, "monday", true)]
    [InlineData("UTC", "2026-11-02T07:00:00Z", "2026-11-02T09:00:00Z", true, "monday", true)]
    [InlineData("UTC", "2026-11-02T09:00:00Z", "2026-11-02T10:00:00Z", false, "monday", true)]
    [InlineData("UTC", "2026-11-02T08:00:00Z", "2026-11-02T17:00:00Z", false, "monday", true)]
    [InlineData("UTC", "2026-11-01T10:00:00Z", "2026-11-01T11:00:00Z", true, "sunday", false)]
    [InlineData("America/Chicago", "2026-11-02T14:00:00Z", "2026-11-02T15:00:00Z", false, "monday", true)]
    [InlineData("America/Chicago", "2026-11-03T01:00:00Z", "2026-11-03T02:00:00Z", true, "monday", true)]
    public void Check_ClassifiesSlotInBranchLocalTime(
        string timezone, string start, string end, bool outside, string day, bool isOpen)
    {
        const string stored = """{"monday":{"start":"08:00","end":"17:00"}}""";

        var check = BusinessHoursRules.Check(stored, timezone, DateTimeOffset.Parse(start), DateTimeOffset.Parse(end));

        Assert.NotNull(check);
        Assert.Equal(outside, check.Outside);
        Assert.Equal(day, check.DayOfWeek);
        Assert.Equal(isOpen, check.IsOpen);
        Assert.Equal(isOpen ? "08:00" : null, check.OpeningTime);
        Assert.Equal(isOpen ? "17:00" : null, check.ClosingTime);
        Assert.Null(BusinessHoursRules.Check(stored, null, DateTimeOffset.Parse(start), DateTimeOffset.Parse(end)));
    }
}
