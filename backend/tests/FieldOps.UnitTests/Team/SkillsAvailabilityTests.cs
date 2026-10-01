using System.Globalization;
using FieldOps.Application.Features.Team;
using FieldOps.Domain.Technicians;
using FieldOps.Domain.WorkOrders;

namespace FieldOps.UnitTests.Team;

/// <summary>BR-18, BR-19 derivations at a fixed clock and the weekly/exception validation tables (AC-08, AC-10, AC-14, AC-15).</summary>
public class SkillsAvailabilityTests
{
    private const string Zone = "America/Chicago";

    // Wednesday 2026-09-30 10:00 local (CDT, UTC-5).
    private static readonly DateTimeOffset Now = Utc("2026-09-30T15:00:00Z");

    private static DateTimeOffset Utc(string iso) =>
        DateTimeOffset.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

    private static AvailabilitySlot Day(int dow, int capacity = 100, bool withBreak = false) =>
        new(
            dow,
            new TimeOnly(8, 0),
            new TimeOnly(17, 0),
            capacity,
            withBreak ? [new TimeRange(new TimeOnly(12, 0), new TimeOnly(13, 0))] : []);

    // Monday to Friday 8-17; Wednesday has a 12-13 break.
    private static AvailabilitySlot[] WorkWeek() => [Day(1), Day(2), Day(3, withBreak: true), Day(4), Day(5)];

    private static TechnicianSchedule Schedule(
        IEnumerable<AvailabilitySlot> slots,
        IEnumerable<AvailabilityOverride>? exceptions = null,
        IEnumerable<AssignedVisit>? visits = null,
        string zone = Zone) =>
        new(TechnicianStatus.Active, zone, [.. slots], [.. exceptions ?? []], [.. visits ?? []]);

    private static AssignedVisit Visit(VisitStatus status, string startUtc, string endUtc) =>
        new(status, Utc(startUtc), Utc(endUtc), null);

    [Theory]
    [InlineData("2026-09-30T15:00:00Z", "none", UpcomingStates.AvailableNow, null)]
    [InlineData("2026-09-30T15:00:00Z", "InProgress", UpcomingStates.NextAvailable, "2026-09-30T16:00:00Z")]
    [InlineData("2026-09-30T15:00:00Z", "Completed", UpcomingStates.AvailableNow, null)]
    [InlineData("2026-09-30T15:00:00Z", "Cancelled", UpcomingStates.AvailableNow, null)]
    [InlineData("2026-09-30T17:30:00Z", "none", UpcomingStates.NextAvailable, "2026-09-30T18:00:00Z")]
    [InlineData("2026-09-30T23:00:00Z", "none", UpcomingStates.Unavailable, null)]
    public void UpcomingAvailability_TodayRowFollowsActiveAssignmentsAndBreaks(
        string nowUtc, string visitStatus, string expectedState, string? expectedNext)
    {
        // The visit runs 09:00-11:00 local when present; Wednesday's break is 12:00-13:00.
        var visits = visitStatus == "none"
            ? []
            : new[] { Visit(Enum.Parse<VisitStatus>(visitStatus), "2026-09-30T14:00:00Z", "2026-09-30T16:00:00Z") };

        var today = TechnicianAvailabilityCalculator.UpcomingAvailability(Schedule(WorkWeek(), visits: visits), Utc(nowUtc))[0];

        Assert.Equal("today", today.Label);
        Assert.Equal(expectedState, today.State);
        Assert.Equal(expectedNext is null ? null : Utc(expectedNext), today.NextAvailableAt);
    }

    [Fact]
    public void UpcomingAvailability_ListsTomorrowWindowsAndThreeReducedDatesOnly()
    {
        var exceptions = new[]
        {
            // Fri 10-02 all day off; Mon 10-05 13:00-17:00 off; Tue 10-06 extended only; Wed 10-07 08:00-12:00 off; Thu 10-08 all day off.
            new AvailabilityOverride(Utc("2026-10-02T05:00:00Z"), Utc("2026-10-03T05:00:00Z"), false),
            new AvailabilityOverride(Utc("2026-10-05T18:00:00Z"), Utc("2026-10-05T22:00:00Z"), false),
            new AvailabilityOverride(Utc("2026-10-06T22:00:00Z"), Utc("2026-10-07T00:00:00Z"), true),
            new AvailabilityOverride(Utc("2026-10-07T13:00:00Z"), Utc("2026-10-07T17:00:00Z"), false),
            new AvailabilityOverride(Utc("2026-10-08T05:00:00Z"), Utc("2026-10-09T05:00:00Z"), false),
        };

        var rows = TechnicianAvailabilityCalculator.UpcomingAvailability(Schedule(WorkWeek(), exceptions), Now);

        Assert.Equal(
            ["today", "tomorrow", "date", "date", "date"],
            rows.Select(row => row.Label));
        Assert.Equal(
            [new DateOnly(2026, 9, 30), new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 2), new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 7)],
            rows.Select(row => row.Date));

        // Tomorrow (Thu): one window. Fri: unavailable. Mon: limited morning. Wed: limited after the break.
        Assert.Equal([new TimeRange(new TimeOnly(8, 0), new TimeOnly(17, 0))], rows[1].Windows);
        Assert.Equal((UpcomingStates.Unavailable, false), (rows[2].State, rows[2].Limited));
        Assert.Equal((UpcomingStates.Windows, true, new TimeRange(new TimeOnly(8, 0), new TimeOnly(13, 0))), (rows[3].State, rows[3].Limited, rows[3].Windows.Single()));
        Assert.Equal(new TimeRange(new TimeOnly(13, 0), new TimeOnly(17, 0)), rows[4].Windows.Single());

        // A day with a break lists both windows when it is tomorrow (Wednesday seen from Tuesday).
        var tuesday = TechnicianAvailabilityCalculator.UpcomingAvailability(Schedule(WorkWeek()), Utc("2026-09-29T15:00:00Z"));
        Assert.Equal(
            [new TimeRange(new TimeOnly(8, 0), new TimeOnly(12, 0)), new TimeRange(new TimeOnly(13, 0), new TimeOnly(17, 0))],
            tuesday[1].Windows);
    }

    [Fact]
    public void SevenDayAndTodayLoad_ClipVisitsWeightCapacityAndHandleOverbookingAndDst()
    {
        var visits = new[]
        {
            Visit(VisitStatus.InProgress, "2026-09-30T14:00:00Z", "2026-09-30T16:00:00Z"),
            Visit(VisitStatus.Completed, "2026-10-01T13:00:00Z", "2026-10-01T15:00:00Z"),
            Visit(VisitStatus.Cancelled, "2026-10-01T16:00:00Z", "2026-10-01T18:00:00Z"),
            Visit(VisitStatus.Unscheduled, "2026-10-01T18:00:00Z", "2026-10-01T20:00:00Z"),

            // Crosses the start (05:00Z) and the end (Oct 7 05:00Z) of the local-day range: clipped to 120 minutes each.
            Visit(VisitStatus.Scheduled, "2026-09-30T03:00:00Z", "2026-09-30T07:00:00Z"),
            Visit(VisitStatus.Scheduled, "2026-10-07T03:00:00Z", "2026-10-07T07:00:00Z"),
        };

        var schedule = Schedule(WorkWeek(), visits: visits);
        var week = TechnicianAvailabilityCalculator.SevenDayLoad(schedule, Now);

        // Wed 8h (break excluded) + Thu, Fri, Mon, Tue 9h each.
        Assert.Equal((2640d, 480d, 2160d, 18), (week.AvailableMinutes, week.ScheduledMinutes, week.RemainingMinutes, week.UtilizationPercent));

        var today = TechnicianAvailabilityCalculator.TodayLoad(schedule, Now);
        Assert.Equal((480d, 240d, 2, 50), (today.AvailableMinutes, today.ScheduledMinutes, today.Jobs, today.UtilizationPercent));

        // Over-booking is not capped by the server; capacity percent weights the window.
        var overbooked = TechnicianAvailabilityCalculator.SevenDayLoad(
            Schedule([Day(4, capacity: 50)], visits: [Visit(VisitStatus.Scheduled, "2026-10-01T13:00:00Z", "2026-10-01T22:00:00Z")]), Now);
        Assert.Equal((270d, 540d, 0d, 200), (overbooked.AvailableMinutes, overbooked.ScheduledMinutes, overbooked.RemainingMinutes, overbooked.UtilizationPercent));

        // No availability: no utilization.
        var none = TechnicianAvailabilityCalculator.SevenDayLoad(
            Schedule([], visits: [Visit(VisitStatus.Scheduled, "2026-10-01T13:00:00Z", "2026-10-01T15:00:00Z")]), Now);
        Assert.Equal((0d, 120d), (none.AvailableMinutes, none.ScheduledMinutes));
        Assert.Null(none.UtilizationPercent);

        // Spring-forward Sunday 2026-03-08: a 01:00-04:00 local window lasts two real hours.
        var dst = TechnicianAvailabilityCalculator.SevenDayLoad(
            Schedule([new AvailabilitySlot(0, new TimeOnly(1, 0), new TimeOnly(4, 0), 100, [])]), Utc("2026-03-07T18:00:00Z"));
        Assert.Equal(120d, dst.AvailableMinutes);

        // Non-UTC zone east of UTC: Tokyo has no DST and its local "today" starts at 15:00Z the day before.
        var tokyo = TechnicianAvailabilityCalculator.TodayLoad(
            Schedule([Day(4)], zone: "Asia/Tokyo"), Utc("2026-10-01T00:30:00Z"));
        Assert.Equal(540d, tokyo.AvailableMinutes);
    }

    [Fact]
    public void Validators_ApplyWeeklySkillAndExceptionTables()
    {
        const string Version = "2026-10-01T10:00:00.0000000Z";
        var skill = Guid.NewGuid().ToString();
        var other = Guid.NewGuid().ToString();

        WeeklyDayInput Weekly(int dow = 1, string start = "08:00", string end = "17:00", string? breakStart = null, string? breakEnd = null) =>
            new(dow, start, end, breakStart, breakEnd);

        SkillAssignmentInput Assign(string id, int? level = 3, bool primary = false) => new(id, level, primary);

        var cases = new (WeeklyDayInput[] Days, SkillAssignmentInput[] Skills, string Key, string Message)[]
        {
            ([Weekly(start: "17:00", end: "08:00")], [], "weeklyAvailability[1].end", "End time must be after start time."),
            ([Weekly(start: "08:15")], [], "weeklyAvailability[1].start", "Choose a start and end time."),
            ([Weekly(end: "")], [], "weeklyAvailability[1].end", "Choose a start and end time."),
            ([Weekly(breakStart: "08:00", breakEnd: "09:00")], [], "weeklyAvailability[1].breakStart", "The break must fit inside the working hours."),
            ([Weekly(breakStart: "16:30", breakEnd: "17:00")], [], "weeklyAvailability[1].breakStart", "The break must fit inside the working hours."),
            ([Weekly(breakStart: "12:00", breakEnd: "13:30")], [], "weeklyAvailability[1].breakStart", "The break must fit inside the working hours."),
            ([Weekly(), Weekly()], [], "weeklyAvailability", "Choose each day only once."),
            ([], [Assign(skill)], "skills", "Choose one primary skill."),
            ([], [Assign(skill, primary: true), Assign(other, primary: true)], "skills", "Choose one primary skill."),
            ([], [Assign(skill, primary: true), Assign(skill)], "skills", "Choose each skill only once."),
            ([], [Assign(skill, level: null, primary: true)], "skills", "Choose a level for each skill."),
            ([], [Assign("nope", primary: true)], "skills", "Choose a skill from your organization."),
        };

        foreach (var (days, skills, key, message) in cases)
        {
            Assert.Null(SkillsAvailabilityRules.ValidateSave(new SaveSkillsAvailabilityInput(Version, days, skills), out var errors));
            Assert.Contains(message, errors[key]);
        }

        Assert.Null(SkillsAvailabilityRules.ValidateSave(new SaveSkillsAvailabilityInput("x", [], []), out var versionErrors));
        Assert.True(versionErrors.ContainsKey("version"));

        var valid = SkillsAvailabilityRules.ValidateSave(
            new SaveSkillsAvailabilityInput(Version, [Weekly(breakStart: "12:00", breakEnd: "13:00")], [Assign(skill, 5, true)]), out var none);
        Assert.Empty(none);
        Assert.Equal(new TimeRange(new TimeOnly(12, 0), new TimeOnly(13, 0)), valid!.Weekly.Single().Break);

        var today = new DateOnly(2026, 10, 1);

        ExceptionInput Exc(string? date = "2026-10-01", string? kind = "partial", string? start = "09:00", string? end = "11:00", string? reason = "Dentist") =>
            new(null, date, kind, start, end, reason);

        var exceptionCases = new (ExceptionInput Input, string Key, string Message)[]
        {
            (Exc(date: "2026-09-30"), "date", "Choose today or a future date."),
            (Exc(date: "10/01/2026"), "date", "Choose today or a future date."),
            (Exc(kind: "other"), "kind", "Choose an availability type."),
            (Exc(kind: "unavailable"), "start", "Times aren't allowed for an all-day exception."),
            (Exc(start: null), "start", "Choose a start and end time."),
            (Exc(end: "09:15"), "end", "Choose a start and end time."),
            (Exc(start: "11:00", end: "11:00"), "end", "End time must be after start time."),
            (Exc(reason: "  "), "reason", "Enter a reason."),
            (Exc(reason: new string('r', 201)), "reason", "Use 200 characters or fewer."),
        };

        foreach (var (input, key, message) in exceptionCases)
        {
            Assert.Null(SkillsAvailabilityRules.ValidateException(input, today, out var errors));
            Assert.Contains(message, errors[key]);
        }

        var allDay = SkillsAvailabilityRules.ValidateException(Exc(kind: "unavailable", start: null, end: null, reason: " Holiday "), today, out _);
        Assert.Equal("Holiday", allDay!.Reason);
        Assert.Null(allDay.Start);
    }
}
