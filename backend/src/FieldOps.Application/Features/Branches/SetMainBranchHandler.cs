using System.Net;

namespace FieldOps.Application.Features.Branches;

/// <summary>
/// Thin wrapper over the store's row-locking BR-11 set-main transaction (FR-15).
/// </summary>
public sealed class SetMainBranchHandler(IBranchStore store, TimeProvider timeProvider)
{
    public const string SetAsMainAuditAction = "branch.set_as_main";

    public Task<SetMainBranchOutcome> HandleAsync(
        Guid organizationId,
        Guid branchId,
        Guid actorUserId,
        IPAddress? clientIp,
        CancellationToken cancellationToken) =>
        store.SetMainAsync(organizationId, branchId, actorUserId, clientIp, timeProvider.GetUtcNow(), cancellationToken);
}
