using System.Net;
using FieldOps.Api.Authentication;
using FieldOps.Api.Authorization;
using FieldOps.Api.Contracts;
using FieldOps.Application.Features.Customers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace FieldOps.Api.Controllers;

/// <summary>Customer tag catalog of the session organization (FR-09, BR-12).</summary>
[Route("customer-tags")]
public sealed class CustomerTagsController(
    ListCustomerTagsHandler listHandler,
    CreateCustomerTagHandler createHandler) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = CustomerPolicies.View)]
    [ProducesResponseType<IReadOnlyList<CustomerTagView>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        return Ok(await listHandler.HandleAsync(ticket.OrganizationId, cancellationToken));
    }

    /// <summary>201 for a new tag, 200 with the existing tag for a case-insensitive match.</summary>
    [HttpPost]
    [Authorize(Policy = CustomerPolicies.Manage)]
    [Consumes("application/json")]
    [RequestSizeLimit(CustomersController.MaxJsonBodyBytes)]
    [ProducesResponseType<CustomerTagView>(StatusCodes.Status200OK)]
    [ProducesResponseType<CustomerTagView>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Create(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] CreateCustomerTagRequest request,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!ModelState.IsValid)
        {
            var unsupported = ModelState.Values
                .SelectMany(entry => entry.Errors)
                .Any(error => error.Exception is UnsupportedContentTypeException);

            return unsupported ? new UnsupportedMediaTypeResult() : BadRequest();
        }

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        var result = await createHandler.HandleAsync(
            ticket.OrganizationId, ticket.UserId, GetClientIpAddress(), request.Name, cancellationToken);

        if (result.Kind == CustomerResultKind.Invalid)
        {
            return new ObjectResult(new ValidationProblemDetails(
                new Dictionary<string, string[]>(result.Errors!, StringComparer.Ordinal))
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "One or more validation errors occurred.",
            })
            {
                StatusCode = StatusCodes.Status400BadRequest,
            };
        }

        var (tag, created) = result.Value;

        return created ? StatusCode(StatusCodes.Status201Created, tag) : Ok(tag);
    }

    private IPAddress? GetClientIpAddress()
    {
        var address = HttpContext.Connection.RemoteIpAddress;

        return address is { IsIPv4MappedToIPv6: true } ? address.MapToIPv4() : address;
    }
}
