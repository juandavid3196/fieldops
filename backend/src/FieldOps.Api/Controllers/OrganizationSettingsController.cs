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
/// Session-organization settings: profile, taxes, currency and document
/// numbering (FR-03, FR-04).
/// </summary>
/// <remarks>
/// Not an [ApiController]: model state is checked here so malformed JSON, a
/// JSON type mismatch, an empty body or a JSON <c>null</c> body return an
/// empty 400 (rendered as ProblemDetails without field keys by
/// UseStatusCodePages), and a missing or non-JSON Content-Type returns 415 —
/// same pattern as <see cref="OrganizationRegistrationsController"/>.
/// </remarks>
[Route("organization-settings")]
public sealed class OrganizationSettingsController(
    GetOrganizationSettingsHandler getHandler,
    UpdateOrganizationSettingsHandler updateHandler) : ControllerBase
{
    public const int MaxRequestBodyBytes = 32 * 1024;

    [HttpGet]
    [Authorize(Policy = CompanySettingsPolicies.View)]
    [ProducesResponseType<OrganizationSettingsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!TryReadSession(out var ticket, out var canManage))
        {
            return Unauthorized();
        }

        var settings = await getHandler.HandleAsync(ticket.OrganizationId, cancellationToken);

        if (settings is null)
        {
            return NotFound();
        }

        return Ok(ToResponse(settings, canManage));
    }

    [HttpPut]
    [Authorize(Policy = CompanySettingsPolicies.Manage)]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxRequestBodyBytes)]
    [ProducesResponseType<OrganizationSettingsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] UpdateOrganizationSettingsRequest request,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!ModelState.IsValid)
        {
            return HasUnsupportedContentType() ? new UnsupportedMediaTypeResult() : BadRequest();
        }

        if (!TryReadSession(out var ticket, out _))
        {
            return Unauthorized();
        }

        var command = new UpdateOrganizationSettingsCommand(
            ticket.OrganizationId,
            request.Name,
            request.LegalName,
            request.TaxId,
            request.Email,
            request.Phone,
            request.Timezone,
            request.Currency,
            request.DefaultTaxRate,
            request.QuotePrefix,
            request.WorkOrderPrefix,
            request.InvoicePrefix,
            request.NextInvoiceNumber,
            request.UpdatedAt,
            ticket.UserId,
            GetClientIpAddress());

        var result = await updateHandler.HandleAsync(command, cancellationToken);

        switch (result)
        {
            case UpdateOrganizationSettingsResult.Succeeded succeeded:
                // Only an owner reaches the Manage policy, so canManage is
                // always true here.
                return Ok(ToResponse(succeeded.Settings, canManage: true));

            case UpdateOrganizationSettingsResult.Invalid invalid:
                AddErrors(invalid.Errors);
                return ValidationProblem(ModelState);

            case UpdateOrganizationSettingsResult.Stale:
                return StaleProblem();

            default:
                throw new InvalidOperationException("Unknown update organization settings result.");
        }
    }

    private static OrganizationSettingsResponse ToResponse(OrganizationSettingsView settings, bool canManage) =>
        new(
            settings.Name,
            settings.LegalName,
            settings.TaxId,
            settings.Email,
            settings.Phone,
            settings.Timezone,
            settings.Currency,
            settings.DefaultTaxRate,
            settings.QuotePrefix,
            settings.WorkOrderPrefix,
            settings.InvoicePrefix,
            settings.NextInvoiceNumber,
            settings.UpdatedAt,
            canManage);

    private bool TryReadSession(out SessionTicket ticket, out bool canManage)
    {
        canManage = false;

        if (!SessionClaims.TryRead(User, out ticket))
        {
            return false;
        }

        var session = SessionCookieEvents.GetValidatedSession(HttpContext);

        if (session is null)
        {
            return false;
        }

        canManage = session.Role.Code == "owner";
        return true;
    }

    private IActionResult StaleProblem() =>
        Conflict(new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = "This record was changed by someone else.",
        });

    private void AddErrors(IReadOnlyDictionary<string, string[]> errors)
    {
        foreach (var (key, messages) in errors)
        {
            foreach (var message in messages)
            {
                ModelState.AddModelError(key, message);
            }
        }
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
