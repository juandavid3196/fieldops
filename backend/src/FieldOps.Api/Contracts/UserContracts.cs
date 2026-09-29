using FieldOps.Application.Features.Users;

namespace FieldOps.Api.Contracts;

/// <summary>
/// Body of POST /users/invitations (BR-08). No organization identifier is
/// accepted; unknown properties are ignored.
/// </summary>
public sealed record InviteUserRequest(
    string? Email,
    string? FirstName,
    string? LastName,
    string? RoleCode,
    bool? IsAllBranches,
    string[]? BranchIds,
    bool? LinkTeamProfile,
    int? ExpiresInDays);

/// <summary>Body of the two PUT access endpoints (BR-09).</summary>
public sealed record UpdateAccessRequest(
    string? RoleCode,
    bool? IsAllBranches,
    string[]? BranchIds);

/// <summary>Query of GET /users (BR-03); strings so bad input maps to a 400 key.</summary>
public sealed class ListUsersRequest
{
    public string? Search { get; init; }

    public string? RoleCode { get; init; }

    public string? BranchId { get; init; }

    public string? Status { get; init; }

    public string? Sort { get; init; }

    public string? Page { get; init; }

    public string? PageSize { get; init; }
}

public sealed record BranchRefResponse(Guid Id, string Name);

public sealed record TeamProfileResponse(bool Applicable, string? Name);

public sealed record UserRowResponse(
    Guid Id,
    string Kind,
    string FirstName,
    string LastName,
    string Email,
    string RoleCode,
    string RoleName,
    bool IsAllBranches,
    IReadOnlyList<BranchRefResponse> Branches,
    TeamProfileResponse TeamProfile,
    string Status,
    bool IsExpired,
    DateTimeOffset? LastActiveAt,
    bool IsCurrentUser,
    bool IsLastOwner)
{
    public static UserRowResponse From(UserRowView view) =>
        new(
            view.Id,
            view.Kind,
            view.FirstName,
            view.LastName,
            view.Email,
            view.RoleCode,
            view.RoleName,
            view.IsAllBranches,
            [.. view.Branches.Select(branch => new BranchRefResponse(branch.Id, branch.Name))],
            new TeamProfileResponse(view.TeamProfile.Applicable, view.TeamProfile.Name),
            view.Status,
            view.IsExpired,
            view.LastActiveAt,
            view.IsCurrentUser,
            view.IsLastOwner);
}

public sealed record UserListResponse(
    IReadOnlyList<UserRowResponse> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record UserSummaryResponse(
    int ActiveUsers,
    int PendingInvitations,
    int SuspendedUsers,
    int Owners);

public sealed record PermissionMatrixResponse(
    IReadOnlyList<PermissionMatrixRoleResponse> Roles,
    IReadOnlyList<PermissionMatrixModuleResponse> Modules);

public sealed record PermissionMatrixRoleResponse(
    string Code,
    string Name,
    string Summary,
    bool ForcesAllBranches,
    bool HasTeamProfile);

public sealed record PermissionMatrixModuleResponse(
    string Key,
    string Name,
    IReadOnlyDictionary<string, PermissionMatrixLevelResponse> Levels);

public sealed record PermissionMatrixLevelResponse(string Level, string Label);
