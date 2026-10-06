using System.Diagnostics;
using FieldOps.Api.Contracts;
using FieldOps.Api.Extensions;
using FieldOps.Application.Features.QuoteLinks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;

namespace FieldOps.Api.Controllers;

/// <summary>
/// The public quote link (customer-quote-approval): anonymous POSTs whose only credential is the token in the JSON body.
/// The organization, quote and version always come from the token row; any such identifier in a body is ignored.
/// </summary>
/// <remarks>
/// Not an [ApiController]: model state is checked here, per <see cref="InvitationsController"/>. Every response carries
/// no-store, no-referrer and nosniff through <c>QuoteLinkPublicHeadersMiddleware</c> (binary responses use
/// <c>private, no-store</c>). Nothing here logs the token, reasons, questions or responder data.
/// </remarks>
[Route("public/quote-links")]
[AllowAnonymous]
public sealed class PublicQuoteLinksController(
    ViewQuoteLinkHandler viewHandler,
    CalculateQuoteLinkHandler calculateHandler,
    ApproveQuoteLinkHandler approveHandler,
    DeclineQuoteLinkHandler declineHandler,
    AskQuoteQuestionHandler askHandler,
    GetQuoteLinkPhotoHandler photoHandler,
    GetQuoteLinkLogoHandler logoHandler,
    DownloadQuoteLinkPdfHandler pdfHandler) : ControllerBase
{
    public const int MaxRequestBodyBytes = 16 * 1024;

    public const string CodeKey = "code";

    private const int MaxUserAgentLength = 256;

    [HttpPost("view")]
    [RequestSizeLimit(MaxRequestBodyBytes)]
    [EnableRateLimiting(ApiRateLimitingExtensions.QuoteLinkReadPolicy)]
    [ProducesResponseType<PublicQuote>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status410Gone)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> View(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] QuoteLinkTokenRequest request,
        CancellationToken cancellationToken) =>
        !ModelState.IsValid
            ? InvalidBody()
            : Map(await viewHandler.HandleAsync(request.Token, cancellationToken), quote => Ok(quote));

    [HttpPost("calculate")]
    [RequestSizeLimit(MaxRequestBodyBytes)]
    [EnableRateLimiting(ApiRateLimitingExtensions.QuoteLinkReadPolicy)]
    [ProducesResponseType<PublicTotals>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status410Gone)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Calculate(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] QuoteLinkCalculateRequest request,
        CancellationToken cancellationToken) =>
        !ModelState.IsValid
            ? InvalidBody()
            : Map(
                await calculateHandler.HandleAsync(request.Token, request.SelectedOptionalLineIds, cancellationToken),
                totals => Ok(totals));

    [HttpPost("approve")]
    [RequestSizeLimit(MaxRequestBodyBytes)]
    [EnableRateLimiting(ApiRateLimitingExtensions.QuoteLinkActionPolicy)]
    [ProducesResponseType<PublicQuote>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status410Gone)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Approve(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] QuoteLinkApproveRequest request,
        CancellationToken cancellationToken) =>
        !ModelState.IsValid
            ? InvalidBody()
            : Map(
                await approveHandler.HandleAsync(
                    request.Token, request.SelectedOptionalLineIds, request.AcceptTerms, Caller(), cancellationToken),
                quote => Ok(quote));

    [HttpPost("decline")]
    [RequestSizeLimit(MaxRequestBodyBytes)]
    [EnableRateLimiting(ApiRateLimitingExtensions.QuoteLinkActionPolicy)]
    [ProducesResponseType<PublicQuote>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status410Gone)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Decline(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] QuoteLinkDeclineRequest request,
        CancellationToken cancellationToken) =>
        !ModelState.IsValid
            ? InvalidBody()
            : Map(await declineHandler.HandleAsync(request.Token, request.Reason, Caller(), cancellationToken), quote => Ok(quote));

    [HttpPost("clarification")]
    [RequestSizeLimit(MaxRequestBodyBytes)]
    [EnableRateLimiting(ApiRateLimitingExtensions.QuoteLinkActionPolicy)]
    [ProducesResponseType<PublicQuote>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status410Gone)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Clarification(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] QuoteLinkQuestionRequest request,
        CancellationToken cancellationToken) =>
        !ModelState.IsValid
            ? InvalidBody()
            : Map(await askHandler.HandleAsync(request.Token, request.Message, Caller(), cancellationToken), quote => Ok(quote));

    [HttpPost("photos/{photoId:guid}")]
    [RequestSizeLimit(MaxRequestBodyBytes)]
    [EnableRateLimiting(ApiRateLimitingExtensions.QuoteLinkReadPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status410Gone)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Photo(
        Guid photoId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] QuoteLinkTokenRequest request,
        CancellationToken cancellationToken) =>
        !ModelState.IsValid
            ? InvalidBody()
            : Map(await photoHandler.HandleAsync(request.Token, photoId, cancellationToken), Image);

    [HttpPost("logo")]
    [RequestSizeLimit(MaxRequestBodyBytes)]
    [EnableRateLimiting(ApiRateLimitingExtensions.QuoteLinkReadPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status410Gone)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Logo(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] QuoteLinkTokenRequest request,
        CancellationToken cancellationToken) =>
        !ModelState.IsValid
            ? InvalidBody()
            : Map(await logoHandler.HandleAsync(request.Token, cancellationToken), Image);

    [HttpPost("pdf")]
    [RequestSizeLimit(MaxRequestBodyBytes)]
    [EnableRateLimiting(ApiRateLimitingExtensions.QuoteLinkPdfPolicy)]
    [ProducesResponseType(typeof(byte[]), StatusCodes.Status200OK, QuoteLinkMessages.PdfContentType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status410Gone)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Pdf(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] QuoteLinkTokenRequest request,
        CancellationToken cancellationToken) =>
        !ModelState.IsValid
            ? InvalidBody()
            : Map(await pdfHandler.HandleAsync(request.Token, cancellationToken), pdf =>
            {
                BinaryHeaders($"attachment; filename=\"{pdf.FileName}\"");

                return File(pdf.Content, QuoteLinkMessages.PdfContentType);
            });

    private IActionResult Image(PublicBinary image)
    {
        BinaryHeaders("inline");

        return File(image.Content, image.ContentType);
    }

    // Binary responses are never cached, not even by the browser (BR-07); nosniff comes from the middleware.
    private void BinaryHeaders(string disposition)
    {
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.ContentDisposition = disposition;
        Response.Headers.XContentTypeOptions = "nosniff";
    }

    private IActionResult Map<T>(QuoteLinkOutcome<T> outcome, Func<T, IActionResult> ok)
    {
        switch (outcome)
        {
            case QuoteLinkOutcome<T>.Succeeded succeeded:
                return ok(succeeded.Value);

            case QuoteLinkOutcome<T>.Invalid invalid:
                foreach (var (key, messages) in invalid.Errors)
                {
                    foreach (var message in messages)
                    {
                        ModelState.AddModelError(key, message);
                    }
                }

                return ValidationProblem(ModelState);

            case QuoteLinkOutcome<T>.Superseded:
                return Problem(StatusCodes.Status410Gone, QuoteLinkMessages.SupersededTitle, QuoteLinkMessages.SupersededCode);

            case QuoteLinkOutcome<T>.AlreadyAnswered:
                return Problem(StatusCodes.Status409Conflict, QuoteLinkMessages.AlreadyAnsweredTitle, QuoteLinkMessages.AlreadyAnsweredCode);

            default:
                // One shape for every unusable token, so nothing reveals why (BR-03).
                return Problem(StatusCodes.Status404NotFound, QuoteLinkMessages.UnavailableTitle, QuoteLinkMessages.UnavailableCode);
        }
    }

    private IActionResult Problem(int status, string title, string code)
    {
        var problem = new ProblemDetails { Status = status, Title = title };
        problem.Extensions[CodeKey] = code;
        problem.Extensions["traceId"] = Activity.Current?.Id ?? HttpContext.TraceIdentifier;

        return StatusCode(status, problem);
    }

    // Malformed JSON, an empty or null body: keyless 400; a missing or non-JSON Content-Type: 415.
    private IActionResult InvalidBody() =>
        ModelState.Values.SelectMany(entry => entry.Errors).Any(error => error.Exception is UnsupportedContentTypeException)
            ? new UnsupportedMediaTypeResult()
            : BadRequest();

    private QuoteLinkCaller Caller()
    {
        var address = HttpContext.Connection.RemoteIpAddress;
        var agent = Request.Headers.UserAgent.ToString().Trim();

        return new QuoteLinkCaller(
            address is { IsIPv4MappedToIPv6: true } ? address.MapToIPv4() : address,
            agent.Length == 0 ? null : agent.Length > MaxUserAgentLength ? agent[..MaxUserAgentLength] : agent);
    }
}
