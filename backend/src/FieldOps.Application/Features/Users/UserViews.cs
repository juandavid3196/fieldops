namespace FieldOps.Application.Features.Users;

public sealed record BranchRefView(Guid Id, string Name);

public sealed record TeamProfileView(bool Applicable, string? Name);

/// <summary>A row of the users table (BR-04).</summary>
public sealed record UserRowView(
    Guid Id,
    string Kind,
    string FirstName,
    string LastName,
    string Email,
    string RoleCode,
    string RoleName,
    bool IsAllBranches,
    IReadOnlyList<BranchRefView> Branches,
    TeamProfileView TeamProfile,
    string Status,
    bool IsExpired,
    DateTimeOffset? LastActiveAt,
    bool IsCurrentUser,
    bool IsLastOwner);

public sealed record UserListPageView(
    IReadOnlyList<UserRowView> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record UserSummaryView(
    int ActiveUsers,
    int PendingInvitations,
    int SuspendedUsers,
    int Owners);

public static class UserRowKinds
{
    public const string Member = "member";

    public const string Invitation = "invitation";
}

public static class UserRowStatuses
{
    public const string Active = "active";

    public const string Suspended = "suspended";

    public const string PendingInvitation = "pending_invitation";
}
