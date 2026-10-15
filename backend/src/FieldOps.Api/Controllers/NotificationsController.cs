using FieldOps.Api.Authentication;
using FieldOps.Api.Authorization;
using FieldOps.Application.Features.Notifications;
using FieldOps.Application.Features.ServiceRequests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FieldOps.Api.Controllers;

/// <summary>
/// The notifications inbox of the caller (notifications-center FR-01, FR-03, FR-04, FR-06). No endpoint accepts an
/// organization or recipient identifier: the organization, the user, the current role and the branch scope come from the
/// session. A notification that is unknown, foreign or hidden is one identical 404.
/// </summary>
[Route("notifications")]
public sealed class NotificationsController(
    ListNotificationsHandler listHandler,
    GetNotificationsSummaryHandler summaryHandler,
    GetNotificationsOptionsHandler optionsHandler,
    MarkNotificationReadHandler markHandler,
    ReadAllNotificationsHandler readAllHandler) : WorkOrderControllerBase
{
    [HttpGet]
    [Authorize(Policy = NotificationInboxPolicies.Inbox)]
    [ProducesResponseType<NotificationPage>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(
        string? tab, string? q, string? type, string? branchId, string? date, int? page, int? pageSize, CancellationToken cancellationToken)
    {
        var failure = Begin(out var ticket);

        if (failure is not null)
        {
            return failure;
        }

        var result = await listHandler.HandleAsync(
            Call(ticket), RoleCode(), new NotificationListRequest(tab, q, type, branchId, date, page, pageSize), cancellationToken);

        return result.Kind == ServiceRequestResultKind.NotFound ? NotFoundWith("branch_not_found") : Map(result);
    }

    [HttpGet("summary")]
    [Authorize(Policy = NotificationInboxPolicies.Inbox)]
    [ProducesResponseType<NotificationSummary>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Summary(CancellationToken cancellationToken)
    {
        var failure = Begin(out var ticket);

        return failure ?? Ok(await summaryHandler.HandleAsync(Call(ticket), RoleCode(), cancellationToken));
    }

    [HttpGet("options")]
    [Authorize(Policy = NotificationInboxPolicies.Inbox)]
    [ProducesResponseType<NotificationOptions>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Options(CancellationToken cancellationToken)
    {
        var failure = Begin(out var ticket);

        return failure ?? Ok(await optionsHandler.HandleAsync(Call(ticket), RoleCode(), cancellationToken));
    }

    [HttpPost("{id:guid}/read")]
    [Authorize(Policy = NotificationInboxPolicies.Inbox)]
    [ProducesResponseType<NotificationMarkResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Read(Guid id, CancellationToken cancellationToken)
    {
        var failure = Begin(out var ticket);

        return failure ?? Mark(await markHandler.ReadAsync(Call(ticket), RoleCode(), id, cancellationToken));
    }

    [HttpPost("{id:guid}/unread")]
    [Authorize(Policy = NotificationInboxPolicies.Inbox)]
    [ProducesResponseType<NotificationMarkResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Unread(Guid id, CancellationToken cancellationToken)
    {
        var failure = Begin(out var ticket);

        return failure ?? Mark(await markHandler.UnreadAsync(Call(ticket), RoleCode(), id, cancellationToken));
    }

    [HttpPost("read-all")]
    [Authorize(Policy = NotificationInboxPolicies.Inbox)]
    [ProducesResponseType<NotificationReadAllResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ReadAll(CancellationToken cancellationToken)
    {
        var failure = Begin(out var ticket);

        return failure ?? Ok(await readAllHandler.HandleAsync(Call(ticket), RoleCode(), cancellationToken));
    }

    private IActionResult Mark(ServiceRequestResult<NotificationMarkResult> result) =>
        result.Kind == ServiceRequestResultKind.Succeeded ? Ok(result.Value) : NotFoundWith("notification_not_found");

    // The role of the session validated on this request (the current one, never a stale ticket value).
    private string RoleCode() => SessionCookieEvents.GetValidatedSession(HttpContext)?.Role.Code ?? string.Empty;

    private NotFoundObjectResult NotFoundWith(string code)
    {
        var problem = new ProblemDetails { Status = StatusCodes.Status404NotFound, Title = "Not found." };
        problem.Extensions["code"] = code;

        return NotFound(problem);
    }
}
