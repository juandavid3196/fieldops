using System.Net;
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
    GetTechnicianVisitHandler visitHandler,
    StartTravelHandler startTravelHandler,
    ArriveHandler arriveHandler,
    GetVisitAssessmentPhotoHandler photoHandler) : ControllerBase
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

    [HttpPost("visits/{visitId:guid}/start-travel")]
    [Authorize(Policy = TeamPolicies.Self)]
    [ProducesResponseType<TravelResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> StartTravel(Guid visitId, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        var result = await startTravelHandler.HandleAsync(Call(ticket), visitId, cancellationToken);

        return result.Kind == TechnicianVisitResultKind.Succeeded ? Ok(result.Value) : MapFailure(result);
    }

    [HttpPost("visits/{visitId:guid}/arrive")]
    [Authorize(Policy = TeamPolicies.Self)]
    [ProducesResponseType<TravelResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Arrive(Guid visitId, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        var result = await arriveHandler.HandleAsync(Call(ticket), visitId, cancellationToken);

        return result.Kind == TechnicianVisitResultKind.Succeeded ? Ok(result.Value) : MapFailure(result);
    }

    [HttpGet("visits/{visitId:guid}/assessment-photos/{photoId:guid}")]
    [Authorize(Policy = TeamPolicies.Self)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAssessmentPhoto(Guid visitId, Guid photoId, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        var result = await photoHandler.HandleAsync(
            ticket.OrganizationId, ticket.MembershipId, visitId, photoId, cancellationToken);

        if (result.Kind != TechnicianVisitResultKind.Succeeded)
        {
            return MapFailure(result);
        }

        // BR-06: the stored image type, inline, never sniffed.
        Response.Headers.ContentDisposition = "inline";
        Response.Headers.XContentTypeOptions = "nosniff";

        return File(result.Value!.Content, result.Value.MimeType);
    }

    private TravelCall Call(SessionTicket ticket) =>
        new(ticket.OrganizationId, ticket.MembershipId, ticket.UserId, ClientIpAddress());

    private IPAddress? ClientIpAddress()
    {
        var address = HttpContext.Connection.RemoteIpAddress;

        return address is { IsIPv4MappedToIPv6: true } ? address.MapToIPv4() : address;
    }

    // The title follows the code (BR-01, BR-02, BR-07, BR-09); a missing code is the identical 404 of every unavailable visit.
    private IActionResult MapFailure<T>(TechnicianVisitResult<T> result)
    {
        var status = result.Kind switch
        {
            TechnicianVisitResultKind.Forbidden => StatusCodes.Status403Forbidden,
            TechnicianVisitResultKind.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status404NotFound,
        };
        var problem = new ProblemDetails
        {
            Status = status,
            Title = TechnicianVisitMessages.Title(result.Code),
        };

        if (result.Code is not null)
        {
            problem.Extensions["code"] = result.Code;
        }

        return new ObjectResult(problem) { StatusCode = status };
    }
}
