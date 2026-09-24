using System.Globalization;
using System.Net;
using FieldOps.Api.Authentication;
using FieldOps.Api.Contracts;
using FieldOps.Api.Extensions;
using FieldOps.Application.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;
using SignInResult = FieldOps.Application.Authentication.SignInResult;

namespace FieldOps.Api.Controllers;

/// <summary>
/// Sign in, current session and sign out.
/// </summary>
/// <remarks>
/// Not an [ApiController]: model state is checked here so malformed JSON,
/// a JSON type mismatch, an empty body or a JSON <c>null</c> body return an
/// empty 400 (rendered as ProblemDetails without field keys by
/// UseStatusCodePages), a missing or non-JSON Content-Type returns 415, and
/// field rules return ValidationProblemDetails with only the "email" and
/// "password" keys.
/// </remarks>
[Route("sessions")]
public sealed class SessionsController(
    SignInHandler signInHandler,
    ILogger<SessionsController> logger) : ControllerBase
{
    public const int MaxRequestBodyBytes = 4 * 1024;

    private const string NoStore = "no-store";

    [HttpPost]
    [AllowAnonymous]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxRequestBodyBytes)]
    [EnableRateLimiting(ApiRateLimitingExtensions.SignInPolicy)]
    [ProducesResponseType<SessionView>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> CreateSession(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] SignInRequest request,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = NoStore;

        // EmptyBodyBehavior.Disallow: an empty or JSON null body is a model
        // state error (keyless 400) and a request without Content-Type fails
        // input formatter selection (415), so request is never null here.
        if (!ModelState.IsValid)
        {
            return HasUnsupportedContentType()
                ? new UnsupportedMediaTypeResult()
                : BadRequest();
        }

        // Only these three fields are read; any identifier sent by the
        // client is ignored and the organization is resolved server-side.
        var command = new SignInCommand(
            request.Email,
            request.Password,
            request.RememberMe ?? false,
            GetClientIpAddress());

        var result = await signInHandler.HandleAsync(command, cancellationToken);

        switch (result)
        {
            case SignInResult.Succeeded succeeded:
                await SignInAsync(succeeded, command.RememberMe);
                return Ok(succeeded.Session);

            case SignInResult.Invalid invalid:
                foreach (var (key, messages) in invalid.Errors)
                {
                    foreach (var message in messages)
                    {
                        ModelState.AddModelError(key, message);
                    }
                }

                return ValidationProblem(ModelState);

            case SignInResult.InvalidCredentials failed:
                // Never the email: only the category and, when known, the user id.
                logger.LogWarning(
                    "Sign-in failed: {FailureCategory} {UserId}",
                    failed.Category,
                    failed.UserId);

                return Unauthorized();

            case SignInResult.Throttled throttled:
                var seconds = Math.Max(1, (long)Math.Ceiling(throttled.RetryAfter.TotalSeconds));
                Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
                return StatusCode(StatusCodes.Status429TooManyRequests);

            default:
                throw new InvalidOperationException("Unknown sign-in result.");
        }
    }

    [HttpGet("current")]
    [Authorize]
    [ProducesResponseType<SessionView>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public IActionResult GetCurrentSession()
    {
        Response.Headers.CacheControl = NoStore;

        var session = SessionCookieEvents.GetValidatedSession(HttpContext);

        return session is null ? Unauthorized() : Ok(session);
    }

    [HttpDelete("current")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteCurrentSession()
    {
        await HttpContext.SignOutAsync(SessionCookie.Scheme);

        return NoContent();
    }

    private Task SignInAsync(SignInResult.Succeeded succeeded, bool rememberMe)
    {
        var principal = SessionClaims.CreatePrincipal(new SessionTicket(
            succeeded.Session.User.Id,
            succeeded.Session.Organization.Id,
            succeeded.MembershipId,
            succeeded.SignedInAt,
            rememberMe));

        // Remember me off: browser-session cookie (no Expires), 8-hour ticket,
        // never renewed. On: persistent 14-day sliding cookie.
        var properties = new AuthenticationProperties
        {
            IssuedUtc = succeeded.SignedInAt,
            IsPersistent = rememberMe,
            AllowRefresh = rememberMe,
            ExpiresUtc = succeeded.SignedInAt + (rememberMe
                ? SessionCookie.RememberMeIdleLifetime
                : SessionCookie.BrowserSessionLifetime),
        };

        return HttpContext.SignInAsync(SessionCookie.Scheme, principal, properties);
    }

    private IPAddress? GetClientIpAddress()
    {
        var address = HttpContext.Connection.RemoteIpAddress;

        return address is { IsIPv4MappedToIPv6: true } ? address.MapToIPv4() : address;
    }

    private bool HasUnsupportedContentType() =>
        ModelState.Values
            .SelectMany(entry => entry.Errors)
            .Any(error => error.Exception is UnsupportedContentTypeException);
}
