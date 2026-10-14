using FieldOps.Api.Authorization;
using FieldOps.Application.Features.BillingReview;
using FieldOps.Application.Features.BusinessOverview;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FieldOps.Api.Controllers;

/// <summary>
/// The business overview of the session organization (business-overview FR-02 to FR-12). Read-only and Owner-only; no endpoint
/// accepts an organization identifier and every response is no-store.
/// </summary>
[Route("business-overview")]
public sealed class BusinessOverviewController(
    GetBusinessOptionsHandler optionsHandler,
    GetBusinessOverviewHandler overviewHandler,
    ExportBusinessOverviewHandler exportHandler) : WorkOrderControllerBase
{
    [HttpGet("options")]
    [Authorize(Policy = BusinessOverviewPolicies.Read)]
    [ProducesResponseType<BusinessOptionsView>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Options(CancellationToken cancellationToken)
    {
        var failure = Begin(out var ticket);

        return failure ?? Ok(await optionsHandler.HandleAsync(Call(ticket), cancellationToken));
    }

    [HttpGet]
    [Authorize(Policy = BusinessOverviewPolicies.Read)]
    [ProducesResponseType<BusinessOverviewView>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Overview(string? from, string? to, string? branchId, CancellationToken cancellationToken)
    {
        var failure = Begin(out var ticket);

        return failure ?? Respond(
            await overviewHandler.HandleAsync(Call(ticket), new BusinessOverviewQueryText(from, to, branchId, null), cancellationToken),
            value => Ok(value));
    }

    [HttpGet("export")]
    [Authorize(Policy = BusinessOverviewPolicies.Read)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Export(
        string? from, string? to, string? branchId, string? compare, CancellationToken cancellationToken)
    {
        var failure = Begin(out var ticket);

        return failure ?? Respond(
            await exportHandler.HandleAsync(Call(ticket), new BusinessOverviewQueryText(from, to, branchId, compare), cancellationToken),
            file =>
            {
                Response.Headers.XContentTypeOptions = "nosniff";

                return File(file.Content, "text/csv; charset=utf-8", file.FileName);
            });
    }

    private IActionResult Respond<T>(BillingOutcome<T> outcome, Func<T, IActionResult> success) =>
        outcome switch
        {
            BillingOutcome<T>.Succeeded succeeded => success(succeeded.Value),
            BillingOutcome<T>.Invalid invalid => new ObjectResult(new ValidationProblemDetails(
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
