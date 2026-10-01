using System.Globalization;
using FieldOps.Application.Features.Team;
using FieldOps.Domain.Technicians;
using FieldOps.Domain.WorkOrders;

namespace FieldOps.UnitTests.Team;

/// <summary>BR-03 to BR-07 derivations at a fixed clock. Chicago is UTC-5 in September (CDT).</summary>
public class TechnicianAvailabilityCalculatorTests
{
    private const string Zone = "America/Chicago";

    // Wednesday 2026-09-30 is the reference day; local time + 5h = UTC.
    private static DateTimeOffset Utc(string iso) =>
        DateTimeOffset.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

    private static AvailabilitySlot Day(int dow, int capacity = 100, bool withBreak = false) =>
        new(
            dow,
            new TimeOnly(8, 0),
            new TimeOnly(17, 0),
            capacity,
            withBreak ? [new TimeRange(new TimeOnly(12, 0), new TimeOnly(13, 0))] : []);

    private static TechnicianSchedule Schedule(
        IEnumerable<AvailabilitySlot>? slots = null,
        TechnicianStatus status = TechnicianStatus.Active,
        IEnumerable<AvailabilityOverride>? exceptions = null,
        IEnumerable<AssignedVisit>? visits = null) =>
        new(status, Zone, [.. slots ?? []], [.. exceptions ?? []], [.. visits ?? []]);

    private static AssignedVisit Visit(VisitStatus status, string startUtc, string endUtc) =>
        new(status, Utc(startUtc), Utc(endUtc), null);

    // Wednesday has a 12:00-13:00 local break.
    private static AvailabilitySlot[] WorkWeek() =>
        [Day(1), Day(2), Day(3, withBreak: true), Day(4), Day(5)];

    [Theory]
    [InlineData("2026-09-30T15:00:00Z", "none", "available")]
    [InlineData("2026-09-30T23:00:00Z", "none", "off")]
    [InlineData("2026-09-30T12:00:00Z", "none", "off")]
    [InlineData("2026-09-30T17:30:00Z", "none", "break")]
    [InlineData("2026-09-30T17:30:00Z", "job", "on_job")]
    [InlineData("2026-09-30T15:00:00Z", "timeoff+job", "time_off")]
    [InlineData("2026-09-30T15:00:00Z", "inactive+timeoff", "inactive")]
    [InlineData("2026-09-30T15:00:00Z", "suspended", "suspended")]
    [InlineData("2026-09-30T23:00:00Z", "addedwindow", "available")]
    public void ResolveStatus_FirstMatchWins(string nowUtc, string scenario, string expected)
    {
        var status = scenario.StartsWith("inactive", StringComparison.Ordinal) ? TechnicianStatus.Inactive
            : scenario.StartsWith("suspended", StringComparison.Ordinal) ? TechnicianStatus.Suspended
            : TechnicianStatus.Active;

        var exceptions = new List<AvailabilityOverride>();
        var visits = new List<AssignedVisit>();

        if (scenario.Contains("timeoff", StringComparison.Ordinal))
        {
            exceptions.Add(new AvailabilityOverride(Utc("2026-09-30T13:00:00Z"), Utc("2026-09-30T20:00:00Z"), false));
        }

        if (scenario.Contains("job", StringComparison.Ordinal))
        {
            visits.Add(Visit(VisitStatus.InProgress, "2026-09-30T14:00:00Z", "2026-09-30T19:00:00Z"));
        }

        if (scenario == "addedwindow")
        {
            exceptions.Add(new AvailabilityOverride(Utc("2026-09-30T22:00:00Z"), Utc("2026-10-01T00:00:00Z"), true));
        }

        var schedule = Schedule(WorkWeek(), status, exceptions, visits);

        Assert.Equal(expected, TechnicianAvailabilityCalculator.ResolveStatus(schedule, Utc(nowUtc)));
    }

    [Theory]
    [InlineData(100, "none", 480)]
    [InlineData(50, "none", 240)]
    [InlineData(100, "unavailable", 420)]
    [InlineData(100, "added-outside", 540)]
    [InlineData(100, "added-overlap", 540)]
    [InlineData(50, "added-outside", 300)]
    public void AvailableMinutes_SubtractsBreaksAndExceptionsAndWeightsCapacity(int capacity, string exception, double expected)
    {
        AvailabilityOverride[] exceptions = exception switch
        {
            "unavailable" => [new AvailabilityOverride(Utc("2026-09-30T19:00:00Z"), Utc("2026-09-30T20:00:00Z"), false)],
            "added-outside" => [new AvailabilityOverride(Utc("2026-09-30T22:00:00Z"), Utc("2026-09-30T23:00:00Z"), true)],
            "added-overlap" => [new AvailabilityOverride(Utc("2026-09-30T21:00:00Z"), Utc("2026-09-30T23:00:00Z"), true)],
            _ => [],
        };

        var schedule = Schedule([Day(3, capacity, withBreak: true)], exceptions: exceptions);

        // Local Wednesday 00:00 to Thursday 00:00. Window minutes weigh by capacity; added minutes count at 100 %.
        var minutes = TechnicianAvailabilityCalculator.AvailableMinutes(
            schedule, Utc("2026-09-30T05:00:00Z"), Utc("2026-10-01T05:00:00Z"));

        Assert.Equal(expected, minutes, 3);
    }

    [Theory]
    [InlineData(480, "assigned", 240, 50, "percent", false)]
    [InlineData(480, "assigned", 480, 100, "percent", true)]
    [InlineData(480, "completed", 600, 125, "percent", true)]
    [InlineData(480, "cancelled", 300, 0, "percent", false)]
    [InlineData(480, "unscheduled", 300, 0, "percent", false)]
    [InlineData(0, "assigned", 60, null, "no_availability", true)]
    [InlineData(0, "assigned", 0, null, "none", false)]
    public void PeriodWorkload_ComputesPercentNoAvailabilityAndCapacity(
        double available, string status, int scheduledMinutes, int? percent, string state, bool atCapacity)
    {
        var from = Utc("2026-09-30T05:00:00Z");
        var to = Utc("2026-10-01T05:00:00Z");
        var visitStatus = Enum.Parse<VisitStatus>(status, ignoreCase: true);
        IReadOnlyList<AssignedVisit> visits = scheduledMinutes == 0
            ? []
            : [new AssignedVisit(visitStatus, from.AddHours(3), from.AddHours(3).AddMinutes(scheduledMinutes), null)];

        var result = TechnicianAvailabilityCalculator.PeriodWorkloadFor(visits, from, to, available);

        Assert.Equal(state, result.State);
        Assert.Equal(percent, result.Percent);
        Assert.Equal(atCapacity, result.AtCapacity);

        // A visit starting outside the period is not counted; one running past its end is clipped.
        var outside = new AssignedVisit(VisitStatus.Assigned, to.AddMinutes(1), to.AddHours(2), null);
        Assert.Equal(0, TechnicianAvailabilityCalculator.PeriodWorkloadFor([outside], from, to, 480).Jobs);

        var crossing = new AssignedVisit(VisitStatus.Assigned, to.AddMinutes(-60), to.AddHours(2), null);
        Assert.Equal(60, TechnicianAvailabilityCalculator.PeriodWorkloadFor([crossing], from, to, 480).ScheduledMinutes);
    }

    [Theory]
    [InlineData("2026-09-30T15:00:00Z", "none", "now", null)]
    [InlineData("2026-09-30T15:00:00Z", "job-until-16", "time", "2026-09-30T16:00:00Z")]
    [InlineData("2026-09-30T12:30:00Z", "booked-all-day", "time", "2026-10-01T13:00:00Z")]
    [InlineData("2026-09-30T23:00:00Z", "none", "time", "2026-10-01T13:00:00Z")]
    [InlineData("2026-10-02T23:00:00Z", "none", "time", "2026-10-05T13:00:00Z")]
    [InlineData("2026-09-30T15:00:00Z", "no-slots", "none", null)]
    public void NextAvailable_ReturnsNowFreeTimeNextWindowOrNone(string nowUtc, string scenario, string kind, string? atUtc)
    {
        AssignedVisit[] visits = scenario switch
        {
            "job-until-16" => [Visit(VisitStatus.InProgress, "2026-09-30T14:00:00Z", "2026-09-30T16:00:00Z")],
            "booked-all-day" => [Visit(VisitStatus.Assigned, "2026-09-30T13:00:00Z", "2026-09-30T22:00:00Z")],
            _ => [],
        };

        var schedule = Schedule(scenario == "no-slots" ? [] : WorkWeek(), visits: visits);
        var result = TechnicianAvailabilityCalculator.Derive(schedule, TeamPeriod.Today, Utc(nowUtc)).NextAvailable;

        Assert.Equal(kind, result.Kind);
        Assert.Equal(atUtc is null ? null : Utc(atUtc), result.At);
    }

    [Fact]
    public void BranchTime_WeekRunsMondayToMondayInTheBranchZone()
    {
        var zone = BranchTime.FindZone(Zone);

        // Monday 00:30 UTC is still Sunday 19:30 in Chicago: the week started the previous Monday, local time.
        var week = BranchTime.Week(Utc("2026-10-05T00:30:00Z"), zone);
        Assert.Equal(Utc("2026-09-28T05:00:00Z"), week.Start);
        Assert.Equal(Utc("2026-10-05T05:00:00Z"), week.End);

        var day = BranchTime.Day(Utc("2026-10-05T00:30:00Z"), zone);
        Assert.Equal(Utc("2026-10-04T05:00:00Z"), day.Start);
        Assert.Equal(Utc("2026-10-05T05:00:00Z"), day.End);

        // Branch zone wins, then the organization zone, then UTC.
        Assert.Equal(Zone, BranchTime.ResolveZoneId(Zone, "Europe/Paris"));
        Assert.Equal("Europe/Paris", BranchTime.ResolveZoneId(null, "Europe/Paris"));
        Assert.Equal("UTC", BranchTime.ResolveZoneId("Not/AZone", null));

        // A visit on Sunday evening local time counts for the week that ends, not the one that starts.
        var visit = new AssignedVisit(VisitStatus.Assigned, Utc("2026-10-05T00:00:00Z"), Utc("2026-10-05T01:00:00Z"), null);
        Assert.Equal(1, TechnicianAvailabilityCalculator.PeriodWorkloadFor([visit], week.Start, week.End, 100).Jobs);
    }
}
