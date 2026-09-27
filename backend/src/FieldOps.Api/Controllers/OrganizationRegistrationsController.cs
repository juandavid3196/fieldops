using System.Net;
using FieldOps.Api.Contracts;
using FieldOps.Api.Extensions;
using FieldOps.Application.Features.Organizations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;

namespace FieldOps.Api.Controllers;

/// <summary>
/// Public, pre-tenant organization registration: creates the organization,
/// its first branch and the initial Owner account in one transaction. No
/// session is created (anonymous by design).
/// </summary>
/// <remarks>
/// Not an [ApiController]: model state is checked here so malformed JSON, a
/// JSON type mismatch, an empty body or a JSON <c>null</c> body return an
/// empty 400 (rendered as ProblemDetails without field keys by
/// UseStatusCodePages), and a missing or non-JSON Content-Type returns 415.
/// </remarks>
[Route("organization-registrations")]
public sealed class OrganizationRegistrationsController(
    RegisterOrganizationHandler handler) : ControllerBase
{
    public const int MaxRequestBodyBytes = 32 * 1024;

    public const string DuplicateEmailKey = "owner.email";

    public const string DuplicateEmailMessage =
        "An account with this email already exists. Sign in instead.";

    [HttpPost]
    [AllowAnonymous]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxRequestBodyBytes)]
    [EnableRateLimiting(ApiRateLimitingExtensions.OrganizationRegistrationPolicy)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Register(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] OrganizationRegistrationRequest request,
        CancellationToken cancellationToken)
    {
        // EmptyBodyBehavior.Disallow: an empty or JSON null body is a model
        // state error (keyless 400) and a request without Content-Type fails
        // input formatter selection (415). A body missing a top-level
        // section (e.g. "{}") binds successfully with a null section, which
        // is treated the same way rather than risking a null-reference 500.
        if (!ModelState.IsValid
            || request.Organization is null
            || request.Branch is null
            || request.Owner is null)
        {
            return HasUnsupportedContentType()
                ? new UnsupportedMediaTypeResult()
                : BadRequest();
        }

        var command = new RegisterOrganizationCommand(
            new RegisterOrganizationCommand.OrganizationInput(
                request.Organization.Name,
                request.Organization.LegalName,
                request.Organization.TaxId,
                request.Organization.Email,
                request.Organization.Phone,
                request.Organization.Timezone,
                request.Organization.Currency,
                request.Organization.DefaultTaxRate,
                request.Organization.QuotePrefix,
                request.Organization.WorkOrderPrefix,
                request.Organization.InvoicePrefix,
                request.Organization.NextInvoiceNumber),
            new RegisterOrganizationCommand.BranchInput(
                request.Branch.Name,
                request.Branch.Code,
                request.Branch.Phone,
                request.Branch.Email,
                request.Branch.Timezone,
                request.Branch.AddressLine1,
                request.Branch.City,
                request.Branch.StateRegion,
                request.Branch.PostalCode,
                request.Branch.CountryCode,
                request.Branch.BusinessHours),
            new RegisterOrganizationCommand.OwnerInput(
                request.Owner.FirstName,
                request.Owner.LastName,
                request.Owner.Email,
                request.Owner.Password,
                request.Owner.Phone),
            GetClientIpAddress());

        var result = await handler.HandleAsync(command, cancellationToken);

        switch (result)
        {
            case RegisterOrganizationResult.Succeeded succeeded:
                // Not Created(...)/CreatedAtAction: those set Location, which
                // the contract forbids. No cookie or auth header either.
                return StatusCode(
                    StatusCodes.Status201Created,
                    new { organizationId = succeeded.OrganizationId });

            case RegisterOrganizationResult.Invalid invalid:
                foreach (var (key, messages) in invalid.Errors)
                {
                    foreach (var message in messages)
                    {
                        ModelState.AddModelError(key, message);
                    }
                }

                return ValidationProblem(ModelState);

            case RegisterOrganizationResult.DuplicateEmail:
                return DuplicateEmailProblem();

            default:
                throw new InvalidOperationException("Unknown organization registration result.");
        }
    }

    private IActionResult DuplicateEmailProblem()
    {
        ModelState.AddModelError(DuplicateEmailKey, DuplicateEmailMessage);

        return ValidationProblem(
            statusCode: StatusCodes.Status409Conflict,
            modelStateDictionary: ModelState);
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
