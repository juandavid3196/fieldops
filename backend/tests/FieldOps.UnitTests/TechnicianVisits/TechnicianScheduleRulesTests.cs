using System.Globalization;
using FieldOps.Application.Features.Team;
using FieldOps.Application.Features.TechnicianVisits;
using FieldOps.Domain.WorkOrders;

namespace FieldOps.UnitTests.TechnicianVisits;

/// <summary>Week boundaries, weekStart validation and day totals (technician-schedule-active-visits BR-02, BR-04) and the cancel decision.</summary>
public class TechnicianScheduleRulesTests
{
    // The US change of 2026-03-08 makes the week 167 hours long; Europe/Madrid changes on 2026-03-29 (week of 03-23).
    [Theory]
    [InlineData("America/New_York", "2026-03-02", "2026-03-02T05:00:00Z", "2026-03-09T04:00:00Z")]
    [InlineData("America/New_York", "2026-10-26", "2026-10-26T04:00:00Z", "2026-11-02T05:00:00Z")]
    [InlineData("Europe/Madrid", "2026-03-23", "2026-03-22T23:00:00Z", "2026-03-29T22:00:00Z")]
    [InlineData("Pacific/Auckland", "2026-03-02", "2026-03-01T11:00:00Z", "2026-03-08T11:00:00Z")]
    [InlineData("UTC", "2026-03-02", "2026-03-02T00:00:00Z", "2026-03-09T00:00:00Z")]
    public void WeekOf_UsesLocalMidnightsOfTheZone_AlsoAcrossDaylightSavingChanges(string zone, string monday, string start, string end)
    {
        var (from, to) = BranchTime.WeekOf(DateOnly.ParseExact(monday, "yyyy-MM-dd", CultureInfo.InvariantCulture), BranchTime.FindZone(zone));

        Assert.Equal(DateTimeOffset.Parse(start, CultureInfo.InvariantCulture), from);
        Assert.Equal(DateTimeOffset.Parse(end, CultureInfo.InvariantCulture), to);
    }

    [Theory]
    [InlineData("2000-01-03", true)]
    [InlineData("2099-12-28", true)]
    [InlineData("2026-03-02", true)]
    [InlineData("2026-03-03", false)]
    [InlineData("1999-12-27", false)]
    [InlineData("2100-01-04", false)]
    [InlineData("2026-02-30", false)]
    [InlineData("03/02/2026", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void TryParseWeekStart_AcceptsOnlyMondaysInTheSupportedRange(string? text, bool valid) =>
        Assert.Equal(valid, GetTechnicianScheduleHandler.TryParseWeekStart(text, out _));

    [Fact]
    public void Days_CountOnlyTheVisitsOfEachLocalDayAndLeaveOutCancelledOnes()
    {
        var zone = BranchTime.FindZone("America/New_York");

        ScheduleVisit Visit(string startIso, string status, int minutes) =>
            new(
                Guid.NewGuid(),
                "WO-1",
                "Job",
                status,
                DateTimeOffset.Parse(startIso, CultureInfo.InvariantCulture),
                DateTimeOffset.Parse(startIso, CultureInfo.InvariantCulture).AddMinutes(minutes),
                minutes,
                "Customer",
                "person",
                new ScheduleAddress("1 Main St", "Austin", null, null),
                false);

        var days = GetTechnicianScheduleHandler.Days(
            new DateOnly(2026, 3, 2),
            [
                Visit("2026-03-09T03:59:00Z", "assigned", 15),
                Visit("2026-03-03T14:00:00Z", "assigned", 60),
                Visit("2026-03-03T16:00:00Z", "cancelled", 30),
                Visit("2026-03-03T18:00:00Z", "completed", 90),
            ],
            zone);

        Assert.Equal(7, days.Count);
        Assert.Equal([0, 2, 0, 0, 0, 0, 1], days.Select(day => day.VisitCount).ToArray());
        Assert.Equal([0, 150, 0, 0, 0, 0, 15], days.Select(day => day.ScheduledMinutes).ToArray());
    }

    [Theory]
    [InlineData(VisitStatus.Scheduled, true)]
    [InlineData(VisitStatus.Assigned, true)]
    [InlineData(VisitStatus.OnTheWay, true)]
    [InlineData(VisitStatus.InProgress, true)]
    [InlineData(VisitStatus.Paused, true)]
    [InlineData(VisitStatus.Unscheduled, false)]
    [InlineData(VisitStatus.Completed, false)]
    [InlineData(VisitStatus.NeedsCorrection, false)]
    [InlineData(VisitStatus.Approved, false)]
    [InlineData(VisitStatus.Cancelled, false)]
    public void IsCancellable_AllowsOnlyTheFiveOpenStatuses(VisitStatus status, bool expected) =>
        Assert.Equal(expected, Visit.IsCancellable(status));
}
