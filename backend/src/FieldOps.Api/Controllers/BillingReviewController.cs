using FieldOps.Api.Authorization;
using FieldOps.Api.Contracts;
using FieldOps.Application.Features.BillingReview;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace FieldOps.Api.Controllers;

/// <summary>
/// The completed jobs review of the session organization (completed-jobs-review FR-01 to FR-12). No endpoint accepts an
/// organization identifier; the organization and branch scope come from the session membership. Reads use the read
/// policy; the review update and the invoice generation the action policy. Every response is no-store.
/// </summary>
public sealed class BillingReviewController(
    GetBillingOptionsHandler optionsHandler,
    GetBillingQueueHandler queueHandler,
    ExportBillingQueueHandler exportHandler,
    GetBillingDetailHandler detailHandler,
    GetBillingEvidenceHandler evidenceHandler,
    UpdateBillingReviewHandler reviewHandler,
    GenerateBillingInvoiceHandler generateHandler,
    IAuthorizationService authorization) : WorkOrderControllerBase
{
    private const string ImageContentSecurityPolicy = "default-src 'none'; sandbox";

    [HttpGet("billing-review/options")]
    [Authorize(Policy = BillingReviewPolicies.Read)]
    [ProducesResponseType<BillingOptionsView>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Options(CancellationToken cancellationToken)
    {
        var failure = Begin(out var ticket);

        if (failure is not null)
        {
            return failure;
        }

        var canAct = (await authorization.AuthorizeAsync(User, BillingReviewPolicies.Act)).Succeeded;

        return Ok(await optionsHandler.HandleAsync(Call(ticket), canAct, cancellationToken));
    }

    [HttpGet("billing-review/queue")]
    [Authorize(Policy = BillingReviewPolicies.Read)]
    [ProducesResponseType<BillingQueuePage>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Queue(
        string? branchId,
        string? completed,
        string? technicianId,
        string? variance,
        string? search,
        string? tab,
        string? page,
        CancellationToken cancellationToken)
    {
        var failure = Begin(out var ticket);

        return failure ?? Respond(
            await queueHandler.HandleAsync(
                Call(ticket), new BillingQueryText(branchId, completed, technicianId, variance, search, tab, page), cancellationToken),
            value => Ok(value));
    }

    [HttpGet("billing-review/export")]
    [Authorize(Policy = BillingReviewPolicies.Read)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Export(
        string? branchId,
        string? completed,
        string? technicianId,
        string? variance,
        string? search,
        string? tab,
        CancellationToken cancellationToken)
    {
        var failure = Begin(out var ticket);

        return failure ?? Respond(
            await exportHandler.HandleAsync(
                Call(ticket), new BillingQueryText(branchId, completed, technicianId, variance, search, tab, null), cancellationToken),
            file =>
            {
                Response.Headers.XContentTypeOptions = "nosniff";

                return File(file.Content, "text/csv; charset=utf-8", file.FileName);
            });
    }

    [HttpGet("billing-review/work-orders/{workOrderId:guid}")]
    [Authorize(Policy = BillingReviewPolicies.Read)]
    [ProducesResponseType<BillingReviewDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid workOrderId, CancellationToken cancellationToken)
    {
        var failure = Begin(out var ticket);

        if (failure is not null)
        {
            return failure;
        }

        var canAct = (await authorization.AuthorizeAsync(User, BillingReviewPolicies.Act)).Succeeded;
        var detail = await detailHandler.HandleAsync(Call(ticket), workOrderId, canAct, cancellationToken);

        return detail is null ? NotFound() : Ok(detail);
    }

    [HttpGet("billing-review/work-orders/{workOrderId:guid}/evidence/{evidenceId:guid}")]
    [Authorize(Policy = BillingReviewPolicies.Read)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetEvidence(Guid workOrderId, Guid evidenceId, CancellationToken cancellationToken)
    {
        var failure = Begin(out var ticket);

        if (failure is not null)
        {
            return failure;
        }

        var image = await evidenceHandler.HandleAsync(Call(ticket), workOrderId, evidenceId, cancellationToken);

        if (image is null)
        {
            return NotFound();
        }

        // The stored image type, inline, never sniffed, never rendered as active content (mobile-job-progress BR-12).
        Response.Headers.ContentDisposition = "inline";
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.ContentSecurityPolicy = ImageContentSecurityPolicy;

        return File(image.Content, image.MimeType);
    }

    [HttpPatch("billing-review/work-orders/{workOrderId:guid}/review")]
    [Authorize(Policy = BillingReviewPolicies.Act)]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxJsonBodyBytes)]
    [ProducesResponseType<ReviewResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Review(
        Guid workOrderId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] ReviewRequestBody body,
        CancellationToken cancellationToken)
    {
        var failure = Begin(out var ticket);

        return failure ?? Respond(await reviewHandler.HandleAsync(Call(ticket), workOrderId, body.ToText(), cancellationToken), value => Ok(value));
    }

    [HttpPost("billing-review/work-orders/{workOrderId:guid}/invoice")]
    [Authorize(Policy = BillingReviewPolicies.Act)]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxJsonBodyBytes)]
    [ProducesResponseType<GenerateResult>(StatusCodes.Status201Created)]
    [ProducesResponseType<GenerateResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> GenerateInvoice(
        Guid workOrderId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] GenerateInvoiceRequestBody body,
        CancellationToken cancellationToken)
    {
        var failure = Begin(out var ticket);

        return failure ?? Respond(
            await generateHandler.HandleAsync(Call(ticket), workOrderId, body.ToText(), cancellationToken),
            result => result.Changed
                ? Created($"/billing-review/work-orders/{workOrderId}", result)
                : Ok(result));
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
            BillingOutcome<T>.Conflict conflict => ConflictResult(conflict.Code, conflict.Title),
            _ => NotFound(),
        };

    // Every 409 carries a machine-readable code.
    private static ObjectResult ConflictResult(string code, string title)
    {
        var problem = new ProblemDetails { Status = StatusCodes.Status409Conflict, Title = title };
        problem.Extensions["code"] = code;

        return new ObjectResult(problem) { StatusCode = StatusCodes.Status409Conflict };
    }
}
