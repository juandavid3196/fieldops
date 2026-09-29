using System.Net;

namespace FieldOps.Application.Features.Users;

/// <summary>Invite body (BR-08); organization and actor come from the session only.</summary>
public sealed record InviteUserCommand(
    Guid OrganizationId,
    Guid ActorUserId,
    IPAddress? ClientIp,
    string? Email,
    string? FirstName,
    string? LastName,
    string? RoleCode,
    bool? IsAllBranches,
    IReadOnlyList<string>? BranchIds,
    bool? LinkTeamProfile,
    int? ExpiresInDays);

/// <summary>Edit access body (BR-09) for a member or an invitation.</summary>
public sealed record UpdateAccessCommand(
    Guid OrganizationId,
    Guid ActorUserId,
    Guid TargetId,
    IPAddress? ClientIp,
    string? RoleCode,
    bool? IsAllBranches,
    IReadOnlyList<string>? BranchIds);

/// <summary>Validated access: forced roles are all-branches with no branch rows.</summary>
public sealed record AccessSpec(string RoleCode, bool IsAllBranches, IReadOnlyList<Guid> BranchIds);

/// <summary>Raw query of GET /users (BR-03); every value is a string so bad input maps to a 400 key.</summary>
public sealed record ListUsersQuery(
    Guid OrganizationId,
    Guid CurrentMembershipId,
    string? Search,
    string? RoleCode,
    string? BranchId,
    string? Status,
    string? Sort,
    string? Page,
    string? PageSize);

public sealed record UserListFilter(
    string? Search,
    string? RoleCode,
    Guid? BranchId,
    string? Status,
    bool Descending,
    int Page,
    int PageSize);

public sealed record CreateInvitationRequest(
    Guid OrganizationId,
    Guid ActorUserId,
    IPAddress? ClientIp,
    string Email,
    string FirstName,
    string LastName,
    AccessSpec Access,
    bool LinkTeamProfile,
    string TokenHash,
    DateTimeOffset ExpiresAt);
