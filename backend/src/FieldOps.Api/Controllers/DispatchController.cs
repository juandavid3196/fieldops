using FieldOps.Api.Authorization;
using FieldOps.Api.Contracts;
using FieldOps.Application.Features.Dispatch;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace FieldOps.Api.Controllers;

/// <summary>
/// The dispatch calendar of the session organization (dispatch-calendar FR-02 to FR-09). No endpoint accepts an
/// organization identifier; the organization and branch scope come from the session membership. Options, calendar,
/// unscheduled list and visit detail use the read policy; evaluation and dispatch the manage policy.
/// </summary>
public sealed class DispatchController(
    GetDispatchOptionsHandler optionsHandler,
    GetDispatchCalendarHandler calendarHandler,
    ListUnscheduledVisitsHandler unscheduledHandler,
    GetVisitDispatchHandler detailHandler,
    EvaluateVisitHandler evaluationHandler,
    DispatchVisitHandler dispatchHandler,
    IAuthorizationService authorization) : WorkOrderControllerBase
{
    [HttpGet("dispatch/options")]
    [Authorize(Policy = DispatchPolicies.Read)]
    [ProducesResponseType<DispatchOptionsView>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Options(CancellationToken cancellationToken)
    {
        var failure = Begin(out var ticket);

        return failure ?? Ok(await optionsHandler.HandleAsync(Call(ticket), cancellationToken));
    }

    [HttpGet("dispatch/calendar")]
    [Authorize(Policy = DispatchPolicies.Read)]
    [ProducesResponseType<DispatchCalendarView>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Calendar(
        string? branchId, string? view, string? date, string? skillId, string? status, CancellationToken cancellationToken)
    {
        var failure = Begin(out var ticket);

        if (failure is not null)
        {
            return failure;
        }

        // The Team filter may arrive as technicianIds=a&technicianIds=b or technicianIds[]=a.
        var technicianIds = Request.Query
            .Where(entry => entry.Key is "technicianIds" or "technicianIds[]")
            .SelectMany(entry => entry.Value)
            .Where(value => value is not null)
            .Select(value => value!)
            .ToList();

        return Respond(await calendarHandler.HandleAsync(
            Call(ticket),
            new CalendarQueryText(branchId, view, date, technicianIds, skillId, status),
            cancellationToken));
    }

    [HttpGet("dispatch/unscheduled")]
    [Authorize(Policy = DispatchPolicies.Read)]
    [ProducesResponseType<UnscheduledPageView>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Unscheduled(
        string? branchId,
        string? filter,
        string? search,
        string? skillId,
        string? page,
        string? pageSize,
        CancellationToken cancellationToken)
    {
        var failure = Begin(out var ticket);

        return failure ?? Respond(await unscheduledHandler.HandleAsync(
            Call(ticket),
            new UnscheduledQueryText(branchId, filter, search, skillId, page, pageSize),
            cancellationToken));
    }

    [HttpGet("dispatch/visits/{visitId:guid}")]
    [Authorize(Policy = DispatchPolicies.Read)]
    [ProducesResponseType<VisitDispatchDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid visitId, CancellationToken cancellationToken)
    {
        var failure = Begin(out var ticket);

        if (failure is not null)
        {
            return failure;
        }

        var canManage = (await authorization.AuthorizeAsync(User, DispatchPolicies.Manage)).Succeeded;
        var detail = await detailHandler.HandleAsync(Call(ticket), visitId, canManage, cancellationToken);

        return detail is null ? NotFound() : Ok(detail);
    }

    [HttpPost("dispatch/visits/{visitId:guid}/evaluation")]
    [Authorize(Policy = DispatchPolicies.Manage)]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxJsonBodyBytes)]
    [ProducesResponseType<VisitEvaluation>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Evaluate(
        Guid visitId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] EvaluationRequestBody body,
        CancellationToken cancellationToken)
    {
        var failure = Begin(out var ticket);

        return failure ?? Respond(await evaluationHandler.HandleAsync(Call(ticket), visitId, body.ToText(), cancellationToken));
    }

    [HttpPut("dispatch/visits/{visitId:guid}")]
    [Authorize(Policy = DispatchPolicies.Manage)]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxJsonBodyBytes)]
    [ProducesResponseType<VisitDispatchResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Dispatch(
        Guid visitId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] DispatchRequestBody body,
        CancellationToken cancellationToken)
    {
        var failure = Begin(out var ticket);

        return failure ?? Respond(await dispatchHandler.HandleAsync(Call(ticket), visitId, body.ToText(), cancellationToken));
    }

    private IActionResult Respond<T>(DispatchOutcome<T> outcome) =>
        outcome switch
        {
            DispatchOutcome<T>.Succeeded succeeded => Ok(succeeded.Value),
            DispatchOutcome<T>.Invalid invalid => new ObjectResult(new ValidationProblemDetails(
                new Dictionary<string, string[]>(invalid.Errors, StringComparer.Ordinal))
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "One or more validation errors occurred.",
            })
            {
                StatusCode = StatusCodes.Status400BadRequest,
            },
            DispatchOutcome<T>.Locked => ConflictResult(DispatchCodes.VisitLocked, null),
            DispatchOutcome<T>.Changed => ConflictResult(DispatchCodes.VisitChanged, null),
            DispatchOutcome<T>.Conflicts conflicts => ConflictResult(DispatchCodes.SchedulingConflicts, conflicts.Items),
            _ => NotFound(),
        };

    // Every 409 carries a machine-readable code; scheduling conflicts also carry the conflict list.
    private static ObjectResult ConflictResult(string code, IReadOnlyList<DispatchConflict>? conflicts)
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = DispatchMessages.ConflictTitle(code),
        };
        problem.Extensions["code"] = code;

        if (conflicts is not null)
        {
            problem.Extensions["conflicts"] = conflicts;
        }

        return new ObjectResult(problem) { StatusCode = StatusCodes.Status409Conflict };
    }
}
