using System.Globalization;
using FieldOps.Application.Features.OperationsDashboard;
using FieldOps.Application.Features.Team;
using FieldOps.Domain.Technicians;
using FieldOps.Domain.WorkOrders;

namespace FieldOps.UnitTests.OperationsDashboard;

/// <summary>Delay, risk, reference, on-time arrival, timing and next-available rules (operations-dashboard BR-04, BR-07, BR-09 to BR-12; AC-07, AC-09, AC-10, AC-12).</summary>
public class OperationsDashboardRulesTests
{
    // Wednesday 2026-09-30, UTC.
    private static DateTimeOffset At(string time, string day = "2026-09-30") =>
        DateTimeOffset.Parse($"{day}T{time}Z", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

    private static DateTimeOffset? AtOrNull(string? time) => time is null ? null : At(time);

    // BR-07: delay is whole minutes rounded down against the arrival window end (or start) before the visit starts, against the scheduled end after.
    [Theory]
    [InlineData(VisitStatus.Scheduled, "11:00:00", "12:00:00", null, true, 60, false)]
    [InlineData(VisitStatus.Scheduled, "11:00:00", "12:00:00", "12:30:00", false, null, false)]
    [InlineData(VisitStatus.Assigned, "11:00:00", "12:00:00", "11:30:00", true, 30, false)]
    [InlineData(VisitStatus.Assigned, "11:59:30", "13:00:00", null, false, null, false)]
    [InlineData(VisitStatus.Scheduled, "11:59:30", "13:00:00", null, false, null, false)]
    [InlineData(VisitStatus.Scheduled, "12:00:00", "13:00:00", null, false, null, true)]
    [InlineData(VisitStatus.Scheduled, "13:00:00", "14:00:00", null, false, null, true)]
    [InlineData(VisitStatus.Scheduled, "13:01:00", "14:00:00", null, false, null, false)]
    [InlineData(VisitStatus.Assigned, "12:30:00", "14:00:00", null, false, null, false)]
    [InlineData(VisitStatus.OnTheWay, "11:00:00", "12:00:00", "11:30:00", true, 30, false)]
    [InlineData(VisitStatus.InProgress, "10:00:00", "11:30:00", "10:15:00", true, 30, false)]
    [InlineData(VisitStatus.Paused, "10:00:00", "11:30:00", null, true, 30, false)]
    [InlineData(VisitStatus.InProgress, "10:00:00", "12:00:00", null, false, null, false)]
    [InlineData(VisitStatus.Completed, "10:00:00", "11:00:00", null, false, null, false)]
    public void Classify_AtReference_FollowsDelayAndRiskBoundaries(
        VisitStatus status, string start, string end, string? window, bool delayed, int? minutes, bool atRisk)
    {
        var timing = OperationsDashboardRules.Classify(status, At(start), At(end), AtOrNull(window), At("12:00:00"));

        Assert.Equal(delayed, timing.IsDelayed);
        Assert.Equal(minutes, timing.DelayMinutes);
        Assert.Equal(atRisk, timing.IsAtRisk);
    }

    // BR-04: the reference instant is now (today), the end of a past day or the start of a future day; a future day has no delays.
    [Fact]
    public void Reference_AndFutureDay_NothingIsDelayed()
    {
        var today = new DateOnly(2026, 9, 30);
        var now = At("12:00:00");
        var start = At("00:00:00", "2026-10-02");
        var end = At("00:00:00", "2026-10-03");

        Assert.Equal(now, OperationsDashboardRules.Reference(today, today, now, At("00:00:00"), At("00:00:00", "2026-10-01")));
        Assert.Equal(At("00:00:00", "2026-10-01"), OperationsDashboardRules.Reference(new DateOnly(2026, 9, 30), new DateOnly(2026, 10, 5), now, At("00:00:00"), At("00:00:00", "2026-10-01")));
        Assert.Equal(start, OperationsDashboardRules.Reference(new DateOnly(2026, 10, 2), today, now, start, end));

        var future = OperationsDashboardRules.Classify(
            VisitStatus.Scheduled, At("00:30:00", "2026-10-02"), At("01:30:00", "2026-10-02"), null, start);

        Assert.False(future.IsDelayed);
        Assert.True(future.IsAtRisk);
    }

    // BR-09: window end or start + 15 minutes; eligible = started, or unstarted scheduled/assigned/on the way past the limit.
    [Fact]
    public void OnTime_UsesWindowOrGraceAndCountsUnstartedPastLimit()
    {
        DashboardArrival Visit(VisitStatus status, string start, string? window, string? started) =>
            new(status, At(start), AtOrNull(window), AtOrNull(started));

        DashboardArrival[] visits =
        [
            Visit(VisitStatus.Completed, "09:00", null, "09:15"),
            Visit(VisitStatus.Completed, "09:00", null, "09:16"),
            Visit(VisitStatus.InProgress, "09:00", "10:00", "09:50"),
            Visit(VisitStatus.Scheduled, "09:00", null, null),
            Visit(VisitStatus.Assigned, "09:00", "10:00", null),
            Visit(VisitStatus.Completed, "09:00", null, null),
        ];

        Assert.Equal((3, 2), OperationsDashboardRules.OnTime(visits, At("09:15")));
        Assert.Equal((4, 2), OperationsDashboardRules.OnTime(visits, At("09:16")));
        Assert.Equal((5, 2), OperationsDashboardRules.OnTime(visits, At("10:01")));
        Assert.Equal((0, 0), OperationsDashboardRules.OnTime([], At("10:01")));
        Assert.Equal(13, OperationsDashboardRules.Percent(1, 8));
        Assert.Null(OperationsDashboardRules.Percent(0, 0));
    }

    // BR-10 projected end and BR-11 over-estimate minutes.
    [Theory]
    [InlineData(VisitStatus.InProgress, "10:00", 600, 120, "12:50", 40)]
    [InlineData(VisitStatus.Paused, "10:00", 600, 120, "12:09", null)]
    [InlineData(VisitStatus.InProgress, "10:00", 0, 120, "12:00:30", null)]
    [InlineData(VisitStatus.Scheduled, "10:00", 0, 30, "13:00", null)]
    public void ElapsedWork_ProjectsEndAndOverEstimate(
        VisitStatus status, string started, int pauseSeconds, int estimated, string reference, int? over)
    {
        Assert.Equal(over, OperationsDashboardRules.OverEstimateMinutes(status, At(started), pauseSeconds, estimated, At(reference)));
        Assert.Equal(
            status is VisitStatus.InProgress or VisitStatus.Paused ? At(started).AddSeconds(pauseSeconds) + TimeSpan.FromHours(2) : (DateTimeOffset?)null,
            OperationsDashboardRules.ProjectedEnd(status, At(started), pauseSeconds, At("09:00"), At("11:00")));
    }

    // BR-12: next free time today across breaks, time off, visits and assessments; null when available now or nothing is left today.
    [Theory]
    [InlineData("12:30", "break", "13:00")]
    [InlineData("12:30", "break_assessment", "14:00")]
    [InlineData("10:00", "time_off", "11:30")]
    [InlineData("09:00", "on_job", "10:00")]
    [InlineData("12:30", "completed_after_break", "13:00")]
    [InlineData("12:30", "needs_correction_after_break", "13:00")]
    [InlineData("12:30", "assigned_after_break", "15:00")]
    [InlineData("07:00", "off_before_shift", "08:00")]
    [InlineData("18:00", "off_after_shift", null)]
    [InlineData("09:00", "available", null)]
    public void NextAvailableToday_AcrossBreaksTimeOffAndVisits(string now, string scenario, string? expected)
    {
        var visits = new List<AssignedVisit>();
        var exceptions = new List<AvailabilityOverride>();
        var assessments = new List<DashboardInterval>();

        switch (scenario)
        {
            case "break_assessment":
                assessments.Add(new DashboardInterval(At("13:00"), At("14:00")));
                break;
            case "time_off":
                exceptions.Add(new AvailabilityOverride(At("09:00"), At("11:30"), false));
                break;
            case "on_job":
                visits.Add(new AssignedVisit(VisitStatus.InProgress, At("08:30"), At("10:00"), null));
                break;
            case "completed_after_break":
                visits.Add(new AssignedVisit(VisitStatus.Completed, At("13:00"), At("15:00"), null));
                break;
            case "needs_correction_after_break":
                visits.Add(new AssignedVisit(VisitStatus.NeedsCorrection, At("13:00"), At("15:00"), null));
                break;
            case "assigned_after_break":
                visits.Add(new AssignedVisit(VisitStatus.Assigned, At("13:00"), At("15:00"), null));
                break;
        }

        AvailabilitySlot[] slots =
        [
            .. Enumerable.Range(0, 7).Select(day => new AvailabilitySlot(
                day, new TimeOnly(8, 0), new TimeOnly(17, 0), 100, [new TimeRange(new TimeOnly(12, 0), new TimeOnly(13, 0))])),
        ];
        var schedule = new TechnicianSchedule(TechnicianStatus.Active, "UTC", slots, exceptions, visits);
        var status = TechnicianAvailabilityCalculator.ResolveStatus(schedule, At(now));

        Assert.Equal(AtOrNull(expected), OperationsDashboardRules.NextAvailableToday(schedule, assessments, status, At(now)));
    }
}
