using FieldOps.Application.Features.PortalAccess;
using FieldOps.Application.Features.PortalDashboard;
using FieldOps.Application.Features.ServiceRequests;
using FieldOps.Domain.Requests;
using FieldOps.Domain.WorkOrders;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Persistence.Portal;

/// <summary>The organization values every portal read needs: display prefixes, currency and the time zone of the dates.</summary>
internal sealed record PortalOrg(
    string Name,
    string? Phone,
    string? Email,
    string Currency,
    string QuotePrefix,
    string WorkOrderPrefix,
    string InvoicePrefix,
    string RequestPrefix,
    string PaymentPrefix,
    TimeZoneInfo Zone)
{
    public static async Task<PortalOrg> LoadAsync(FieldOpsDbContext dbContext, PortalScope scope, CancellationToken cancellationToken)
    {
        var organizationId = scope.OrganizationId;
        var row = await dbContext.Organizations.AsNoTracking()
            .Where(organization => organization.Id == organizationId)
            .Select(organization => new
            {
                organization.Name,
                organization.Phone,
                organization.Email,
                organization.Currency,
                organization.QuotePrefix,
                organization.WorkOrderPrefix,
                organization.InvoicePrefix,
                organization.RequestPrefix,
                organization.PaymentPrefix,
                organization.Timezone,
            })
            .SingleAsync(cancellationToken);

        return new PortalOrg(
            row.Name,
            row.Phone,
            row.Email,
            row.Currency,
            row.QuotePrefix,
            row.WorkOrderPrefix,
            row.InvoicePrefix,
            row.RequestPrefix,
            row.PaymentPrefix,
            OrganizationTime.FindZone(row.Timezone));
    }

    public DateOnly Today(DateTimeOffset now) => OrganizationTime.LocalDate(now, Zone);

    public string Local(DateTimeOffset instant) =>
        TimeZoneInfo.ConvertTime(instant, Zone).ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
}

internal static class PortalReaders
{
    public const int Candidates = 200;

    /// <summary>The request rows (customer portal BR-23, BR-27): title is the service, else the category, else a generic text.</summary>
    public static async Task<List<PortalRequestRow>> RequestRowsAsync(
        FieldOpsDbContext dbContext, PortalScope scope, PortalOrg org, IQueryable<ServiceRequest> requests, CancellationToken cancellationToken)
    {
        var rows = await (
            from request in requests
            join category in dbContext.ServiceCategories.AsNoTracking()
                on new { request.OrganizationId, Id = request.CategoryId } equals new { category.OrganizationId, Id = (Guid?)category.Id } into categories
            from category in categories.DefaultIfEmpty()
            join item in dbContext.CatalogItems.AsNoTracking()
                on new { request.OrganizationId, Id = request.CatalogItemId } equals new { item.OrganizationId, Id = (Guid?)item.Id } into items
            from item in items.DefaultIfEmpty()
            join property in dbContext.Properties.AsNoTracking().ForPortal(scope)
                on request.PropertyId equals (Guid?)property.Id into properties
            from property in properties.DefaultIfEmpty()
            select new
            {
                request.Id,
                request.RequestNumber,
                request.Status,
                request.CreatedAt,
                ItemName = item.Name,
                CategoryName = category.Name,
                PropertyName = property.Name,
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(row => new PortalRequestRow(
                row.Id,
                RequestCardRules.DisplayNumber(org.RequestPrefix, row.RequestNumber),
                row.ItemName ?? row.CategoryName ?? "Service request",
                row.PropertyName,
                PortalRequestRules.StatusCode(row.Status),
                PortalRequestRules.StatusLabel(row.Status),
                OrganizationTime.LocalDate(row.CreatedAt, org.Zone)))
            .ToList();
    }

    /// <summary>
    /// Builds the appointments of the given visit ids, in the order given, for the customer of the scope. Technician name
    /// comes from the active primary assignment; a pending reschedule request is one recorded for the current scheduled start.
    /// </summary>
    public static async Task<List<PortalAppointment>> AppointmentsAsync(
        FieldOpsDbContext dbContext, PortalScope scope, PortalOrg org, IReadOnlyList<Guid> visitIds, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (visitIds.Count == 0)
        {
            return [];
        }

        var rows = await (
            from visit in dbContext.Visits.AsNoTracking().ForPortal(dbContext, scope)
            where visitIds.Contains(visit.Id)
            join order in dbContext.WorkOrders.AsNoTracking()
                on new { visit.OrganizationId, Id = visit.WorkOrderId } equals new { order.OrganizationId, order.Id }
            join property in dbContext.Properties.AsNoTracking()
                on new { order.OrganizationId, Id = order.PropertyId } equals new { property.OrganizationId, property.Id }
            select new
            {
                visit.Id,
                visit.WorkOrderId,
                visit.Status,
                visit.ScheduledStart,
                visit.ScheduledEnd,
                visit.ArrivalWindowStart,
                visit.ArrivalWindowEnd,
                order.WorkOrderNumber,
                WorkOrderStatus = order.Status,
                order.Title,
                PropertyName = property.Name,
                property.AddressLine1,
            })
            .ToListAsync(cancellationToken);

        var technicians = await (
            from assignment in dbContext.VisitAssignments.AsNoTracking()
            where visitIds.Contains(assignment.VisitId) && assignment.IsPrimary && assignment.UnassignedAt == null
            join profile in dbContext.TechnicianProfiles.AsNoTracking() on assignment.TechnicianId equals profile.Id
            select new { assignment.VisitId, profile.FirstName, profile.LastName })
            .ToListAsync(cancellationToken);

        var pending = await dbContext.VisitRescheduleRequests.AsNoTracking()
            .Where(request => request.OrganizationId == scope.OrganizationId && visitIds.Contains(request.VisitId))
            .Select(request => new { request.VisitId, request.OriginalScheduledStart, request.CreatedAt })
            .ToListAsync(cancellationToken);

        var byId = rows.ToDictionary(row => row.Id);
        var result = new List<PortalAppointment>(visitIds.Count);

        foreach (var id in visitIds)
        {
            if (!byId.TryGetValue(id, out var row) || row.ScheduledStart is not { } start)
            {
                continue;
            }

            var end = row.ScheduledEnd ?? start;
            var technician = technicians.FirstOrDefault(candidate => candidate.VisitId == id);
            var request = pending.FirstOrDefault(candidate =>
                candidate.VisitId == id && PortalVisitRules.IsPending(candidate.OriginalScheduledStart, start));
            var local = TimeZoneInfo.ConvertTime(start, org.Zone);

            result.Add(new PortalAppointment(
                row.Id,
                row.WorkOrderId,
                RequestCardRules.DisplayNumber(org.WorkOrderPrefix, row.WorkOrderNumber),
                row.Title,
                DateOnly.FromDateTime(local.DateTime),
                org.Local(start),
                org.Local(end),
                row.ArrivalWindowStart is { } windowStart && row.ArrivalWindowEnd is { } windowEnd
                    ? new PortalArrivalWindow(org.Local(windowStart), org.Local(windowEnd))
                    : null,
                technician is null ? null : new PortalTechnician(FullName(technician.FirstName, technician.LastName), technician.FirstName.Trim()),
                new PortalAppointmentProperty(row.PropertyName, row.AddressLine1),
                PortalVisitRules.StatusCode(row.Status),
                PortalVisitRules.ProgressStep(row.Status),
                PortalVisitRules.CanRequestReschedule(row.Status, start, now, request is not null),
                request is null ? null : OrganizationTime.LocalDate(request.CreatedAt, org.Zone),
                PortalVisitRules.IsCompletedStatus(row.Status)
                    && row.WorkOrderStatus is WorkOrderStatus.Completed or WorkOrderStatus.ApprovedForBilling));
        }

        return result;
    }

    public static string FullName(string first, string last) =>
        string.Join(' ', new[] { first, last }.Where(part => !string.IsNullOrWhiteSpace(part))).Trim();
}
