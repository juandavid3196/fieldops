using FieldOps.Api.Contracts;
using FieldOps.Api.Extensions;
using FieldOps.Application.Features.PortalAccess;
using FieldOps.Application.Features.PortalDashboard;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;

namespace FieldOps.Api.Controllers.Portal;

/// <summary>
/// The appointments module (customer portal BR-22, BR-29, BR-32): upcoming and past visits of the customer, the detail and the
/// reschedule request. The visit is never changed by the portal.
/// </summary>
[Route("portal/appointments")]
public sealed class PortalAppointmentsController(PortalAppointmentsHandler handler) : PortalControllerBase
{
    public const int MaxRequestBodyBytes = 4 * 1024;

    [HttpGet]
    [ProducesResponseType<PortalPage<PortalAppointment>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(
        [FromQuery] string? scope,
        [FromQuery] string? propertyId,
        [FromQuery] string? page,
        CancellationToken cancellationToken) =>
        Map(await handler.ListAsync(Scope, scope, propertyId, page, cancellationToken), result => Ok(result));

    [HttpGet("{visitId:guid}")]
    [ProducesResponseType<PortalAppointment>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid visitId, CancellationToken cancellationToken) =>
        Map(await handler.GetAsync(Scope, visitId, cancellationToken), appointment => Ok(appointment));

    [HttpPost("{visitId:guid}/reschedule-requests")]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxRequestBodyBytes)]
    [EnableRateLimiting(ApiRateLimitingExtensions.PortalReschedulePolicy)]
    [ProducesResponseType<PortalRescheduleRequested>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> RequestReschedule(
        Guid visitId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] PortalRescheduleRequestBody request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return InvalidBody();
        }

        var input = new PortalRescheduleInput(request.PreferredDate, request.TimeWindow, request.Reason);

        return Map(
            await handler.RequestRescheduleAsync(Scope, visitId, input, ClientIp(), cancellationToken),
            requested => StatusCode(StatusCodes.Status201Created, requested));
    }
}
