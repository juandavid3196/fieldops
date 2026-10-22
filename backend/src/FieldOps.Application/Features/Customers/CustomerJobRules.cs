using FieldOps.Application.Features.WorkOrders;
using FieldOps.Domain.WorkOrders;

namespace FieldOps.Application.Features.Customers;

/// <summary>A visit reduced to what the Jobs tab derives from (BR-07).</summary>
public sealed record CustomerJobVisit(
    Guid Id,
    int VisitNumber,
    VisitStatus Status,
    DateTimeOffset? ScheduledStart,
    DateTimeOffset? ScheduledEnd,
    DateTimeOffset? ActualCompletedAt);

/// <summary>
/// Pure derivations of one job row: the current visit (jobs-management BR-04), the operational status (BR-05) and the
/// Next visit column (customer-related-records BR-07).
/// </summary>
public static class CustomerJobRules
{
    /// <summary>The lowest-numbered open visit, else the highest non-cancelled one, else the highest.</summary>
    public static CustomerJobVisit? CurrentVisit(IReadOnlyList<CustomerJobVisit> visits)
    {
        var open = visits.Where(visit => !IsClosed(visit.Status) && visit.Status != VisitStatus.Cancelled)
            .OrderBy(visit => visit.VisitNumber)
            .FirstOrDefault();

        if (open is not null)
        {
            return open;
        }

        return visits.Where(visit => visit.Status != VisitStatus.Cancelled).OrderByDescending(visit => visit.VisitNumber).FirstOrDefault()
            ?? visits.OrderByDescending(visit => visit.VisitNumber).FirstOrDefault();
    }

    public static JobStatus Status(WorkOrderStatus order, CustomerJobVisit? current, bool anyNeedsCorrection) =>
        order switch
        {
            WorkOrderStatus.ReadyToSchedule => JobStatus.ReadyToSchedule,
            WorkOrderStatus.Scheduled => current?.Status switch
            {
                VisitStatus.Assigned => JobStatus.Assigned,
                VisitStatus.OnTheWay => JobStatus.OnTheWay,
                _ => JobStatus.Scheduled,
            },
            WorkOrderStatus.InProgress => current?.Status == VisitStatus.Paused ? JobStatus.Paused : JobStatus.InProgress,
            WorkOrderStatus.Completed => anyNeedsCorrection ? JobStatus.NeedsCorrection : JobStatus.Completed,
            WorkOrderStatus.ApprovedForBilling => JobStatus.ApprovedForBilling,
            _ => JobStatus.Cancelled,
        };

    /// <summary>
    /// "Completed" jobs show the latest completed or approved visit end (actual completion, else scheduled end);
    /// others show the earliest scheduled, assigned or on-the-way visit from <paramref name="now"/>.
    /// </summary>
    public static (DateTimeOffset? NextVisitAt, DateTimeOffset? CompletedAt) VisitColumn(
        JobStatus status, IReadOnlyList<CustomerJobVisit> visits, DateTimeOffset now)
    {
        if (status is JobStatus.Completed or JobStatus.ApprovedForBilling)
        {
            var completed = visits
                .Where(visit => visit.Status is VisitStatus.Completed or VisitStatus.Approved)
                .Select(visit => visit.ActualCompletedAt ?? visit.ScheduledEnd)
                .Where(at => at is not null)
                .Max();

            return (null, completed);
        }

        var next = visits
            .Where(visit => visit.Status is VisitStatus.Scheduled or VisitStatus.Assigned or VisitStatus.OnTheWay
                && visit.ScheduledStart is { } start
                && start >= now)
            .Min(visit => visit.ScheduledStart);

        return (next, null);
    }

    private static bool IsClosed(VisitStatus status) =>
        status is VisitStatus.Completed or VisitStatus.NeedsCorrection or VisitStatus.Approved;
}
