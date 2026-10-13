using FieldOps.Api.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

namespace FieldOps.Api.Middleware;

/// <summary>
/// Deletes a session cookie when a request carried it but authentication did not succeed (tampered, foreign-key, expired or no
/// longer valid), on every endpoint including anonymous ones. Runs after UseAuthentication. Nothing is added when the response
/// already sets the cookie, such as a new sign-in or an explicit sign-out.
/// </summary>
/// <remarks>
/// Two cookies exist (customer portal BR-01). The internal cookie is authenticated by UseAuthentication on every endpoint. The
/// portal cookie is authenticated only under <c>/portal</c>: outside it, internal endpoints ignore it and it is left untouched.
/// </remarks>
public sealed class InvalidSessionCookieMiddleware(
    RequestDelegate next,
    IOptionsMonitor<CookieAuthenticationOptions> optionsMonitor)
{
    private const string PortalPath = "/portal";

    public async Task InvokeAsync(HttpContext context)
    {
        var internalOptions = optionsMonitor.Get(SessionCookie.Scheme);
        var internalName = internalOptions.Cookie.Name ?? SessionCookie.Name;

        if (context.Request.Cookies.ContainsKey(internalName)
            && context.User.Identity?.IsAuthenticated != true)
        {
            DeleteWhenNotSet(context, internalOptions, internalName);
        }

        if (context.Request.Path.StartsWithSegments(PortalPath, StringComparison.OrdinalIgnoreCase))
        {
            var portalOptions = optionsMonitor.Get(PortalSessionCookie.Scheme);
            var portalName = portalOptions.Cookie.Name ?? PortalSessionCookie.Name;

            if (context.Request.Cookies.ContainsKey(portalName)
                && !(await context.AuthenticateAsync(PortalSessionCookie.Scheme)).Succeeded)
            {
                DeleteWhenNotSet(context, portalOptions, portalName);
            }
        }

        await next(context);
    }

    private static void DeleteWhenNotSet(HttpContext context, CookieAuthenticationOptions options, string cookieName) =>
        context.Response.OnStarting(() =>
        {
            if (!SetsCookie(context.Response, cookieName))
            {
                options.CookieManager.DeleteCookie(context, cookieName, options.Cookie.Build(context));
            }

            return Task.CompletedTask;
        });

    private static bool SetsCookie(HttpResponse response, string cookieName)
    {
        foreach (var header in response.Headers.SetCookie)
        {
            if (header is not null
                && header.StartsWith(cookieName + "=", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
