using System.Net;
using FieldOps.Api.Authentication;
using FieldOps.Api.Authorization;
using FieldOps.Api.Contracts;
using FieldOps.Application.Features.Organizations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FieldOps.Api.Controllers;

/// <summary>
/// The session organization's logo: retrieval, upload/replace and removal
/// (FR-09, BR-07, BR-08). The endpoints carry no organization identifier.
/// </summary>
/// <remarks>
/// Not an [ApiController]: model state is checked here, per
/// <see cref="OrganizationSettingsController"/>. A body over the size limit
/// surfaces as a <see cref="BadHttpRequestException"/> mapped to 413 by
/// <c>BadHttpRequestExceptionHandler</c>; a non-multipart request gets 415
/// from the <c>Consumes</c> constraint.
/// </remarks>
[Route("organization-settings/logo")]
public sealed class OrganizationLogoController(
    GetOrganizationLogoHandler getHandler,
    UploadOrganizationLogoHandler uploadHandler,
    RemoveOrganizationLogoHandler removeHandler) : ControllerBase
{
    public const int MaxRequestBodyBytes = 3 * 1024 * 1024;

    public const string FileKey = "file";

    private const string LogoContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'; sandbox";

    [HttpGet]
    [Authorize(Policy = CompanySettingsPolicies.View)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        var logo = await getHandler.HandleAsync(ticket.OrganizationId, cancellationToken);

        if (logo is null)
        {
            return NotFound();
        }

        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.ContentSecurityPolicy = LogoContentSecurityPolicy;
        Response.Headers.ContentDisposition = "inline";

        return File(logo.Content, logo.ContentType);
    }

    [HttpPut]
    [Authorize(Policy = CompanySettingsPolicies.Manage)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxRequestBodyBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBodyBytes)]
    [ProducesResponseType<OrganizationLogoResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status415UnsupportedMediaType)]
    public async Task<IActionResult> Upload(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        // The form is read here, not by model binding, so an oversized body
        // (BadHttpRequestException, 413) reaches BadHttpRequestExceptionHandler
        // while a malformed multipart body is a plain 400.
        IFormFile? file;

        try
        {
            var form = await Request.ReadFormAsync(cancellationToken);
            file = form.Files.GetFile(FileKey);
        }
        catch (InvalidDataException)
        {
            return BadRequest();
        }

        if (file is null)
        {
            return FileProblem(OrganizationLogoContentValidator.TypeMessage);
        }

        // Copy at most limit + 1 bytes so an oversized part is detected
        // without buffering more than the validator needs.
        var buffer = new MemoryStream();
        await using (var source = file.OpenReadStream())
        {
            await CopyAtMostAsync(source, buffer, OrganizationLogoContentValidator.MaxBytes + 1L, cancellationToken);
        }

        var result = await uploadHandler.HandleAsync(
            ticket.OrganizationId,
            ticket.UserId,
            GetClientIpAddress(),
            buffer.ToArray(),
            file.ContentType,
            cancellationToken);

        return result switch
        {
            UploadOrganizationLogoResult.Succeeded succeeded => Ok(
                new OrganizationLogoResponse(
                    succeeded.Logo.ContentType, succeeded.Logo.SizeBytes, succeeded.Logo.UpdatedAt)),
            UploadOrganizationLogoResult.Invalid invalid => FileProblem(invalid.Message),
            _ => throw new InvalidOperationException("Unknown upload organization logo result."),
        };
    }

    [HttpDelete]
    [Authorize(Policy = CompanySettingsPolicies.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Remove(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        await removeHandler.HandleAsync(
            ticket.OrganizationId, ticket.UserId, GetClientIpAddress(), cancellationToken);

        return NoContent();
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

    private IActionResult FileProblem(string message)
    {
        ModelState.AddModelError(FileKey, message);

        return ValidationProblem(ModelState);
    }

    private IPAddress? GetClientIpAddress()
    {
        var address = HttpContext.Connection.RemoteIpAddress;

        return address is { IsIPv4MappedToIPv6: true } ? address.MapToIPv4() : address;
    }
}
