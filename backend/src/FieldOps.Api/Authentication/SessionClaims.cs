using System.Globalization;
using System.Security.Claims;

namespace FieldOps.Api.Authentication;

/// <summary>
/// Claims stored in the encrypted session ticket: user id, organization id,
/// membership id, sign-in time and the Remember me flag. The ticket never
/// holds a role, email or password.
/// </summary>
public static class SessionClaims
{
    public const string OrganizationId = "fieldops:org";

    public const string MembershipId = "fieldops:membership";

    public const string SignedInAt = "fieldops:signed_in_at";

    public const string RememberMe = "fieldops:remember_me";

    public static ClaimsPrincipal CreatePrincipal(SessionTicket ticket)
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, ticket.UserId.ToString()),
                new Claim(OrganizationId, ticket.OrganizationId.ToString()),
                new Claim(MembershipId, ticket.MembershipId.ToString()),
                new Claim(
                    SignedInAt,
                    ticket.SignedInAt.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)),
                new Claim(RememberMe, ticket.RememberMe ? "true" : "false"),
            ],
            SessionCookie.Scheme);

        return new ClaimsPrincipal(identity);
    }

    public static bool TryRead(ClaimsPrincipal principal, out SessionTicket ticket)
    {
        ticket = default;

        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            || !Guid.TryParse(principal.FindFirstValue(OrganizationId), out var organizationId)
            || !Guid.TryParse(principal.FindFirstValue(MembershipId), out var membershipId)
            || !long.TryParse(
                principal.FindFirstValue(SignedInAt),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var signedInAtMilliseconds)
            || !bool.TryParse(principal.FindFirstValue(RememberMe), out var rememberMe))
        {
            return false;
        }

        DateTimeOffset signedInAt;

        try
        {
            signedInAt = DateTimeOffset.FromUnixTimeMilliseconds(signedInAtMilliseconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }

        ticket = new SessionTicket(userId, organizationId, membershipId, signedInAt, rememberMe);
        return true;
    }
}

public readonly record struct SessionTicket(
    Guid UserId,
    Guid OrganizationId,
    Guid MembershipId,
    DateTimeOffset SignedInAt,
    bool RememberMe);
