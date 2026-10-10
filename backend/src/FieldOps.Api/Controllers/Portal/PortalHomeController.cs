using FieldOps.Application.Features.PortalAccess;
using FieldOps.Application.Features.PortalDashboard;
using Microsoft.AspNetCore.Mvc;

namespace FieldOps.Api.Controllers.Portal;

/// <summary>
/// The dashboard, the updates feed, the recent activity and the messages of the portal (customer portal BR-17 … BR-26,
/// BR-35). An absent <c>propertyId</c> means "All"; an id that is not an active property of the customer is a 404.
/// </summary>
[Route("portal")]
public sealed class PortalHomeController(
    GetPortalDashboardHandler dashboardHandler,
    PortalUpdatesHandler updatesHandler,
    ListPortalActivityHandler activityHandler) : PortalControllerBase
{
    [HttpGet("dashboard")]
    [ProducesResponseType<PortalDashboard>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Dashboard([FromQuery] string? propertyId, CancellationToken cancellationToken) =>
        Map(await dashboardHandler.HandleAsync(Scope, propertyId, cancellationToken), dashboard => Ok(dashboard));

    [HttpGet("updates")]
    [ProducesResponseType<PortalUpdates>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Updates(CancellationToken cancellationToken) =>
        Ok(await updatesHandler.ListAsync(Scope, cancellationToken));

    [HttpPost("updates/seen")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> MarkUpdatesSeen(CancellationToken cancellationToken)
    {
        await updatesHandler.MarkSeenAsync(Scope, cancellationToken);

        return NoContent();
    }

    [HttpGet("activity")]
    [ProducesResponseType<PortalPage<PortalActivityRow>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Activity(
        [FromQuery] string? propertyId, [FromQuery] string? page, CancellationToken cancellationToken) =>
        Map(await activityHandler.HandleAsync(Scope, propertyId, page, cancellationToken), result => Ok(result));
}
