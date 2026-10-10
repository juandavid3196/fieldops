using FieldOps.Api.Contracts;
using FieldOps.Api.Extensions;
using FieldOps.Application.Features.InvoiceDelivery;
using FieldOps.Application.Features.OnlinePayments;
using FieldOps.Application.Features.PortalAccess;
using FieldOps.Application.Features.PortalDashboard;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;

namespace FieldOps.Api.Controllers.Portal;

/// <summary>
/// The invoices module (customer portal BR-31): the list and the shared invoice view and payment use cases reached by the
/// invoice id of the session customer, never by a public token. The per-invoice action limits are keyed by user and invoice.
/// </summary>
public sealed class PortalInvoicesController(
    ListPortalCatalogHandler catalogHandler,
    ViewInvoiceLinkHandler viewHandler,
    DownloadInvoiceLinkPdfHandler pdfHandler,
    CreateCardIntentHandler cardIntentHandler,
    GetPaymentStatusHandler statusHandler,
    ReportBankTransferHandler bankNoticeHandler,
    SubmitInvoiceReviewHandler reviewHandler,
    DownloadReceiptPdfHandler receiptHandler,
    DownloadCompletionReportHandler reportHandler,
    ListInvoicePhotosHandler photosHandler,
    GetInvoicePhotoHandler photoHandler) : PortalControllerBase
{
    public const int MaxRequestBodyBytes = 16 * 1024;

    [HttpGet("portal/invoices")]
    [ProducesResponseType<PortalPage<PortalInvoiceRow>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(
        [FromQuery] string? propertyId, [FromQuery] string? page, CancellationToken cancellationToken) =>
        Map(await catalogHandler.InvoicesAsync(Scope, propertyId, page, cancellationToken), result => Ok(result));

    [HttpGet("portal/invoices/{invoiceId:guid}")]
    [EnableRateLimiting(ApiRateLimitingExtensions.InvoiceLinkReadPolicy)]
    [ProducesResponseType<PublicInvoice>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> View(Guid invoiceId, CancellationToken cancellationToken) =>
        await viewHandler.HandleAsync(Access(invoiceId), cancellationToken) is { } invoice ? Ok(invoice) : Unavailable();

    [HttpGet("portal/invoices/{invoiceId:guid}/pdf")]
    [EnableRateLimiting(ApiRateLimitingExtensions.InvoiceLinkPdfPolicy)]
    [ProducesResponseType(typeof(byte[]), StatusCodes.Status200OK, InvoiceDeliveryMessages.PdfContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Pdf(Guid invoiceId, CancellationToken cancellationToken) =>
        await pdfHandler.HandleAsync(Access(invoiceId), cancellationToken) is { } pdf
            ? Pdf(pdf.FileName, InvoiceDeliveryMessages.PdfContentType, pdf.Content)
            : Unavailable();

    [HttpGet("portal/invoices/{invoiceId:guid}/payments/{paymentId:guid}/receipt")]
    [EnableRateLimiting(ApiRateLimitingExtensions.InvoiceLinkPdfPolicy)]
    [ProducesResponseType(typeof(byte[]), StatusCodes.Status200OK, InvoiceDeliveryMessages.PdfContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Receipt(Guid invoiceId, Guid paymentId, CancellationToken cancellationToken)
    {
        var outcome = await receiptHandler.HandleAsync(Access(invoiceId), paymentId.ToString(), cancellationToken);

        return outcome is PublicOutcome<DocumentFile>.Ok { Value: var document }
            ? Pdf(document.FileName, document.ContentType, document.Content)
            : MapPayment(outcome);
    }

    [HttpGet("portal/invoices/{invoiceId:guid}/completion-report")]
    [EnableRateLimiting(ApiRateLimitingExtensions.InvoiceLinkPdfPolicy)]
    [ProducesResponseType(typeof(byte[]), StatusCodes.Status200OK, InvoiceDeliveryMessages.PdfContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CompletionReport(Guid invoiceId, CancellationToken cancellationToken) =>
        await reportHandler.HandleAsync(Access(invoiceId), cancellationToken) is { } document
            ? Pdf(document.FileName, document.ContentType, document.Content)
            : Unavailable();

    [HttpGet("portal/invoices/{invoiceId:guid}/photos")]
    [EnableRateLimiting(ApiRateLimitingExtensions.InvoiceLinkReadPolicy)]
    [ProducesResponseType<IReadOnlyList<InvoicePhoto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Photos(Guid invoiceId, CancellationToken cancellationToken) =>
        await photosHandler.HandleAsync(Access(invoiceId), cancellationToken) is { } photos ? Ok(photos) : Unavailable();

    [HttpGet("portal/invoices/{invoiceId:guid}/photos/{photoId:guid}/content")]
    [EnableRateLimiting(ApiRateLimitingExtensions.InvoiceLinkReadPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PhotoContent(Guid invoiceId, Guid photoId, CancellationToken cancellationToken)
    {
        var outcome = await photoHandler.HandleAsync(Access(invoiceId), photoId.ToString(), cancellationToken);

        return outcome is PublicOutcome<Application.Features.QuoteLinks.PublicBinary>.Ok { Value: var photo } ? Image(photo) : MapPayment(outcome);
    }

    [HttpPost("portal/invoices/{invoiceId:guid}/payment-attempts")]
    [Consumes("application/json")]
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
        Guid invoiceId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] PortalKeyRequest request,
        CancellationToken cancellationToken) =>
        !ModelState.IsValid
            ? InvalidBody()
            : MapPayment(await cardIntentHandler.HandleAsync(Access(invoiceId), request.IdempotencyKey, cancellationToken));

    [HttpGet("portal/invoices/{invoiceId:guid}/payment-attempts/{attemptId:guid}")]
    [EnableRateLimiting(ApiRateLimitingExtensions.InvoiceLinkStatusPolicy)]
    [ProducesResponseType<PaymentStatusView>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Status(Guid invoiceId, Guid attemptId, CancellationToken cancellationToken) =>
        MapPayment(await statusHandler.HandleAsync(Access(invoiceId), attemptId.ToString(), cancellationToken));

    [HttpPost("portal/invoices/{invoiceId:guid}/bank-transfer-notice")]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxRequestBodyBytes)]
    [EnableRateLimiting(ApiRateLimitingExtensions.InvoiceLinkActionPolicy)]
    [ProducesResponseType<BankNoticeView>(StatusCodes.Status201Created)]
    [ProducesResponseType<BankNoticeView>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> BankTransferNotice(
        Guid invoiceId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] PortalKeyRequest request,
        CancellationToken cancellationToken) =>
        !ModelState.IsValid
            ? InvalidBody()
            : MapPayment(await bankNoticeHandler.HandleAsync(Access(invoiceId), request.IdempotencyKey, cancellationToken));

    [HttpPost("portal/invoices/{invoiceId:guid}/review")]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxRequestBodyBytes)]
    [EnableRateLimiting(ApiRateLimitingExtensions.InvoiceLinkActionPolicy)]
    [ProducesResponseType<ReviewSubmittedView>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Review(
        Guid invoiceId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] PortalReviewRequest request,
        CancellationToken cancellationToken) =>
        !ModelState.IsValid
            ? InvalidBody()
            : MapPayment(await reviewHandler.HandleAsync(Access(invoiceId), request.Rating, request.Comment, cancellationToken));
}
