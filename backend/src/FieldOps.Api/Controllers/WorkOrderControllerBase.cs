using System.Net;
using FieldOps.Api.Authentication;
using FieldOps.Application.Features.ServiceRequests;
using FieldOps.Application.Features.WorkOrders;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace FieldOps.Api.Controllers;

/// <summary>
/// Shared plumbing of the work order controllers. Not an [ApiController], like <see cref="QuotesController"/>:
/// model state is checked here, a resource outside the caller scope or organization is a plain 404 and every
/// response is no-store.
/// </summary>
public abstract class WorkOrderControllerBase : ControllerBase
{
    public const int MaxJsonBodyBytes = 64 * 1024;

    /// <summary>no-store, the body check of a non-[ApiController] and the session; a result means the request ends here.</summary>
    protected IActionResult? Begin(out SessionTicket ticket)
    {
        Response.Headers.CacheControl = "no-store";
        ticket = default;

        if (!ModelState.IsValid)
        {
            return HasUnsupportedContentType() ? new UnsupportedMediaTypeResult() : BadRequest();
        }

        return SessionClaims.TryRead(User, out ticket) ? null : Unauthorized();
    }

    protected MembershipCall Call(SessionTicket ticket) =>
        new(ticket.OrganizationId, ticket.MembershipId, ticket.UserId, GetClientIpAddress());

    protected IActionResult Map<T>(ServiceRequestResult<T> result) =>
        result.Kind == ServiceRequestResultKind.Succeeded ? Ok(result.Value) : MapFailure(result);

    protected IActionResult MapFailure<T>(ServiceRequestResult<T> result) =>
        result.Kind switch
        {
            ServiceRequestResultKind.Invalid => new ObjectResult(new ValidationProblemDetails(
                new Dictionary<string, string[]>(result.Errors!, StringComparer.Ordinal))
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "One or more validation errors occurred.",
            })
            {
                StatusCode = StatusCodes.Status400BadRequest,
            },
            ServiceRequestResultKind.NotFound => NotFound(),
            ServiceRequestResultKind.Conflict => Conflict(ConflictProblem(result.Message, result.Code)),
            _ => throw new InvalidOperationException("Unknown work order result."),
        };

    // Every 409 carries a machine-readable code.
    private static ProblemDetails ConflictProblem(string? title, string? code)
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = title ?? WorkOrderMessages.WorkOrderChangedTitle,
        };
        problem.Extensions["code"] = code ?? WorkOrderMessages.WorkOrderChangedCode;

        return problem;
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
