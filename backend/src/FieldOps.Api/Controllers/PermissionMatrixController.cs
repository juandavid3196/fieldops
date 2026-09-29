using FieldOps.Api.Authorization;
using FieldOps.Api.Contracts;
using FieldOps.Application.Features.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FieldOps.Api.Controllers;

/// <summary>The read-only role and module catalog (FR-05, BR-07).</summary>
[Route("permission-matrix")]
public sealed class PermissionMatrixController(GetPermissionMatrixHandler handler) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = CompanySettingsPolicies.View)]
    [ProducesResponseType<PermissionMatrixResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public IActionResult Get()
    {
        Response.Headers.CacheControl = "no-store";

        var (roles, modules) = handler.Handle();

        return Ok(new PermissionMatrixResponse(
            [.. roles.Select(role => new PermissionMatrixRoleResponse(
                role.Code, role.Name, role.Summary, role.ForcesAllBranches, role.HasTeamProfile))],
            [.. modules.Select(module => new PermissionMatrixModuleResponse(
                module.Key,
                module.Name,
                module.Levels.ToDictionary(
                    entry => entry.Key,
                    entry => new PermissionMatrixLevelResponse(entry.Value.Level, entry.Value.Label),
                    StringComparer.Ordinal)))]));
    }
}
