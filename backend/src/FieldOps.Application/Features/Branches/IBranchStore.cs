using System.Net;
using FieldOps.Domain.Branches;
using FieldOps.Domain.Notifications;

namespace FieldOps.Application.Features.Branches;

/// <summary>
/// Persistence operations needed by branch management (FR-05 to FR-09).
/// </summary>
public interface IBranchStore
{
    /// <summary>
    /// Branches of the organization the caller may access (BR-11), sorted by
    /// name ascending.
    /// </summary>
    Task<IReadOnlyList<BranchListItemView>> ListAsync(
        Guid organizationId, Guid membershipId, CancellationToken cancellationToken);

    /// <summary>The branch detail, scoped by BR-11 access, or null (FR-10).</summary>
    Task<BranchDetailView?> GetDetailAsync(
        Guid organizationId, Guid membershipId, Guid branchId, CancellationToken cancellationToken);

    /// <summary>
    /// The tracked branch row for a Manage-policy write, org-scoped only (no
    /// BR-11 filter: the Manage policy already restricts callers to owner),
    /// or null (FR-10).
    /// </summary>
    Task<Branch?> GetForManageAsync(Guid organizationId, Guid branchId, CancellationToken cancellationToken);

    /// <summary>Active and inactive branch count for the organization (BR-05).</summary>
    Task<int> CountAsync(Guid organizationId, CancellationToken cancellationToken);

    /// <summary>Whether <paramref name="code"/> is already used by another branch (BR-04).</summary>
    Task<bool> CodeExistsAsync(
        Guid organizationId, string code, Guid? excludeBranchId, CancellationToken cancellationToken);

    /// <summary>
    /// Inserts the branch and its <c>branch.created</c> audit row in one
    /// <c>SaveChangesAsync</c> call. Throws
    /// <see cref="DuplicateBranchCodeException"/> on a concurrent
    /// unique-constraint race.
    /// </summary>
    Task CreateAsync(Branch branch, AuditLog auditLog, CancellationToken cancellationToken);

    /// <summary>
    /// Saves the already-mutated <paramref name="branch"/> and its audit row
    /// in one <c>SaveChangesAsync</c> call. Returns <c>false</c> (stale,
    /// BR-07) on a concurrency conflict; throws
    /// <see cref="DuplicateBranchCodeException"/> on a concurrent
    /// unique-constraint race.
    /// </summary>
    Task<bool> TrySaveUpdateAsync(Branch branch, AuditLog auditLog, CancellationToken cancellationToken);

    /// <summary>
    /// Deactivates a branch inside a row-locking transaction that also
    /// enforces BR-06 (the last active branch cannot be deactivated) and
    /// writes the audit row only when the state actually changes.
    /// </summary>
    Task<DeactivateBranchOutcome> DeactivateAsync(
        Guid organizationId,
        Guid branchId,
        Guid actorUserId,
        IPAddress? clientIp,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    /// <summary>
    /// Makes an active branch the organization's only main branch (BR-11)
    /// inside the same row-locking transaction as deactivation, writing the
    /// <c>branch.set_as_main</c> audit row only when the state changes.
    /// </summary>
    Task<SetMainBranchOutcome> SetMainAsync(
        Guid organizationId,
        Guid branchId,
        Guid actorUserId,
        IPAddress? clientIp,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reactivates a branch and writes the audit row only when the state
    /// actually changes (BR-08).
    /// </summary>
    Task<ReactivateBranchOutcome> ReactivateAsync(
        Guid organizationId,
        Guid branchId,
        Guid actorUserId,
        IPAddress? clientIp,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}
