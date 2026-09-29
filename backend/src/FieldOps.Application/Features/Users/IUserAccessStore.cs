using System.Net;

namespace FieldOps.Application.Features.Users;

/// <summary>Persistence for users, invitations and access (FR-02 to FR-13). Every call is scoped to the session organization.</summary>
public interface IUserAccessStore
{
    Task<UserListPageView> ListAsync(
        Guid organizationId, Guid currentMembershipId, UserListFilter filter, CancellationToken cancellationToken);

    Task<UserSummaryView> GetSummaryAsync(Guid organizationId, CancellationToken cancellationToken);

    /// <summary>
    /// Creates the invitation, branch rows and audit row in one transaction and
    /// calls <paramref name="beforeCommit"/> before commit; an exception from it
    /// rolls back and returns <see cref="UserResultKind.DeliveryFailed"/>.
    /// </summary>
    Task<UserResult<UserRowView>> CreateInvitationAsync(
        CreateInvitationRequest request,
        Func<InvitationDeliveryContext, CancellationToken, Task> beforeCommit,
        CancellationToken cancellationToken);

    Task<UserResult<UserRowView>> ResendInvitationAsync(
        Guid organizationId,
        Guid actorUserId,
        IPAddress? clientIp,
        Guid invitationId,
        string newTokenHash,
        DateTimeOffset expiresAt,
        Func<InvitationDeliveryContext, CancellationToken, Task> beforeCommit,
        CancellationToken cancellationToken);

    Task<UserResult<NoValue>> RevokeInvitationAsync(
        Guid organizationId,
        Guid actorUserId,
        IPAddress? clientIp,
        Guid invitationId,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<UserResult<UserRowView>> UpdateInvitationAccessAsync(
        Guid organizationId,
        Guid actorUserId,
        IPAddress? clientIp,
        Guid invitationId,
        AccessSpec access,
        CancellationToken cancellationToken);

    Task<UserResult<UserRowView>> UpdateMemberAccessAsync(
        Guid organizationId,
        Guid actorUserId,
        Guid actorMembershipId,
        IPAddress? clientIp,
        Guid membershipId,
        AccessSpec access,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<UserResult<NoValue>> SuspendMemberAsync(
        Guid organizationId,
        Guid actorUserId,
        Guid actorMembershipId,
        IPAddress? clientIp,
        Guid membershipId,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<UserResult<NoValue>> ReactivateMemberAsync(
        Guid organizationId,
        Guid actorUserId,
        IPAddress? clientIp,
        Guid membershipId,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}
