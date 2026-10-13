using System.Text.Json;
using FieldOps.Api.Contracts;
using FieldOps.Api.Extensions;
using FieldOps.Application.Features.OnlinePayments;
using FieldOps.Application.Features.PortalAccess;
using FieldOps.Application.Features.PortalDashboard;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;

namespace FieldOps.Api.Controllers.Portal;

/// <summary>
/// The properties, the messages, the organization logo and the work order report of the portal (customer portal BR-31, BR-33,
/// BR-35). A property edit accepts only the name, the access instructions, the primary flag and the updated-at token.
/// </summary>
public sealed class PortalAccountController(
    PortalPropertiesHandler propertiesHandler,
    SendPortalMessageHandler messageHandler,
    GetPortalWorkOrderReportHandler reportHandler,
    IPortalCatalogStore catalogStore) : PortalControllerBase
{
    public const int MaxRequestBodyBytes = 8 * 1024;

    private static readonly HashSet<string> EditableKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "name",
        "accessInstructions",
        "isPrimary",
        "updatedAt",
    };

    private static readonly HashSet<string> AddressKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "addressLine1",
        "addressLine2",
        "city",
        "state",
        "stateRegion",
        "postalCode",
        "countryCode",
    };

    [HttpGet("portal/properties")]
    [ProducesResponseType<IReadOnlyList<PortalProperty>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ListProperties(CancellationToken cancellationToken) =>
        Ok(await propertiesHandler.ListAsync(Scope, cancellationToken));

    [HttpPost("portal/properties")]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxRequestBodyBytes)]
    [EnableRateLimiting(ApiRateLimitingExtensions.PortalPropertyCreatePolicy)]
    [ProducesResponseType<PortalProperty>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> CreateProperty(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] PortalPropertyCreateRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return InvalidBody();
        }

        var input = new PortalPropertyCreateInput(
            request.Name,
            request.AddressLine1,
            request.AddressLine2,
            request.City,
            request.StateRegion,
            request.PostalCode,
            request.CountryCode,
            request.AccessInstructions);

        return Map(await propertiesHandler.CreateAsync(Scope, input, ClientIp(), cancellationToken), property => Ok(property));
    }

    [HttpPatch("portal/properties/{propertyId:guid}")]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxRequestBodyBytes)]
    [ProducesResponseType<PortalProperty>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateProperty(
        Guid propertyId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] JsonElement body,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || body.ValueKind != JsonValueKind.Object)
        {
            return InvalidBody();
        }

        // Any other field, the address included, is a 400 (customer portal BR-33).
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        foreach (var member in body.EnumerateObject())
        {
            if (!EditableKeys.Contains(member.Name))
            {
                errors[member.Name] = [AddressKeys.Contains(member.Name)
                    ? PortalPropertyMessages.AddressChangeMessage
                    : "This field can't be changed online."];
            }
        }

        if (errors.Count > 0)
        {
            return FieldErrors(errors);
        }

        var hasName = TryGet(body, "name", out var name);
        var hasInstructions = TryGet(body, "accessInstructions", out var instructions);
        bool? isPrimary = body.TryGetProperty("isPrimary", out var primary)
            ? primary.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null,
            }
            : null;
        TryGet(body, "updatedAt", out var updatedAt);

        var input = new PortalPropertyEditInput(hasName, name, hasInstructions, instructions, isPrimary, updatedAt);

        return Map(await propertiesHandler.UpdateAsync(Scope, propertyId, input, ClientIp(), cancellationToken), property => Ok(property));
    }

    [HttpPost("portal/messages")]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxRequestBodyBytes)]
    [EnableRateLimiting(ApiRateLimitingExtensions.PortalMessagePolicy)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> SendMessage(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] PortalMessageRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return InvalidBody();
        }

        return Map(await messageHandler.HandleAsync(Scope, request.Message, ClientIp(), cancellationToken), _ => Accepted());
    }

    [HttpGet("portal/work-orders/{id:guid}/completion-report")]
    [EnableRateLimiting(ApiRateLimitingExtensions.InvoiceLinkPdfPolicy)]
    [ProducesResponseType(typeof(byte[]), StatusCodes.Status200OK, "application/pdf")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> WorkOrderReport(Guid id, CancellationToken cancellationToken) =>
        await reportHandler.HandleAsync(Scope, id, cancellationToken) is { } document
            ? Pdf(document.FileName, document.ContentType, document.Content)
            : Unavailable();

    [HttpGet("portal/organization/logo")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Logo(CancellationToken cancellationToken) =>
        await catalogStore.GetOrganizationLogoAsync(Scope, cancellationToken) is { } logo ? Image(logo) : Unavailable();

    // A string member, or an explicit null; a value of another type reads as absent text so the rules report the field.
    private static bool TryGet(JsonElement body, string name, out string? value)
    {
        value = null;

        if (!body.TryGetProperty(name, out var element))
        {
            return false;
        }

        value = element.ValueKind == JsonValueKind.String ? element.GetString() : null;

        return true;
    }
}
