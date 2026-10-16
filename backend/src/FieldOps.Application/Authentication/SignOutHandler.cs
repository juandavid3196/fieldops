using System.Net;
using FieldOps.Domain.Notifications;

namespace FieldOps.Application.Authentication;

/// <summary>
/// Records <c>auth.signed_out</c> for a valid session (audit-trail BR-18). The sign-out itself never depends on it: a
/// failed insert is swallowed here and reported to <paramref name="onFailure"/> with the exception type only, never the user,
/// the organization or the session.
/// </summary>
public sealed class SignOutHandler(IAuthenticationStore store)
{
    public const string SignedOutAuditAction = "auth.signed_out";

    /// <returns>True when the row was written.</returns>
    public async Task<bool> HandleAsync(
        Guid userId,
        Guid organizationId,
        IPAddress? clientIp,
        Action<string> onFailure,
        CancellationToken cancellationToken)
    {
        try
        {
            await store.RecordSignedOutAsync(
                AuditLog.Create(
                    organizationId,
                    SignedOutAuditAction,
                    SignInHandler.UserAuditEntityType,
                    actorUserId: userId,
                    entityId: userId,
                    ipAddress: clientIp),
                cancellationToken);

            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            onFailure(exception.GetType().Name);

            return false;
        }
    }
}
