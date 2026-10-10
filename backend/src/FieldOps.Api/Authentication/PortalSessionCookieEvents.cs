using FieldOps.Application.Features.PortalAuth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace FieldOps.Api.Authentication;

/// <summary>
/// Re-checks the portal session on every request that carries its cookie (customer portal BR-06) and turns challenges into
/// plain 401 responses with no body and no redirect. A failure deletes the portal cookie only.
/// </summary>
public sealed class PortalSessionCookieEvents(
    GetCurrentPortalSessionHandler getCurrentSession,
    TimeProvider timeProvider) : CookieAuthenticationEvents
{
    private const string SessionItemKey = "FieldOps.PortalSession";

    /// <summary>The portal session validated for the current request, or null.</summary>
    public static PortalSessionContext? GetValidatedSession(HttpContext httpContext) =>
        httpContext.Items.TryGetValue(SessionItemKey, out var session) ? session as PortalSessionContext : null;

    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        if (context.Principal is null || !PortalSessionClaims.TryRead(context.Principal, out var ticket))
        {
            await RejectAsync(context);

            return;
        }

        var now = timeProvider.GetUtcNow();
        var absoluteLifetime = ticket.RememberMe
            ? PortalSessionCookie.RememberMeAbsoluteLifetime
            : PortalSessionCookie.BrowserSessionLifetime;

        if (now >= ticket.SignedInAt + absoluteLifetime
            || context.Properties.ExpiresUtc is not { } expiresUtc
            || expiresUtc <= now)
        {
            await RejectAsync(context);

            return;
        }

        var session = await getCurrentSession.HandleAsync(
            ticket.UserId,
            ticket.ContactId,
            ticket.CustomerId,
            ticket.OrganizationId,
            ticket.SignedInAt,
            context.HttpContext.RequestAborted);

        if (session is null)
        {
            await RejectAsync(context);

            return;
        }

        context.HttpContext.Items[SessionItemKey] = session;

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
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.CacheControl = "no-store";

        return Task.CompletedTask;
    }

    public override Task RedirectToLogout(RedirectContext<CookieAuthenticationOptions> context) => Task.CompletedTask;

    public override Task RedirectToReturnUrl(RedirectContext<CookieAuthenticationOptions> context) => Task.CompletedTask;

    // The request continues unauthenticated and the response deletes the portal cookie.
    private static Task RejectAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();

        return context.HttpContext.SignOutAsync(context.Scheme.Name);
    }
}
