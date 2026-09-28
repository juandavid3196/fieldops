namespace FieldOps.Application.Features.Branches;

/// <summary>Lists branches the caller may access (FR-05, BR-11).</summary>
public sealed class ListBranchesHandler(IBranchStore store)
{
    public Task<IReadOnlyList<BranchListItemView>> HandleAsync(
        Guid organizationId, Guid membershipId, CancellationToken cancellationToken) =>
        store.ListAsync(organizationId, membershipId, cancellationToken);
}
