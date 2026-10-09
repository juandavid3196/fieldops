using System.Net;
using FieldOps.Api.Authentication;
using FieldOps.Api.Authorization;
using FieldOps.Api.Contracts;
using FieldOps.Application.Features.Organizations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace FieldOps.Api.Controllers;

/// <summary>
/// The bank transfer details of the session organization (customer-invoice-payments BR-24, BR-25): Owner only for both reads
/// and writes (<see cref="CompanySettingsPolicies.Manage"/> is the owner role), never cached, and the full account number is
/// never returned. Not an [ApiController], like <see cref="OrganizationSettingsController"/>.
/// </summary>
[Route("organization-settings/bank-details")]
public sealed class BankDetailsController(GetBankDetailsHandler getHandler, UpdateBankDetailsHandler updateHandler) : ControllerBase
{
    public const int MaxRequestBodyBytes = 8 * 1024;

    [HttpGet]
    [Authorize(Policy = CompanySettingsPolicies.Manage)]
    [ProducesResponseType<BankDetailsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        return Respond(await getHandler.HandleAsync(ticket.OrganizationId, cancellationToken));
    }

    [HttpPut]
    [Authorize(Policy = CompanySettingsPolicies.Manage)]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxRequestBodyBytes)]
    [ProducesResponseType<BankDetailsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Update(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] UpdateBankDetailsRequest request,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!ModelState.IsValid)
        {
            return ModelState.Values.SelectMany(entry => entry.Errors).Any(error => error.Exception is UnsupportedContentTypeException)
                ? new UnsupportedMediaTypeResult()
                : BadRequest();
        }

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        var address = HttpContext.Connection.RemoteIpAddress;
        var result = await updateHandler.HandleAsync(
            new UpdateBankDetailsCommand(
                ticket.OrganizationId,
                ticket.UserId,
                address is { IsIPv4MappedToIPv6: true } ? address.MapToIPv4() : address,
                request.BankName,
                request.AccountNumber,
                request.RoutingNumber,
                request.UpdatedAt),
            cancellationToken);

        return Respond(result);
    }

    private IActionResult Respond(BankDetailsResult result)
    {
        switch (result)
        {
            case BankDetailsResult.Succeeded succeeded:
                var details = succeeded.Details;

                return Ok(new BankDetailsResponse(
                    details.Configured, details.BankName, details.AccountNumberMasked, details.RoutingNumber, details.UpdatedAt));
            case BankDetailsResult.Invalid invalid:
                foreach (var (key, messages) in invalid.Errors)
                {
                    foreach (var message in messages)
                    {
                        ModelState.AddModelError(key, message);
                    }
                }

                return ValidationProblem(ModelState);
            case BankDetailsResult.Stale:
                return Problem(StatusCodes.Status409Conflict, BankDetailsMessages.ChangedCode, BankDetailsMessages.ChangedTitle);
            default:
                return Problem(StatusCodes.Status503ServiceUnavailable, BankDetailsMessages.UnavailableCode, BankDetailsMessages.UnavailableTitle);
        }
    }

    // Every answer of this endpoint that is not a success carries a machine-readable code and no bank data.
    private ObjectResult Problem(int status, string code, string title)
    {
        var problem = new ProblemDetails { Status = status, Title = title };
        problem.Extensions["code"] = code;

        return StatusCode(status, problem);
    }
}
