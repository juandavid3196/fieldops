using FieldOps.Application.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace FieldOps.Api.Authentication;

/// <summary>
/// Re-checks the session on every request that carries the cookie and turns
/// challenges into plain 401/403 responses instead of redirects.
/// </summary>
public sealed class SessionCookieEvents(
    GetCurrentSessionHandler getCurrentSession,
    TimeProvider timeProvider) : CookieAuthenticationEvents
{
    private const string SessionItemKey = "FieldOps.Session";

    /// <summary>
    /// The session validated for the current request, or null.
    /// </summary>
    public static SessionView? GetValidatedSession(HttpContext httpContext) =>
        httpContext.Items.TryGetValue(SessionItemKey, out var session)
            ? session as SessionView
            : null;

    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        if (context.Principal is null || !SessionClaims.TryRead(context.Principal, out var ticket))
        {
            await RejectAsync(context);
            return;
        }

        var now = timeProvider.GetUtcNow();
        var absoluteLifetime = ticket.RememberMe
            ? SessionCookie.RememberMeAbsoluteLifetime
            : SessionCookie.BrowserSessionLifetime;

        if (now >= ticket.SignedInAt + absoluteLifetime
            || context.Properties.ExpiresUtc is not { } expiresUtc
            || expiresUtc <= now)
        {
            await RejectAsync(context);
            return;
        }

        var session = await getCurrentSession.HandleAsync(
            ticket.UserId,
            ticket.OrganizationId,
            ticket.MembershipId,
            context.HttpContext.RequestAborted);

        if (session is null)
        {
            await RejectAsync(context);
            return;
        }

        context.HttpContext.Items[SessionItemKey] = session;

        // Remember me sessions slide: every authenticated request renews the
        // 14-day idle window. Browser sessions are never renewed.
        if (ticket.RememberMe)
        {
            context.ShouldRenew = true;
        }
    }

    public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.CacheControl = "no-store";
        return Task.CompletedTask;
    }

    public override Task RedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }

    public override Task RedirectToLogout(RedirectContext<CookieAuthenticationOptions> context) =>
        Task.CompletedTask;

    public override Task RedirectToReturnUrl(RedirectContext<CookieAuthenticationOptions> context) =>
        Task.CompletedTask;

    // The request continues unauthenticated and the response deletes the cookie.
    private static Task RejectAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        return context.HttpContext.SignOutAsync(context.Scheme.Name);
    }
}
