namespace FieldOps.Application.Features.Access;

/// <summary>
/// The branches a member may see: every branch (<see cref="All"/>) or only the listed ones. A missing
/// or inactive membership resolves to an empty scope, which sees nothing.
/// </summary>
public sealed record BranchScope(bool All, IReadOnlyList<Guid> BranchIds)
{
    public static BranchScope Everything { get; } = new(true, []);

    public static BranchScope Nothing { get; } = new(false, []);

    public bool Contains(Guid branchId) => All || BranchIds.Contains(branchId);
}

/// <summary>Resolves the branch scope of a session membership inside its organization (BR-02).</summary>
public interface IBranchScopeResolver
{
    Task<BranchScope> ResolveAsync(Guid organizationId, Guid membershipId, CancellationToken cancellationToken);
}
