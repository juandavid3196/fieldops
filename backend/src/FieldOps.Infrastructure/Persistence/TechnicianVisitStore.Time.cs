using FieldOps.Application.Features.ServiceRequests;
using FieldOps.Application.Features.TechnicianVisits;
using FieldOps.Application.Features.WorkOrders;
using FieldOps.Domain.WorkOrders;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Persistence;

/// <summary>technician-time-summary: the caller's own time entries. A read; nothing is tracked or written.</summary>
internal sealed partial class TechnicianVisitStore
{
    public async Task<IReadOnlyList<TechnicianTimeEntryRow>> ListTimeEntriesAsync(
        Guid organizationId, Guid technicianId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        // BR-01: the technician's own rows on visits of the session organization; the current assignment never filters.
        // BR-05: visitAccessible repeats the rule of GET /technician/visits/{id} (active assignment, scheduled, not unscheduled).
        var rows = await (
            from entry in dbContext.VisitTimeEntries.AsNoTracking()
            join visit in dbContext.Visits.AsNoTracking() on entry.VisitId equals visit.Id
            join order in dbContext.WorkOrders.AsNoTracking()
                on new { visit.OrganizationId, Id = visit.WorkOrderId } equals new { order.OrganizationId, order.Id }
            where entry.TechnicianId == technicianId
                && visit.OrganizationId == organizationId
                && entry.StartedAt >= @from
                && entry.StartedAt < to
            select new
            {
                entry.Id,
                entry.EntryType,
                entry.StartedAt,
                entry.EndedAt,
                VisitId = visit.Id,
                order.WorkOrderNumber,
                order.Title,
                visit.Status,
                visit.VisitNumber,
                Accessible = visit.Status != VisitStatus.Unscheduled
                    && visit.ScheduledStart != null
                    && visit.ScheduledEnd != null
                    && dbContext.VisitAssignments.Any(assignment =>
                        assignment.VisitId == visit.Id && assignment.TechnicianId == technicianId && assignment.UnassignedAt == null),
            })
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return [];
        }

        var prefix = await dbContext.Organizations.AsNoTracking()
            .Where(organization => organization.Id == organizationId)
            .Select(organization => organization.WorkOrderPrefix)
            .SingleAsync(cancellationToken);

        return
        [
            .. rows.Select(row => new TechnicianTimeEntryRow(
                row.Id,
                row.EntryType,
                row.StartedAt,
                row.EndedAt,
                row.VisitId,
                RequestCardRules.DisplayNumber(prefix, row.WorkOrderNumber),
                row.Title,
                WorkOrderCodes.VisitStatusCode(row.Status),
                row.VisitNumber,
                row.Accessible)),
        ];
    }
}
