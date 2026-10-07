using FieldOps.Api.Authentication;
using FieldOps.Api.Authorization;
using FieldOps.Application.Features.TechnicianVisits;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FieldOps.Api.Controllers;

/// <summary>
/// The signed-in technician's own visits (technician-todays-jobs). No parameter carries a technician or
/// organization identifier; both come from the session and the caller's linked profile.
/// </summary>
[Route("technician")]
public sealed class TechnicianController(
    GetTodayVisitsHandler todayHandler,
    GetTechnicianVisitHandler visitHandler) : ControllerBase
{
    [HttpGet("today")]
    [Authorize(Policy = TeamPolicies.Self)]
    [ProducesResponseType<TodayJobs>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Today(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        var result = await todayHandler.HandleAsync(ticket.OrganizationId, ticket.MembershipId, cancellationToken);

        return result.Kind == TechnicianVisitResultKind.Succeeded ? Ok(result.Value) : MapFailure(result);
    }

    [HttpGet("visits/{visitId:guid}")]
    [Authorize(Policy = TeamPolicies.Self)]
    [ProducesResponseType<TechnicianVisitDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetVisit(Guid visitId, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        var result = await visitHandler.HandleAsync(ticket.OrganizationId, ticket.MembershipId, visitId, cancellationToken);

        return result.Kind == TechnicianVisitResultKind.Succeeded ? Ok(result.Value) : MapFailure(result);
    }

    private IActionResult MapFailure<T>(TechnicianVisitResult<T> result)
    {
        if (result.Kind == TechnicianVisitResultKind.Forbidden)
        {
            var forbidden = new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "Your technician profile is inactive.",
            };
            forbidden.Extensions["code"] = result.Code;

            return new ObjectResult(forbidden) { StatusCode = StatusCodes.Status403Forbidden };
        }

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status404NotFound,
            Title = result.Code is null ? "This job isn't available." : "Your team profile isn't linked yet.",
        };

        if (result.Code is not null)
        {
            problem.Extensions["code"] = result.Code;
        }

        return NotFound(problem);
    }
}
