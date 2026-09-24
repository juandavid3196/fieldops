using FieldOps.Api.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

namespace FieldOps.Api.Middleware;

/// <summary>
/// Deletes the session cookie when a request carried it but authentication
/// did not succeed (tampered, foreign-key, expired or no longer valid), on
/// every endpoint including anonymous ones. Runs after UseAuthentication.
/// Nothing is added when the response already sets the cookie, such as a new
/// sign-in or an explicit sign-out.
/// </summary>
public sealed class InvalidSessionCookieMiddleware(
    RequestDelegate next,
    IOptionsMonitor<CookieAuthenticationOptions> optionsMonitor)
{
    public Task InvokeAsync(HttpContext context)
    {
        var options = optionsMonitor.Get(SessionCookie.Scheme);
        var cookieName = options.Cookie.Name ?? SessionCookie.Name;

        if (context.Request.Cookies.ContainsKey(cookieName)
            && context.User.Identity?.IsAuthenticated != true)
        {
            context.Response.OnStarting(() =>
            {
                if (!SetsCookie(context.Response, cookieName))
                {
                    options.CookieManager.DeleteCookie(
                        context,
                        cookieName,
                        options.Cookie.Build(context));
                }

                return Task.CompletedTask;
            });
        }

        return next(context);
    }

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
