using System.Diagnostics;
using FieldOps.Api.Contracts;
using FieldOps.Api.Extensions;
using FieldOps.Application.Features.InvoiceDelivery;
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
/// logs the token or any invoice content, and nothing here writes.
/// </remarks>
[Route("public/invoice-links")]
[AllowAnonymous]
public sealed class PublicInvoiceLinksController(
    ViewInvoiceLinkHandler viewHandler,
    DownloadInvoiceLinkPdfHandler pdfHandler,
    GetInvoiceLinkLogoHandler logoHandler) : ControllerBase
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
