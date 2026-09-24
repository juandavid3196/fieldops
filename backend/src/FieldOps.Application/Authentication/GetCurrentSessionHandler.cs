namespace FieldOps.Application.Authentication;

/// <summary>
/// Re-checks a session on every request: the user must be active, the
/// membership active and owned by that user and organization, and the
/// organization active. The role is always read from the membership.
/// </summary>
public sealed class GetCurrentSessionHandler(IAuthenticationStore store)
{
    public Task<SessionView?> HandleAsync(
        Guid userId,
        Guid organizationId,
        Guid membershipId,
        CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty || organizationId == Guid.Empty || membershipId == Guid.Empty)
        {
            return Task.FromResult<SessionView?>(null);
        }

        return store.FindActiveSessionAsync(
            userId,
            organizationId,
            membershipId,
            cancellationToken);
    }
}
