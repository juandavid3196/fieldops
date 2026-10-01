using System.Net;
using FieldOps.Api.Authentication;
using FieldOps.Api.Authorization;
using FieldOps.Application.Features.Team;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace FieldOps.Api.Controllers;

/// <summary>
/// Skills and availability of technician profiles, their exceptions and the skill catalog (FR-02 to FR-09). The
/// organization and branch scope come from the session membership; no endpoint accepts an organization identifier.
/// </summary>
/// <remarks>Not an [ApiController]: model state is checked here, per <see cref="TeamController"/>.</remarks>
[Route("team")]
public sealed class TeamSkillsController(
    GetSkillsAvailabilityHandler getHandler,
    SaveSkillsAvailabilityHandler saveHandler,
    TechnicianExceptionsHandler exceptionsHandler,
    SkillCatalogHandler catalogHandler,
    IAuthorizationService authorization) : ControllerBase
{
    private const int MaxJsonBodyBytes = TeamController.MaxJsonBodyBytes;

    [HttpGet("technicians/{id:guid}/skills-availability")]
    [Authorize(Policy = TeamPolicies.SkillsView)]
    [ProducesResponseType<SkillsAvailabilityView>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        // Owner, operations manager and dispatcher pass the Team view policy; the technician sees only their own profile.
        var ownProfileOnly = !(await authorization.AuthorizeAsync(User, TeamPolicies.View)).Succeeded;

        var result = await getHandler.HandleAsync(
            ticket.OrganizationId, ticket.MembershipId, ownProfileOnly, id, cancellationToken);

        return result.Kind == TeamResultKind.Succeeded ? Ok(result.Value) : MapFailure(result);
    }

    [HttpPut("technicians/{id:guid}/skills-availability")]
    [Authorize(Policy = TeamPolicies.Manage)]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxJsonBodyBytes)]
    [ProducesResponseType<SkillsAvailabilityView>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Save(
        Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] SaveSkillsAvailabilityInput request,
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

        var result = await saveHandler.HandleAsync(
            ticket.OrganizationId, ticket.MembershipId, id, ticket.UserId, GetClientIpAddress(), request, cancellationToken);

        return result.Kind == TeamResultKind.Succeeded ? Ok(result.Value) : MapFailure(result);
    }

    [HttpPost("technicians/{id:guid}/exceptions")]
    [Authorize(Policy = TeamPolicies.Manage)]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxJsonBodyBytes)]
    [ProducesResponseType<ExceptionView>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateException(
        Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] ExceptionInput request,
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

        var result = await exceptionsHandler.CreateAsync(
            ticket.OrganizationId, ticket.MembershipId, id, ticket.UserId, GetClientIpAddress(), request, cancellationToken);

        return result.Kind == TeamResultKind.Succeeded
            ? Created($"/team/technicians/{id}/exceptions/{result.Value!.Id}", result.Value)
            : MapFailure(result);
    }

    [HttpPut("technicians/{id:guid}/exceptions/{exceptionId:guid}")]
    [Authorize(Policy = TeamPolicies.Manage)]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxJsonBodyBytes)]
    [ProducesResponseType<ExceptionView>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateException(
        Guid id,
        Guid exceptionId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] ExceptionInput request,
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

        var result = await exceptionsHandler.UpdateAsync(
            ticket.OrganizationId, ticket.MembershipId, id, exceptionId, ticket.UserId, GetClientIpAddress(), request, cancellationToken);

        return result.Kind == TeamResultKind.Succeeded ? Ok(result.Value) : MapFailure(result);
    }

    [HttpPost("technicians/{id:guid}/exceptions/{exceptionId:guid}/cancel")]
    [Authorize(Policy = TeamPolicies.Manage)]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxJsonBodyBytes)]
    [ProducesResponseType<ExceptionView>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<IActionResult> CancelException(
        Guid id,
        Guid exceptionId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] VersionInput request,
        CancellationToken cancellationToken) =>
        SetExceptionActiveAsync(id, exceptionId, false, request, cancellationToken);

    [HttpPost("technicians/{id:guid}/exceptions/{exceptionId:guid}/activate")]
    [Authorize(Policy = TeamPolicies.Manage)]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxJsonBodyBytes)]
    [ProducesResponseType<ExceptionView>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<IActionResult> ActivateException(
        Guid id,
        Guid exceptionId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] VersionInput request,
        CancellationToken cancellationToken) =>
        SetExceptionActiveAsync(id, exceptionId, true, request, cancellationToken);

    [HttpGet("skills")]
    [Authorize(Policy = TeamPolicies.Manage)]
    [ProducesResponseType<IReadOnlyList<SkillView>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListSkills(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        return Ok(await catalogHandler.ListAsync(ticket.OrganizationId, cancellationToken));
    }

    [HttpPost("skills")]
    [Authorize(Policy = TeamPolicies.Manage)]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxJsonBodyBytes)]
    [ProducesResponseType<SkillView>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CreateSkill(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] SkillInput request,
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

        var result = await catalogHandler.CreateAsync(
            ticket.OrganizationId, ticket.UserId, GetClientIpAddress(), request, cancellationToken);

        return result.Kind == TeamResultKind.Succeeded
            ? Created($"/team/skills/{result.Value!.Id}", result.Value)
            : MapFailure(result);
    }

    [HttpPut("skills/{skillId:guid}")]
    [Authorize(Policy = TeamPolicies.Manage)]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxJsonBodyBytes)]
    [ProducesResponseType<SkillView>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateSkill(
        Guid skillId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] SkillInput request,
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

        var result = await catalogHandler.UpdateAsync(
            ticket.OrganizationId, skillId, ticket.UserId, GetClientIpAddress(), request, cancellationToken);

        return result.Kind == TeamResultKind.Succeeded ? Ok(result.Value) : MapFailure(result);
    }

    [HttpPost("skills/{skillId:guid}/deactivate")]
    [Authorize(Policy = TeamPolicies.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<IActionResult> DeactivateSkill(Guid skillId, CancellationToken cancellationToken) =>
        SetSkillActiveAsync(skillId, false, cancellationToken);

    [HttpPost("skills/{skillId:guid}/activate")]
    [Authorize(Policy = TeamPolicies.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<IActionResult> ActivateSkill(Guid skillId, CancellationToken cancellationToken) =>
        SetSkillActiveAsync(skillId, true, cancellationToken);

    private async Task<IActionResult> SetExceptionActiveAsync(
        Guid id, Guid exceptionId, bool activate, VersionInput request, CancellationToken cancellationToken)
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

        var result = await exceptionsHandler.SetActiveAsync(
            ticket.OrganizationId,
            ticket.MembershipId,
            id,
            exceptionId,
            activate,
            ticket.UserId,
            GetClientIpAddress(),
            request,
            cancellationToken);

        return result.Kind == TeamResultKind.Succeeded ? Ok(result.Value) : MapFailure(result);
    }

    private async Task<IActionResult> SetSkillActiveAsync(Guid skillId, bool active, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        var result = await catalogHandler.SetActiveAsync(
            ticket.OrganizationId, skillId, active, ticket.UserId, GetClientIpAddress(), cancellationToken);

        return result.Kind == TeamResultKind.NoContent ? NoContent() : MapFailure(result);
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
