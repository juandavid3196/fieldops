using System.Net;
using FieldOps.Api.Authentication;
using FieldOps.Api.Authorization;
using FieldOps.Api.Contracts;
using FieldOps.Application.Features.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace FieldOps.Api.Controllers;

/// <summary>
/// Users, invitations and access of the session organization (FR-02 to FR-14).
/// </summary>
/// <remarks>
/// Not an [ApiController]: model state is checked here, per
/// <see cref="BranchesController"/>. The static "invitations" routes never
/// collide with the <c>{id:guid}</c> routes.
/// </remarks>
[Route("users")]
public sealed class UsersController(
    ListUsersHandler listHandler,
    GetUsersSummaryHandler summaryHandler,
    InviteUserHandler inviteHandler,
    ResendInvitationHandler resendHandler,
    RevokeInvitationHandler revokeHandler,
    UpdateMemberAccessHandler updateMemberHandler,
    UpdateInvitationAccessHandler updateInvitationHandler,
    SuspendMemberHandler suspendHandler,
    ReactivateMemberHandler reactivateHandler) : ControllerBase
{
    public const int MaxRequestBodyBytes = 32 * 1024;

    [HttpGet]
    [Authorize(Policy = CompanySettingsPolicies.View)]
    [ProducesResponseType<UserListResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List([FromQuery] ListUsersRequest request, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        var result = await listHandler.HandleAsync(
            new ListUsersQuery(
                ticket.OrganizationId,
                ticket.MembershipId,
                request.Search,
                request.RoleCode,
                request.BranchId,
                request.Status,
                request.Sort,
                request.Page,
                request.PageSize),
            cancellationToken);

        if (result.Kind == UserResultKind.Invalid)
        {
            return FieldErrors(result.Errors!, StatusCodes.Status400BadRequest);
        }

        var page = result.Value!;

        return Ok(new UserListResponse(
            [.. page.Items.Select(UserRowResponse.From)],
            page.Page,
            page.PageSize,
            page.TotalCount));
    }

    [HttpGet("summary")]
    [Authorize(Policy = CompanySettingsPolicies.View)]
    [ProducesResponseType<UserSummaryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Summary(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        var summary = await summaryHandler.HandleAsync(ticket.OrganizationId, cancellationToken);

        return Ok(new UserSummaryResponse(
            summary.ActiveUsers, summary.PendingInvitations, summary.SuspendedUsers, summary.Owners));
    }

    [HttpPost("invitations")]
    [Authorize(Policy = CompanySettingsPolicies.Manage)]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxRequestBodyBytes)]
    [ProducesResponseType<UserRowResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> Invite(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] InviteUserRequest request,
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

        var result = await inviteHandler.HandleAsync(
            new InviteUserCommand(
                ticket.OrganizationId,
                ticket.UserId,
                GetClientIpAddress(),
                request.Email,
                request.FirstName,
                request.LastName,
                request.RoleCode,
                request.IsAllBranches,
                request.BranchIds,
                request.LinkTeamProfile,
                request.ExpiresInDays),
            cancellationToken);

        return result.Kind == UserResultKind.Succeeded
            ? Created($"/users/invitations/{result.Value!.Id}", UserRowResponse.From(result.Value))
            : MapFailure(result);
    }

    [HttpPost("invitations/{id:guid}/resend")]
    [Authorize(Policy = CompanySettingsPolicies.Manage)]
    [ProducesResponseType<UserRowResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> Resend(Guid id, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        var result = await resendHandler.HandleAsync(
            ticket.OrganizationId, ticket.UserId, GetClientIpAddress(), id, cancellationToken);

        return result.Kind == UserResultKind.Succeeded ? Ok(UserRowResponse.From(result.Value!)) : MapFailure(result);
    }

    [HttpPost("invitations/{id:guid}/revoke")]
    [Authorize(Policy = CompanySettingsPolicies.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        var result = await revokeHandler.HandleAsync(
            ticket.OrganizationId, ticket.UserId, GetClientIpAddress(), id, cancellationToken);

        return MapNoContent(result);
    }

    [HttpPut("invitations/{id:guid}/access")]
    [Authorize(Policy = CompanySettingsPolicies.Manage)]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxRequestBodyBytes)]
    [ProducesResponseType<UserRowResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateInvitationAccess(
        Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] UpdateAccessRequest request,
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

        var result = await updateInvitationHandler.HandleAsync(ToCommand(ticket, id, request), cancellationToken);

        return result.Kind == UserResultKind.Succeeded ? Ok(UserRowResponse.From(result.Value!)) : MapFailure(result);
    }

    [HttpPut("{id:guid}/access")]
    [Authorize(Policy = CompanySettingsPolicies.Manage)]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxRequestBodyBytes)]
    [ProducesResponseType<UserRowResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateMemberAccess(
        Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] UpdateAccessRequest request,
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

        var result = await updateMemberHandler.HandleAsync(
            ToCommand(ticket, id, request), ticket.MembershipId, cancellationToken);

        return result.Kind == UserResultKind.Succeeded ? Ok(UserRowResponse.From(result.Value!)) : MapFailure(result);
    }

    [HttpPost("{id:guid}/suspend")]
    [Authorize(Policy = CompanySettingsPolicies.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Suspend(Guid id, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        var result = await suspendHandler.HandleAsync(
            ticket.OrganizationId, ticket.UserId, ticket.MembershipId, GetClientIpAddress(), id, cancellationToken);

        return MapNoContent(result);
    }

    [HttpPost("{id:guid}/reactivate")]
    [Authorize(Policy = CompanySettingsPolicies.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        var result = await reactivateHandler.HandleAsync(
            ticket.OrganizationId, ticket.UserId, GetClientIpAddress(), id, cancellationToken);

        return MapNoContent(result);
    }

    private UpdateAccessCommand ToCommand(SessionTicket ticket, Guid targetId, UpdateAccessRequest request) =>
        new(
            ticket.OrganizationId,
            ticket.UserId,
            targetId,
            GetClientIpAddress(),
            request.RoleCode,
            request.IsAllBranches,
            request.BranchIds);

    private IActionResult MapNoContent(UserResult<NoValue> result) =>
        result.Kind == UserResultKind.NoContent ? NoContent() : MapFailure(result);

    private IActionResult MapFailure<T>(UserResult<T> result)
    {
        switch (result.Kind)
        {
            case UserResultKind.Invalid:
                return FieldErrors(result.Errors!, StatusCodes.Status400BadRequest);

            case UserResultKind.NotFound:
                return NotFound();

            case UserResultKind.Conflict when result.ConflictKey is { } key:
                return FieldErrors(
                    new Dictionary<string, string[]>(StringComparer.Ordinal) { [key] = [result.Message!] },
                    StatusCodes.Status409Conflict);

            case UserResultKind.Conflict:
                return Conflict(new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = result.Message,
                });

            case UserResultKind.DeliveryFailed:
                return StatusCode(
                    StatusCodes.Status502BadGateway,
                    new ProblemDetails
                    {
                        Status = StatusCodes.Status502BadGateway,
                        Title = UserMessages.DeliveryFailed,
                    });

            default:
                throw new InvalidOperationException("Unknown users result.");
        }
    }

    // Built without ModelState: ModelState reuses the casing of keys created
    // by query binding, which would rename "roleCode" to "RoleCode".
    private IActionResult FieldErrors(IReadOnlyDictionary<string, string[]> errors, int statusCode) =>
        new ObjectResult(new ValidationProblemDetails(new Dictionary<string, string[]>(errors, StringComparer.Ordinal))
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
