using System.Text.Json;
using FieldOps.Api.Contracts;
using FieldOps.Api.Extensions;
using FieldOps.Application.Features.PortalAccess;
using FieldOps.Application.Features.PortalDashboard;
using FieldOps.Application.Features.PortalRequests;
using FieldOps.Application.Features.PublicRequests;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FieldOps.Api.Controllers.Portal;

/// <summary>
/// The requests module (customer portal BR-27, BR-28): list and detail of the customer's requests, the form configuration of
/// the session organization and the new request (the public multipart contract without the contact step).
/// </summary>
[Route("portal")]
public sealed class PortalRequestsController(
    ListPortalCatalogHandler catalogHandler,
    GetPortalServiceRequestFormHandler formHandler,
    SubmitPortalServiceRequestHandler submitHandler) : PortalControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [HttpGet("requests")]
    [ProducesResponseType<PortalPage<PortalRequestRow>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(
        [FromQuery] string? propertyId, [FromQuery] string? page, CancellationToken cancellationToken) =>
        Map(await catalogHandler.RequestsAsync(Scope, propertyId, page, cancellationToken), result => Ok(result));

    [HttpGet("requests/{requestId:guid}")]
    [ProducesResponseType<PortalRequestDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid requestId, CancellationToken cancellationToken) =>
        Map(await catalogHandler.RequestAsync(Scope, requestId, cancellationToken), detail => Ok(detail));

    [HttpGet("service-request-form")]
    [ProducesResponseType<PublicServiceRequestFormResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetForm(CancellationToken cancellationToken)
    {
        var form = await formHandler.HandleAsync(Scope, cancellationToken);

        if (form is null)
        {
            return CodedProblem(
                StatusCodes.Status404NotFound, "Online requests aren't available right now.", PortalMessages.RequestsUnavailableCode);
        }

        return Ok(new PublicServiceRequestFormResponse(
            form.OrganizationName,
            form.Phone,
            form.Website,
            form.RequestPrefix,
            form.Timezone,
            form.Categories
                .Select(category => new PublicFormCategoryResponse(
                    category.Id,
                    category.Name,
                    category.Services.Select(service => new PublicFormServiceResponse(service.Id, service.Name)).ToList()))
                .ToList()));
    }

    [HttpPost("service-requests")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(PublicServiceRequestsController.MaxRequestBodyBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = PublicServiceRequestsController.MaxRequestBodyBytes)]
    [EnableRateLimiting(ApiRateLimitingExtensions.PortalServiceRequestPolicy)]
    [ProducesResponseType<PortalServiceRequestCreatedResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Submit(CancellationToken cancellationToken)
    {
        PortalServiceRequestBody? body;
        List<PublicAttachmentInput> attachments;

        try
        {
            var form = await Request.ReadFormAsync(cancellationToken);

            body = await PublicServiceRequestsController.ReadBodyAsync<PortalServiceRequestBody>(form, cancellationToken);
            attachments = await PublicServiceRequestsController.ReadAttachmentsAsync(form, cancellationToken);
        }
        catch (InvalidDataException)
        {
            return BadRequest();
        }
        catch (JsonException)
        {
            return BadRequest();
        }

        if (body is null)
        {
            return BadRequest();
        }

        var property = body.Property ?? new PortalServiceRequestBody.PortalPropertyBody();
        var service = body.Service ?? new PublicServiceRequestBody.ServiceBody();
        var availability = body.Availability ?? new PublicServiceRequestBody.AvailabilityBody();
        var newProperty = property.NewProperty;

        var command = new SubmitPortalServiceRequestCommand(
            new PortalPropertyChoice(
                property.PropertyId,
                newProperty is null
                    ? null
                    : new PublicPropertyInput(
                        newProperty.PropertyType,
                        newProperty.AddressLine1,
                        newProperty.AddressLine2,
                        newProperty.City,
                        newProperty.State,
                        newProperty.PostalCode,
                        newProperty.AccessInstructions)),
            new PublicServiceInput(
                service.CategoryId, service.ServiceId, service.NotSure, service.Description, service.Urgency, service.HasActiveDamage),
            new PublicAvailabilityInput(
                availability.DateMode, availability.PreferredDate, availability.TimeWindow, availability.SchedulingNotes),
            body.Consent,
            body.Website,
            attachments);

        var outcome = await submitHandler.HandleAsync(Scope, command, cancellationToken);

        // Not found: the organization no longer accepts requests (the same code as the form).
        return outcome is PortalOutcome<PortalRequestCreated>.NotFound
            ? CodedProblem(
                StatusCodes.Status404NotFound, "Online requests aren't available right now.", PortalMessages.RequestsUnavailableCode)
            : Map(outcome, created => StatusCode(StatusCodes.Status201Created, new PortalServiceRequestCreatedResponse(created.RequestId, created.RequestNumber)));
    }
}
