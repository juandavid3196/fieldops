using FieldOps.Application.Authentication;
using Microsoft.AspNetCore.Authentication;

namespace FieldOps.Api.Authentication;

/// <summary>
/// Issues the session cookie (sign-in contract): remember me off is a
/// browser-session cookie (no Expires) with an 8-hour ticket that is never
/// renewed; on is a persistent 14-day sliding cookie.
/// </summary>
public static class SessionCookieIssuer
{
    public static Task SignInAsync(
        HttpContext httpContext,
        SessionView session,
        Guid membershipId,
        DateTimeOffset signedInAt,
        bool rememberMe)
    {
        var principal = SessionClaims.CreatePrincipal(new SessionTicket(
            session.User.Id,
            session.Organization.Id,
            membershipId,
            signedInAt,
            rememberMe));

        var properties = new AuthenticationProperties
        {
            IssuedUtc = signedInAt,
            IsPersistent = rememberMe,
            AllowRefresh = rememberMe,
            ExpiresUtc = signedInAt + (rememberMe
                ? SessionCookie.RememberMeIdleLifetime
                : SessionCookie.BrowserSessionLifetime),
        };

        return httpContext.SignInAsync(SessionCookie.Scheme, principal, properties);
    }
}
