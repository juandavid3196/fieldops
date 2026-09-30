using System.Diagnostics;
using System.Net;
using FieldOps.Api.Authentication;
using FieldOps.Api.Contracts;
using FieldOps.Api.Extensions;
using FieldOps.Application.Authentication;
using FieldOps.Application.Features.Invitations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;

namespace FieldOps.Api.Controllers;

/// <summary>
/// Invitation acceptance: validate, accept as a new user and accept as an
/// existing user. Possession of a usable token is the authorization; the
/// organization always comes from the invitation the token resolves to.
/// </summary>
/// <remarks>
/// Not an [ApiController]: model state is checked here, per
/// <see cref="SessionsController"/>. BR-18 order: rate limit, session
/// (accept-existing only), content type/size/body, field shape, usable token,
/// eligibility. Every response carries no-store through
/// <c>InvitationNoStoreMiddleware</c>. There is no <c>[Consumes]</c> attribute on
/// purpose: it rejects by routing, before the rate limiter and the session
/// check, so the media type is enforced by body binding (415) instead.
/// </remarks>
[Route("invitations")]
public sealed class InvitationsController(
    ValidateInvitationHandler validateHandler,
    AcceptInvitationHandler acceptHandler,
    AcceptExistingInvitationHandler acceptExistingHandler,
    ILogger<InvitationsController> logger) : ControllerBase
{
    public const int MaxRequestBodyBytes = 4 * 1024;

    public const string ConflictCodeKey = "code";

    private const string GoneTitle = "This invitation is no longer available.";

    private const string ConflictTitle = "The invitation can't be accepted.";

    [HttpPost("validate")]
    [AllowAnonymous]
    [RequestSizeLimit(MaxRequestBodyBytes)]
    [EnableRateLimiting(ApiRateLimitingExtensions.InvitationPolicy)]
    [ProducesResponseType<InvitationDetailsView>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status410Gone)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Validate(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] InvitationTokenRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return InvalidBody();
        }

        var result = await validateHandler.HandleAsync(new ValidateInvitationCommand(request.Token), cancellationToken);

        return Map(result, details => Ok(details));
    }

    [HttpPost("accept")]
    [AllowAnonymous]
    [RequestSizeLimit(MaxRequestBodyBytes)]
    [EnableRateLimiting(ApiRateLimitingExtensions.InvitationPolicy)]
    [ProducesResponseType<SessionView>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status410Gone)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Accept(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] AcceptInvitationRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return InvalidBody();
        }

        var result = await acceptHandler.HandleAsync(
            new AcceptInvitationCommand(
                request.Token,
                request.FirstName,
                request.LastName,
                request.Password,
                GetClientIpAddress()),
            cancellationToken);

        return await MapAcceptedAsync(result);
    }

    // [Authorize] runs in middleware before body binding, so a missing or
    // invalid session is a 401 whatever the body is (BR-18 step 2).
    [HttpPost("accept-existing")]
    [Authorize]
    [RequestSizeLimit(MaxRequestBodyBytes)]
    [EnableRateLimiting(ApiRateLimitingExtensions.InvitationPolicy)]
    [ProducesResponseType<SessionView>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status410Gone)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> AcceptExisting(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] InvitationTokenRequest request,
        CancellationToken cancellationToken)
    {
        var session = SessionCookieEvents.GetValidatedSession(HttpContext);

        if (session is null)
        {
            return Unauthorized();
        }

        if (!ModelState.IsValid)
        {
            return InvalidBody();
        }

        // The user comes from the session; the email is read from the database.
        var result = await acceptExistingHandler.HandleAsync(
            new AcceptExistingInvitationCommand(request.Token, session.User.Id, GetClientIpAddress()),
            cancellationToken);

        return await MapAcceptedAsync(result);
    }

    private async Task<IActionResult> MapAcceptedAsync(InvitationResult<AcceptedInvitation> result)
    {
        var mapped = Map(result, accepted => Ok(accepted.Session));

        // The cookie is issued only after the transaction committed.
        if (result.Kind == InvitationResultKind.Succeeded)
        {
            var accepted = result.Value!;

            await SessionCookieIssuer.SignInAsync(
                HttpContext,
                accepted.Session,
                accepted.MembershipId,
                accepted.SignedInAt,
                rememberMe: false);

            // Ids and category only; never the token, email or request body.
            logger.LogInformation(
                "Invitation accepted for membership {MembershipId}",
                accepted.MembershipId);
        }

        return mapped;
    }

    private IActionResult Map<T>(InvitationResult<T> result, Func<T, IActionResult> ok)
    {
        switch (result.Kind)
        {
            case InvitationResultKind.Succeeded:
                return ok(result.Value!);

            case InvitationResultKind.Invalid:
                foreach (var (key, messages) in result.Errors!)
                {
                    foreach (var message in messages)
                    {
                        ModelState.AddModelError(key, message);
                    }
                }

                return ValidationProblem(ModelState);

            case InvitationResultKind.Gone:
                // One shared shape for every unusable token (BR-05).
                return StatusCode(StatusCodes.Status410Gone, NewProblem(StatusCodes.Status410Gone, GoneTitle));

            case InvitationResultKind.Conflict:
                var problem = NewProblem(StatusCodes.Status409Conflict, ConflictTitle);
                problem.Extensions[ConflictCodeKey] = ConflictCode(result.Conflict!.Value);

                return Conflict(problem);

            default:
                throw new InvalidOperationException("Unknown invitation result.");
        }
    }

    private static string ConflictCode(InvitationConflict conflict) =>
        conflict switch
        {
            InvitationConflict.AccountExists => "account_exists",
            InvitationConflict.IdentityMismatch => "identity_mismatch",
            InvitationConflict.MembershipExists => "membership_exists",
            InvitationConflict.AccessUnavailable => "access_unavailable",
            _ => throw new InvalidOperationException("Unknown invitation conflict."),
        };

    private ProblemDetails NewProblem(int status, string title)
    {
        var problem = new ProblemDetails { Status = status, Title = title };
        problem.Extensions["traceId"] = Activity.Current?.Id ?? HttpContext.TraceIdentifier;

        return problem;
    }

    // Malformed JSON, an empty or null body: keyless 400; a missing or
    // non-JSON Content-Type: 415.
    private IActionResult InvalidBody() =>
        HasUnsupportedContentType()
            ? new UnsupportedMediaTypeResult()
            : BadRequest();

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
