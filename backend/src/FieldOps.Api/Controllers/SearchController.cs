using System.Diagnostics;
using FieldOps.Api.Authorization;
using FieldOps.Application.Features.BillingReview;
using FieldOps.Application.Features.Search;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FieldOps.Api.Controllers;

/// <summary>
/// The global search of the session organization (global-search FR-01 to FR-05). Read-only; no endpoint accepts an
/// organization, branch or entity-type parameter, unknown query parameters are ignored and every response is no-store. The
/// query text is never logged: only the duration, the item count, <c>hasMore</c> and the error type are.
/// </summary>
[Route("search")]
public sealed class SearchController(
    GlobalSearchHandler handler,
    IAuthorizationService authorization,
    ILogger<SearchController> logger) : WorkOrderControllerBase
{
    [HttpGet("global")]
    [Authorize(Policy = SearchPolicies.Global)]
    [ProducesResponseType<GlobalSearchView>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Global(string? q, string? limit, CancellationToken cancellationToken)
    {
        var failure = Begin(out var ticket);

        if (failure is not null)
        {
            return failure;
        }

        // BR-03: the per-type eligibility reuses the existing read policies of each destination.
        var eligibility = new SearchEligibility(
            await CanAsync(CustomerPolicies.View),
            await CanAsync(ServiceRequestPolicies.View),
            await CanAsync(ServiceRequestPolicies.View),
            await CanAsync(WorkOrderPolicies.Read),
            await CanAsync(BillingReviewPolicies.Read));
        var clock = Stopwatch.StartNew();

        try
        {
            var outcome = await handler.HandleAsync(Call(ticket), eligibility, q, limit, cancellationToken);

            if (outcome is BillingOutcome<GlobalSearchView>.Succeeded succeeded)
            {
                logger.LogInformation(
                    "Global search finished in {ElapsedMs} ms with {ItemCount} items, hasMore {HasMore}.",
                    clock.ElapsedMilliseconds,
                    succeeded.Value.Items.Count,
                    succeeded.Value.HasMore);

                return Ok(succeeded.Value);
            }

            var errors = outcome is BillingOutcome<GlobalSearchView>.Invalid invalid
                ? invalid.Errors
                : new Dictionary<string, string[]>();

            return new ObjectResult(new ValidationProblemDetails(new Dictionary<string, string[]>(errors, StringComparer.Ordinal))
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "One or more validation errors occurred.",
            })
            {
                StatusCode = StatusCodes.Status400BadRequest,
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                "Global search failed after {ElapsedMs} ms with {ErrorType}.", clock.ElapsedMilliseconds, exception.GetType().Name);

            throw;
        }
    }

    private async Task<bool> CanAsync(string policy) => (await authorization.AuthorizeAsync(User, policy)).Succeeded;
}
