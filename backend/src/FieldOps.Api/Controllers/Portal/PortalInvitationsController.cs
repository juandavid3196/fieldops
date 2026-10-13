using FieldOps.Api.Authentication;
using FieldOps.Api.Contracts;
using FieldOps.Api.Extensions;
using FieldOps.Application.Features.PasswordResets;
using FieldOps.Application.Features.PortalAccess;
using FieldOps.Application.Features.PortalAuth;
using FieldOps.Application.Features.PortalInvitations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;

namespace FieldOps.Api.Controllers.Portal;

/// <summary>
/// Portal activation (customer portal BR-12, BR-13): validate, accept as a new user and accept as an existing user. Possession
/// of a usable token is the authorization; the organization, customer and contact always come from the invitation row. The
/// three endpoints share one rate limit partition. The cookie is issued only after the activation committed.
/// </summary>
[Route("portal/invitations")]
[AllowAnonymous]
public sealed class PortalInvitationsController(
    ValidatePortalInvitationHandler validateHandler,
    AcceptPortalInvitationHandler acceptHandler,
    AcceptExistingPortalInvitationHandler acceptExistingHandler,
    ILogger<PortalInvitationsController> logger) : PortalControllerBase
{
    public const int MaxRequestBodyBytes = 4 * 1024;

    [HttpPost("validate")]
    [RequestSizeLimit(MaxRequestBodyBytes)]
    [EnableRateLimiting(ApiRateLimitingExtensions.InvitationPolicy)]
    [ProducesResponseType<PortalInvitationDetails>(StatusCodes.Status200OK)]
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

        return Map(
            await validateHandler.HandleAsync(new ValidatePortalInvitationCommand(request.Token), cancellationToken),
            details => Ok(details),
            Gone);
    }

    [HttpPost("accept")]
    [RequestSizeLimit(MaxRequestBodyBytes)]
    [EnableRateLimiting(ApiRateLimitingExtensions.InvitationPolicy)]
    [ProducesResponseType<PortalSessionView>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status410Gone)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Accept(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] PortalAcceptInvitationRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return InvalidBody();
        }

        var outcome = await acceptHandler.HandleAsync(
            new AcceptPortalInvitationCommand(request.Token, request.Password, request.LastName, ClientIp()), cancellationToken);

        return await MapAcceptedAsync(outcome);
    }

    [HttpPost("accept-existing")]
    [RequestSizeLimit(MaxRequestBodyBytes)]
    [EnableRateLimiting(ApiRateLimitingExtensions.InvitationPolicy)]
    [ProducesResponseType<PortalSessionView>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status410Gone)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> AcceptExisting(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] PortalAcceptExistingInvitationRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return InvalidBody();
        }

        var outcome = await acceptExistingHandler.HandleAsync(
            new AcceptExistingPortalInvitationCommand(request.Token, request.Password, ClientIp()), cancellationToken);

        return await MapAcceptedAsync(outcome);
    }

    private async Task<IActionResult> MapAcceptedAsync(PortalOutcome<PortalAcceptedInvitation> outcome)
    {
        var mapped = Map(outcome, accepted => Ok(accepted.Session.View), Gone);

        if (outcome is PortalOutcome<PortalAcceptedInvitation>.Ok { Value: var accepted })
        {
            // Remember me is off for an activation; the cookie follows the commit of the activation.
            await PortalSessionCookieIssuer.SignInAsync(HttpContext, accepted.Session, accepted.SignedInAt, rememberMe: false);

            // The contact id only; never the token, email or request body.
            logger.LogInformation("Portal access activated for contact {ContactId}", accepted.Session.Scope.ContactId);
        }

        return mapped;
    }

    // One shared shape for every unusable token (customer portal BR-12).
    private IActionResult Gone() =>
        CodedProblem(StatusCodes.Status410Gone, PortalMessages.InvitationUnavailableTitle, PortalMessages.InvitationUnavailableCode);
}

/// <summary>POST /portal/password-resets (customer portal BR-15): the neutral 202 of the internal endpoint with portal links.</summary>
[Route("portal/password-resets")]
[AllowAnonymous]
public sealed class PortalPasswordResetsController(RequestPortalPasswordResetHandler requestHandler) : PortalControllerBase
{
    public const int MaxRequestBodyBytes = 4 * 1024;

    [HttpPost]
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

        return result.Kind == PasswordResetResultKind.Succeeded ? Accepted() : FieldErrors(result.Errors!);
    }
}
