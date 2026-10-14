using FieldOps.Application.Features.OperationsDashboard;
using FieldOps.Application.Features.WorkOrders;
using FieldOps.Domain.WorkOrders;

namespace FieldOps.UnitTests.WorkOrders;

public class JobsRulesTests
{
    // BR-06, BR-07, BR-09: tab statuses, per-tab status validation, priority ranges and sort parsing.
    [Fact]
    public void Parsers_TabStatusPrioritySort_FollowTheContract()
    {
        Assert.True(JobsRules.TryParseTab(null, out var tab));
        Assert.Equal(JobsTab.All, tab);
        Assert.True(JobsRules.TryParseTab("completed", out tab));
        Assert.Equal(JobsTab.Completed, tab);
        Assert.False(JobsRules.TryParseTab("1", out _));
        Assert.False(JobsRules.TryParseTab("ACTIVE", out _));

        Assert.True(JobsRules.TryParseSort("schedule", out var sort));
        Assert.Equal(JobsSort.Schedule, sort);
        Assert.False(JobsRules.TryParseSort("0", out _));

        Assert.True(JobsRules.TryParseStatus("assigned", JobsTab.Active, out var status));
        Assert.Equal(JobStatus.Assigned, status);
        Assert.True(JobsRules.TryParseStatus("cancelled", JobsTab.All, out status));
        Assert.True(JobsRules.TryParseStatus(null, JobsTab.Unscheduled, out status));
        Assert.Null(status);
        Assert.False(JobsRules.TryParseStatus("assigned", JobsTab.Completed, out _));
        Assert.False(JobsRules.TryParseStatus("cancelled", JobsTab.Active, out _));
        Assert.False(JobsRules.TryParseStatus("bogus", JobsTab.All, out _));

        Assert.True(JobsRules.TryParsePriority("low", out var range));
        Assert.Equal(((short)4, (short)5), range);
        Assert.True(JobsRules.TryParsePriority("urgent", out range));
        Assert.Equal(((short)1, (short)1), range);
        Assert.False(JobsRules.TryParsePriority("critical", out _));

        Assert.DoesNotContain(WorkOrderStatus.Cancelled, JobsRules.OrderStatuses(JobsTab.Active));
        Assert.Contains(WorkOrderStatus.Cancelled, JobsRules.OrderStatuses(JobsTab.All));
        Assert.DoesNotContain(WorkOrderStatus.Draft, JobsRules.OrderStatuses(JobsTab.All));
    }

    // BR-14: row actions by status, current visit, schedule and permission, in contract order.
    [Theory]
    [InlineData(WorkOrderStatus.ReadyToSchedule, true, false, true, "view,schedule_visit")]
    [InlineData(WorkOrderStatus.ReadyToSchedule, true, false, false, "view")]
    [InlineData(WorkOrderStatus.ReadyToSchedule, false, false, true, "view")]
    [InlineData(WorkOrderStatus.ReadyToSchedule, true, true, true, "view,schedule_visit")]
    [InlineData(WorkOrderStatus.Scheduled, true, true, true, "view,open_in_schedule")]
    [InlineData(WorkOrderStatus.Scheduled, true, true, false, "view,open_in_schedule")]
    [InlineData(WorkOrderStatus.InProgress, true, false, true, "view")]
    [InlineData(WorkOrderStatus.Completed, true, true, false, "view,open_in_schedule,review_completion")]
    [InlineData(WorkOrderStatus.ApprovedForBilling, true, true, true, "view,open_in_schedule")]
    [InlineData(WorkOrderStatus.Cancelled, true, true, true, "view")]
    public void Actions_ByStateScheduleAndPermission_AreEligibleInOrder(
        WorkOrderStatus status, bool hasVisit, bool hasSchedule, bool canManage, string expected) =>
        Assert.Equal(expected, string.Join(',', JobsRules.Actions(status, hasVisit, hasSchedule, canManage)));

    // BR-11 reuses operations-dashboard BR-07: the database predicate must agree with Classify around the one-minute boundary.
    [Theory]
    [InlineData(VisitStatus.Scheduled)]
    [InlineData(VisitStatus.Assigned)]
    [InlineData(VisitStatus.OnTheWay)]
    [InlineData(VisitStatus.InProgress)]
    [InlineData(VisitStatus.Paused)]
    [InlineData(VisitStatus.Completed)]
    [InlineData(VisitStatus.Cancelled)]
    public void IsDelayed_AroundTheLimit_MatchesDashboardClassify(VisitStatus status)
    {
        var start = new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.Zero);
        var end = start.AddHours(1);
        var windowEnd = start.AddMinutes(30);

        foreach (var window in new DateTimeOffset?[] { null, windowEnd })
        {
            var visit = Visit.Create(Guid.NewGuid(), Guid.NewGuid(), 1, start, end);

            if (window is { } found)
            {
                visit.ApplyDispatch(start, end, start.AddMinutes(-30), found, null, start);
            }

            visit.SetStatus(status);
            var limits = new[] { window ?? start, end };

            foreach (var limit in limits)
            {
                foreach (var seconds in new[] { -1, 0, 59, 60, 61, 3600 })
                {
                    var now = limit.AddSeconds(seconds);
                    var delayed = JobsRules.IsDelayed(now).Compile()(visit);
                    var expected = OperationsDashboardRules.Classify(status, start, end, window, now).IsDelayed;

                    Assert.True(expected == delayed, $"{status} window={window} now={now:O}: classify={expected} predicate={delayed}");
                }
            }

            // A scheduled visit needs attention even on time; a completed one never does.
            var early = JobsRules.NeedsAttention(start.AddHours(-2)).Compile();
            Assert.Equal(status == VisitStatus.Scheduled, early(visit));
        }
    }
}
