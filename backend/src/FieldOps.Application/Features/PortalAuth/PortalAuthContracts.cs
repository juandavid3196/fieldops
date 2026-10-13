using FieldOps.Application.Authentication;
using FieldOps.Application.Features.PortalAccess;
using FieldOps.Domain.Notifications;

namespace FieldOps.Application.Features.PortalAuth;

public sealed record PortalSessionUser(string FirstName, string LastName, string Email);

public sealed record PortalSessionAccount(Guid ContactId, string CustomerName, string OrganizationName, bool HasLogo);

public sealed record PortalSessionAccountSummary(Guid ContactId, string CustomerName, string OrganizationName);

/// <summary>
/// The portal session as returned by the API (customer portal BR-05). Only the contact ids of the user's own links
/// appear; no organization or customer id is exposed.
/// </summary>
public sealed record PortalSessionView(
    PortalSessionUser User,
    PortalSessionAccount Account,
    IReadOnlyList<PortalSessionAccountSummary> Accounts);

/// <summary>One portal link of a user: the contact, its customer and organization (customer portal BR-02).</summary>
public sealed record PortalLink(
    Guid UserId,
    Guid ContactId,
    Guid CustomerId,
    Guid OrganizationId,
    string CustomerName,
    string OrganizationName,
    bool HasLogo,
    DateTimeOffset LinkedAt);

/// <summary>A revalidated portal session: the response body and the scope every portal query starts from.</summary>
public sealed record PortalSessionContext(PortalSessionView View, PortalScope Scope);

public abstract record PortalSignInResult
{
    private PortalSignInResult()
    {
    }

    public sealed record Succeeded(PortalSessionContext Session, DateTimeOffset SignedInAt) : PortalSignInResult;

    public sealed record Invalid(IReadOnlyDictionary<string, string[]> Errors) : PortalSignInResult;

    /// <summary>Every credential failure; the category is for logs only (customer portal BR-03).</summary>
    public sealed record InvalidCredentials(SignInFailureCategory Category, Guid? UserId) : PortalSignInResult;

    public sealed record Throttled(TimeSpan RetryAfter) : PortalSignInResult;
}

public static class PortalAuditActions
{
    public const string SignedIn = "portal.signed_in";

    public const string AccountSwitched = "portal.account_switched";

    public const string AccessActivated = "portal.access_activated";

    public const string AccessRemoved = "portal.access_removed";

    public const string InvitationSent = "portal.invitation_sent";

    public const string MessageSent = "portal.message_sent";

    public const string ContactEntityType = "customer_contact";
}

/// <summary>Persistence of the portal session: the user's active links and the per-request revalidation (BR-06).</summary>
public interface IPortalAuthenticationStore
{
    /// <summary>Every active link of the user (BR-02), ordered by link time then contact id (BR-04).</summary>
    Task<IReadOnlyList<PortalLink>> GetActiveLinksAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the session when the user is active, the password did not change after <paramref name="signedInAt"/> and
    /// the contact is an active link of the user with the claimed customer and organization; otherwise null.
    /// </summary>
    Task<PortalSessionContext?> FindActiveLinkAsync(
        Guid userId,
        Guid contactId,
        Guid customerId,
        Guid organizationId,
        DateTimeOffset signedInAt,
        CancellationToken cancellationToken);

    /// <summary>Writes one audit row of a portal session event.</summary>
    Task SaveAuditAsync(AuditLog auditLog, CancellationToken cancellationToken);
}
