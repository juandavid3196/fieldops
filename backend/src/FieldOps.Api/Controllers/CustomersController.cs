using System.Net;
using FieldOps.Api.Authentication;
using FieldOps.Api.Authorization;
using FieldOps.Api.Contracts;
using FieldOps.Application.Features.Customers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace FieldOps.Api.Controllers;

/// <summary>
/// Customers of the session organization (FR-02 to FR-11). No endpoint accepts an organization
/// identifier; the organization and branch scope come from the session membership.
/// </summary>
/// <remarks>
/// Not an [ApiController]: model state is checked here, per <see cref="CatalogItemsController"/>. The
/// static routes never collide with the <c>{id:guid}</c> routes. A body over a size limit surfaces as a
/// <see cref="BadHttpRequestException"/> mapped to 413; a non-multipart upload gets 415 from <c>Consumes</c>.
/// </remarks>
[Route("customers")]
public sealed class CustomersController(
    ListCustomersHandler listHandler,
    GetCustomerMetricsHandler metricsHandler,
    GetCustomerHandler detailHandler,
    GetCustomerBranchOptionsHandler branchOptionsHandler,
    CreateCustomerHandler createHandler,
    UpdateCustomerHandler updateHandler,
    SetCustomerActiveHandler activeHandler,
    CheckCustomerDuplicatesHandler duplicateHandler,
    PreviewCustomerImportHandler previewHandler,
    ImportCustomersHandler importHandler) : ControllerBase
{
    public const int MaxJsonBodyBytes = 64 * 1024;

    public const int MaxImportRequestBytes = 2 * 1024 * 1024;

    public const string FileKey = "file";

    [HttpGet]
    [Authorize(Policy = CustomerPolicies.View)]
    [ProducesResponseType<CustomerListPage>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List([FromQuery] CustomerListRequest request, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        var result = await listHandler.HandleAsync(
            ticket.OrganizationId, ticket.MembershipId, request.ToQuery(), cancellationToken);

        return result.Kind == CustomerResultKind.Succeeded ? Ok(result.Value) : MapFailure(result);
    }

    [HttpGet("branch-options")]
    [Authorize(Policy = CustomerPolicies.View)]
    [ProducesResponseType<CustomerBranchOptions>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> BranchOptions(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        return Ok(await branchOptionsHandler.HandleAsync(ticket.OrganizationId, ticket.MembershipId, cancellationToken));
    }

    [HttpGet("metrics")]
    [Authorize(Policy = CustomerPolicies.View)]
    [ProducesResponseType<CustomerMetrics>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Metrics([FromQuery] string? branchId, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        var result = await metricsHandler.HandleAsync(
            ticket.OrganizationId, ticket.MembershipId, branchId, cancellationToken);

        return result.Kind == CustomerResultKind.Succeeded ? Ok(result.Value) : MapFailure(result);
    }

    [HttpGet("import/template")]
    [Authorize(Policy = CustomerPolicies.Import)]
    [Produces("text/csv")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public IActionResult ImportTemplate()
    {
        Response.Headers.CacheControl = "no-store";

        return File(CustomerCsv.Template(), "text/csv; charset=utf-8", CustomerCsv.TemplateFileName);
    }

    [HttpPost("import/preview")]
    [Authorize(Policy = CustomerPolicies.Import)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxImportRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxImportRequestBytes)]
    [ProducesResponseType<CustomerImportPreview>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status415UnsupportedMediaType)]
    public async Task<IActionResult> PreviewImport(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        var (content, failure) = await ReadCsvAsync(cancellationToken);

        if (failure is not null)
        {
            return failure;
        }

        var result = await previewHandler.HandleAsync(
            ticket.OrganizationId, ticket.MembershipId, content!, cancellationToken);

        return result.Kind == CustomerResultKind.Succeeded ? Ok(result.Value) : MapFailure(result);
    }

    [HttpPost("import")]
    [Authorize(Policy = CustomerPolicies.Import)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxImportRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxImportRequestBytes)]
    [ProducesResponseType<CustomerImportResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status415UnsupportedMediaType)]
    public async Task<IActionResult> Import(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        var (content, failure) = await ReadCsvAsync(cancellationToken);

        if (failure is not null)
        {
            return failure;
        }

        var result = await importHandler.HandleAsync(
            ticket.OrganizationId, ticket.MembershipId, ticket.UserId, GetClientIpAddress(), content!, cancellationToken);

        return result.Kind == CustomerResultKind.Succeeded
            ? Ok(new CustomerImportResponse(result.Value))
            : MapFailure(result);
    }

    [HttpPost("duplicate-check")]
    [Authorize(Policy = CustomerPolicies.Manage)]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxJsonBodyBytes)]
    [ProducesResponseType<CustomerDuplicateCheck>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> DuplicateCheck(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] DuplicateCheckRequest request,
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

        return Ok(await duplicateHandler.HandleAsync(
            ticket.OrganizationId,
            ticket.MembershipId,
            request.Email,
            request.Phone,
            request.ExcludeCustomerId,
            cancellationToken));
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = CustomerPolicies.View)]
    [ProducesResponseType<CustomerDetail>(StatusCodes.Status200OK)]
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

        var detail = await detailHandler.HandleAsync(ticket.OrganizationId, ticket.MembershipId, id, cancellationToken);

        return detail is null ? NotFound() : Ok(detail);
    }

    [HttpPost]
    [Authorize(Policy = CustomerPolicies.Manage)]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxJsonBodyBytes)]
    [ProducesResponseType<CustomerCreatedResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Create(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] CustomerRequest request,
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

        var result = await createHandler.HandleAsync(
            ticket.OrganizationId,
            ticket.MembershipId,
            ticket.UserId,
            GetClientIpAddress(),
            request.ToInput(),
            cancellationToken);

        return result.Kind == CustomerResultKind.Succeeded
            ? Created($"/customers/{result.Value}", new CustomerCreatedResponse(result.Value))
            : MapFailure(result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = CustomerPolicies.Manage)]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxJsonBodyBytes)]
    [ProducesResponseType<CustomerDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] CustomerRequest request,
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

        var result = await updateHandler.HandleAsync(
            ticket.OrganizationId,
            ticket.MembershipId,
            id,
            ticket.UserId,
            GetClientIpAddress(),
            request.ToInput(),
            cancellationToken);

        return result.Kind == CustomerResultKind.Succeeded ? Ok(result.Value) : MapFailure(result);
    }

    [HttpPost("{id:guid}/archive")]
    [Authorize(Policy = CustomerPolicies.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<IActionResult> Archive(Guid id, CancellationToken cancellationToken) =>
        SetActiveAsync(id, false, cancellationToken);

    [HttpPost("{id:guid}/reactivate")]
    [Authorize(Policy = CustomerPolicies.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<IActionResult> Reactivate(Guid id, CancellationToken cancellationToken) =>
        SetActiveAsync(id, true, cancellationToken);

    private async Task<IActionResult> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        var result = await activeHandler.HandleAsync(
            ticket.OrganizationId,
            ticket.MembershipId,
            id,
            isActive,
            ticket.UserId,
            GetClientIpAddress(),
            cancellationToken);

        return result.Kind == CustomerResultKind.NoContent ? NoContent() : MapFailure(result);
    }

    // The form is read here, not by model binding, so an oversized body (BadHttpRequestException, 413)
    // reaches BadHttpRequestExceptionHandler while a malformed multipart body is a plain 400.
    private async Task<(byte[]? Content, IActionResult? Failure)> ReadCsvAsync(CancellationToken cancellationToken)
    {
        IFormFile? file;

        try
        {
            var form = await Request.ReadFormAsync(cancellationToken);
            file = form.Files.GetFile(FileKey);
        }
        catch (InvalidDataException)
        {
            return (null, BadRequest());
        }

        if (file is null)
        {
            return (null, FieldErrors(
                new Dictionary<string, string[]>(StringComparer.Ordinal) { [FileKey] = [CustomerCsv.NotCsvMessage] },
                StatusCodes.Status400BadRequest));
        }

        // At most limit bytes, so an oversized part is detected without buffering more than needed.
        var limit = CustomerCsv.MaxImportFileBytes + 1L;
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

        return (buffer.ToArray(), null);
    }

    private IActionResult MapFailure<T>(CustomerResult<T> result)
    {
        switch (result.Kind)
        {
            case CustomerResultKind.Invalid when result.RowErrors is { } rowErrors:
                var problem = new ProblemDetails
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "The file has errors.",
                };
                problem.Extensions["rowErrors"] = rowErrors;

                return new ObjectResult(problem) { StatusCode = StatusCodes.Status400BadRequest };

            case CustomerResultKind.Invalid:
                return FieldErrors(result.Errors!, StatusCodes.Status400BadRequest);

            case CustomerResultKind.NotFound:
                return NotFound();

            case CustomerResultKind.Conflict:
                return Conflict(new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = result.Message,
                });

            default:
                throw new InvalidOperationException("Unknown customer result.");
        }
    }

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
