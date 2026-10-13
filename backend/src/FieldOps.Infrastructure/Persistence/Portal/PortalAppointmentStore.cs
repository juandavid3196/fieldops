using System.Net;
using System.Text.Json;
using FieldOps.Application.Features.PortalAccess;
using FieldOps.Application.Features.PortalDashboard;
using FieldOps.Application.Features.ServiceRequests;
using FieldOps.Domain.Notifications;
using FieldOps.Domain.WorkOrders;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FieldOps.Infrastructure.Persistence.Portal;

/// <summary>
/// Appointments of the portal (customer portal BR-22, BR-29) and the reschedule request (BR-32). The visit is never changed:
/// the request locks the visit row, rechecks the eligibility and stores the request in one transaction; the unique index on
/// (visit, original scheduled start) is the backstop of two concurrent requests.
/// </summary>
internal sealed class PortalAppointmentStore(FieldOpsDbContext dbContext) : IPortalAppointmentStore
{
    private static readonly VisitStatus[] UpcomingStatuses =
        [VisitStatus.Scheduled, VisitStatus.Assigned, VisitStatus.OnTheWay, VisitStatus.InProgress, VisitStatus.Paused];

    private static readonly VisitStatus[] PastStatuses =
        [VisitStatus.Completed, VisitStatus.NeedsCorrection, VisitStatus.Approved, VisitStatus.Cancelled];

    public async Task<PortalPage<PortalAppointment>?> ListAsync(
        PortalScope scope, PortalAppointmentScope kind, Guid? propertyId, int page, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (propertyId is { } selected
            && !await dbContext.Properties.AsNoTracking().ForPortal(scope).AnyAsync(
                property => property.Id == selected && property.IsActive, cancellationToken))
        {
            return null;
        }

        var org = await PortalOrg.LoadAsync(dbContext, scope, cancellationToken);
        var upcoming = kind == PortalAppointmentScope.Upcoming;
        var statuses = upcoming ? UpcomingStatuses : PastStatuses;
        var query =
            from visit in dbContext.Visits.AsNoTracking().ForPortal(dbContext, scope)
            where visit.ScheduledStart != null && statuses.Contains(visit.Status)
            join order in dbContext.WorkOrders.AsNoTracking()
                on new { visit.OrganizationId, Id = visit.WorkOrderId } equals new { order.OrganizationId, order.Id }
            where (!upcoming || order.Status != WorkOrderStatus.Cancelled) && (propertyId == null || order.PropertyId == propertyId)
            select new { visit.Id, visit.ScheduledStart };

        var total = await query.CountAsync(cancellationToken);
        var ordered = upcoming
            ? query.OrderBy(row => row.ScheduledStart).ThenBy(row => row.Id)
            : query.OrderByDescending(row => row.ScheduledStart).ThenBy(row => row.Id);
        var ids = await ordered
            .Skip((page - 1) * PortalPaging.PageSize)
            .Take(PortalPaging.PageSize)
            .Select(row => row.Id)
            .ToListAsync(cancellationToken);

        return new PortalPage<PortalAppointment>(
            await PortalReaders.AppointmentsAsync(dbContext, scope, org, ids, now, cancellationToken),
            page,
            PortalPaging.PageSize,
            total);
    }

    public async Task<PortalAppointment?> GetAsync(PortalScope scope, Guid visitId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var org = await PortalOrg.LoadAsync(dbContext, scope, cancellationToken);

        return (await PortalReaders.AppointmentsAsync(dbContext, scope, org, [visitId], now, cancellationToken)).FirstOrDefault();
    }

    public Task<string> GetTimezoneAsync(PortalScope scope, CancellationToken cancellationToken)
    {
        var organizationId = scope.OrganizationId;

        return dbContext.Organizations.AsNoTracking()
            .Where(organization => organization.Id == organizationId)
            .Select(organization => organization.Timezone)
            .SingleAsync(cancellationToken);
    }

    public async Task<PortalRescheduleResult> RequestRescheduleAsync(
        PortalScope scope,
        Guid visitId,
        PortalRescheduleValues values,
        IPAddress? clientIp,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var organizationId = scope.OrganizationId;
        var customerId = scope.CustomerId;

        // The visit row of the customer's own work order, locked and verified by the same statement.
        var locked = await dbContext.Database
            .SqlQuery<Guid>(
                $"""
                SELECT v.id AS "Value" FROM visits v
                JOIN work_orders w ON w.organization_id = v.organization_id AND w.id = v.work_order_id
                WHERE v.id = {visitId} AND v.organization_id = {organizationId} AND w.customer_id = {customerId}
                FOR UPDATE OF v
                """)
            .ToListAsync(cancellationToken);

        if (locked.Count != 1)
        {
            return new PortalRescheduleResult.NotFound();
        }

        var visit = await dbContext.Visits.AsNoTracking()
            .Where(candidate => candidate.OrganizationId == organizationId && candidate.Id == visitId)
            .Select(candidate => new
            {
                candidate.Status,
                candidate.ScheduledStart,
                candidate.ScheduledEnd,
                candidate.ArrivalWindowStart,
                candidate.ArrivalWindowEnd,
                candidate.WorkOrderId,
            })
            .SingleAsync(cancellationToken);

        if (!PortalVisitRules.CanRequestReschedule(visit.Status, visit.ScheduledStart, now, hasPendingRequest: false))
        {
            return new PortalRescheduleResult.NotEligible();
        }

        var start = visit.ScheduledStart!.Value;

        if (await dbContext.VisitRescheduleRequests.AsNoTracking().AnyAsync(
            request => request.VisitId == visitId && request.OriginalScheduledStart == start, cancellationToken))
        {
            return new PortalRescheduleResult.AlreadyRequested();
        }

        var request = VisitRescheduleRequest.Create(
            organizationId, visitId, scope.ContactId, start, values.PreferredDate, values.TimeWindow, values.Reason, now);

        dbContext.VisitRescheduleRequests.Add(request);

        // No reason text in the audit row (customer portal BR-32).
        dbContext.AuditLogs.Add(AuditLog.Create(
            organizationId,
            "visit.reschedule_requested",
            "visit",
            scope.UserId,
            visitId,
            ipAddress: clientIp,
            metadata: JsonSerializer.Serialize(new { requestId = request.Id, channel = "portal" })));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            dbContext.ChangeTracker.Clear();

            return new PortalRescheduleResult.AlreadyRequested();
        }

        var org = await PortalOrg.LoadAsync(dbContext, scope, cancellationToken);
        var order = await dbContext.WorkOrders.AsNoTracking()
            .Where(candidate => candidate.OrganizationId == organizationId && candidate.Id == visit.WorkOrderId)
            .Select(candidate => candidate.WorkOrderNumber)
            .SingleAsync(cancellationToken);
        var customerName = await dbContext.Customers.AsNoTracking()
            .Where(candidate => candidate.OrganizationId == organizationId && candidate.Id == customerId)
            .Select(candidate => candidate.DisplayName)
            .SingleAsync(cancellationToken);
        var recipient = await RecipientAsync(organizationId, org, cancellationToken);
        var currentWindow = visit.ArrivalWindowStart is { } windowStart && visit.ArrivalWindowEnd is { } windowEnd
            ? $"{org.Local(windowStart)}-{org.Local(windowEnd)}"
            : $"{org.Local(start)}-{org.Local(visit.ScheduledEnd ?? start)}";

        return new PortalRescheduleResult.Created(
            OrganizationTime.LocalDate(now, org.Zone),
            new PortalRescheduleEmailData(
                recipient,
                RequestCardRules.DisplayNumber(org.WorkOrderPrefix, order),
                customerName,
                OrganizationTime.LocalDate(start, org.Zone),
                currentWindow,
                values.PreferredDate,
                values.TimeWindow,
                values.Reason));
    }

    // The organization email, else the email of the main branch (customer portal BR-32, BR-35).
    private async Task<string?> RecipientAsync(Guid organizationId, PortalOrg org, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(org.Email))
        {
            return org.Email.Trim();
        }

        var branchEmail = await dbContext.Branches.AsNoTracking()
            .Where(branch => branch.OrganizationId == organizationId && branch.IsMain)
            .Select(branch => branch.Email)
            .FirstOrDefaultAsync(cancellationToken);

        return string.IsNullOrWhiteSpace(branchEmail) ? null : branchEmail.Trim();
    }
}
