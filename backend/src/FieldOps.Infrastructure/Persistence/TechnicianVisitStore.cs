using FieldOps.Application.Features.ServiceRequests;
using FieldOps.Application.Features.Team;
using FieldOps.Application.Features.TechnicianVisits;
using FieldOps.Application.Features.WorkOrders;
using FieldOps.Domain.WorkOrders;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Persistence;

/// <summary>Read-only visits of the signed-in technician (technician-todays-jobs). Never tracks or writes.</summary>
internal sealed class TechnicianVisitStore(FieldOpsDbContext dbContext) : ITechnicianVisitStore
{
    public async Task<TechnicianVisitProfile?> GetProfileAsync(
        Guid organizationId, Guid membershipId, CancellationToken cancellationToken)
    {
        var row = await (
            from profile in dbContext.TechnicianProfiles.AsNoTracking()
            join branch in dbContext.Branches.AsNoTracking()
                on profile.BranchId equals branch.Id
            join organization in dbContext.Organizations.AsNoTracking()
                on profile.OrganizationId equals organization.Id
            where profile.OrganizationId == organizationId && profile.OrganizationUserId == membershipId
            select new
            {
                profile.Id,
                profile.Status,
                profile.FirstName,
                profile.LastName,
                profile.ColorHex,
                BranchTimezone = branch.Timezone,
                OrganizationTimezone = organization.Timezone,
            })
            .SingleOrDefaultAsync(cancellationToken);

        return row is null
            ? null
            : new TechnicianVisitProfile(
                row.Id,
                row.Status,
                row.FirstName,
                row.LastName,
                row.ColorHex,
                BranchTime.ResolveZoneId(row.BranchTimezone, row.OrganizationTimezone));
    }

    public async Task<IReadOnlyList<TodayVisit>> ListScheduledAsync(
        Guid organizationId,
        Guid technicianId,
        string zoneId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var rows = await Query(organizationId, technicianId, from, to, null).ToListAsync(cancellationToken);

        return await MapAsync(organizationId, zoneId, rows, cancellationToken);
    }

    public async Task<FoundVisit?> FindAsync(
        Guid organizationId, Guid technicianId, string zoneId, Guid visitId, CancellationToken cancellationToken)
    {
        var rows = await Query(organizationId, technicianId, null, null, visitId).ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return null;
        }

        return new FoundVisit((await MapAsync(organizationId, zoneId, rows, cancellationToken))[0], rows[0].DispatchNote);
    }

    /// <summary>
    /// The caller's actively assigned, scheduled and not cancelled visits of the organization (BR-03), in route
    /// order (BR-05), optionally limited to a start range or one visit.
    /// </summary>
    private IQueryable<VisitRow> Query(
        Guid organizationId, Guid technicianId, DateTimeOffset? rangeFrom, DateTimeOffset? rangeTo, Guid? visitId) =>
        from visit in dbContext.Visits.AsNoTracking()
        join order in dbContext.WorkOrders.AsNoTracking()
            on new { visit.OrganizationId, Id = visit.WorkOrderId } equals new { order.OrganizationId, order.Id }
        join property in dbContext.Properties.AsNoTracking()
            on new { order.OrganizationId, Id = order.PropertyId } equals new { property.OrganizationId, property.Id }
        join customer in dbContext.Customers.AsNoTracking()
            on new { order.OrganizationId, Id = order.CustomerId } equals new { customer.OrganizationId, customer.Id }
        join category in dbContext.ServiceCategories.AsNoTracking()
            on new { order.OrganizationId, Id = order.ServiceCategoryId } equals new { category.OrganizationId, category.Id }
        where visit.OrganizationId == organizationId
            && visit.Status != VisitStatus.Unscheduled
            && visit.Status != VisitStatus.Cancelled
            && visit.ScheduledStart != null
            && visit.ScheduledEnd != null
            && dbContext.VisitAssignments.Any(assignment =>
                assignment.VisitId == visit.Id && assignment.TechnicianId == technicianId && assignment.UnassignedAt == null)
            && (rangeFrom == null || visit.ScheduledStart >= rangeFrom)
            && (rangeTo == null || visit.ScheduledStart < rangeTo)
            && (visitId == null || visit.Id == visitId)
        orderby visit.ScheduledStart, order.WorkOrderNumber, visit.VisitNumber
        select new VisitRow(
            visit.Id,
            visit.VisitNumber,
            order.Id,
            order.WorkOrderNumber,
            order.Title,
            visit.Status,
            visit.ScheduledStart!.Value,
            visit.ScheduledEnd!.Value,
            visit.ArrivalWindowStart,
            visit.ArrivalWindowEnd,
            order.Priority,
            category.Name,
            customer.DisplayName,
            dbContext.CustomerContacts
                .Where(contact => contact.OrganizationId == order.OrganizationId
                    && contact.CustomerId == order.CustomerId
                    && contact.IsPrimary
                    && contact.IsActive)
                .Select(contact => contact.Phone)
                .FirstOrDefault(),
            property.AddressLine1,
            property.AddressLine2,
            property.City,
            property.StateRegion,
            property.PostalCode,
            property.CountryCode,
            property.Latitude,
            property.Longitude,
            dbContext.WorkOrderPlannedMaterials.Count(material =>
                material.OrganizationId == order.OrganizationId && material.WorkOrderId == order.Id),
            visit.DispatchNote);

    private async Task<IReadOnlyList<TodayVisit>> MapAsync(
        Guid organizationId, string zoneId, List<VisitRow> rows, CancellationToken cancellationToken)
    {
        var prefix = await dbContext.Organizations.AsNoTracking()
            .Where(organization => organization.Id == organizationId)
            .Select(organization => organization.WorkOrderPrefix)
            .SingleAsync(cancellationToken);
        var zone = BranchTime.FindZone(zoneId);

        return
        [
            .. rows.Select(row => new TodayVisit(
                row.VisitId,
                row.VisitNumber,
                row.WorkOrderId,
                RequestCardRules.DisplayNumber(prefix, row.WorkOrderNumber),
                row.Title,
                WorkOrderCodes.VisitStatusCode(row.Status),
                OrganizationTime.ToZone(row.Start, zone),
                OrganizationTime.ToZone(row.End, zone),
                row.ArrivalWindowStart is { } windowStart ? OrganizationTime.ToZone(windowStart, zone) : null,
                row.ArrivalWindowEnd is { } windowEnd ? OrganizationTime.ToZone(windowEnd, zone) : null,
                row.Priority,
                row.ServiceCategory,
                row.CustomerName,
                row.Phone,
                new TodayAddress(row.Line1, row.Line2, row.City, row.StateRegion, row.PostalCode, row.CountryCode),
                row.Latitude,
                row.Longitude,
                row.PlannedMaterialsCount)),
        ];
    }

    private sealed record VisitRow(
        Guid VisitId,
        int VisitNumber,
        Guid WorkOrderId,
        long WorkOrderNumber,
        string Title,
        VisitStatus Status,
        DateTimeOffset Start,
        DateTimeOffset End,
        DateTimeOffset? ArrivalWindowStart,
        DateTimeOffset? ArrivalWindowEnd,
        short Priority,
        string ServiceCategory,
        string CustomerName,
        string? Phone,
        string Line1,
        string? Line2,
        string City,
        string? StateRegion,
        string? PostalCode,
        string CountryCode,
        decimal? Latitude,
        decimal? Longitude,
        int PlannedMaterialsCount,
        string? DispatchNote);
}
