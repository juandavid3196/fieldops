using Microsoft.AspNetCore.Authorization;

namespace FieldOps.Api.Authorization;

/// <summary>Satisfied when the validated session's membership role is one of the allowed codes.</summary>
public sealed class MembershipRoleRequirement(params string[] allowedRoleCodes) : IAuthorizationRequirement
{
    public IReadOnlyCollection<string> AllowedRoleCodes { get; } = allowedRoleCodes;
}
