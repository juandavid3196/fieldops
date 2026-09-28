using FieldOps.Domain.Notifications;
using FieldOps.Domain.Organizations;

namespace FieldOps.Application.Features.Organizations;

/// <summary>
/// Persistence operations needed by organization settings (FR-03, FR-04).
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

    /// <summary>
    /// Inserts <paramref name="auditLog"/> and saves the already-mutated
    /// <paramref name="organization"/> in one <c>SaveChangesAsync</c> call.
    /// Returns <c>false</c> (stale, BR-07) when the row's <c>updated_at</c>
    /// concurrency token no longer matches what was loaded.
    /// </summary>
    Task<bool> TrySaveUpdateAsync(Organization organization, AuditLog auditLog, CancellationToken cancellationToken);
}
