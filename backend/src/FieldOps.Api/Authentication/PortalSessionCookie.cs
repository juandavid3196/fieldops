using System.Globalization;
using System.Security.Claims;
using FieldOps.Application.Features.PortalAuth;
using Microsoft.AspNetCore.Authentication;

namespace FieldOps.Api.Authentication;

/// <summary>
/// The portal session cookie scheme (customer portal BR-01): a second cookie, separate from the internal one, with the same
/// options and lifetimes. Every <c>/portal/*</c> endpoint but the anonymous ones authenticates only with this scheme, and
/// every internal endpoint ignores it.
/// </summary>
public static class PortalSessionCookie
{
    public const string Scheme = "PortalSession";

    public const string Name = "fieldops_portal_session";

    public static readonly TimeSpan BrowserSessionLifetime = SessionCookie.BrowserSessionLifetime;

    public static readonly TimeSpan RememberMeIdleLifetime = SessionCookie.RememberMeIdleLifetime;

    public static readonly TimeSpan RememberMeAbsoluteLifetime = SessionCookie.RememberMeAbsoluteLifetime;
}

/// <summary>The claims of the portal ticket: user, contact, customer, organization, sign-in time and Remember me. Never an email, role or password.</summary>
public static class PortalSessionClaims
{
    public const string ContactId = "fieldops:portal:contact";

    public const string CustomerId = "fieldops:portal:customer";

    public const string OrganizationId = "fieldops:portal:org";

    public const string SignedInAt = "fieldops:portal:signed_in_at";

    public const string RememberMe = "fieldops:portal:remember_me";

    public static ClaimsPrincipal CreatePrincipal(PortalSessionTicket ticket)
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, ticket.UserId.ToString()),
                new Claim(ContactId, ticket.ContactId.ToString()),
                new Claim(CustomerId, ticket.CustomerId.ToString()),
                new Claim(OrganizationId, ticket.OrganizationId.ToString()),
                new Claim(
                    SignedInAt,
                    ticket.SignedInAt.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)),
                new Claim(RememberMe, ticket.RememberMe ? "true" : "false"),
            ],
            PortalSessionCookie.Scheme);

        return new ClaimsPrincipal(identity);
    }

    public static bool TryRead(ClaimsPrincipal principal, out PortalSessionTicket ticket)
    {
        ticket = default;

        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            || !Guid.TryParse(principal.FindFirstValue(ContactId), out var contactId)
            || !Guid.TryParse(principal.FindFirstValue(CustomerId), out var customerId)
            || !Guid.TryParse(principal.FindFirstValue(OrganizationId), out var organizationId)
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

        ticket = new PortalSessionTicket(userId, contactId, customerId, organizationId, signedInAt, rememberMe);

        return true;
    }
}

public readonly record struct PortalSessionTicket(
    Guid UserId,
    Guid ContactId,
    Guid CustomerId,
    Guid OrganizationId,
    DateTimeOffset SignedInAt,
    bool RememberMe);

/// <summary>
/// Issues the portal cookie: remember me off is a browser-session cookie (no Expires) with an 8-hour ticket that is never
/// renewed; on is a persistent 14-day sliding cookie (customer portal BR-01).
/// </summary>
public static class PortalSessionCookieIssuer
{
    public static Task SignInAsync(
        HttpContext httpContext, PortalSessionContext session, DateTimeOffset signedInAt, bool rememberMe, DateTimeOffset? reissuedAt = null)
    {
        var principal = PortalSessionClaims.CreatePrincipal(new PortalSessionTicket(
            session.Scope.UserId,
            session.Scope.ContactId,
            session.Scope.CustomerId,
            session.Scope.OrganizationId,
            signedInAt,
            rememberMe));

        var properties = new AuthenticationProperties
        {
            IssuedUtc = rememberMe && reissuedAt is { } renewed ? renewed : signedInAt,
            IsPersistent = rememberMe,
            AllowRefresh = rememberMe,
            ExpiresUtc = (rememberMe && reissuedAt is { } renewedAt ? renewedAt : signedInAt) + (rememberMe
                ? PortalSessionCookie.RememberMeIdleLifetime
                : PortalSessionCookie.BrowserSessionLifetime),
        };

        return httpContext.SignInAsync(PortalSessionCookie.Scheme, principal, properties);
    }
}
