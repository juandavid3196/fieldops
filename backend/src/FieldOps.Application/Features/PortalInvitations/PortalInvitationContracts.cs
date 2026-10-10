using System.Net;
using FieldOps.Application.Features.PortalAccess;
using FieldOps.Application.Features.PortalAuth;

namespace FieldOps.Application.Features.PortalInvitations;

public static class PortalStatusCodes
{
    public const string Active = "active";

    public const string Invited = "invited";

    public const string NotInvited = "not_invited";
}

/// <summary>The portal status of a customer's primary contact (customer portal BR-10).</summary>
public sealed record PortalStatusView(string PortalStatus, DateOnly? PortalLinkedOn, DateOnly? InvitationExpiresOn);

/// <summary>The only data an invitation token holder may read (customer portal BR-12).</summary>
public sealed record PortalInvitationDetails(
    string OrganizationName,
    string FirstName,
    string Email,
    bool AccountExists,
    bool LastNameRequired);

public sealed record ValidatePortalInvitationCommand(string? Token);

public sealed record AcceptPortalInvitationCommand(string? Token, string? Password, string? LastName, IPAddress? ClientIp);

public sealed record AcceptExistingPortalInvitationCommand(string? Token, string? Password, IPAddress? ClientIp);

/// <summary>A committed activation: the session to issue after the commit.</summary>
public sealed record PortalAcceptedInvitation(PortalSessionContext Session, DateTimeOffset SignedInAt);

public sealed record PortalInvitationEmailData(
    string RecipientEmail,
    string FirstName,
    string OrganizationName,
    DateOnly ExpiresOn,
    string RawToken);

public abstract record PortalInviteResult
{
    private PortalInviteResult()
    {
    }

    public sealed record NotFound : PortalInviteResult;

    /// <summary>The customer or its primary contact is inactive, the contact has no email or is already linked (BR-11).</summary>
    public sealed record Unavailable : PortalInviteResult;

    public sealed record Sent(PortalStatusView Status, PortalInvitationEmailData Email) : PortalInviteResult;
}

public sealed record PortalUsableInvitation(PortalInvitationDetails Details, string NormalizedEmail);

public enum PortalActivationFailure
{
    /// <summary>Unusable invitation (BR-12), also under the lock.</summary>
    Gone,

    /// <summary>The user is not active, has no account for a new-user activation, or is already linked in the organization (BR-13).</summary>
    Ineligible,

    LastNameRequired,
}

public abstract record PortalActivationResult
{
    private PortalActivationResult()
    {
    }

    public sealed record Activated(PortalAcceptedInvitation Accepted) : PortalActivationResult;

    public sealed record Failed(PortalActivationFailure Failure) : PortalActivationResult;
}

/// <summary>Persistence of portal invitations, activation and the staff status (customer portal BR-10 … BR-14).</summary>
public interface IPortalInvitationStore
{
    Task<PortalStatusView?> GetStatusAsync(Guid organizationId, Guid customerId, string timezone, CancellationToken cancellationToken);

    /// <summary>Revokes the open invitation and inserts a new one in one transaction (BR-11); retried once on a unique violation.</summary>
    Task<PortalInviteResult> InviteAsync(
        Guid organizationId,
        Guid customerId,
        Guid invitedByUserId,
        string rawToken,
        string tokenHash,
        IPAddress? clientIp,
        CancellationToken cancellationToken);

    /// <summary>Unlinks the contact and revokes open invitations in one transaction (BR-14); idempotent. Null when the customer has no primary contact.</summary>
    Task<PortalStatusView?> RemoveAccessAsync(
        Guid organizationId,
        Guid customerId,
        Guid actorUserId,
        IPAddress? clientIp,
        CancellationToken cancellationToken);

    /// <summary>Read-only; null unless the invitation is usable (BR-12).</summary>
    Task<PortalUsableInvitation?> FindUsableAsync(string tokenHash, CancellationToken cancellationToken);

    Task<PortalActivationResult> AcceptNewUserAsync(
        string tokenHash, string passwordHash, string? lastName, IPAddress? clientIp, CancellationToken cancellationToken);

    Task<PortalActivationResult> AcceptExistingUserAsync(
        string tokenHash, Guid userId, IPAddress? clientIp, CancellationToken cancellationToken);

    /// <summary>The user with the normalized email, for the existing-account password proof.</summary>
    Task<(Guid UserId, string PasswordHash)?> FindUserCredentialsAsync(string normalizedEmail, CancellationToken cancellationToken);
}

/// <summary>Builds the portal links of the emails from the raw token (the token travels in the fragment).</summary>
public interface IPortalLinkBuilder
{
    string BuildActivationLink(string rawToken);

    string BuildResetLink(string rawToken);
}

/// <summary>Sends the invitation email after the commit; a failure never reaches the caller (logged by category only).</summary>
public interface IPortalInvitationNotifier
{
    Task SendAsync(PortalInvitationEmailData email, string link, CancellationToken cancellationToken);
}
