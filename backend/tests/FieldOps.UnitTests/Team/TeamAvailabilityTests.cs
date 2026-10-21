using System.Globalization;
using FieldOps.Application.Features.Team;

namespace FieldOps.UnitTests.Team;

/// <summary>team-availability-skills BR-04, BR-05, BR-07, BR-09 and BR-14 rules (pure).</summary>
public class TeamAvailabilityTests
{
    private const string Chicago = "America/Chicago";

    private const string WorkWeekJson =
        """{"monday":{"start":"08:00","end":"17:00"},"tuesday":{"start":"08:00","end":"17:00"},"wednesday":{"start":"08:30","end":"12:00"},"saturday":{"start":"09:00","end":"09:00"},"sunday":"closed"}""";

    private static readonly TimeOnly Nine = new(9, 0);

    private static readonly TimeOnly Seventeen = new(17, 0);

    private static DateTimeOffset Utc(string iso) =>
        DateTimeOffset.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

    private static AvailabilitySlot Slot(int day, TimeOnly start, TimeOnly end, int capacity = 100) => new(day, start, end, capacity, []);

    // BR-07: inheriting maps the branch hours (Sunday = 0, capacity 100, no breaks, closed/invalid days absent); custom is unchanged.
    [Theory]
    [InlineData(true, WorkWeekJson, "1:08:00-17:00,2:08:00-17:00,3:08:30-12:00")]
    [InlineData(true, "{}", "")]
    [InlineData(true, "not json", "")]
    [InlineData(false, WorkWeekJson, "6:10:00-11:00@40")]
    public void Resolve_UsesBranchHoursOrOwnSlots(bool usesBranchHours, string json, string expected)
    {
        var own = new[] { new AvailabilitySlot(6, new TimeOnly(10, 0), new TimeOnly(11, 0), 40, [new TimeRange(new TimeOnly(10, 15), new TimeOnly(10, 30))]) };

        var slots = EffectiveWeeklyAvailability.Resolve(usesBranchHours, json, own);

        var text = string.Join(
            ',',
            slots.Select(slot => $"{slot.DayOfWeek}:{slot.Start:HH:mm}-{slot.End:HH:mm}" + (usesBranchHours ? string.Empty : $"@{slot.CapacityPercent}")));
        Assert.Equal(expected, text);
        Assert.All(slots.Where(_ => usesBranchHours), slot => Assert.True(slot.CapacityPercent == 100 && slot.Breaks.Count == 0));
        Assert.Equal(!usesBranchHours, ReferenceEquals(slots, own));
    }

    // BR-04, BR-05: precedence, earliest legacy window, partial and extended exceptions ignored, weekly minutes without breaks or capacity.
    [Fact]
    public void Build_AppliesTimeOffPrecedenceEarliestWindowAndWeeklyMinutes()
    {
        var monday = new DateOnly(2026, 9, 28);
        var slots = new[]
        {
            Slot(1, Nine, Seventeen, 50),
            Slot(1, new TimeOnly(7, 0), new TimeOnly(8, 0)),
            Slot(2, Nine, Seventeen),
            Slot(3, Nine, Seventeen),
            Slot(4, Nine, Seventeen),
            Slot(5, Nine, Seventeen),
            Slot(6, Nine, new TimeOnly(10, 30)),
        };

        // UTC zone: Tuesday is covered whole, Wednesday only partially, Thursday by an extended (available) exception.
        var exceptions = new[]
        {
            new AvailabilityOverride(Utc("2026-09-29T00:00:00Z"), Utc("2026-09-30T00:00:00Z"), false),
            new AvailabilityOverride(Utc("2026-09-30T10:00:00Z"), Utc("2026-09-30T12:00:00Z"), false),
            new AvailabilityOverride(Utc("2026-10-01T00:00:00Z"), Utc("2026-10-02T00:00:00Z"), true),
        };

        var row = TeamAvailabilityGrid.Build(monday, "UTC", slots, exceptions);

        Assert.Equal(
            ["available", "time_off", "available", "available", "available", "available", "unavailable"],
            row.Days.Select(cell => cell.State));
        Assert.Equal(new TimeOnly(10, 30), row.Days[5].End);
        Assert.Equal(new TimeOnly(7, 0), row.Days[0].Start);
        Assert.Equal(new TimeOnly(8, 0), row.Days[0].End);
        Assert.Null(row.Days[1].Start);
        Assert.Equal(60 + (3 * 480) + 90, row.WeeklyMinutes);
    }

    // BR-04, BR-07: a Mon-Fri 8-17 week is 45 h; time off spans the week boundary through a DST change (23 h Sunday) and a UTC row is unaffected.
    [Fact]
    public void Build_HandlesWeeklyHoursCrossWeekTimeOffAndDstWeek()
    {
        var inherited = EffectiveWeeklyAvailability.Resolve(
            true,
            """{"monday":{"start":"08:00","end":"17:00"},"tuesday":{"start":"08:00","end":"17:00"},"wednesday":{"start":"08:00","end":"17:00"},"thursday":{"start":"08:00","end":"17:00"},"friday":{"start":"08:00","end":"17:00"}}""",
            []);

        // 2027-03-08 is a Monday; US clocks change on Sunday 2027-03-14. Time off: Thursday 11 through Monday 15 local days.
        var week = new DateOnly(2027, 3, 8);
        var timeOff = new AvailabilityOverride(
            BranchTime.ToInstant(new DateOnly(2027, 3, 11), TimeOnly.MinValue, TimeZoneInfo.FindSystemTimeZoneById(Chicago)),
            BranchTime.ToInstant(new DateOnly(2027, 3, 16), TimeOnly.MinValue, TimeZoneInfo.FindSystemTimeZoneById(Chicago)),
            false);

        var plain = TeamAvailabilityGrid.Build(week, Chicago, inherited, []);
        var cut = TeamAvailabilityGrid.Build(week, Chicago, inherited, [timeOff]);
        var nextWeek = TeamAvailabilityGrid.Build(week.AddDays(7), Chicago, inherited, [timeOff]);

        Assert.Equal(45 * 60, plain.WeeklyMinutes);
        Assert.Equal(
            ["available", "available", "available", "time_off", "time_off", "time_off", "time_off"],
            cut.Days.Select(cell => cell.State));
        Assert.Equal(3 * 9 * 60, cut.WeeklyMinutes);
        Assert.Equal(
            ["time_off", "available", "available", "available", "available", "unavailable", "unavailable"],
            nextWeek.Days.Select(cell => cell.State));

        // The 23-hour Sunday is still one whole local day; the range helper agrees with the stored instants.
        var zone = TimeZoneInfo.FindSystemTimeZoneById(Chicago);
        Assert.True(TimeOffRules.TryFullDayRange(timeOff.StartsAt, timeOff.EndsAt, zone, out var start, out var end));
        Assert.Equal((new DateOnly(2027, 3, 11), new DateOnly(2027, 3, 15)), (start, end));
        Assert.False(TimeOffRules.TryFullDayRange(timeOff.StartsAt.AddHours(1), timeOff.EndsAt, zone, out _, out _));
    }

    // BR-09, BR-14: availability day and time off validation messages, table driven.
    [Fact]
    public void Validate_ReportsKeyedMessagesForAvailabilityDaysAndTimeOff()
    {
        static AvailabilityDayInput Day(string name, bool? on = true, string? start = "09:00", string? end = "17:00") => new(name, on, start, end);

        static AvailabilityDayInput[] Week(Func<string, AvailabilityDayInput?> change) =>
            [.. AvailabilityDays.Names.Select(name => change(name) ?? Day(name))];

        var cases = new (AvailabilityDayInput[] Days, string Key, string Message)[]
        {
            (Week(name => name == "monday" ? Day(name, start: null) : null), "days.monday.start", "This field is required."),
            (Week(name => name == "monday" ? Day(name, end: "25:00") : null), "days.monday.end", "Enter a valid value."),
            (Week(name => name == "monday" ? Day(name, start: "9:00") : null), "days.monday.start", "Enter a valid value."),
            (Week(name => name == "monday" ? Day(name, start: "17:00", end: "09:00") : null), "days.monday.end", "End time must be after start time."),
            (Week(name => name == "monday" ? Day(name, start: "23:30", end: "00:30") : null), "days.monday.end", "End time must be after start time."),
            (Week(name => name == "monday" ? Day(name, on: false) : null), "days.monday", "Enter a valid value."),
            (Week(name => name == "monday" ? Day(name, on: null) : null), "days.monday.isAvailable", "This field is required."),
            (Week(name => name == "sunday" ? Day("monday") : null), "days", "Enter a valid value."),
            (Week(_ => null)[..6], "days", "Enter a valid value."),
        };

        foreach (var (days, key, message) in cases)
        {
            var errors = new Dictionary<string, string[]>();

            Assert.Null(TeamAvailabilityRules.ValidateDays(days, errors));
            Assert.Equal(message, errors[key].Single());
        }

        var valid = new Dictionary<string, string[]>();
        var parsed = TeamAvailabilityRules.ValidateDays(
            Week(name => name is "saturday" or "sunday" ? Day(name, on: false, start: null, end: null) : name == "friday" ? Day(name, start: "00:00", end: "23:59") : null),
            valid)!;

        Assert.Empty(valid);
        Assert.Equal(7, parsed.Count);
        Assert.Equal(new TimeOnly(23, 59), parsed.Single(day => day.DayOfWeek == 5).End);
        Assert.Equal(0, parsed[^1].DayOfWeek);

        var today = new DateOnly(2026, 10, 21);
        var timeOffCases = new (CreateTimeOffInput Input, string Key, string Message)[]
        {
            (new("2026-10-20", "2026-10-22", null), "startDate", "Choose today or a future date."),
            (new("2026-10-22", "2026-10-21", null), "endDate", "End date can't be before start date."),
            (new("2026-10-22", "2026-10-22", new string('n', 251)), "note", "Use 250 characters or fewer."),
            (new(null, "2026-10-22", null), "startDate", "This field is required."),
            (new("2026-10-22", "22/10/2026", null), "endDate", "Enter a valid value."),
        };

        foreach (var (input, key, message) in timeOffCases)
        {
            Assert.Null(TeamAvailabilityRules.ValidateTimeOff(input, today, out var errors));
            Assert.Equal(message, errors[key].Single());
        }

        var accepted = TeamAvailabilityRules.ValidateTimeOff(new("2026-10-21", "2026-10-21", "   "), today, out _)!;
        Assert.Equal((today, today, (string?)null), (accepted.StartDate, accepted.EndDate, accepted.Note));
        Assert.Equal("Dentist", TeamAvailabilityRules.ValidateTimeOff(new("2026-10-21", "2026-10-23", "  Dentist "), today, out _)!.Note);
    }
}
