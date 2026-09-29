using FieldOps.Application.Features.Organizations;
using FieldOps.Domain.Notifications;
using FieldOps.Domain.Organizations;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Persistence;

internal sealed class OrganizationSettingsStore(FieldOpsDbContext dbContext) : IOrganizationSettingsStore
{
    public Task<Organization?> GetAsync(Guid organizationId, CancellationToken cancellationToken) =>
        dbContext.Organizations.SingleOrDefaultAsync(
            organization => organization.Id == organizationId, cancellationToken);

    public async Task<long> GetMaxInvoiceNumberAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var max = await dbContext.Invoices.AsNoTracking()
            .Where(invoice => invoice.OrganizationId == organizationId)
            .Select(invoice => (long?)invoice.InvoiceNumber)
            .MaxAsync(cancellationToken);

        return max ?? 0L;
    }

    public async Task<long> GetMaxQuoteNumberAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var max = await dbContext.Quotes.AsNoTracking()
            .Where(quote => quote.OrganizationId == organizationId)
            .Select(quote => (long?)quote.QuoteNumber)
            .MaxAsync(cancellationToken);

        return max ?? 0L;
    }

    public async Task<long> GetMaxWorkOrderNumberAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var max = await dbContext.WorkOrders.AsNoTracking()
            .Where(workOrder => workOrder.OrganizationId == organizationId)
            .Select(workOrder => (long?)workOrder.WorkOrderNumber)
            .MaxAsync(cancellationToken);

        return max ?? 0L;
    }

    public Task<bool> HasInvoicesAsync(Guid organizationId, CancellationToken cancellationToken) =>
        dbContext.Invoices.AsNoTracking()
            .AnyAsync(invoice => invoice.OrganizationId == organizationId, cancellationToken);

    // Projects only the metadata columns: the bytea content is never read here.
    public Task<OrganizationLogoMetadata?> GetLogoMetadataAsync(
        Guid organizationId, CancellationToken cancellationToken) =>
        dbContext.OrganizationLogos.AsNoTracking()
            .Where(logo => logo.OrganizationId == organizationId)
            .Select(logo => new OrganizationLogoMetadata(logo.ContentType, logo.SizeBytes, logo.UpdatedAt))
            .SingleOrDefaultAsync(cancellationToken);

    // One SaveChangesAsync call: the organization update and the audit
    // insert are written together, or neither is (FR-04). UpdatedAt is a
    // concurrency token (BR-07): a row changed since it was loaded makes
    // this throw DbUpdateConcurrencyException instead of overwriting it.
    public async Task<bool> TrySaveUpdateAsync(
        Organization organization,
        AuditLog auditLog,
        CancellationToken cancellationToken)
    {
        dbContext.AuditLogs.Add(auditLog);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }
}
