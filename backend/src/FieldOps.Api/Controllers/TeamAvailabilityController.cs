using System.Net;
using FieldOps.Api.Authentication;
using FieldOps.Api.Authorization;
using FieldOps.Application.Features.Team;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace FieldOps.Api.Controllers;

/// <summary>
/// Availability grid, skills matrix and their drawers (team-availability-skills FR-02 to FR-11). The organization and
/// branch scope come from the session membership; no endpoint accepts an organization identifier.
/// </summary>
/// <remarks>Not an [ApiController]: model state is checked here, per <see cref="TeamController"/>.</remarks>
[Route("team")]
public sealed class TeamAvailabilityController(
    GetTeamAvailabilityHandler gridHandler,
    GetSkillsMatrixHandler matrixHandler,
    TechnicianAvailabilityHandler availabilityHandler,
    TechnicianTimeOffHandler timeOffHandler,
    TechnicianSkillAssignmentsHandler assignmentsHandler) : ControllerBase
{
    private const int MaxJsonBodyBytes = TeamController.MaxJsonBodyBytes;

    [HttpGet("availability")]
    [Authorize(Policy = TeamPolicies.View)]
    [ProducesResponseType<TeamAvailabilityPage>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Grid(
        [FromQuery] string? weekStart,
        [FromQuery] string? search,
        [FromQuery] string? branchId,
        [FromQuery] string? availability,
        [FromQuery] string? page,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        var result = await gridHandler.HandleAsync(
            ticket.OrganizationId,
            ticket.MembershipId,
            new TeamAvailabilityQuery(weekStart, search, branchId, availability, page),
            cancellationToken);

        return result.Kind == TeamResultKind.Succeeded ? Ok(result.Value) : MapFailure(result);
    }

    [HttpGet("skills-matrix")]
    [Authorize(Policy = TeamPolicies.View)]
    [ProducesResponseType<SkillsMatrixPage>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Matrix(
        [FromQuery] string? search,
        [FromQuery] string? branchId,
        [FromQuery] string? skillId,
        [FromQuery] string? coverage,
        [FromQuery] string? sort,
        [FromQuery] string? page,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        var result = await matrixHandler.HandleAsync(
            ticket.OrganizationId,
            ticket.MembershipId,
            new SkillsMatrixQuery(search, branchId, skillId, coverage, sort, page),
            cancellationToken);

        return result.Kind == TeamResultKind.Succeeded ? Ok(result.Value) : MapFailure(result);
    }

    [HttpGet("technicians/{id:guid}/availability")]
    [Authorize(Policy = TeamPolicies.Manage)]
    [ProducesResponseType<TechnicianAvailabilityView>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAvailability(Guid id, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        var result = await availabilityHandler.GetAsync(ticket.OrganizationId, ticket.MembershipId, id, cancellationToken);

        return result.Kind == TeamResultKind.Succeeded ? Ok(result.Value) : MapFailure(result);
    }

    [HttpPut("technicians/{id:guid}/availability")]
    [Authorize(Policy = TeamPolicies.Manage)]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxJsonBodyBytes)]
    [ProducesResponseType<TechnicianAvailabilityView>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SaveAvailability(
        Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] SaveAvailabilityInput request,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!ModelState.IsValid)
        {
            return InvalidBody();
        }

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        var result = await availabilityHandler.SaveAsync(
            ticket.OrganizationId, ticket.MembershipId, id, ticket.UserId, GetClientIpAddress(), request, cancellationToken);

        return result.Kind == TeamResultKind.Succeeded ? Ok(result.Value) : MapFailure(result);
    }

    [HttpPost("technicians/{id:guid}/availability/use-branch-hours")]
    [Authorize(Policy = TeamPolicies.Manage)]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxJsonBodyBytes)]
    [ProducesResponseType<TechnicianAvailabilityView>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UseBranchHours(
        Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] UpdatedAtInput request,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!ModelState.IsValid)
        {
            return InvalidBody();
        }

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        var result = await availabilityHandler.UseBranchHoursAsync(
            ticket.OrganizationId, ticket.MembershipId, id, ticket.UserId, GetClientIpAddress(), request, cancellationToken);

        return result.Kind == TeamResultKind.Succeeded ? Ok(result.Value) : MapFailure(result);
    }

    [HttpPost("technicians/{id:guid}/time-off")]
    [Authorize(Policy = TeamPolicies.Manage)]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxJsonBodyBytes)]
    [ProducesResponseType<TimeOffView>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateTimeOff(
        Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] CreateTimeOffInput request,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!ModelState.IsValid)
        {
            return InvalidBody();
        }

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        var result = await timeOffHandler.CreateAsync(
            ticket.OrganizationId, ticket.MembershipId, id, ticket.UserId, GetClientIpAddress(), request, cancellationToken);

        return result.Kind == TeamResultKind.Succeeded
            ? Created($"/team/technicians/{id}/time-off/{result.Value!.Id}", result.Value)
            : MapFailure(result);
    }

    [HttpPost("technicians/{id:guid}/time-off/{timeOffId:guid}/retire")]
    [Authorize(Policy = TeamPolicies.Manage)]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxJsonBodyBytes)]
    [ProducesResponseType<RetireTimeOffView>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RetireTimeOff(
        Guid id,
        Guid timeOffId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] UpdatedAtInput request,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!ModelState.IsValid)
        {
            return InvalidBody();
        }

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        var result = await timeOffHandler.RetireAsync(
            ticket.OrganizationId, ticket.MembershipId, id, timeOffId, ticket.UserId, GetClientIpAddress(), request, cancellationToken);

        return result.Kind == TeamResultKind.Succeeded ? Ok(result.Value) : MapFailure(result);
    }

    [HttpGet("technicians/{id:guid}/skill-assignments")]
    [Authorize(Policy = TeamPolicies.Manage)]
    [ProducesResponseType<SkillAssignmentsView>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSkillAssignments(Guid id, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        var result = await assignmentsHandler.GetAsync(ticket.OrganizationId, ticket.MembershipId, id, cancellationToken);

        return result.Kind == TeamResultKind.Succeeded ? Ok(result.Value) : MapFailure(result);
    }

    [HttpPut("technicians/{id:guid}/skill-assignments")]
    [Authorize(Policy = TeamPolicies.Manage)]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxJsonBodyBytes)]
    [ProducesResponseType<SkillAssignmentsView>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SaveSkillAssignments(
        Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] SaveSkillAssignmentsInput request,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!ModelState.IsValid)
        {
            return InvalidBody();
        }

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        var result = await assignmentsHandler.SaveAsync(
            ticket.OrganizationId, ticket.MembershipId, id, ticket.UserId, GetClientIpAddress(), request, cancellationToken);

        return result.Kind == TeamResultKind.Succeeded ? Ok(result.Value) : MapFailure(result);
    }

    private IActionResult InvalidBody() =>
        ModelState.Values.SelectMany(entry => entry.Errors).Any(error => error.Exception is UnsupportedContentTypeException)
            ? new UnsupportedMediaTypeResult()
            : BadRequest();

    private IActionResult MapFailure<T>(TeamResult<T> result)
    {
        switch (result.Kind)
        {
            case TeamResultKind.Invalid:
                return new ObjectResult(new ValidationProblemDetails(new Dictionary<string, string[]>(result.Errors!, StringComparer.Ordinal))
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "One or more validation errors occurred.",
                })
                {
                    StatusCode = StatusCodes.Status400BadRequest,
                };

            case TeamResultKind.NotFound:
                return NotFound(new ProblemDetails
                {
                    Status = StatusCodes.Status404NotFound,
                    Title = result.Message ?? "This technician profile isn't available.",
                });

            case TeamResultKind.Conflict:
                var problem = new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = result.Message,
                };

                if (result.Code is not null)
                {
                    problem.Extensions["code"] = result.Code;
                }

                return Conflict(problem);

            default:
                throw new InvalidOperationException("Unknown team result.");
        }
    }

    private IPAddress? GetClientIpAddress()
    {
        var address = HttpContext.Connection.RemoteIpAddress;

        return address is { IsIPv4MappedToIPv6: true } ? address.MapToIPv4() : address;
    }
}
