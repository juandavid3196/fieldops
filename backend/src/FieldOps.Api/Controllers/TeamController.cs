using System.Net;
using FieldOps.Api.Authentication;
using FieldOps.Api.Authorization;
using FieldOps.Api.Contracts;
using FieldOps.Application.Features.Team;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace FieldOps.Api.Controllers;

/// <summary>
/// Technician profiles of the session organization (FR-02 to FR-11). No endpoint accepts an organization
/// identifier; the organization and branch scope come from the session membership.
/// </summary>
/// <remarks>Not an [ApiController]: model state is checked here, per <see cref="CustomersController"/>.</remarks>
[Route("team")]
public sealed class TeamController(
    GetTeamOptionsHandler optionsHandler,
    GetTeamMetricsHandler metricsHandler,
    ListTechniciansHandler listHandler,
    GetTechnicianHandler detailHandler,
    GetOwnTechnicianHandler ownHandler,
    CreateTechnicianHandler createHandler,
    UpdateTechnicianHandler updateHandler,
    SetTechnicianStatusHandler statusHandler,
    ListLinkableAccountsHandler linkableHandler,
    ChangeTechnicianAccountLinkHandler linkHandler,
    GetSkillCoverageHandler coverageHandler,
    IAuthorizationService authorization) : ControllerBase
{
    public const int MaxJsonBodyBytes = 64 * 1024;

    [HttpGet("options")]
    [Authorize(Policy = TeamPolicies.View)]
    [ProducesResponseType<TeamOptions>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Options(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        return Ok(await optionsHandler.HandleAsync(ticket.OrganizationId, ticket.MembershipId, cancellationToken));
    }

    [HttpGet("metrics")]
    [Authorize(Policy = TeamPolicies.View)]
    [ProducesResponseType<TeamMetrics>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Metrics(
        [FromQuery] string? branchId, [FromQuery] string? period, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        var result = await metricsHandler.HandleAsync(
            ticket.OrganizationId, ticket.MembershipId, branchId, period, cancellationToken);

        return result.Kind == TeamResultKind.Succeeded ? Ok(result.Value) : MapFailure(result);
    }

    [HttpGet("technicians")]
    [Authorize(Policy = TeamPolicies.View)]
    [ProducesResponseType<TechnicianListPage>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List([FromQuery] TechnicianListRequest request, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        var result = await listHandler.HandleAsync(
            ticket.OrganizationId, ticket.MembershipId, request.ToQuery(), cancellationToken);

        return result.Kind == TeamResultKind.Succeeded ? Ok(result.Value) : MapFailure(result);
    }

    [HttpGet("technicians/{id:guid}")]
    [Authorize(Policy = TeamPolicies.View)]
    [ProducesResponseType<TechnicianDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDetail(Guid id, [FromQuery] string? period, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        var includeNotes = (await authorization.AuthorizeAsync(User, TeamPolicies.Manage)).Succeeded;

        var result = await detailHandler.HandleAsync(
            ticket.OrganizationId, ticket.MembershipId, id, period, includeNotes, cancellationToken);

        return result.Kind == TeamResultKind.Succeeded ? Ok(result.Value) : MapFailure(result);
    }

    [HttpGet("me")]
    [Authorize(Policy = TeamPolicies.Self)]
    [ProducesResponseType<TechnicianDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        var result = await ownHandler.HandleAsync(ticket.OrganizationId, ticket.MembershipId, cancellationToken);

        return result.Kind == TeamResultKind.Succeeded ? Ok(result.Value) : MapFailure(result);
    }

    [HttpGet("skill-coverage")]
    [Authorize(Policy = TeamPolicies.View)]
    [ProducesResponseType<IReadOnlyList<SkillCoverage>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SkillCoverage([FromQuery] string? branchId, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        var result = await coverageHandler.HandleAsync(
            ticket.OrganizationId, ticket.MembershipId, branchId, cancellationToken);

        return result.Kind == TeamResultKind.Succeeded ? Ok(result.Value) : MapFailure(result);
    }

    [HttpGet("linkable-accounts")]
    [Authorize(Policy = TeamPolicies.Manage)]
    [ProducesResponseType<IReadOnlyList<LinkableAccount>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> LinkableAccounts([FromQuery] string? search, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        var result = await linkableHandler.HandleAsync(ticket.OrganizationId, search, cancellationToken);

        return result.Kind == TeamResultKind.Succeeded ? Ok(result.Value) : MapFailure(result);
    }

    [HttpPost("technicians")]
    [Authorize(Policy = TeamPolicies.Manage)]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxJsonBodyBytes)]
    [ProducesResponseType<TechnicianCreated>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Create(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] TechnicianRequest request,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!ModelState.IsValid)
        {
            return HasUnsupportedContentType() ? new UnsupportedMediaTypeResult() : BadRequest();
        }

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        var result = await createHandler.HandleAsync(
            ticket.OrganizationId,
            ticket.MembershipId,
            ticket.UserId,
            GetClientIpAddress(),
            request.ToInput(),
            cancellationToken);

        return result.Kind == TeamResultKind.Succeeded
            ? Created($"/team/technicians/{result.Value}", new TechnicianCreated(result.Value))
            : MapFailure(result);
    }

    [HttpPut("technicians/{id:guid}")]
    [Authorize(Policy = TeamPolicies.Manage)]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxJsonBodyBytes)]
    [ProducesResponseType<TechnicianDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] TechnicianRequest request,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!ModelState.IsValid)
        {
            return HasUnsupportedContentType() ? new UnsupportedMediaTypeResult() : BadRequest();
        }

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        var result = await updateHandler.HandleAsync(
            ticket.OrganizationId,
            ticket.MembershipId,
            id,
            ticket.UserId,
            GetClientIpAddress(),
            request.ToInput(),
            cancellationToken);

        return result.Kind == TeamResultKind.Succeeded ? Ok(result.Value) : MapFailure(result);
    }

    [HttpPost("technicians/{id:guid}/activate")]
    [Authorize(Policy = TeamPolicies.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<IActionResult> Activate(Guid id, CancellationToken cancellationToken) =>
        SetStatusAsync(id, true, cancellationToken);

    [HttpPost("technicians/{id:guid}/deactivate")]
    [Authorize(Policy = TeamPolicies.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken) =>
        SetStatusAsync(id, false, cancellationToken);

    [HttpPut("technicians/{id:guid}/account-link")]
    [Authorize(Policy = TeamPolicies.Manage)]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxJsonBodyBytes)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Link(
        Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] AccountLinkRequest request,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!ModelState.IsValid)
        {
            return HasUnsupportedContentType() ? new UnsupportedMediaTypeResult() : BadRequest();
        }

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        if (request.OrganizationUserId is not { } organizationUserId)
        {
            return FieldErrors(
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["organizationUserId"] = [TeamMessages.QueryInvalid] });
        }

        var result = await linkHandler.LinkAsync(
            ticket.OrganizationId,
            ticket.MembershipId,
            id,
            organizationUserId,
            ticket.UserId,
            GetClientIpAddress(),
            cancellationToken);

        return result.Kind == TeamResultKind.NoContent ? NoContent() : MapFailure(result);
    }

    [HttpDelete("technicians/{id:guid}/account-link")]
    [Authorize(Policy = TeamPolicies.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Unlink(Guid id, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        var result = await linkHandler.UnlinkAsync(
            ticket.OrganizationId, ticket.MembershipId, id, ticket.UserId, GetClientIpAddress(), cancellationToken);

        return result.Kind == TeamResultKind.NoContent ? NoContent() : MapFailure(result);
    }

    private async Task<IActionResult> SetStatusAsync(Guid id, bool activate, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        var result = await statusHandler.HandleAsync(
            ticket.OrganizationId, ticket.MembershipId, id, activate, ticket.UserId, GetClientIpAddress(), cancellationToken);

        return result.Kind == TeamResultKind.NoContent ? NoContent() : MapFailure(result);
    }

    private IActionResult MapFailure<T>(TeamResult<T> result)
    {
        switch (result.Kind)
        {
            case TeamResultKind.Invalid:
                return FieldErrors(result.Errors!);

            case TeamResultKind.NotFound:
                return NotFound(new ProblemDetails
                {
                    Status = StatusCodes.Status404NotFound,
                    Title = "This technician profile isn't available.",
                });

            case TeamResultKind.Conflict:
                var problem = new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = result.Message,
                };

                if (result.UpcomingVisitCount is { } count)
                {
                    problem.Extensions["upcomingVisitCount"] = count;
                }

                return Conflict(problem);

            default:
                throw new InvalidOperationException("Unknown team result.");
        }
    }

    private static ObjectResult FieldErrors(IReadOnlyDictionary<string, string[]> errors) =>
        new(new ValidationProblemDetails(new Dictionary<string, string[]>(errors, StringComparer.Ordinal))
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "One or more validation errors occurred.",
        })
        {
            StatusCode = StatusCodes.Status400BadRequest,
        };

    private IPAddress? GetClientIpAddress()
    {
        var address = HttpContext.Connection.RemoteIpAddress;

        return address is { IsIPv4MappedToIPv6: true } ? address.MapToIPv4() : address;
    }

    private bool HasUnsupportedContentType() =>
        ModelState.Values
            .SelectMany(entry => entry.Errors)
            .Any(error => error.Exception is UnsupportedContentTypeException);
}
