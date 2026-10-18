using FieldOps.Application.Features.ServiceRequests;
using FieldOps.Application.Features.Team;
using FieldOps.Application.Features.TechnicianVisits;
using FieldOps.Application.Features.WorkOrders;
using FieldOps.Domain.Customers;
using FieldOps.Domain.WorkOrders;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Persistence;

/// <summary>
/// technician-schedule-active-visits: the one shared active-visit definition (BR-05) used by the Start travel guard and the
/// unresolved list (BR-08), and the week schedule read (BR-02 to BR-04). Reads never track or write.
/// </summary>
internal sealed partial class TechnicianVisitStore
{
    public async Task<IReadOnlyList<UnresolvedVisit>> ListUnresolvedAsync(
        Guid organizationId, Guid technicianId, string zoneId, DateTimeOffset dayStart, CancellationToken cancellationToken)
    {
        var rows = await ActiveVisits(organizationId, technicianId)
            .Where(row => row.ScheduledStart != null && row.ScheduledStart < dayStart)
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return [];
        }

        var summaries = await SummariesAsync(organizationId, zoneId, rows, cancellationToken);

        var customerIds = rows.Select(row => row.CustomerId).Distinct().ToList();
        var customers = await dbContext.Customers.AsNoTracking()
            .Where(customer => customer.OrganizationId == organizationId && customerIds.Contains(customer.Id))
            .Select(customer => new { customer.Id, customer.DisplayName, customer.Type })
            .ToDictionaryAsync(customer => customer.Id, cancellationToken);

        var visitIds = rows.Select(row => row.VisitId).ToList();
        var history = await dbContext.VisitStatusHistories.AsNoTracking()
            .Where(entry => visitIds.Contains(entry.VisitId))
            .Select(entry => new { entry.VisitId, entry.ToStatus, entry.ChangedAt })
            .ToListAsync(cancellationToken);
        var zone = BranchTime.FindZone(zoneId);

        return
        [
            .. rows.Select((row, index) =>
            {
                var summary = summaries[index];
                var customer = customers[row.CustomerId];
                DateTimeOffset? since = history
                    .Where(entry => entry.VisitId == row.VisitId && entry.ToStatus == row.Status)
                    .Select(entry => (DateTimeOffset?)entry.ChangedAt)
                    .Max();

                return new UnresolvedVisit(
                    summary.VisitId,
                    summary.DisplayNumber,
                    summary.Title,
                    summary.Status,
                    summary.ScheduledStart,
                    customer.DisplayName,
                    customer.Type == CustomerType.Company ? "company" : "person",
                    since is { } changedAt ? OrganizationTime.ToZone(changedAt, zone) : null);
            }),
        ];
    }

    public async Task<IReadOnlyList<ScheduleVisit>> ListWeekAsync(
        Guid organizationId,
        Guid technicianId,
        string zoneId,
        DateTimeOffset rangeFrom,
        DateTimeOffset rangeTo,
        DateTimeOffset dayStart,
        CancellationToken cancellationToken)
    {
        var rows = await (
            from visit in dbContext.Visits.AsNoTracking()
            join order in dbContext.WorkOrders.AsNoTracking()
                on new { visit.OrganizationId, Id = visit.WorkOrderId } equals new { order.OrganizationId, order.Id }
            join property in dbContext.Properties.AsNoTracking()
                on new { order.OrganizationId, Id = order.PropertyId } equals new { property.OrganizationId, property.Id }
            join customer in dbContext.Customers.AsNoTracking()
                on new { order.OrganizationId, Id = order.CustomerId } equals new { customer.OrganizationId, customer.Id }
            where visit.OrganizationId == organizationId
                && visit.Status != VisitStatus.Unscheduled
                && visit.ScheduledStart != null
                && visit.ScheduledEnd != null
                && visit.ScheduledStart >= rangeFrom
                && visit.ScheduledStart < rangeTo
                && dbContext.VisitAssignments.Any(assignment =>
                    assignment.VisitId == visit.Id && assignment.TechnicianId == technicianId && assignment.UnassignedAt == null)
            orderby visit.ScheduledStart, order.WorkOrderNumber, visit.VisitNumber
            select new
            {
                visit.Id,
                order.WorkOrderNumber,
                order.Title,
                visit.Status,
                Start = visit.ScheduledStart!.Value,
                End = visit.ScheduledEnd!.Value,
                customer.DisplayName,
                CustomerType = customer.Type,
                property.AddressLine1,
                property.City,
                property.StateRegion,
                property.PostalCode,
            })
            .ToListAsync(cancellationToken);

        var prefix = await OrganizationPrefixAsync(organizationId, cancellationToken);
        var zone = BranchTime.FindZone(zoneId);

        return
        [
            .. rows.Select(row => new ScheduleVisit(
                row.Id,
                RequestCardRules.DisplayNumber(prefix, row.WorkOrderNumber),
                row.Title,
                WorkOrderCodes.VisitStatusCode(row.Status),
                OrganizationTime.ToZone(row.Start, zone),
                OrganizationTime.ToZone(row.End, zone),
                (int)Math.Round((row.End - row.Start).TotalMinutes, MidpointRounding.AwayFromZero),
                row.DisplayName,
                row.CustomerType == CustomerType.Company ? "company" : "person",
                new ScheduleAddress(row.AddressLine1, row.City, row.StateRegion, row.PostalCode),
                IsPastNotStarted(row.Status, row.Start, dayStart))),
        ];
    }

    /// <summary>The active visit of the caller, other than <paramref name="targetVisitId"/>, that blocks Start travel (BR-05).</summary>
    private async Task<ActiveVisitSummary?> FirstActiveVisitAsync(
        Guid organizationId, Guid technicianId, string zoneId, Guid targetVisitId, CancellationToken cancellationToken)
    {
        var row = await ActiveVisits(organizationId, technicianId)
            .Where(candidate => candidate.VisitId != targetVisitId)
            .FirstOrDefaultAsync(cancellationToken);

        return row is null
            ? null
            : (await SummariesAsync(organizationId, zoneId, [row], cancellationToken))[0];
    }

    /// <summary>
    /// BR-05: visits actively assigned to the caller in the organization, not cancelled, that are on the way, in progress or
    /// paused, or have an open time entry of the caller. The scheduled date never makes a visit active. Ordered by
    /// scheduled start, work order number, then visit number.
    /// </summary>
    private IQueryable<ActiveRow> ActiveVisits(Guid organizationId, Guid technicianId) =>
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
        select new ActiveRow
        {
            VisitId = visit.Id,
            WorkOrderNumber = order.WorkOrderNumber,
            Title = order.Title,
            CustomerId = order.CustomerId,
            Status = visit.Status,
            ScheduledStart = visit.ScheduledStart,
        };

    private async Task<string> OrganizationPrefixAsync(Guid organizationId, CancellationToken cancellationToken) =>
        await dbContext.Organizations.AsNoTracking()
            .Where(organization => organization.Id == organizationId)
            .Select(organization => organization.WorkOrderPrefix)
            .SingleAsync(cancellationToken);

    private async Task<IReadOnlyList<ActiveVisitSummary>> SummariesAsync(
        Guid organizationId, string zoneId, IReadOnlyList<ActiveRow> rows, CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var prefix = await OrganizationPrefixAsync(organizationId, cancellationToken);
        var zone = BranchTime.FindZone(zoneId);

        return
        [
            .. rows.Select(row => new ActiveVisitSummary(
                row.VisitId,
                RequestCardRules.DisplayNumber(prefix, row.WorkOrderNumber),
                row.Title,
                WorkOrderCodes.VisitStatusCode(row.Status),
                row.ScheduledStart is { } start ? OrganizationTime.ToZone(start, zone) : null)),
        ];
    }

    private sealed class ActiveRow
    {
        public Guid VisitId { get; init; }

        public long WorkOrderNumber { get; init; }

        public string Title { get; init; } = string.Empty;

        public Guid CustomerId { get; init; }

        public VisitStatus Status { get; init; }

        public DateTimeOffset? ScheduledStart { get; init; }
    }
}
