using FieldOps.Api.Authorization;
using FieldOps.Api.Contracts;
using FieldOps.Application.Features.BillingReview;
using FieldOps.Application.Features.InvoiceDelivery;
using FieldOps.Application.Features.InvoicePayments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace FieldOps.Api.Controllers;

/// <summary>
/// The internal invoice endpoints (invoice-draft-delivery FR-01 to FR-06). No endpoint accepts an organization identifier:
/// the organization and branch scope come from the session membership. Reads use the billing read policy; the draft save,
/// the send and the resend use the action policy, which rejects other roles before anything is read or written. Every
/// response is no-store.
/// </summary>
public sealed class InvoicesController(
    GetInvoiceHandler detailHandler,
    DownloadInvoicePdfHandler pdfHandler,
    SaveInvoiceDraftHandler saveHandler,
    SendInvoiceHandler sendHandler,
    ResendInvoiceEmailHandler resendHandler,
    RecordInvoicePaymentHandler recordPaymentHandler,
    IAuthorizationService authorization) : WorkOrderControllerBase
{
    [HttpGet("invoices/{invoiceId:guid}")]
    [Authorize(Policy = BillingReviewPolicies.Read)]
    [ProducesResponseType<InvoiceDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid invoiceId, CancellationToken cancellationToken)
    {
        var failure = Begin(out var ticket);

        if (failure is not null)
        {
            return failure;
        }

        var canAct = (await authorization.AuthorizeAsync(User, BillingReviewPolicies.Act)).Succeeded;
        var detail = await detailHandler.HandleAsync(Call(ticket), invoiceId, canAct, cancellationToken);

        return detail is null ? NotFound() : Ok(detail);
    }

    [HttpGet("invoices/{invoiceId:guid}/pdf")]
    [Authorize(Policy = BillingReviewPolicies.Read)]
    [ProducesResponseType(typeof(byte[]), StatusCodes.Status200OK, InvoiceDeliveryMessages.PdfContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Pdf(Guid invoiceId, CancellationToken cancellationToken)
    {
        var failure = Begin(out var ticket);

        if (failure is not null)
        {
            return failure;
        }

        var pdf = await pdfHandler.HandleAsync(Call(ticket), invoiceId, cancellationToken);

        if (pdf is null)
        {
            return NotFound();
        }

        Response.Headers.ContentDisposition = $"attachment; filename=\"{pdf.FileName}\"";
        Response.Headers.XContentTypeOptions = "nosniff";

        return File(pdf.Content, InvoiceDeliveryMessages.PdfContentType);
    }

    [HttpPut("invoices/{invoiceId:guid}/draft")]
    [Authorize(Policy = BillingReviewPolicies.Act)]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxJsonBodyBytes)]
    [ProducesResponseType<InvoiceDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SaveDraft(
        Guid invoiceId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] InvoiceDeliveryRequestBody body,
        CancellationToken cancellationToken)
    {
        var failure = Begin(out var ticket);

        return failure ?? Respond(await saveHandler.HandleAsync(Call(ticket), invoiceId, body.ToText(), cancellationToken), value => Ok(value));
    }

    [HttpPost("invoices/{invoiceId:guid}/send")]
    [Authorize(Policy = BillingReviewPolicies.Act)]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxJsonBodyBytes)]
    [ProducesResponseType<InvoiceSendResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Send(
        Guid invoiceId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] InvoiceDeliveryRequestBody body,
        CancellationToken cancellationToken)
    {
        var failure = Begin(out var ticket);

        return failure ?? Respond(await sendHandler.HandleAsync(Call(ticket), invoiceId, body.ToText(), cancellationToken), value => Ok(value));
    }

    [HttpPost("invoices/{invoiceId:guid}/resend-email")]
    [Authorize(Policy = BillingReviewPolicies.Act)]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxJsonBodyBytes)]
    [ProducesResponseType<InvoiceResendResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Resend(
        Guid invoiceId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] InvoiceResendRequestBody body,
        CancellationToken cancellationToken)
    {
        var failure = Begin(out var ticket);

        return failure ?? Respond(await resendHandler.HandleAsync(Call(ticket), invoiceId, body.UpdatedAt, cancellationToken), value => Ok(value));
    }

    [HttpPost("invoices/{invoiceId:guid}/payments")]
    [Authorize(Policy = BillingReviewPolicies.Act)]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxJsonBodyBytes)]
    [ProducesResponseType<PaymentRecordResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<PaymentRecordResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RecordPayment(
        Guid invoiceId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] RecordPaymentRequestBody body,
        CancellationToken cancellationToken)
    {
        var failure = Begin(out var ticket);

        return failure ?? Respond(
            await recordPaymentHandler.HandleAsync(Call(ticket), invoiceId, body.ToText(), cancellationToken),
            result => result.Changed ? Created($"/invoices/{invoiceId}", result) : Ok(result));
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
