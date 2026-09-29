namespace FieldOps.Application.Features.Users;

/// <summary>Role and branch rules shared by invite and edit-access (BR-08, BR-09).</summary>
public static class AccessValidation
{
    public const string RoleMessage = "Select a role.";

    public const string BranchRequiredMessage = "Select at least one branch.";

    /// <summary>Adds failures for the role and branch keys; format checks only (branch existence is checked in the store).</summary>
    public static void Validate(
        string? roleCode,
        bool? isAllBranches,
        IReadOnlyList<string>? branchIds,
        Action<string, string> addFailure)
    {
        var role = PermissionCatalog.Find(roleCode);

        if (role is null)
        {
            addFailure("roleCode", RoleMessage);
            return;
        }

        if (role.ForcesAllBranches || isAllBranches == true)
        {
            return;
        }

        if (branchIds is null || branchIds.Count == 0)
        {
            addFailure("branchIds", BranchRequiredMessage);
            return;
        }

        if (branchIds.Any(id => !Guid.TryParse(id, out var parsed) || parsed == Guid.Empty))
        {
            addFailure("branchIds", UserMessages.InvalidBranch);
        }
    }

    /// <summary>Normalizes already-validated input; forced or all-branches roles carry no branch ids.</summary>
    public static AccessSpec Resolve(string? roleCode, bool? isAllBranches, IReadOnlyList<string>? branchIds)
    {
        var role = PermissionCatalog.Find(roleCode)!;
        var allBranches = role.ForcesAllBranches || isAllBranches == true;

        IReadOnlyList<Guid> ids = allBranches
            ? []
            : [.. (branchIds ?? []).Select(Guid.Parse).Distinct()];

        return new AccessSpec(role.Code, allBranches, ids);
    }
}
