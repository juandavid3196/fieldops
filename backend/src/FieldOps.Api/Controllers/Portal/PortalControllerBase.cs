using System.Diagnostics;
using System.Globalization;
using System.Net;
using FieldOps.Api.Authentication;
using FieldOps.Application.Features.OnlinePayments;
using FieldOps.Application.Features.PortalAccess;
using FieldOps.Application.Features.QuoteLinks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace FieldOps.Api.Controllers.Portal;

/// <summary>
/// Base of the customer portal controllers: every action authenticates only with the portal cookie scheme (customer portal
/// BR-01), so the internal cookie never reaches a portal endpoint (401 without a code or redirect). Anonymous actions opt out
/// with <c>[AllowAnonymous]</c>. The organization, customer and contact always come from the revalidated session, never from
/// a request value.
/// </summary>
/// <remarks>
/// Not an [ApiController]: model state is checked here, per <see cref="InvitationsController"/>. Every response is no-store
/// through <c>PortalNoStoreMiddleware</c>; binary responses use <c>private, no-store</c>.
/// </remarks>
[Authorize(AuthenticationSchemes = PortalSessionCookie.Scheme)]
public abstract class PortalControllerBase : ControllerBase
{
    public const string CodeKey = "code";

    private const int MaxUserAgentLength = 256;

    /// <summary>The scope of the validated portal session.</summary>
    protected PortalScope Scope =>
        PortalSessionCookieEvents.GetValidatedSession(HttpContext)?.Scope
        ?? throw new InvalidOperationException("The portal session was not validated.");

    protected ResourceAccess Access(Guid resourceId) => new ResourceAccess.Portal(Scope, resourceId);

    protected IPAddress? ClientIp()
    {
        var address = HttpContext.Connection.RemoteIpAddress;

        return address is { IsIPv4MappedToIPv6: true } ? address.MapToIPv4() : address;
    }

    protected QuoteLinkCaller Caller()
    {
        var agent = Request.Headers.UserAgent.ToString().Trim();

        return new QuoteLinkCaller(
            ClientIp(),
            agent.Length == 0 ? null : agent.Length > MaxUserAgentLength ? agent[..MaxUserAgentLength] : agent);
    }

    // Malformed JSON, an empty or null body: keyless 400; a missing or non-JSON Content-Type: 415.
    protected IActionResult InvalidBody() =>
        ModelState.Values.SelectMany(entry => entry.Errors).Any(error => error.Exception is UnsupportedContentTypeException)
            ? new UnsupportedMediaTypeResult()
            : BadRequest();

    protected ObjectResult CodedProblem(int status, string title, string code)
    {
        var problem = new ProblemDetails { Status = status, Title = title };
        problem.Extensions[CodeKey] = code;
        problem.Extensions["traceId"] = Activity.Current?.Id ?? HttpContext.TraceIdentifier;

        return StatusCode(status, problem);
    }

    /// <summary>The one identical 404 of every unknown, foreign-customer and foreign-organization resource (customer portal BR-37).</summary>
    protected ObjectResult Unavailable() =>
        CodedProblem(StatusCodes.Status404NotFound, PortalMessages.UnavailableTitle, PortalMessages.UnavailableCode);

    protected IActionResult FieldErrors(IReadOnlyDictionary<string, string[]> errors)
    {
        // An empty error set is the honeypot or a keyless failure: the generic 400.
        if (errors.Count == 0)
        {
            return BadRequest();
        }

        foreach (var (key, messages) in errors)
        {
            foreach (var message in messages)
            {
                ModelState.AddModelError(key, message);
            }
        }

        return ValidationProblem(ModelState);
    }

    protected IActionResult TooManyRequests(TimeSpan retryAfter)
    {
        Response.Headers.RetryAfter = Math.Max(1, (long)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

        return StatusCode(StatusCodes.Status429TooManyRequests);
    }

    /// <summary>Maps a portal outcome; <paramref name="gone"/> is the 410 of the anonymous invitation endpoints.</summary>
    protected IActionResult Map<T>(PortalOutcome<T> outcome, Func<T, IActionResult> ok, Func<IActionResult>? gone = null)
    {
        switch (outcome)
        {
            case PortalOutcome<T>.Ok success:
                return ok(success.Value);
            case PortalOutcome<T>.Created created:
                return StatusCode(StatusCodes.Status201Created, created.Value);
            case PortalOutcome<T>.Invalid invalid:
                return FieldErrors(invalid.Errors);
            case PortalOutcome<T>.Conflict conflict:
                return CodedProblem(StatusCodes.Status409Conflict, conflict.Title, conflict.Code);
            case PortalOutcome<T>.Throttled throttled:
                return TooManyRequests(throttled.RetryAfter);
            case PortalOutcome<T>.Unauthorized:
                return Unauthorized();
            case PortalOutcome<T>.Gone when gone is not null:
                return gone();
            default:
                return Unavailable();
        }
    }

    /// <summary>Maps the outcome of a shared quote use case (customer portal BR-30).</summary>
    protected IActionResult MapQuote<T>(QuoteLinkOutcome<T> outcome, Func<T, IActionResult> ok)
    {
        switch (outcome)
        {
            case QuoteLinkOutcome<T>.Succeeded succeeded:
                return ok(succeeded.Value);
            case QuoteLinkOutcome<T>.Invalid invalid:
                return FieldErrors(invalid.Errors);
            case QuoteLinkOutcome<T>.AlreadyAnswered:
                return CodedProblem(
                    StatusCodes.Status409Conflict, QuoteLinkMessages.AlreadyAnsweredTitle, QuoteLinkMessages.AlreadyAnsweredCode);
            case QuoteLinkOutcome<T>.Expired:
                return CodedProblem(StatusCodes.Status409Conflict, QuoteLinkMessages.ExpiredTitle, QuoteLinkMessages.ExpiredCode);
            default:
                return Unavailable();
        }
    }

    /// <summary>Maps the outcome of a shared invoice payment use case (customer portal BR-31).</summary>
    protected IActionResult MapPayment<T>(PublicOutcome<T> outcome)
    {
        switch (outcome)
        {
            case PublicOutcome<T>.Ok { Created: true } created:
                return StatusCode(StatusCodes.Status201Created, created.Value);
            case PublicOutcome<T>.Ok ok:
                return Ok(ok.Value);
            case PublicOutcome<T>.Invalid invalid:
                return FieldErrors(invalid.Errors);
            case PublicOutcome<T>.Conflict conflict:
                return CodedProblem(StatusCodes.Status409Conflict, conflict.Title, conflict.Code);
            case PublicOutcome<T>.RateLimited limited:
                return TooManyRequests(limited.RetryAfter);
            case PublicOutcome<T>.ProviderUnavailable:
                return CodedProblem(
                    StatusCodes.Status502BadGateway, OnlinePaymentMessages.ProviderUnavailableTitle, OnlinePaymentMessages.ProviderUnavailableCode);
            default:
                return Unavailable();
        }
    }

    // Binary responses are never cached, not even by the browser; nosniff included.
    protected void BinaryHeaders(string disposition)
    {
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.ContentDisposition = disposition;
        Response.Headers.XContentTypeOptions = "nosniff";
    }

    protected IActionResult Pdf(string fileName, string contentType, byte[] content)
    {
        BinaryHeaders($"attachment; filename=\"{fileName}\"");

        return File(content, contentType);
    }

    protected IActionResult Image(PublicBinary image)
    {
        BinaryHeaders("inline");

        return File(image.Content, image.ContentType);
    }
}
