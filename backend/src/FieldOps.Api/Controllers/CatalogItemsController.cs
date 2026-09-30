using System.Net;
using FieldOps.Api.Authentication;
using FieldOps.Api.Authorization;
using FieldOps.Api.Contracts;
using FieldOps.Application.Features.Catalog;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace FieldOps.Api.Controllers;

/// <summary>
/// Products and services catalog of the session organization (FR-02 to FR-16). No endpoint
/// accepts an organization identifier; every id is resolved against the session organization.
/// </summary>
/// <remarks>
/// Not an [ApiController]: model state is checked here, per <see cref="UsersController"/>. The
/// static routes never collide with the <c>{id:guid}</c> routes. A body over a size limit
/// surfaces as a <see cref="BadHttpRequestException"/> mapped to 413 by
/// <c>BadHttpRequestExceptionHandler</c>; a non-multipart upload gets 415 from <c>Consumes</c>.
/// </remarks>
[Route("catalog-items")]
public sealed class CatalogItemsController(
    ListCatalogItemsHandler listHandler,
    GetCatalogSummaryHandler summaryHandler,
    GetCatalogItemHandler detailHandler,
    CreateCatalogItemHandler createHandler,
    UpdateCatalogItemHandler updateHandler,
    SetCatalogItemActiveHandler activeHandler,
    GetCatalogImageHandler getImageHandler,
    UploadCatalogImageHandler uploadImageHandler,
    RemoveCatalogImageHandler removeImageHandler,
    ExportCatalogItemsHandler exportHandler,
    ImportCatalogItemsHandler importHandler) : ControllerBase
{
    public const int MaxJsonBodyBytes = 64 * 1024;

    public const int MaxImageRequestBytes = 6 * 1024 * 1024;

    public const int MaxImportRequestBytes = 2 * 1024 * 1024;

    public const string FileKey = "file";

    private const string ImageContentSecurityPolicy = "default-src 'none'; sandbox";

    [HttpGet]
    [Authorize(Policy = CatalogPolicies.View)]
    [ProducesResponseType<CatalogListResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List([FromQuery] CatalogListRequest request, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        var result = await listHandler.HandleAsync(ticket.OrganizationId, request.ToQuery(), cancellationToken);

        if (result.Kind == CatalogResultKind.Invalid)
        {
            return FieldErrors(result.Errors!, StatusCodes.Status400BadRequest);
        }

        var page = result.Value!;

        return Ok(new CatalogListResponse(page.Items, page.Page, page.PageSize, page.TotalCount));
    }

    [HttpGet("summary")]
    [Authorize(Policy = CatalogPolicies.View)]
    [ProducesResponseType<CatalogSummary>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Summary(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        return Ok(await summaryHandler.HandleAsync(ticket.OrganizationId, cancellationToken));
    }

    [HttpGet("export")]
    [Authorize(Policy = CatalogPolicies.View)]
    [Produces("text/csv")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Export([FromQuery] CatalogListRequest request, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        var result = await exportHandler.HandleAsync(ticket.OrganizationId, request.ToQuery(), cancellationToken);

        if (result.Kind == CatalogResultKind.Invalid)
        {
            return FieldErrors(result.Errors!, StatusCodes.Status400BadRequest);
        }

        var file = result.Value!;

        return File(file.Content, file.ContentType, file.FileName);
    }

    [HttpGet("import-template")]
    [Authorize(Policy = CatalogPolicies.Import)]
    [Produces("text/csv")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public IActionResult ImportTemplate()
    {
        Response.Headers.CacheControl = "no-store";

        return File(CatalogCsv.Template(), "text/csv; charset=utf-8", CatalogCsv.TemplateFileName);
    }

    [HttpPost("import")]
    [Authorize(Policy = CatalogPolicies.Import)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxImportRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxImportRequestBytes)]
    [ProducesResponseType<CatalogImportResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status415UnsupportedMediaType)]
    public async Task<IActionResult> Import(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        var (file, malformed) = await ReadFilePartAsync(cancellationToken);

        if (malformed)
        {
            return BadRequest();
        }

        if (file is null)
        {
            return FileProblem(CatalogCsv.NotCsvMessage);
        }

        var content = await ReadAtMostAsync(file, CatalogCsv.MaxImportFileBytes + 1L, cancellationToken);

        var result = await importHandler.HandleAsync(
            ticket.OrganizationId, ticket.UserId, GetClientIpAddress(), content, cancellationToken);

        switch (result.Kind)
        {
            case CatalogResultKind.Succeeded:
                return Ok(new CatalogImportResponse(result.Value));

            case CatalogResultKind.Invalid when result.RowErrors is { } rowErrors:
                var problem = new ProblemDetails
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "The file has errors.",
                };
                problem.Extensions["rowErrors"] = rowErrors;

                return new ObjectResult(problem) { StatusCode = StatusCodes.Status400BadRequest };

            case CatalogResultKind.Invalid:
                return FieldErrors(result.Errors!, StatusCodes.Status400BadRequest);

            case CatalogResultKind.Conflict:
                return Conflict(new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = result.Message,
                });

            default:
                throw new InvalidOperationException("Unknown catalog import result.");
        }
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = CatalogPolicies.View)]
    [ProducesResponseType<CatalogItemDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDetail(Guid id, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        var detail = await detailHandler.HandleAsync(ticket.OrganizationId, id, cancellationToken);

        return detail is null ? NotFound() : Ok(detail);
    }

    [HttpPost]
    [Authorize(Policy = CatalogPolicies.Manage)]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxJsonBodyBytes)]
    [ProducesResponseType<CatalogItemDetail>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] CatalogItemRequest request,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!ModelState.IsValid)
        {
            return HasUnsupportedContentType() ? new UnsupportedMediaTypeResult() : BadRequest();
        }

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        var input = request.ToInput(out var typeErrors);

        if (input is null)
        {
            return FieldErrors(typeErrors, StatusCodes.Status400BadRequest);
        }

        var result = await createHandler.HandleAsync(
            ticket.OrganizationId, ticket.UserId, GetClientIpAddress(), input, cancellationToken);

        return result.Kind == CatalogResultKind.Succeeded
            ? Created($"/catalog-items/{result.Value!.Id}", result.Value)
            : MapFailure(result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = CatalogPolicies.Manage)]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxJsonBodyBytes)]
    [ProducesResponseType<CatalogItemDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] CatalogItemRequest request,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!ModelState.IsValid)
        {
            return HasUnsupportedContentType() ? new UnsupportedMediaTypeResult() : BadRequest();
        }

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        var input = request.ToInput(out var typeErrors);

        if (input is null)
        {
            return FieldErrors(typeErrors, StatusCodes.Status400BadRequest);
        }

        var result = await updateHandler.HandleAsync(
            ticket.OrganizationId, id, ticket.UserId, GetClientIpAddress(), input, cancellationToken);

        return result.Kind == CatalogResultKind.Succeeded ? Ok(result.Value) : MapFailure(result);
    }

    [HttpPost("{id:guid}/activate")]
    [Authorize(Policy = CatalogPolicies.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<IActionResult> Activate(Guid id, CancellationToken cancellationToken) =>
        SetActiveAsync(id, true, cancellationToken);

    [HttpPost("{id:guid}/deactivate")]
    [Authorize(Policy = CatalogPolicies.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken) =>
        SetActiveAsync(id, false, cancellationToken);

    [HttpGet("{id:guid}/image")]
    [Authorize(Policy = CatalogPolicies.View)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetImage(Guid id, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        var image = await getImageHandler.HandleAsync(ticket.OrganizationId, id, cancellationToken);

        if (image is null)
        {
            return NotFound();
        }

        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.ContentSecurityPolicy = ImageContentSecurityPolicy;
        Response.Headers.ContentDisposition = "inline";

        return File(image.Content, image.ContentType);
    }

    [HttpPut("{id:guid}/image")]
    [Authorize(Policy = CatalogPolicies.Manage)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxImageRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxImageRequestBytes)]
    [ProducesResponseType<CatalogImageResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status415UnsupportedMediaType)]
    public async Task<IActionResult> UploadImage(Guid id, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        var (file, malformed) = await ReadFilePartAsync(cancellationToken);

        if (malformed)
        {
            return BadRequest();
        }

        if (file is null)
        {
            return FileProblem(CatalogImageContentValidator.TypeMessage);
        }

        var content = await ReadAtMostAsync(file, CatalogImageContentValidator.MaxBytes + 1L, cancellationToken);

        var result = await uploadImageHandler.HandleAsync(
            ticket.OrganizationId,
            id,
            ticket.UserId,
            GetClientIpAddress(),
            content,
            file.ContentType,
            cancellationToken);

        return result.Kind == CatalogResultKind.Succeeded
            ? Ok(new CatalogImageResponse(result.Value!.ContentType, result.Value.SizeBytes, result.Value.UpdatedAt))
            : MapFailure(result);
    }

    [HttpDelete("{id:guid}/image")]
    [Authorize(Policy = CatalogPolicies.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveImage(Guid id, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        var result = await removeImageHandler.HandleAsync(
            ticket.OrganizationId, id, ticket.UserId, GetClientIpAddress(), cancellationToken);

        return result.Kind == CatalogResultKind.NotFound ? NotFound() : NoContent();
    }

    private async Task<IActionResult> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        var result = await activeHandler.HandleAsync(
            ticket.OrganizationId, id, isActive, ticket.UserId, GetClientIpAddress(), cancellationToken);

        return result.Kind == CatalogResultKind.NotFound ? NotFound() : NoContent();
    }

    // The form is read here, not by model binding, so an oversized body (BadHttpRequestException, 413)
    // reaches BadHttpRequestExceptionHandler while a malformed multipart body is a plain 400.
    private async Task<(IFormFile? File, bool Malformed)> ReadFilePartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var form = await Request.ReadFormAsync(cancellationToken);

            return (form.Files.GetFile(FileKey), false);
        }
        catch (InvalidDataException)
        {
            return (null, true);
        }
    }

    // Copies at most limit bytes so an oversized part is detected without buffering more than needed.
    private static async Task<byte[]> ReadAtMostAsync(IFormFile file, long limit, CancellationToken cancellationToken)
    {
        var buffer = new MemoryStream();
        var chunk = new byte[81920];
        long total = 0;

        await using var source = file.OpenReadStream();

        while (total < limit)
        {
            var toRead = (int)Math.Min(chunk.Length, limit - total);
            var read = await source.ReadAsync(chunk.AsMemory(0, toRead), cancellationToken);

            if (read == 0)
            {
                break;
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
            total += read;
        }

        return buffer.ToArray();
    }

    private IActionResult MapFailure<T>(CatalogResult<T> result)
    {
        switch (result.Kind)
        {
            case CatalogResultKind.Invalid:
                return FieldErrors(result.Errors!, StatusCodes.Status400BadRequest);

            case CatalogResultKind.NotFound:
                return NotFound();

            case CatalogResultKind.Conflict:
                return FieldErrors(result.Errors!, StatusCodes.Status409Conflict);

            default:
                throw new InvalidOperationException("Unknown catalog result.");
        }
    }

    private IActionResult FileProblem(string message) =>
        FieldErrors(
            new Dictionary<string, string[]>(StringComparer.Ordinal) { [FileKey] = [message] },
            StatusCodes.Status400BadRequest);

    private static ObjectResult FieldErrors(IReadOnlyDictionary<string, string[]> errors, int statusCode) =>
        new(new ValidationProblemDetails(new Dictionary<string, string[]>(errors, StringComparer.Ordinal))
        {
            Status = statusCode,
            Title = "One or more validation errors occurred.",
        })
        {
            StatusCode = statusCode,
        };

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
