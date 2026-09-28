using System.Net;

namespace FieldOps.Application.Features.Branches;

/// <summary>Thin wrapper over the store's reactivate operation (FR-09, BR-08).</summary>
public sealed class ReactivateBranchHandler(IBranchStore store, TimeProvider timeProvider)
{
    public Task<ReactivateBranchOutcome> HandleAsync(
        Guid organizationId,
        Guid branchId,
        Guid actorUserId,
        IPAddress? clientIp,
        CancellationToken cancellationToken) =>
        store.ReactivateAsync(organizationId, branchId, actorUserId, clientIp, timeProvider.GetUtcNow(), cancellationToken);
}
