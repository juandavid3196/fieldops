using System.Text;
using System.Text.Json;
using FieldOps.Api.Contracts;
using FieldOps.Api.Extensions;
using FieldOps.Application.Features.PublicRequests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FieldOps.Api.Controllers;

/// <summary>
/// Anonymous public service request form: configuration and submission for an
/// organization identified only by its public slug (BR-01, BR-02). No session
/// is read; every response is <c>no-store</c>.
/// </summary>
/// <remarks>
/// Not an [ApiController], like the other anonymous controllers: the multipart
/// form is read here so a malformed body is a keyless 400 while an oversized
/// body surfaces as <see cref="BadHttpRequestException"/> (413 through
/// <c>BadHttpRequestExceptionHandler</c>). All BR-01 failures return the same 404.
/// </remarks>
[Route("public/organizations/{slug}")]
public sealed class PublicServiceRequestsController(
    GetPublicServiceRequestFormHandler getFormHandler,
    SubmitPublicServiceRequestHandler submitHandler) : ControllerBase
{
    public const int MaxRequestBodyBytes = 27_262_976;

    public const string RequestPartName = "request";

    public const string AttachmentsPartName = "attachments";

    private const int MaxRequestJsonBytes = 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [HttpGet("service-request-form")]
    [AllowAnonymous]
    [EnableRateLimiting(ApiRateLimitingExtensions.PublicRequestFormPolicy)]
    [ProducesResponseType<PublicServiceRequestFormResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> GetForm(string slug, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        var form = await getFormHandler.HandleAsync(slug, cancellationToken);

        if (form is null)
        {
            return NotFound();
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
                    category.Services
                        .Select(service => new PublicFormServiceResponse(service.Id, service.Name))
                        .ToList()))
                .ToList()));
    }

    [HttpPost("service-requests")]
    [AllowAnonymous]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxRequestBodyBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBodyBytes)]
    [EnableRateLimiting(ApiRateLimitingExtensions.PublicRequestSubmitPolicy)]
    [ProducesResponseType<PublicServiceRequestCreatedResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Submit(string slug, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        PublicServiceRequestBody? body;
        List<PublicAttachmentInput> attachments;

        try
        {
            var form = await Request.ReadFormAsync(cancellationToken);

            body = await ReadBodyAsync(form, cancellationToken);
            attachments = await ReadAttachmentsAsync(form, cancellationToken);
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

        var result = await submitHandler.HandleAsync(ToCommand(slug, body, attachments), cancellationToken);

        switch (result)
        {
            case SubmitPublicServiceRequestResult.Created created:
                // Not CreatedAtAction: the contract carries no Location.
                return StatusCode(
                    StatusCodes.Status201Created,
                    new PublicServiceRequestCreatedResponse(created.RequestNumber));

            case SubmitPublicServiceRequestResult.Invalid invalid:
                foreach (var (key, messages) in invalid.Errors)
                {
                    foreach (var message in messages)
                    {
                        ModelState.AddModelError(key, message);
                    }
                }

                return ValidationProblem(ModelState);

            case SubmitPublicServiceRequestResult.Honeypot:
                return BadRequest();

            case SubmitPublicServiceRequestResult.NotFound:
                return NotFound();

            default:
                throw new InvalidOperationException("Unknown public service request result.");
        }
    }

    // The request part is a form value, or a file part when the client sent it as a Blob.
    private static async Task<PublicServiceRequestBody?> ReadBodyAsync(
        IFormCollection form, CancellationToken cancellationToken)
    {
        string? json = form[RequestPartName].FirstOrDefault();

        if (json is null && form.Files.GetFile(RequestPartName) is { } part)
        {
            if (part.Length > MaxRequestJsonBytes)
            {
                throw new InvalidDataException("The request part is too large.");
            }

            using var reader = new StreamReader(part.OpenReadStream(), Encoding.UTF8);
            json = await reader.ReadToEndAsync(cancellationToken);
        }

        return json is null
            ? null
            : JsonSerializer.Deserialize<PublicServiceRequestBody>(json, JsonOptions);
    }

    private static async Task<List<PublicAttachmentInput>> ReadAttachmentsAsync(
        IFormCollection form, CancellationToken cancellationToken)
    {
        var files = form.Files.GetFiles(AttachmentsPartName);
        var attachments = new List<PublicAttachmentInput>(files.Count);

        for (var index = 0; index < files.Count; index++)
        {
            // Beyond the maximum the count error is reported without reading content.
            if (index >= AttachmentContentInspector.MaxFiles)
            {
                attachments.Add(new PublicAttachmentInput(files[index].FileName, []));
                continue;
            }

            // Copy at most limit + 1 bytes so an oversized part is detected
            // without buffering more than the inspector needs.
            var buffer = new MemoryStream();
            await using (var source = files[index].OpenReadStream())
            {
                await CopyAtMostAsync(source, buffer, AttachmentContentInspector.MaxFileBytes + 1L, cancellationToken);
            }

            attachments.Add(new PublicAttachmentInput(files[index].FileName, buffer.ToArray()));
        }

        return attachments;
    }

    private static async Task CopyAtMostAsync(
        Stream source, Stream destination, long limit, CancellationToken cancellationToken)
    {
        var chunk = new byte[81920];
        long total = 0;

        while (total < limit)
        {
            var toRead = (int)Math.Min(chunk.Length, limit - total);
            var read = await source.ReadAsync(chunk.AsMemory(0, toRead), cancellationToken);

            if (read == 0)
            {
                return;
            }

            await destination.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
            total += read;
        }
    }

    private static SubmitPublicServiceRequestCommand ToCommand(
        string slug, PublicServiceRequestBody body, List<PublicAttachmentInput> attachments)
    {
        var contact = body.Contact ?? new PublicServiceRequestBody.ContactBody();
        var property = body.Property ?? new PublicServiceRequestBody.PropertyBody();
        var service = body.Service ?? new PublicServiceRequestBody.ServiceBody();
        var availability = body.Availability ?? new PublicServiceRequestBody.AvailabilityBody();

        return new SubmitPublicServiceRequestCommand(
            slug,
            new PublicContactInput(
                contact.FirstName,
                contact.LastName,
                contact.Email,
                contact.Phone,
                contact.PrefersEmail,
                contact.PrefersSms),
            new PublicPropertyInput(
                property.PropertyType,
                property.AddressLine1,
                property.AddressLine2,
                property.City,
                property.State,
                property.PostalCode,
                property.AccessInstructions),
            new PublicServiceInput(
                service.CategoryId,
                service.ServiceId,
                service.NotSure,
                service.Description,
                service.Urgency,
                service.HasActiveDamage),
            new PublicAvailabilityInput(
                availability.DateMode,
                availability.PreferredDate,
                availability.TimeWindow,
                availability.SchedulingNotes),
            body.Consent,
            body.Website,
            attachments);
    }
}
