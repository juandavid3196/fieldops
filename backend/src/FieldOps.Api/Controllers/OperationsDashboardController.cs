using FieldOps.Api.Authorization;
using FieldOps.Application.Features.Dispatch;
using FieldOps.Application.Features.OperationsDashboard;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FieldOps.Api.Controllers;

/// <summary>
/// The operations dashboard of the session organization (operations-dashboard BR-01 to BR-15). Read-only; no endpoint
/// accepts an organization identifier, the organization and branch scope come from the session membership. Every
/// endpoint requires the dispatch manage policy and every response is no-store.
/// </summary>
[Route("operations-dashboard")]
public sealed class OperationsDashboardController(
    GetOperationsOptionsHandler optionsHandler,
    GetOperationsDashboardHandler dashboardHandler,
    GetServicePerformanceHandler performanceHandler) : WorkOrderControllerBase
{
    [HttpGet("options")]
    [Authorize(Policy = DispatchPolicies.Manage)]
    [ProducesResponseType<OperationsOptionsView>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Options(CancellationToken cancellationToken)
    {
        var failure = Begin(out var ticket);

        return failure ?? Ok(await optionsHandler.HandleAsync(Call(ticket), cancellationToken));
    }

    [HttpGet]
    [Authorize(Policy = DispatchPolicies.Manage)]
    [ProducesResponseType<OperationsDashboardView>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Dashboard(
        string? branchId, string? date, string? technicianId, string? categoryId, CancellationToken cancellationToken)
    {
        var failure = Begin(out var ticket);

        return failure ?? Respond(await dashboardHandler.HandleAsync(
            Call(ticket), new OperationsQueryText(branchId, date, technicianId, categoryId, null), cancellationToken));
    }

    [HttpGet("performance")]
    [Authorize(Policy = DispatchPolicies.Manage)]
    [ProducesResponseType<PerformanceView>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Performance(
        string? branchId,
        string? date,
        string? period,
        string? technicianId,
        string? categoryId,
        CancellationToken cancellationToken)
    {
        var failure = Begin(out var ticket);

        return failure ?? Respond(await performanceHandler.HandleAsync(
            Call(ticket), new OperationsQueryText(branchId, date, technicianId, categoryId, period), cancellationToken));
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
            _ => NotFound(),
        };
}
