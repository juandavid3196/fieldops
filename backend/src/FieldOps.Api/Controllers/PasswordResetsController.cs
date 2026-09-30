using System.Diagnostics;
using FieldOps.Api.Contracts;
using FieldOps.Api.Extensions;
using FieldOps.Application.Features.PasswordResets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;

namespace FieldOps.Api.Controllers;

/// <summary>
/// Password recovery: request a reset link, validate a token, confirm a new
/// password. Possession of a usable token is the only authorization; no user
/// or organization identifier is accepted.
/// </summary>
/// <remarks>
/// Not an [ApiController]: model state is checked here, per
/// <see cref="InvitationsController"/>. BR-13 order: rate limit, content
/// type/size/body, field shape, usable token. Every response carries no-store
/// through <c>PasswordResetNoStoreMiddleware</c>. There is no
/// <c>[Consumes]</c> attribute on purpose: the media type is enforced by body
/// binding (415) after the rate limiter.
/// </remarks>
[Route("password-resets")]
public sealed class PasswordResetsController(
    RequestPasswordResetHandler requestHandler,
    ValidatePasswordResetHandler validateHandler,
    ConfirmPasswordResetHandler confirmHandler) : ControllerBase
{
    public const int MaxRequestBodyBytes = 4 * 1024;

    private const string GoneTitle = "This password reset link is no longer available.";

    [HttpPost]
    [AllowAnonymous]
    [RequestSizeLimit(MaxRequestBodyBytes)]
    [EnableRateLimiting(ApiRateLimitingExtensions.PasswordResetRequestPolicy)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> RequestReset(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] PasswordResetRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return InvalidBody();
        }

        var result = await requestHandler.HandleAsync(new RequestPasswordResetCommand(request.Email), cancellationToken);

        return Map(result, _ => Accepted());
    }

    [HttpPost("validate")]
    [AllowAnonymous]
    [RequestSizeLimit(MaxRequestBodyBytes)]
    [EnableRateLimiting(ApiRateLimitingExtensions.PasswordResetTokenPolicy)]
    [ProducesResponseType<PasswordResetAccountView>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status410Gone)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Validate(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] PasswordResetTokenRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return InvalidBody();
        }

        var result = await validateHandler.HandleAsync(new ValidatePasswordResetCommand(request.Token), cancellationToken);

        return Map(result, account => Ok(account));
    }

    [HttpPost("confirm")]
    [AllowAnonymous]
    [RequestSizeLimit(MaxRequestBodyBytes)]
    [EnableRateLimiting(ApiRateLimitingExtensions.PasswordResetTokenPolicy)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status410Gone)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Confirm(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] ConfirmPasswordResetRequestBody request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return InvalidBody();
        }

        var result = await confirmHandler.HandleAsync(
            new ConfirmPasswordResetCommand(request.Token, request.Password),
            cancellationToken);

        return Map(result, _ => NoContent());
    }

    private IActionResult Map<T>(PasswordResetResult<T> result, Func<T, IActionResult> ok)
    {
        switch (result.Kind)
        {
            case PasswordResetResultKind.Succeeded:
                return ok(result.Value!);

            case PasswordResetResultKind.Invalid:
                foreach (var (key, messages) in result.Errors!)
                {
                    foreach (var message in messages)
                    {
                        ModelState.AddModelError(key, message);
                    }
                }

                return ValidationProblem(ModelState);

            case PasswordResetResultKind.Gone:
                // One shared shape for every unusable token (BR-08).
                var problem = new ProblemDetails { Status = StatusCodes.Status410Gone, Title = GoneTitle };
                problem.Extensions["traceId"] = Activity.Current?.Id ?? HttpContext.TraceIdentifier;

                return StatusCode(StatusCodes.Status410Gone, problem);

            default:
                throw new InvalidOperationException("Unknown password reset result.");
        }
    }

    // Malformed JSON, an empty or null body: keyless 400; a missing or
    // non-JSON Content-Type: 415.
    private IActionResult InvalidBody() =>
        ModelState.Values
            .SelectMany(entry => entry.Errors)
            .Any(error => error.Exception is UnsupportedContentTypeException)
            ? new UnsupportedMediaTypeResult()
            : BadRequest();
}
