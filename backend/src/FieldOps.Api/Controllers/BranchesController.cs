using System.Net;
using System.Text.Json;
using FieldOps.Api.Authentication;
using FieldOps.Api.Authorization;
using FieldOps.Api.Contracts;
using FieldOps.Application.Features.Branches;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace FieldOps.Api.Controllers;

/// <summary>
/// Branches of the session organization: list, detail, create, update,
/// deactivate and reactivate (FR-05 to FR-10).
/// </summary>
/// <remarks>
/// Not an [ApiController]: model state is checked here, per
/// <see cref="OrganizationSettingsController"/> and
/// <see cref="OrganizationRegistrationsController"/>.
/// </remarks>
[Route("branches")]
public sealed class BranchesController(
    ListBranchesHandler listHandler,
    GetBranchDetailHandler detailHandler,
    CreateBranchHandler createHandler,
    UpdateBranchHandler updateHandler,
    DeactivateBranchHandler deactivateHandler,
    ReactivateBranchHandler reactivateHandler) : ControllerBase
{
    public const int MaxRequestBodyBytes = 32 * 1024;

    public const string DuplicateCodeKey = "code";

    public const string DuplicateCodeMessage = "Another branch already uses this code.";

    [HttpGet]
    [Authorize(Policy = CompanySettingsPolicies.View)]
    [ProducesResponseType<BranchListResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        var items = await listHandler.HandleAsync(ticket.OrganizationId, ticket.MembershipId, cancellationToken);

        return Ok(new BranchListResponse(items.Select(ToListItemResponse).ToArray()));
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = CompanySettingsPolicies.View)]
    [ProducesResponseType<BranchDetailResponse>(StatusCodes.Status200OK)]
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

        var detail = await detailHandler.HandleAsync(
            ticket.OrganizationId, ticket.MembershipId, id, cancellationToken);

        return detail is null ? NotFound() : Ok(ToDetailResponse(detail));
    }

    [HttpPost]
    [Authorize(Policy = CompanySettingsPolicies.Manage)]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxRequestBodyBytes)]
    [ProducesResponseType<BranchDetailResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] CreateBranchRequest request,
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

        var command = new CreateBranchCommand(
            ticket.OrganizationId,
            request.Name,
            request.Code,
            request.Phone,
            request.Email,
            request.Timezone,
            request.AddressLine1,
            request.AddressLine2,
            request.City,
            request.StateRegion,
            request.PostalCode,
            request.CountryCode,
            request.BusinessHours,
            ticket.UserId,
            GetClientIpAddress());

        var result = await createHandler.HandleAsync(command, cancellationToken);

        switch (result)
        {
            case CreateBranchResult.Succeeded succeeded:
                var body = ToDetailResponse(succeeded.Branch);
                return Created($"/branches/{succeeded.Branch.Id}", body);

            case CreateBranchResult.Invalid invalid:
                AddErrors(invalid.Errors);
                return ValidationProblem(ModelState);

            case CreateBranchResult.DuplicateCode:
                return DuplicateCodeProblem();

            case CreateBranchResult.LimitReached:
                return LimitReachedProblem();

            default:
                throw new InvalidOperationException("Unknown create branch result.");
        }
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = CompanySettingsPolicies.Manage)]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxRequestBodyBytes)]
    [ProducesResponseType<BranchDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] UpdateBranchRequest request,
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

        var command = new UpdateBranchCommand(
            ticket.OrganizationId,
            id,
            request.Name,
            request.Code,
            request.Phone,
            request.Email,
            request.Timezone,
            request.AddressLine1,
            request.AddressLine2,
            request.City,
            request.StateRegion,
            request.PostalCode,
            request.CountryCode,
            request.BusinessHours,
            request.UpdatedAt,
            ticket.UserId,
            GetClientIpAddress());

        var result = await updateHandler.HandleAsync(command, cancellationToken);

        switch (result)
        {
            case UpdateBranchResult.Succeeded succeeded:
                return Ok(ToDetailResponse(succeeded.Branch));

            case UpdateBranchResult.Invalid invalid:
                AddErrors(invalid.Errors);
                return ValidationProblem(ModelState);

            case UpdateBranchResult.NotFound:
                return NotFound();

            case UpdateBranchResult.DuplicateCode:
                return DuplicateCodeProblem();

            case UpdateBranchResult.Stale:
                return StaleProblem();

            default:
                throw new InvalidOperationException("Unknown update branch result.");
        }
    }

    [HttpPost("{id:guid}/deactivate")]
    [Authorize(Policy = CompanySettingsPolicies.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        var outcome = await deactivateHandler.HandleAsync(
            ticket.OrganizationId, id, ticket.UserId, GetClientIpAddress(), cancellationToken);

        return outcome switch
        {
            DeactivateBranchOutcome.NotFound => NotFound(),
            DeactivateBranchOutcome.LastActiveConflict => LastActiveConflictProblem(),
            DeactivateBranchOutcome.NoOp or DeactivateBranchOutcome.Changed => NoContent(),
            _ => throw new InvalidOperationException("Unknown deactivate branch outcome."),
        };
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

        var outcome = await reactivateHandler.HandleAsync(
            ticket.OrganizationId, id, ticket.UserId, GetClientIpAddress(), cancellationToken);

        return outcome switch
        {
            ReactivateBranchOutcome.NotFound => NotFound(),
            ReactivateBranchOutcome.NoOp or ReactivateBranchOutcome.Changed => NoContent(),
            _ => throw new InvalidOperationException("Unknown reactivate branch outcome."),
        };
    }

    private static BranchListItemResponse ToListItemResponse(BranchListItemView item) =>
        new(
            item.Id,
            item.Name,
            item.Code,
            item.AddressLine1,
            item.AddressLine2,
            item.City,
            item.StateRegion,
            item.PostalCode,
            item.CountryCode,
            item.Timezone,
            item.IsActive);

    private static BranchDetailResponse ToDetailResponse(BranchDetailView detail) =>
        new(
            detail.Id,
            detail.Name,
            detail.Code,
            detail.Email,
            detail.Phone,
            detail.AddressLine1,
            detail.AddressLine2,
            detail.City,
            detail.StateRegion,
            detail.PostalCode,
            detail.CountryCode,
            detail.Timezone,
            JsonSerializer.Deserialize<JsonElement>(detail.BusinessHours),
            detail.IsActive,
            detail.UpdatedAt);

    private IActionResult DuplicateCodeProblem()
    {
        ModelState.AddModelError(DuplicateCodeKey, DuplicateCodeMessage);

        return ValidationProblem(
            statusCode: StatusCodes.Status409Conflict,
            modelStateDictionary: ModelState);
    }

    private IActionResult LimitReachedProblem() =>
        Conflict(new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = "Your organization has reached the limit of 100 branches.",
        });

    private IActionResult LastActiveConflictProblem() =>
        Conflict(new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = "At least one branch must stay active.",
        });

    private IActionResult StaleProblem() =>
        Conflict(new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = "This record was changed by someone else.",
        });

    private void AddErrors(IReadOnlyDictionary<string, string[]> errors)
    {
        foreach (var (key, messages) in errors)
        {
            foreach (var message in messages)
            {
                ModelState.AddModelError(key, message);
            }
        }
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
