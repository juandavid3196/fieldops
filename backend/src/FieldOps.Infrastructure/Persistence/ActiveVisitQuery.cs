using FieldOps.Domain.WorkOrders;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Persistence;

internal sealed class ActiveVisitRow
{
    public Guid VisitId { get; init; }

    public Guid WorkOrderId { get; init; }

    public long WorkOrderNumber { get; init; }

    public string Title { get; init; } = string.Empty;

    public Guid CustomerId { get; init; }

    public VisitStatus Status { get; init; }

    public DateTimeOffset? ScheduledStart { get; init; }
}

/// <summary>
/// technician-schedule-active-visits BR-05: the one shared active-visit definition, used by the Start travel guard, the
/// unresolved list and the technician-messages <c>visitId</c> preference (BR-08).
/// </summary>
internal static class ActiveVisitQuery
{
    /// <summary>
    /// Visits actively assigned to the caller in the organization, not cancelled, that are on the way, in progress or
    /// paused, or have an open time entry of the caller. The scheduled date never makes a visit active. Ordered by
    /// scheduled start, work order number, then visit number.
    /// </summary>
    public static IQueryable<ActiveVisitRow> For(FieldOpsDbContext dbContext, Guid organizationId, Guid technicianId) =>
        from visit in dbContext.Visits.AsNoTracking()
        join order in dbContext.WorkOrders.AsNoTracking()
            on new { visit.OrganizationId, Id = visit.WorkOrderId } equals new { order.OrganizationId, order.Id }
        where visit.OrganizationId == organizationId
            && visit.Status != VisitStatus.Cancelled
            && dbContext.VisitAssignments.Any(assignment =>
                assignment.VisitId == visit.Id && assignment.TechnicianId == technicianId && assignment.UnassignedAt == null)
            && (visit.Status == VisitStatus.OnTheWay
                || visit.Status == VisitStatus.InProgress
                || visit.Status == VisitStatus.Paused
                || dbContext.VisitTimeEntries.Any(entry =>
                    entry.VisitId == visit.Id && entry.TechnicianId == technicianId && entry.EndedAt == null))
        orderby visit.ScheduledStart, order.WorkOrderNumber, visit.VisitNumber
        select new ActiveVisitRow
        {
            VisitId = visit.Id,
            WorkOrderId = order.Id,
            WorkOrderNumber = order.WorkOrderNumber,
            Title = order.Title,
            CustomerId = order.CustomerId,
            Status = visit.Status,
            ScheduledStart = visit.ScheduledStart,
        };
}
