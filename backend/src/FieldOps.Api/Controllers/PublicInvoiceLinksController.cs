using System.Diagnostics;
using System.Globalization;
using FieldOps.Api.Contracts;
using FieldOps.Api.Extensions;
using FieldOps.Application.Features.InvoiceDelivery;
using FieldOps.Application.Features.OnlinePayments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;

namespace FieldOps.Api.Controllers;

/// <summary>
/// The public invoice link (invoice-draft-delivery BR-18 to BR-22): anonymous POSTs whose only credential is the token in
/// the JSON body. The organization and invoice always come from the token row; any such identifier in a body is ignored.
/// </summary>
/// <remarks>
/// Not an [ApiController], like <see cref="PublicQuoteLinksController"/>. Every response carries no-store, no-referrer and
/// nosniff through <c>QuoteLinkPublicHeadersMiddleware</c> (binary responses use <c>private, no-store</c>). Nothing here
/// logs the token or any invoice content. The view, pdf and logo endpoints write nothing; the payment endpoints of
/// customer-invoice-payments (card intent, status, bank transfer notice, receipt, completion report, photos and review) share
/// the same token flow, reject unknown body keys with a 400 and write only what their business rules allow (the lazy expiry of
/// a pending card attempt, the attempt, the notice and the review). Token validity and ownership (404) come before field
/// validation (400).
/// </remarks>
[Route("public/invoice-links")]
[AllowAnonymous]
public sealed class PublicInvoiceLinksController(
    ViewInvoiceLinkHandler viewHandler,
    DownloadInvoiceLinkPdfHandler pdfHandler,
    GetInvoiceLinkLogoHandler logoHandler,
    CreateCardIntentHandler cardIntentHandler,
    GetPaymentStatusHandler statusHandler,
    ReportBankTransferHandler bankNoticeHandler,
    SubmitInvoiceReviewHandler reviewHandler,
    DownloadReceiptPdfHandler receiptHandler,
    DownloadCompletionReportHandler reportHandler,
    ListInvoicePhotosHandler photosHandler,
    GetInvoicePhotoHandler photoHandler) : ControllerBase
{
    public const int MaxRequestBodyBytes = 16 * 1024;

    [HttpPost("view")]
    [RequestSizeLimit(MaxRequestBodyBytes)]
    [EnableRateLimiting(ApiRateLimitingExtensions.InvoiceLinkReadPolicy)]
    [ProducesResponseType<PublicInvoice>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> View(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] InvoiceLinkTokenRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return InvalidBody();
        }

        return await viewHandler.HandleAsync(request.Token, cancellationToken) is { } invoice ? Ok(invoice) : Unavailable();
    }

    [HttpPost("pdf")]
    [RequestSizeLimit(MaxRequestBodyBytes)]
    [EnableRateLimiting(ApiRateLimitingExtensions.InvoiceLinkPdfPolicy)]
    [ProducesResponseType(typeof(byte[]), StatusCodes.Status200OK, InvoiceDeliveryMessages.PdfContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Pdf(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] InvoiceLinkTokenRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return InvalidBody();
        }

        if (await pdfHandler.HandleAsync(request.Token, cancellationToken) is not { } pdf)
        {
            return Unavailable();
        }

        BinaryHeaders($"attachment; filename=\"{pdf.FileName}\"");

        return File(pdf.Content, InvoiceDeliveryMessages.PdfContentType);
    }

    [HttpPost("logo")]
    [RequestSizeLimit(MaxRequestBodyBytes)]
    [EnableRateLimiting(ApiRateLimitingExtensions.InvoiceLinkReadPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Logo(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] InvoiceLinkTokenRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return InvalidBody();
        }

        if (await logoHandler.HandleAsync(request.Token, cancellationToken) is not { } logo)
        {
            return Unavailable();
        }

        BinaryHeaders("inline");

        return File(logo.Content, logo.ContentType);
    }

    [HttpPost("payments/card-intent")]
    [RequestSizeLimit(MaxRequestBodyBytes)]
    [EnableRateLimiting(ApiRateLimitingExtensions.InvoiceLinkCardIntentPolicy)]
    [ProducesResponseType<CardIntentView>(StatusCodes.Status201Created)]
    [ProducesResponseType<CardIntentView>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> CardIntent(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] InvoiceLinkKeyRequest request,
        CancellationToken cancellationToken) =>
        !ModelState.IsValid
            ? InvalidBody()
            : Respond(await cardIntentHandler.HandleAsync(request.Token, request.IdempotencyKey, cancellationToken));

    [HttpPost("payments/status")]
    [RequestSizeLimit(MaxRequestBodyBytes)]
    [EnableRateLimiting(ApiRateLimitingExtensions.InvoiceLinkStatusPolicy)]
    [ProducesResponseType<PaymentStatusView>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Status(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] PaymentStatusRequest request,
        CancellationToken cancellationToken) =>
        !ModelState.IsValid
            ? InvalidBody()
            : Respond(await statusHandler.HandleAsync(request.Token, request.AttemptId, cancellationToken));

    [HttpPost("payments/bank-transfer-notice")]
    [RequestSizeLimit(MaxRequestBodyBytes)]
    [EnableRateLimiting(ApiRateLimitingExtensions.InvoiceLinkActionPolicy)]
    [ProducesResponseType<BankNoticeView>(StatusCodes.Status201Created)]
    [ProducesResponseType<BankNoticeView>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> BankTransferNotice(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] InvoiceLinkKeyRequest request,
        CancellationToken cancellationToken) =>
        !ModelState.IsValid
            ? InvalidBody()
            : Respond(await bankNoticeHandler.HandleAsync(request.Token, request.IdempotencyKey, cancellationToken));

    [HttpPost("receipt")]
    [RequestSizeLimit(MaxRequestBodyBytes)]
    [EnableRateLimiting(ApiRateLimitingExtensions.InvoiceLinkPdfPolicy)]
    [ProducesResponseType(typeof(byte[]), StatusCodes.Status200OK, InvoiceDeliveryMessages.PdfContentType)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Receipt(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] ReceiptRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return InvalidBody();
        }

        var outcome = await receiptHandler.HandleAsync(request.Token, request.PaymentId, cancellationToken);

        return outcome is PublicOutcome<DocumentFile>.Ok { Value: var document } ? Pdf(document) : Respond(outcome);
    }

    [HttpPost("completion-report")]
    [RequestSizeLimit(MaxRequestBodyBytes)]
    [EnableRateLimiting(ApiRateLimitingExtensions.InvoiceLinkPdfPolicy)]
    [ProducesResponseType(typeof(byte[]), StatusCodes.Status200OK, InvoiceDeliveryMessages.PdfContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> CompletionReport(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] StrictInvoiceLinkTokenRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return InvalidBody();
        }

        return await reportHandler.HandleAsync(request.Token, cancellationToken) is { } document ? Pdf(document) : Unavailable();
    }

    [HttpPost("photos")]
    [RequestSizeLimit(MaxRequestBodyBytes)]
    [EnableRateLimiting(ApiRateLimitingExtensions.InvoiceLinkReadPolicy)]
    [ProducesResponseType<IReadOnlyList<InvoicePhoto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Photos(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] StrictInvoiceLinkTokenRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return InvalidBody();
        }

        return await photosHandler.HandleAsync(request.Token, cancellationToken) is { } photos ? Ok(photos) : Unavailable();
    }

    [HttpPost("photos/content")]
    [RequestSizeLimit(MaxRequestBodyBytes)]
    [EnableRateLimiting(ApiRateLimitingExtensions.InvoiceLinkReadPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> PhotoContent(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] PhotoContentRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return InvalidBody();
        }

        var outcome = await photoHandler.HandleAsync(request.Token, request.PhotoId, cancellationToken);

        if (outcome is not PublicOutcome<Application.Features.QuoteLinks.PublicBinary>.Ok { Value: var photo })
        {
            return Respond(outcome);
        }

        BinaryHeaders("inline");

        return File(photo.Content, photo.ContentType);
    }

    [HttpPost("review")]
    [RequestSizeLimit(MaxRequestBodyBytes)]
    [EnableRateLimiting(ApiRateLimitingExtensions.InvoiceLinkActionPolicy)]
    [ProducesResponseType<ReviewSubmittedView>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Review(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] InvoiceReviewRequest request,
        CancellationToken cancellationToken) =>
        !ModelState.IsValid
            ? InvalidBody()
            : Respond(await reviewHandler.HandleAsync(request.Token, request.Rating, request.Comment, cancellationToken));

    private IActionResult Pdf(DocumentFile document)
    {
        BinaryHeaders($"attachment; filename=\"{document.FileName}\"");

        return File(document.Content, document.ContentType);
    }

    // One mapping for the outcomes of the payment endpoints; a 404 is the same body as every unusable token (BR-01).
    private IActionResult Respond<T>(PublicOutcome<T> outcome)
    {
        switch (outcome)
        {
            case PublicOutcome<T>.Ok { Created: true } created:
                return StatusCode(StatusCodes.Status201Created, created.Value);
            case PublicOutcome<T>.Ok ok:
                return Ok(ok.Value);
            case PublicOutcome<T>.Invalid invalid:
                return new ObjectResult(new ValidationProblemDetails(new Dictionary<string, string[]>(invalid.Errors, StringComparer.Ordinal))
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "One or more validation errors occurred.",
                })
                {
                    StatusCode = StatusCodes.Status400BadRequest,
                };
            case PublicOutcome<T>.Conflict conflict:
                return CodedProblem(StatusCodes.Status409Conflict, conflict.Code, conflict.Title);
            case PublicOutcome<T>.RateLimited limited:
                Response.Headers.RetryAfter = Math.Max(1, (long)Math.Ceiling(limited.RetryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

                return StatusCode(StatusCodes.Status429TooManyRequests);
            case PublicOutcome<T>.ProviderUnavailable:
                return CodedProblem(
                    StatusCodes.Status502BadGateway, OnlinePaymentMessages.ProviderUnavailableCode, OnlinePaymentMessages.ProviderUnavailableTitle);
            default:
                return Unavailable();
        }
    }

    private ObjectResult CodedProblem(int status, string code, string title)
    {
        var problem = new ProblemDetails { Status = status, Title = title };
        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = Activity.Current?.Id ?? HttpContext.TraceIdentifier;

        return StatusCode(status, problem);
    }

    // Binary responses are never cached, not even by the browser; nosniff comes from the middleware.
    private void BinaryHeaders(string disposition)
    {
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.ContentDisposition = disposition;
        Response.Headers.XContentTypeOptions = "nosniff";
    }

    // One shape for every unusable token, so nothing reveals why (BR-19).
    private ObjectResult Unavailable()
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status404NotFound,
            Title = InvoiceDeliveryMessages.UnavailableTitle,
        };
        problem.Extensions["code"] = InvoiceDeliveryMessages.UnavailableCode;
        problem.Extensions["traceId"] = Activity.Current?.Id ?? HttpContext.TraceIdentifier;

        return StatusCode(StatusCodes.Status404NotFound, problem);
    }

    // Malformed JSON, an empty or null body: keyless 400; a missing or non-JSON Content-Type: 415.
    private IActionResult InvalidBody() =>
        ModelState.Values.SelectMany(entry => entry.Errors).Any(error => error.Exception is UnsupportedContentTypeException)
            ? new UnsupportedMediaTypeResult()
            : BadRequest();
}
