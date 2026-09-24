using Microsoft.AspNetCore.Authentication.Cookies;

namespace FieldOps.Api.Authentication;

/// <summary>
/// Session cookie scheme, name and lifetimes.
/// </summary>
public static class SessionCookie
{
    public const string Scheme = CookieAuthenticationDefaults.AuthenticationScheme;

    public const string Name = "fieldops_session";

    /// <summary>Remember me off: absolute lifetime, never renewed.</summary>
    public static readonly TimeSpan BrowserSessionLifetime = TimeSpan.FromHours(8);

    /// <summary>Remember me on: sliding idle lifetime.</summary>
    public static readonly TimeSpan RememberMeIdleLifetime = TimeSpan.FromDays(14);

    /// <summary>Remember me on: absolute limit after sign-in.</summary>
    public static readonly TimeSpan RememberMeAbsoluteLifetime = TimeSpan.FromDays(30);
}
