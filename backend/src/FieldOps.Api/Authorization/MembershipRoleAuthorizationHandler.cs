using FieldOps.Api.Authentication;
using Microsoft.AspNetCore.Authorization;

namespace FieldOps.Api.Authorization;

/// <summary>
/// Reads the role from the request's already-validated session
/// (<see cref="SessionCookieEvents.GetValidatedSession"/>, the existing
/// source of truth, AS-01) and succeeds when it is in the requirement's
/// allowed list. <c>[Authorize]</c> already requires authentication first,
/// so a mismatch here is purely the 403 case (BR-10).
/// </summary>
public sealed class MembershipRoleAuthorizationHandler(IHttpContextAccessor httpContextAccessor)
    : AuthorizationHandler<MembershipRoleRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        MembershipRoleRequirement requirement)
    {
        var httpContext = httpContextAccessor.HttpContext;
        var session = httpContext is null ? null : SessionCookieEvents.GetValidatedSession(httpContext);

        if (session is not null
            && requirement.AllowedRoleCodes.Contains(session.Role.Code, StringComparer.Ordinal))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
