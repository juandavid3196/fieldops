namespace FieldOps.Application.Features.PortalAccess;

/// <summary>
/// The outcome of a portal use case, mapped to HTTP by the controllers. <see cref="NotFound"/> is the one identical
/// "portal_resource_unavailable" answer for unknown, foreign-customer and foreign-organization ids (customer portal BR-37).
/// </summary>
public abstract record PortalOutcome<T>
{
    private PortalOutcome()
    {
    }

    public sealed record Ok(T Value) : PortalOutcome<T>;

    /// <summary>A resource created by the call (201).</summary>
    public sealed record Created(T Value) : PortalOutcome<T>;

    public sealed record Invalid(IReadOnlyDictionary<string, string[]> Errors) : PortalOutcome<T>;

    public sealed record NotFound : PortalOutcome<T>;

    public sealed record Gone : PortalOutcome<T>;

    public sealed record Unauthorized : PortalOutcome<T>;

    public sealed record Conflict(string Code, string Title) : PortalOutcome<T>;

    public sealed record Throttled(TimeSpan RetryAfter) : PortalOutcome<T>;

    public static PortalOutcome<T> Failure(string key, string message) =>
        new Invalid(new Dictionary<string, string[]>(StringComparer.Ordinal) { [key] = [message] });
}

public static class PortalMessages
{
    public const string UnavailableCode = "portal_resource_unavailable";

    public const string UnavailableTitle = "This item isn't available.";

    public const string AccountUnavailableCode = "portal_account_unavailable";

    public const string InvitationUnavailableCode = "portal_invitation_unavailable";

    public const string InvitationUnavailableTitle = "This invitation isn't available.";

    public const string InvitationIneligibleCode = "portal_invitation_ineligible";

    public const string InvitationIneligibleTitle = "This account can't be linked.";

    public const string InviteUnavailableCode = "portal_invite_unavailable";

    public const string InviteUnavailableTitle = "Portal access can't be sent for this contact.";

    public const string PropertyChangedCode = "property_changed";

    public const string PropertyChangedTitle = "This property changed. Reload and try again.";

    public const string RescheduleUnavailableCode = "reschedule_not_available";

    public const string RescheduleUnavailableTitle = "This appointment can't be rescheduled online.";

    public const string RescheduleExistsCode = "reschedule_already_requested";

    public const string RescheduleExistsTitle = "You already asked to reschedule this appointment.";

    public const string MessagingUnavailableCode = "messaging_unavailable";

    public const string MessagingUnavailableTitle = "Messages can't be sent right now.";

    public const string RequestsUnavailableCode = "requests_unavailable";
}
