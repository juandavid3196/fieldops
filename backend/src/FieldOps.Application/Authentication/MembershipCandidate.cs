using FieldOps.Domain.Users;

namespace FieldOps.Application.Authentication;

/// <summary>
/// A membership of the signing-in user with the organization and role data
/// needed to resolve and describe the session.
/// </summary>
public sealed record MembershipCandidate(
    Guid MembershipId,
    Guid OrganizationId,
    string OrganizationName,
    bool OrganizationIsActive,
    UserStatus MembershipStatus,
    string RoleCode,
    string RoleName,
    DateTimeOffset? JoinedAt,
    DateTimeOffset CreatedAt);
