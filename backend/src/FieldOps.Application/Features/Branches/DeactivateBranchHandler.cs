using System.Net;

namespace FieldOps.Application.Features.Branches;

/// <summary>
/// Thin wrapper over the store's row-locking BR-06 transaction (FR-09).
/// </summary>
public sealed class DeactivateBranchHandler(IBranchStore store, TimeProvider timeProvider)
{
    public Task<DeactivateBranchOutcome> HandleAsync(
        Guid organizationId,
        Guid branchId,
        Guid actorUserId,
        IPAddress? clientIp,
        CancellationToken cancellationToken) =>
        store.DeactivateAsync(organizationId, branchId, actorUserId, clientIp, timeProvider.GetUtcNow(), cancellationToken);
}
