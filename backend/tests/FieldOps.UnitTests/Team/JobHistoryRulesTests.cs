using FieldOps.Application.Features.Team;
using FieldOps.Domain.WorkOrders;

namespace FieldOps.UnitTests.Team;

/// <summary>technician-profile-overview-history BR-13, BR-14 and BR-16 (AC-11, AC-13, AC-14).</summary>
public class JobHistoryRulesTests
{
    [Theory]
    [InlineData(VisitStatus.Cancelled, true, JobOutcome.Cancelled)]
    [InlineData(VisitStatus.Cancelled, false, JobOutcome.Cancelled)]
    [InlineData(VisitStatus.Completed, true, JobOutcome.Completed)]
    [InlineData(VisitStatus.NeedsCorrection, true, JobOutcome.Completed)]
    [InlineData(VisitStatus.Approved, true, JobOutcome.Completed)]
    [InlineData(VisitStatus.Approved, false, JobOutcome.Reassigned)]
    [InlineData(VisitStatus.Scheduled, false, JobOutcome.Reassigned)]
    [InlineData(VisitStatus.InProgress, true, null)]
    public void Classify_AppliesOutcomePrecedence(VisitStatus status, bool active, JobOutcome? expected) =>
        Assert.Equal(expected, JobHistoryRules.Classify(status, active));

    [Fact]
    public void BoundsAndSummary_UseLocalDaysAndOnlyCompletedRows()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");
        var now = DateTimeOffset.Parse("2026-03-09T03:30:00Z"); // Sunday 22:30 local, the day after the DST change

        Assert.Equal((new DateOnly(2026, 2, 7), new DateOnly(2026, 3, 8)), JobHistoryRules.Bounds(JobHistoryPeriod.Days30, now, zone));
        Assert.Equal((new DateOnly(2025, 3, 9), new DateOnly(2026, 3, 8)), JobHistoryRules.Bounds(JobHistoryPeriod.Months12, now, zone));
        Assert.Equal((null, new DateOnly(2026, 3, 8)), JobHistoryRules.Bounds(JobHistoryPeriod.All, now, zone));

        var start = DateTimeOffset.Parse("2026-03-01T15:00:00Z");
        var rows = new[]
        {
            new JobHistorySummaryRow(JobOutcome.Completed, 30, start, null, start.AddMinutes(15), start.AddMinutes(75), 0),
            new JobHistorySummaryRow(JobOutcome.Completed, 10, start, null, start.AddMinutes(16), start.AddMinutes(76), 600),
            new JobHistorySummaryRow(JobOutcome.Completed, 0, start, null, null, null, 0),
            new JobHistorySummaryRow(JobOutcome.Cancelled, 5, start, null, start.AddMinutes(1), start.AddMinutes(2), 0),
        };

        var summary = JobHistoryRules.Summarize(rows, now);

        Assert.Equal((3, 50, 45, 55), (summary.CompletedJobs, summary.OnTimePercent, summary.LoggedMinutes, summary.AvgDurationMinutes));
        Assert.Null(JobHistoryRules.Summarize([rows[3]], now).OnTimePercent);
    }
}
