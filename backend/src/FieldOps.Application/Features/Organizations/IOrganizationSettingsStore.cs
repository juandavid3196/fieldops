using FieldOps.Domain.Notifications;
using FieldOps.Domain.Organizations;

namespace FieldOps.Application.Features.Organizations;

/// <summary>
/// Persistence operations needed by organization settings (FR-03, FR-04,
/// FR-07). Every aggregate is scoped by the organization id.
/// </summary>
public interface IOrganizationSettingsStore
{
    /// <summary>The tracked organization row, or null if it does not exist.</summary>
    Task<Organization?> GetAsync(Guid organizationId, CancellationToken cancellationToken);

    /// <summary>
    /// The highest <c>invoices.invoice_number</c> for the organization, or 0
    /// when it has no invoices (BR-02).
    /// </summary>
    Task<long> GetMaxInvoiceNumberAsync(Guid organizationId, CancellationToken cancellationToken);

    /// <summary>The highest <c>quotes.quote_number</c> of the organization, or 0 (BR-05).</summary>
    Task<long> GetMaxQuoteNumberAsync(Guid organizationId, CancellationToken cancellationToken);

    /// <summary>The highest <c>work_orders.work_order_number</c> of the organization, or 0 (BR-05).</summary>
    Task<long> GetMaxWorkOrderNumberAsync(Guid organizationId, CancellationToken cancellationToken);

    /// <summary>Whether the organization has at least one invoice (BR-06).</summary>
    Task<bool> HasInvoicesAsync(Guid organizationId, CancellationToken cancellationToken);

    /// <summary>
    /// Logo content type, size and update time; never loads the content
    /// column. Null when the organization has no logo.
    /// </summary>
    Task<OrganizationLogoMetadata?> GetLogoMetadataAsync(Guid organizationId, CancellationToken cancellationToken);

    /// <summary>
    /// Inserts <paramref name="auditLog"/> and saves the already-mutated
    /// <paramref name="organization"/> in one <c>SaveChangesAsync</c> call.
    /// Returns <c>false</c> (stale, BR-07) when the row's <c>updated_at</c>
    /// concurrency token no longer matches what was loaded.
    /// </summary>
    Task<bool> TrySaveUpdateAsync(Organization organization, AuditLog auditLog, CancellationToken cancellationToken);
}
