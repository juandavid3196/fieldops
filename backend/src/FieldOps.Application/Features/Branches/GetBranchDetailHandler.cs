namespace FieldOps.Application.Features.Branches;

/// <summary>Reads one branch's detail, scoped by BR-11 access (FR-06, FR-10).</summary>
public sealed class GetBranchDetailHandler(IBranchStore store)
{
    public Task<BranchDetailView?> HandleAsync(
        Guid organizationId, Guid membershipId, Guid branchId, CancellationToken cancellationToken) =>
        store.GetDetailAsync(organizationId, membershipId, branchId, cancellationToken);
}
