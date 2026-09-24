using FieldOps.Domain.Notifications;
using FieldOps.Domain.Users;

namespace FieldOps.Application.Authentication;

/// <summary>
/// Persistence operations needed by sign-in and session validation.
/// </summary>
public interface IAuthenticationStore
{
    /// <summary>Finds a user by normalized email, tracked for update.</summary>
    Task<User?> FindUserByEmailAsync(string normalizedEmail, CancellationToken cancellationToken);

    /// <summary>Every membership of the user, whatever its status.</summary>
    Task<IReadOnlyList<MembershipCandidate>> GetMembershipsAsync(
        Guid userId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns the session when the user is active, the membership is active
    /// and belongs to that user and organization, and the organization is
    /// active; otherwise null. The role is read from the membership.
    /// </summary>
    Task<SessionView?> FindActiveSessionAsync(
        Guid userId,
        Guid organizationId,
        Guid membershipId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Saves the user's sign-in time and the audit row in one transaction.
    /// </summary>
    Task SaveSignInAsync(User user, AuditLog auditLog, CancellationToken cancellationToken);
}
