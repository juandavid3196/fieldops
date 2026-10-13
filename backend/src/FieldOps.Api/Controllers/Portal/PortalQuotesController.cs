using FieldOps.Api.Contracts;
using FieldOps.Api.Extensions;
using FieldOps.Application.Features.PortalAccess;
using FieldOps.Application.Features.PortalDashboard;
using FieldOps.Application.Features.QuoteLinks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;

namespace FieldOps.Api.Controllers.Portal;

/// <summary>
/// The quotes module (customer portal BR-30): the list and the shared quote approval use cases reached by the quote id of the
/// session customer, never by a public token. Responder, audit actor and expiry follow the portal rules.
/// </summary>
[Route("portal/quotes")]
public sealed class PortalQuotesController(
    ListPortalCatalogHandler catalogHandler,
    ViewQuoteLinkHandler viewHandler,
    CalculateQuoteLinkHandler calculateHandler,
    ApproveQuoteLinkHandler approveHandler,
    DeclineQuoteLinkHandler declineHandler,
    AskQuoteQuestionHandler askHandler,
    GetQuoteLinkPhotoHandler photoHandler,
    DownloadQuoteLinkPdfHandler pdfHandler) : PortalControllerBase
{
    public const int MaxRequestBodyBytes = 16 * 1024;

    [HttpGet]
    [ProducesResponseType<PortalPage<PortalQuoteRow>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(
        [FromQuery] string? propertyId, [FromQuery] string? page, CancellationToken cancellationToken) =>
        Map(await catalogHandler.QuotesAsync(Scope, propertyId, page, cancellationToken), result => Ok(result));

    [HttpGet("{quoteId:guid}")]
    [EnableRateLimiting(ApiRateLimitingExtensions.QuoteLinkReadPolicy)]
    [ProducesResponseType<PublicQuote>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> View(Guid quoteId, CancellationToken cancellationToken) =>
        MapQuote(await viewHandler.HandleAsync(Access(quoteId), cancellationToken), quote => Ok(quote));

    [HttpGet("{quoteId:guid}/pdf")]
    [EnableRateLimiting(ApiRateLimitingExtensions.QuoteLinkPdfPolicy)]
    [ProducesResponseType(typeof(byte[]), StatusCodes.Status200OK, QuoteLinkMessages.PdfContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Pdf(Guid quoteId, CancellationToken cancellationToken) =>
        MapQuote(
            await pdfHandler.HandleAsync(Access(quoteId), cancellationToken),
            pdf => Pdf(pdf.FileName, QuoteLinkMessages.PdfContentType, pdf.Content));

    [HttpGet("{quoteId:guid}/photos")]
    [EnableRateLimiting(ApiRateLimitingExtensions.QuoteLinkReadPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Photos(Guid quoteId, CancellationToken cancellationToken) =>
        MapQuote(
            await viewHandler.HandleAsync(Access(quoteId), cancellationToken),
            quote => Ok(quote.Photos.Select(photo => new { photoId = photo.Id }).ToList()));

    [HttpGet("{quoteId:guid}/photos/{photoId:guid}/content")]
    [EnableRateLimiting(ApiRateLimitingExtensions.QuoteLinkReadPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PhotoContent(Guid quoteId, Guid photoId, CancellationToken cancellationToken) =>
        MapQuote(await photoHandler.HandleAsync(Access(quoteId), photoId, cancellationToken), Image);

    [HttpPost("{quoteId:guid}/calculate")]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxRequestBodyBytes)]
    [EnableRateLimiting(ApiRateLimitingExtensions.QuoteLinkReadPolicy)]
    [ProducesResponseType<PublicTotals>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Calculate(
        Guid quoteId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] PortalQuoteSelectionRequest request,
        CancellationToken cancellationToken) =>
        !ModelState.IsValid
            ? InvalidBody()
            : MapQuote(
                await calculateHandler.HandleAsync(Access(quoteId), request.SelectedOptionalLineIds, cancellationToken),
                totals => Ok(totals));

    [HttpPost("{quoteId:guid}/approve")]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxRequestBodyBytes)]
    [EnableRateLimiting(ApiRateLimitingExtensions.QuoteLinkActionPolicy)]
    [ProducesResponseType<PublicQuote>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Approve(
        Guid quoteId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] PortalQuoteApproveRequest request,
        CancellationToken cancellationToken) =>
        !ModelState.IsValid
            ? InvalidBody()
            : MapQuote(
                await approveHandler.HandleAsync(
                    Access(quoteId), request.SelectedOptionalLineIds, request.AcceptTerms, Caller(), cancellationToken),
                quote => Ok(quote));

    [HttpPost("{quoteId:guid}/reject")]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxRequestBodyBytes)]
    [EnableRateLimiting(ApiRateLimitingExtensions.QuoteLinkActionPolicy)]
    [ProducesResponseType<PublicQuote>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reject(
        Guid quoteId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] PortalQuoteDeclineRequest request,
        CancellationToken cancellationToken) =>
        !ModelState.IsValid
            ? InvalidBody()
            : MapQuote(await declineHandler.HandleAsync(Access(quoteId), request.Reason, Caller(), cancellationToken), quote => Ok(quote));

    [HttpPost("{quoteId:guid}/clarification")]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxRequestBodyBytes)]
    [EnableRateLimiting(ApiRateLimitingExtensions.QuoteLinkActionPolicy)]
    [ProducesResponseType<PublicQuote>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Clarification(
        Guid quoteId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] PortalQuoteQuestionRequest request,
        CancellationToken cancellationToken) =>
        !ModelState.IsValid
            ? InvalidBody()
            : MapQuote(await askHandler.HandleAsync(Access(quoteId), request.Message, Caller(), cancellationToken), quote => Ok(quote));
}
