using System.Linq.Expressions;
using FieldOps.Domain.WorkOrders;

namespace FieldOps.Application.Features.WorkOrders;

public enum JobsTab
{
    All,
    Active,
    Unscheduled,
    Completed,
}

public enum JobsSort
{
    Newest,
    Oldest,
    Updated,
    Schedule,
    Priority,
}

/// <summary>The operational job status (jobs-management BR-05), derived in the database from the order and its current visit.</summary>
public enum JobStatus
{
    ReadyToSchedule,
    Scheduled,
    Assigned,
    OnTheWay,
    InProgress,
    Paused,
    Completed,
    NeedsCorrection,
    ApprovedForBilling,
    Cancelled,
}

public static class JobActions
{
    public const string View = "view";

    public const string ScheduleVisit = "schedule_visit";

    public const string OpenInSchedule = "open_in_schedule";

    public const string ReviewCompletion = "review_completion";
}

/// <summary>Pure rules of the jobs workspace: tabs, status and priority filters, sorting, row actions and the delay predicate.</summary>
public static class JobsRules
{
    public const int SearchMaxLength = 100;

    private static readonly TimeSpan DelayGrace = TimeSpan.FromMinutes(1);

    private static readonly JobStatus[] AllStatuses = Enum.GetValues<JobStatus>();

    public static bool TryParseTab(string? value, out JobsTab tab)
    {
        tab = JobsTab.All;

        switch (value)
        {
            case null or "" or "all":
                return true;
            case "active":
                tab = JobsTab.Active;

                return true;
            case "unscheduled":
                tab = JobsTab.Unscheduled;

                return true;
            case "completed":
                tab = JobsTab.Completed;

                return true;
            default:
                return false;
        }
    }

    public static bool TryParseSort(string? value, out JobsSort sort)
    {
        sort = JobsSort.Newest;

        switch (value)
        {
            case null or "" or "newest":
                return true;
            case "oldest":
                sort = JobsSort.Oldest;

                return true;
            case "updated":
                sort = JobsSort.Updated;

                return true;
            case "schedule":
                sort = JobsSort.Schedule;

                return true;
            case "priority":
                sort = JobsSort.Priority;

                return true;
            default:
                return false;
        }
    }

    /// <summary>BR-03: the work order statuses of a tab. All includes cancelled jobs; drafts never appear.</summary>
    public static WorkOrderStatus[] OrderStatuses(JobsTab tab) => tab switch
    {
        JobsTab.Active => [WorkOrderStatus.Scheduled, WorkOrderStatus.InProgress],
        JobsTab.Unscheduled => [WorkOrderStatus.ReadyToSchedule],
        JobsTab.Completed => [WorkOrderStatus.Completed, WorkOrderStatus.ApprovedForBilling],
        _ => [WorkOrderStatus.ReadyToSchedule, WorkOrderStatus.Scheduled, WorkOrderStatus.InProgress, WorkOrderStatus.Completed, WorkOrderStatus.ApprovedForBilling, WorkOrderStatus.Cancelled],
    };

    /// <summary>BR-06: the operational status codes a tab accepts.</summary>
    public static JobStatus[] TabStatuses(JobsTab tab) => tab switch
    {
        JobsTab.Active => [JobStatus.Scheduled, JobStatus.Assigned, JobStatus.OnTheWay, JobStatus.InProgress, JobStatus.Paused],
        JobsTab.Unscheduled => [JobStatus.ReadyToSchedule],
        JobsTab.Completed => [JobStatus.Completed, JobStatus.NeedsCorrection, JobStatus.ApprovedForBilling],
        _ => AllStatuses,
    };

    public static string StatusCode(JobStatus status) => status switch
    {
        JobStatus.ReadyToSchedule => "ready_to_schedule",
        JobStatus.Scheduled => "scheduled",
        JobStatus.Assigned => "assigned",
        JobStatus.OnTheWay => "on_the_way",
        JobStatus.InProgress => "in_progress",
        JobStatus.Paused => "paused",
        JobStatus.Completed => "completed",
        JobStatus.NeedsCorrection => "needs_correction",
        JobStatus.ApprovedForBilling => "approved_for_billing",
        _ => "cancelled",
    };

    /// <summary>A blank status means no filter; a code outside the tab's set is invalid (BR-06).</summary>
    public static bool TryParseStatus(string? value, JobsTab tab, out JobStatus? status)
    {
        status = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        foreach (var candidate in TabStatuses(tab))
        {
            if (string.Equals(StatusCode(candidate), value, StringComparison.Ordinal))
            {
                status = candidate;

                return true;
            }
        }

        return false;
    }

    /// <summary>BR-07: urgent 1, high 2, normal 3, low 4 – 5. A blank code means no filter.</summary>
    public static bool TryParsePriority(string? value, out (short Min, short Max)? range)
    {
        range = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        range = value switch
        {
            "urgent" => (1, 1),
            "high" => (2, 2),
            "normal" => (3, 3),
            "low" => (4, 5),
            _ => null,
        };

        return range is not null;
    }

    /// <summary>BR-14: the row actions in contract order. Eligibility only; the frontend builds the targets.</summary>
    public static IReadOnlyList<string> Actions(WorkOrderStatus status, bool hasCurrentVisit, bool hasSchedule, bool canManage)
    {
        var actions = new List<string> { JobActions.View };

        if (canManage && status == WorkOrderStatus.ReadyToSchedule && hasCurrentVisit)
        {
            actions.Add(JobActions.ScheduleVisit);
        }

        if (hasSchedule && status is not (WorkOrderStatus.ReadyToSchedule or WorkOrderStatus.Cancelled))
        {
            actions.Add(JobActions.OpenInSchedule);
        }

        if (status == WorkOrderStatus.Completed)
        {
            actions.Add(JobActions.ReviewCompletion);
        }

        return actions;
    }

    /// <summary>
    /// Operations-dashboard BR-07 as a database predicate: scheduled, assigned and on-the-way visits are delayed a
    /// full minute after the arrival window end (or the start); in-progress and paused ones a full minute after the end.
    /// </summary>
    public static Expression<Func<Visit, bool>> IsDelayed(DateTimeOffset now)
    {
        var limit = now - DelayGrace;

        return visit =>
            ((visit.Status == VisitStatus.Scheduled || visit.Status == VisitStatus.Assigned || visit.Status == VisitStatus.OnTheWay)
                && (visit.ArrivalWindowEnd ?? visit.ScheduledStart) <= limit)
            || ((visit.Status == VisitStatus.InProgress || visit.Status == VisitStatus.Paused)
                && visit.ScheduledEnd <= limit);
    }

    /// <summary>BR-11 Needs attention: delayed, or in <c>scheduled</c> (no technician).</summary>
    public static Expression<Func<Visit, bool>> NeedsAttention(DateTimeOffset now)
    {
        var delayed = IsDelayed(now);
        var visit = delayed.Parameters[0];
        var unassigned = Expression.Equal(Expression.Property(visit, nameof(Visit.Status)), Expression.Constant(VisitStatus.Scheduled));

        return Expression.Lambda<Func<Visit, bool>>(Expression.OrElse(unassigned, delayed.Body), visit);
    }
}
