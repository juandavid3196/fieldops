using System.Net;
using FieldOps.Application.Features.PortalAccess;
using FieldOps.Application.Features.PortalDashboard;
using FieldOps.Application.Features.ServiceRequests;
using FieldOps.Domain.Customers;
using FieldOps.Domain.WorkOrders;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FieldOps.Infrastructure.Persistence.Portal;

/// <summary>
/// Properties of the portal (customer portal BR-33). A portal contact adds properties and edits the name, the access
/// instructions and the primary flag only; the address is never written here. Edit locks the row and compares the stored
/// <c>updated_at</c>; making a property primary clears the previous primary in the same transaction.
/// </summary>
internal sealed class PortalPropertyStore(FieldOpsDbContext dbContext) : IPortalPropertyStore
{
    public async Task<IReadOnlyList<PortalProperty>> ListAsync(PortalScope scope, CancellationToken cancellationToken)
    {
        var org = await PortalOrg.LoadAsync(dbContext, scope, cancellationToken);
        var properties = await dbContext.Properties.AsNoTracking().ForPortal(scope)
            .Where(property => property.IsActive)
            .OrderByDescending(property => property.IsPrimary)
            .ThenBy(property => property.Name)
            .ThenBy(property => property.Id)
            .ToListAsync(cancellationToken);
        var lastService = await LastServiceAsync(scope, cancellationToken);

        return properties.Select(property => Map(property, org, lastService)).ToList();
    }

    public async Task<PortalProperty> CreateAsync(
        PortalScope scope, PortalPropertyCreateInput values, IPAddress? clientIp, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var org = await PortalOrg.LoadAsync(dbContext, scope, cancellationToken);
        var primary = !await HasActivePrimaryAsync(scope, cancellationToken);

        for (var attempt = 0; ; attempt++)
        {
            var property = Property.Create(
                scope.OrganizationId,
                scope.CustomerId,
                values.Name!,
                values.AddressLine1!,
                values.City!,
                "US",
                branchId: null,
                stateRegion: values.StateRegion,
                postalCode: values.PostalCode,
                isPrimary: primary && attempt == 0,
                addressLine2: values.AddressLine2,
                accessInstructions: values.AccessInstructions);

            dbContext.Properties.Add(property);
            dbContext.AuditLogs.Add(PropertyAudit.Created(property, scope.UserId, clientIp));

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);

                // Reloaded so the returned updated-at is the stored microsecond value that PATCH compares.
                await dbContext.Entry(property).ReloadAsync(cancellationToken);

                return Map(property, org, []);
            }
            catch (DbUpdateException exception) when (
                attempt == 0 && primary && exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // A concurrent request became the primary: the new property is created non-primary.
                dbContext.ChangeTracker.Clear();
            }
        }
    }

    public async Task<PortalPropertyEditResult> UpdateAsync(
        PortalScope scope,
        Guid propertyId,
        PortalPropertyEditInput input,
        DateTimeOffset expectedUpdatedAt,
        IPAddress? clientIp,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var organizationId = scope.OrganizationId;
        var customerId = scope.CustomerId;
        var locked = await dbContext.Database
            .SqlQuery<Guid>(
                $"""
                SELECT id AS "Value" FROM properties
                WHERE id = {propertyId} AND organization_id = {organizationId} AND customer_id = {customerId} AND is_active
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);

        if (locked.Count != 1)
        {
            return new PortalPropertyEditResult.NotFound();
        }

        var property = await dbContext.Properties.SingleAsync(
            candidate => candidate.Id == propertyId && candidate.OrganizationId == organizationId, cancellationToken);

        if (property.UpdatedAt != expectedUpdatedAt)
        {
            return new PortalPropertyEditResult.Changed();
        }

        var stamp = Micro(now);
        var changed = property.EditFromPortal(input.HasName, input.Name, input.HasAccessInstructions, input.AccessInstructions, stamp);

        if (changed.Count > 0)
        {
            dbContext.AuditLogs.Add(PropertyAudit.Updated(property, changed, scope.UserId, clientIp));
        }

        if (input.IsPrimary == true && !property.IsPrimary)
        {
            var previous = await dbContext.Properties.AsNoTracking()
                .Where(candidate => candidate.OrganizationId == organizationId
                    && candidate.CustomerId == customerId
                    && candidate.IsPrimary
                    && candidate.Id != propertyId)
                .Select(candidate => (Guid?)candidate.Id)
                .FirstOrDefaultAsync(cancellationToken);

            // The previous primary is cleared first: the unique index allows one primary per customer.
            await dbContext.Properties
                .Where(candidate => candidate.OrganizationId == organizationId && candidate.CustomerId == customerId && candidate.IsPrimary)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(candidate => candidate.IsPrimary, false)
                        .SetProperty(candidate => candidate.UpdatedAt, stamp),
                    cancellationToken);

            property.SetPrimary(stamp);
            dbContext.AuditLogs.Add(PropertyAudit.PrimaryChanged(property, previous, scope.UserId, clientIp));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var org = await PortalOrg.LoadAsync(dbContext, scope, cancellationToken);

        return new PortalPropertyEditResult.Saved(Map(property, org, await LastServiceAsync(scope, cancellationToken)));
    }

    private Task<bool> HasActivePrimaryAsync(PortalScope scope, CancellationToken cancellationToken) =>
        dbContext.Properties.AsNoTracking().ForPortal(scope).AnyAsync(property => property.IsActive && property.IsPrimary, cancellationToken);

    private async Task<Dictionary<Guid, DateTimeOffset>> LastServiceAsync(PortalScope scope, CancellationToken cancellationToken)
    {
        var rows = await (
            from order in dbContext.WorkOrders.AsNoTracking().ForPortal(scope)
            join visit in dbContext.Visits.AsNoTracking()
                on new { order.OrganizationId, WorkOrderId = order.Id } equals new { visit.OrganizationId, visit.WorkOrderId }
            where visit.ActualCompletedAt != null
                && (visit.Status == VisitStatus.Completed || visit.Status == VisitStatus.NeedsCorrection || visit.Status == VisitStatus.Approved)
            group visit by order.PropertyId into byProperty
            select new { PropertyId = byProperty.Key, At = byProperty.Max(visit => visit.ActualCompletedAt) })
            .ToListAsync(cancellationToken);

        return rows.Where(row => row.At is not null).ToDictionary(row => row.PropertyId, row => row.At!.Value);
    }

    private static PortalProperty Map(Property property, PortalOrg org, Dictionary<Guid, DateTimeOffset> lastService) =>
        new(
            property.Id,
            property.Name,
            property.AddressLine1,
            property.AddressLine2,
            property.City,
            property.StateRegion,
            property.PostalCode,
            property.CountryCode,
            property.AccessInstructions,
            property.IsPrimary,
            lastService.TryGetValue(property.Id, out var at) ? OrganizationTime.LocalDate(at, org.Zone) : null,
            property.UpdatedAt);

    // PostgreSQL keeps microseconds: the stored and the returned value must compare equal on the next edit.
    private static DateTimeOffset Micro(DateTimeOffset value) => new(value.Ticks - (value.Ticks % 10), value.Offset);
}
