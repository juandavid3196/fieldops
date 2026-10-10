using System.Net;
using FieldOps.Api.Authentication;
using FieldOps.Api.Authorization;
using FieldOps.Api.Contracts;
using FieldOps.Application.Features.Customers;
using FieldOps.Application.Features.PortalAccess;
using FieldOps.Application.Features.PortalInvitations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace FieldOps.Api.Controllers;

/// <summary>
/// Customer detail page endpoints (FR-02, FR-04 to FR-13, FR-16). No endpoint accepts an organization
/// identifier; an id that is not a valid UUID never matches the <c>{id:guid}</c> route (404).
/// </summary>
/// <remarks>Not an [ApiController]: model state is checked here, per <see cref="CustomersController"/>.</remarks>
[Route("customers/{id:guid}")]
public sealed class CustomerDetailController(
    GetCustomerOverviewHandler overviewHandler,
    ListCustomerPropertiesHandler listPropertiesHandler,
    GetCustomerPropertyHandler getPropertyHandler,
    CreateCustomerPropertyHandler createPropertyHandler,
    UpdateCustomerPropertyHandler updatePropertyHandler,
    ChangeCustomerPropertyStateHandler propertyStateHandler,
    GetCustomerRecentWorkHandler recentWorkHandler,
    GetCustomerUpcomingAppointmentsHandler upcomingHandler,
    ListCustomerNotesHandler listNotesHandler,
    AddCustomerNoteHandler addNoteHandler,
    ListCustomerActivityHandler activityHandler,
    InvitePortalContactHandler inviteHandler,
    RemovePortalAccessHandler removeAccessHandler) : ControllerBase
{
    [HttpGet("detail")]
    [Authorize(Policy = CustomerPolicies.View)]
    [ProducesResponseType<CustomerOverview>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<IActionResult> Overview(Guid id, CancellationToken cancellationToken) =>
        ReadAsync(ticket => overviewHandler.HandleAsync(ticket.OrganizationId, ticket.MembershipId, id, cancellationToken));

    [HttpGet("properties")]
    [Authorize(Policy = CustomerPolicies.View)]
    [ProducesResponseType<CustomerPropertyList>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<IActionResult> ListProperties(Guid id, CancellationToken cancellationToken) =>
        ReadAsync(ticket => listPropertiesHandler.HandleAsync(ticket.OrganizationId, ticket.MembershipId, id, cancellationToken));

    [HttpGet("properties/{propertyId:guid}")]
    [Authorize(Policy = CustomerPolicies.View)]
    [ProducesResponseType<CustomerPropertyView>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<IActionResult> GetProperty(Guid id, Guid propertyId, CancellationToken cancellationToken) =>
        ReadAsync(ticket =>
            getPropertyHandler.HandleAsync(ticket.OrganizationId, ticket.MembershipId, id, propertyId, cancellationToken));

    [HttpPost("properties")]
    [Authorize(Policy = CustomerPolicies.Manage)]
    [Consumes("application/json")]
    [RequestSizeLimit(CustomersController.MaxJsonBodyBytes)]
    [ProducesResponseType<CustomerPropertyView>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateProperty(
        Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] CustomerPropertyWriteRequest request,
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

        var result = await createPropertyHandler.HandleAsync(
            ticket.OrganizationId,
            ticket.MembershipId,
            id,
            ticket.UserId,
            GetClientIpAddress(),
            request.ToInput(),
            cancellationToken);

        return result.Kind == CustomerResultKind.Succeeded
            ? Created($"/customers/{id}/properties/{result.Value!.Id}", result.Value)
            : MapFailure(result);
    }

    [HttpPut("properties/{propertyId:guid}")]
    [Authorize(Policy = CustomerPolicies.Manage)]
    [Consumes("application/json")]
    [RequestSizeLimit(CustomersController.MaxJsonBodyBytes)]
    [ProducesResponseType<CustomerPropertyView>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateProperty(
        Guid id,
        Guid propertyId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] CustomerPropertyWriteRequest request,
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

        var result = await updatePropertyHandler.HandleAsync(
            ticket.OrganizationId,
            ticket.MembershipId,
            id,
            propertyId,
            ticket.UserId,
            GetClientIpAddress(),
            request.ToInput(),
            cancellationToken);

        return result.Kind == CustomerResultKind.Succeeded ? Ok(result.Value) : MapFailure(result);
    }

    [HttpPost("properties/{propertyId:guid}/set-primary")]
    [Authorize(Policy = CustomerPolicies.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<IActionResult> SetPrimary(Guid id, Guid propertyId, CancellationToken cancellationToken) =>
        ChangeStateAsync(id, propertyId, CustomerPropertyAction.SetPrimary, cancellationToken);

    [HttpPost("properties/{propertyId:guid}/archive")]
    [Authorize(Policy = CustomerPolicies.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<IActionResult> ArchiveProperty(Guid id, Guid propertyId, CancellationToken cancellationToken) =>
        ChangeStateAsync(id, propertyId, CustomerPropertyAction.Archive, cancellationToken);

    [HttpPost("properties/{propertyId:guid}/reactivate")]
    [Authorize(Policy = CustomerPolicies.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<IActionResult> ReactivateProperty(Guid id, Guid propertyId, CancellationToken cancellationToken) =>
        ChangeStateAsync(id, propertyId, CustomerPropertyAction.Reactivate, cancellationToken);

    [HttpGet("recent-work")]
    [Authorize(Policy = CustomerPolicies.View)]
    [ProducesResponseType<CustomerRecentWork>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<IActionResult> RecentWork(Guid id, CancellationToken cancellationToken) =>
        ReadAsync(ticket => recentWorkHandler.HandleAsync(ticket.OrganizationId, ticket.MembershipId, id, cancellationToken));

    [HttpGet("upcoming-appointments")]
    [Authorize(Policy = CustomerPolicies.View)]
    [ProducesResponseType<CustomerUpcomingAppointments>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<IActionResult> UpcomingAppointments(Guid id, CancellationToken cancellationToken) =>
        ReadAsync(ticket => upcomingHandler.HandleAsync(ticket.OrganizationId, ticket.MembershipId, id, cancellationToken));

    [HttpGet("notes")]
    [Authorize(Policy = CustomerPolicies.View)]
    [ProducesResponseType<CustomerNotesPage>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListNotes(Guid id, [FromQuery] string? page, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        var result = await listNotesHandler.HandleAsync(ticket.OrganizationId, ticket.MembershipId, id, page, cancellationToken);

        return result.Kind == CustomerResultKind.Succeeded ? Ok(result.Value) : MapFailure(result);
    }

    [HttpPost("notes")]
    [Authorize(Policy = CustomerPolicies.Manage)]
    [Consumes("application/json")]
    [RequestSizeLimit(CustomersController.MaxJsonBodyBytes)]
    [ProducesResponseType<CustomerNoteView>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddNote(
        Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] CustomerNoteRequest request,
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

        var result = await addNoteHandler.HandleAsync(
            ticket.OrganizationId, ticket.MembershipId, id, ticket.UserId, GetClientIpAddress(), request.Note, cancellationToken);

        return result.Kind == CustomerResultKind.Succeeded
            ? Created($"/customers/{id}/notes", result.Value)
            : MapFailure(result);
    }

    [HttpGet("activity")]
    [Authorize(Policy = CustomerPolicies.View)]
    [ProducesResponseType<CustomerActivityPage>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Activity(Guid id, [FromQuery] string? page, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        var result = await activityHandler.HandleAsync(ticket.OrganizationId, ticket.MembershipId, id, page, cancellationToken);

        return result.Kind == CustomerResultKind.Succeeded ? Ok(result.Value) : MapFailure(result);
    }

    /// <summary>Invite or re-invite the primary contact to the customer portal (customer portal BR-11).</summary>
    [HttpPost("portal-invitation")]
    [Authorize(Policy = CustomerPolicies.Manage)]
    [ProducesResponseType<PortalStatusView>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> InvitePortal(Guid id, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        return MapPortal(await inviteHandler.HandleAsync(
            ticket.OrganizationId, ticket.MembershipId, ticket.UserId, id, GetClientIpAddress(), cancellationToken));
    }

    /// <summary>Remove portal access of the primary contact (customer portal BR-14); idempotent.</summary>
    [HttpDelete("portal-access")]
    [Authorize(Policy = CustomerPolicies.Manage)]
    [ProducesResponseType<PortalStatusView>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemovePortalAccess(Guid id, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        return MapPortal(await removeAccessHandler.HandleAsync(
            ticket.OrganizationId, ticket.MembershipId, ticket.UserId, id, GetClientIpAddress(), cancellationToken));
    }

    private IActionResult MapPortal(PortalOutcome<PortalStatusView> outcome) =>
        outcome switch
        {
            PortalOutcome<PortalStatusView>.Ok ok => Ok(ok.Value),
            PortalOutcome<PortalStatusView>.Conflict conflict => StatusCode(
                StatusCodes.Status409Conflict,
                new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = conflict.Title,
                    Extensions = { ["code"] = conflict.Code },
                }),
            _ => NotFound(),
        };

    private async Task<IActionResult> ReadAsync<T>(Func<SessionTicket, Task<T?>> read)
        where T : class
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        var value = await read(ticket);

        return value is null ? NotFound() : Ok(value);
    }

    private async Task<IActionResult> ChangeStateAsync(
        Guid id, Guid propertyId, CustomerPropertyAction action, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        var result = await propertyStateHandler.HandleAsync(
            ticket.OrganizationId,
            ticket.MembershipId,
            id,
            propertyId,
            action,
            ticket.UserId,
            GetClientIpAddress(),
            cancellationToken);

        return result.Kind == CustomerResultKind.NoContent ? NoContent() : MapFailure(result);
    }

    private IActionResult MapFailure<T>(CustomerResult<T> result) =>
        result.Kind switch
        {
            CustomerResultKind.Invalid => FieldErrors(result.Errors!),
            CustomerResultKind.NotFound => NotFound(),
            CustomerResultKind.Conflict => Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = result.Message,
            }),
            _ => throw new InvalidOperationException("Unknown customer result."),
        };

    private static ObjectResult FieldErrors(IReadOnlyDictionary<string, string[]> errors) =>
        new(new ValidationProblemDetails(new Dictionary<string, string[]>(errors, StringComparer.Ordinal))
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "One or more validation errors occurred.",
        })
        {
            StatusCode = StatusCodes.Status400BadRequest,
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
