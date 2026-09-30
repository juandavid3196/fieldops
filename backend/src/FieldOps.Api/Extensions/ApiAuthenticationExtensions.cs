using FieldOps.Api.Authentication;
using FieldOps.Api.Authorization;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FieldOps.Api.Extensions;

public static class ApiAuthenticationExtensions
{
    /// <summary>
    /// Cookie session authentication: HttpOnly, Secure, SameSite=Strict,
    /// Path=/, no Domain; the ticket is protected by Data Protection with the
    /// default key store (key persistence is deployment work).
    /// </summary>
    public static IServiceCollection AddApiAuthentication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<SessionCookieEvents>();

        services
            .AddAuthentication(SessionCookie.Scheme)
            .AddCookie(SessionCookie.Scheme, options =>
            {
                options.Cookie.Name = SessionCookie.Name;
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.Cookie.Path = "/";
                options.Cookie.Domain = null;
                options.Cookie.IsEssential = true;

                // Remember me tickets slide over 14 days. Browser-session
                // tickets set their own 8-hour expiry and disallow refresh.
                options.ExpireTimeSpan = SessionCookie.RememberMeIdleLifetime;
                options.SlidingExpiration = true;
                options.EventsType = typeof(SessionCookieEvents);
            });

        // The ticket clock follows the registered TimeProvider.
        services
            .AddOptions<CookieAuthenticationOptions>(SessionCookie.Scheme)
            .Configure<TimeProvider>((options, timeProvider) => options.TimeProvider = timeProvider);

        // BR-10: owner/viewer read the GET endpoints; owner only writes.
        // AddAuthorizationBuilder still configures the same default policy
        // (authenticated user) that bare [Authorize] endpoints (e.g.
        // SessionsController) rely on.
        services.AddHttpContextAccessor();
        services.AddScoped<IAuthorizationHandler, MembershipRoleAuthorizationHandler>();
        services.AddAuthorizationBuilder()
            .AddPolicy(CompanySettingsPolicies.View, policy =>
                policy.Requirements.Add(new MembershipRoleRequirement("owner", "viewer")))
            .AddPolicy(CompanySettingsPolicies.Manage, policy =>
                policy.Requirements.Add(new MembershipRoleRequirement("owner")))
            .AddPolicy(CatalogPolicies.View, policy =>
                policy.Requirements.Add(new MembershipRoleRequirement(
                    "owner", "operations_manager", "dispatcher", "accounting", "viewer")))
            .AddPolicy(CatalogPolicies.Manage, policy =>
                policy.Requirements.Add(new MembershipRoleRequirement("owner", "operations_manager")))
            .AddPolicy(CatalogPolicies.Import, policy =>
                policy.Requirements.Add(new MembershipRoleRequirement("owner")));

        return services;
    }
}
