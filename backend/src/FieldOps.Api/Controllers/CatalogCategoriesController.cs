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
/// Catalog categories of the session organization. No endpoint accepts an organization
/// identifier; every id is resolved against the session organization.
/// </summary>
[Route("catalog-categories")]
public sealed class CatalogCategoriesController(CatalogCategoryHandler handler) : ControllerBase
{
    public const int MaxJsonBodyBytes = 16 * 1024;

    [HttpGet]
    [Authorize(Policy = CatalogPolicies.View)]
    [ProducesResponseType<CatalogCategoryListResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        return Ok(new CatalogCategoryListResponse(await handler.ListAsync(ticket.OrganizationId, cancellationToken)));
    }

    [HttpPost]
    [Authorize(Policy = CatalogPolicies.Manage)]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxJsonBodyBytes)]
    [ProducesResponseType<CatalogCategoryView>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] CatalogCategoryRequest request,
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

        var result = await handler.CreateAsync(
            ticket.OrganizationId, ticket.UserId, GetClientIpAddress(), request.NameText, cancellationToken);

        return result.Kind == CatalogResultKind.Succeeded
            ? Created($"/catalog-categories/{result.Value!.Id}", result.Value)
            : MapFailure(result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = CatalogPolicies.Manage)]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxJsonBodyBytes)]
    [ProducesResponseType<CatalogCategoryView>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Rename(
        Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] CatalogCategoryRequest request,
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

        var result = await handler.RenameAsync(
            ticket.OrganizationId, id, ticket.UserId, GetClientIpAddress(), request.NameText, cancellationToken);

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

    private async Task<IActionResult> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        var result = await handler.SetActiveAsync(
            ticket.OrganizationId, id, isActive, ticket.UserId, GetClientIpAddress(), cancellationToken);

        return result.Kind == CatalogResultKind.NotFound ? NotFound() : NoContent();
    }

    private IActionResult MapFailure<T>(CatalogResult<T> result)
    {
        if (result.Kind == CatalogResultKind.NotFound)
        {
            return NotFound();
        }

        var status = result.Kind switch
        {
            CatalogResultKind.Invalid => StatusCodes.Status400BadRequest,
            CatalogResultKind.Conflict => StatusCodes.Status409Conflict,
            _ => throw new InvalidOperationException("Unknown catalog category result."),
        };

        return new ObjectResult(new ValidationProblemDetails(
            new Dictionary<string, string[]>(result.Errors!, StringComparer.Ordinal))
        {
            Status = status,
            Title = "One or more validation errors occurred.",
        })
        {
            StatusCode = status,
        };
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
