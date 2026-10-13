using System.Globalization;
using FieldOps.Api.Authentication;
using FieldOps.Api.Contracts;
using FieldOps.Api.Extensions;
using FieldOps.Application.Authentication;
using FieldOps.Application.Features.PortalAccess;
using FieldOps.Application.Features.PortalAuth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;

namespace FieldOps.Api.Controllers.Portal;

/// <summary>
/// Portal sign in, current session, switch account and sign out (customer portal BR-03 … BR-08). Sign-in follows the internal
/// rules: every credential failure is the same 401, field errors carry only the "email" and "password" keys.
/// </summary>
[Route("portal/sessions")]
public sealed class PortalSessionsController(
    SignInPortalHandler signInHandler,
    SwitchPortalAccountHandler switchHandler,
    TimeProvider timeProvider,
    ILogger<PortalSessionsController> logger) : PortalControllerBase
{
    public const int MaxRequestBodyBytes = 4 * 1024;

    [HttpPost]
    [AllowAnonymous]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxRequestBodyBytes)]
    [EnableRateLimiting(ApiRateLimitingExtensions.SignInPolicy)]
    [ProducesResponseType<PortalSessionView>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> CreateSession(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] SignInRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return InvalidBody();
        }

        var command = new SignInCommand(request.Email, request.Password, request.RememberMe ?? false, ClientIp());

        switch (await signInHandler.HandleAsync(command, cancellationToken))
        {
            case PortalSignInResult.Succeeded succeeded:
                await PortalSessionCookieIssuer.SignInAsync(HttpContext, succeeded.Session, succeeded.SignedInAt, command.RememberMe);

                return Ok(succeeded.Session.View);

            case PortalSignInResult.Invalid invalid:
                return FieldErrors(invalid.Errors);

            case PortalSignInResult.InvalidCredentials failed:
                // Never the email: only the category and, when known, the user id.
                logger.LogWarning("Portal sign-in failed: {FailureCategory} {UserId}", failed.Category, failed.UserId);

                return Unauthorized();

            case PortalSignInResult.Throttled throttled:
                return TooManyRequests(throttled.RetryAfter);

            default:
                throw new InvalidOperationException("Unknown portal sign-in result.");
        }
    }

    [HttpGet("current")]
    [ProducesResponseType<PortalSessionView>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public IActionResult GetCurrentSession() =>
        PortalSessionCookieEvents.GetValidatedSession(HttpContext) is { } session ? Ok(session.View) : Unauthorized();

    [HttpDelete("current")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteCurrentSession()
    {
        // Only the portal cookie: the internal session is untouched (customer portal BR-08).
        await HttpContext.SignOutAsync(PortalSessionCookie.Scheme);

        return NoContent();
    }

    [HttpPost("current/account")]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxRequestBodyBytes)]
    [ProducesResponseType<PortalSessionView>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SwitchAccount(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] PortalSwitchAccountRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return InvalidBody();
        }

        if (!PortalSessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        var session = await switchHandler.HandleAsync(Scope, ticket.SignedInAt, request.ContactId, ClientIp(), cancellationToken);

        if (session is null)
        {
            return CodedProblem(StatusCodes.Status404NotFound, "This account isn't available.", PortalMessages.AccountUnavailableCode);
        }

        // The cookie is reissued only after the switch was audited; the sign-in time and Remember me are kept.
        await PortalSessionCookieIssuer.SignInAsync(HttpContext, session, ticket.SignedInAt, ticket.RememberMe, timeProvider.GetUtcNow());

        return Ok(session.View);
    }
}
