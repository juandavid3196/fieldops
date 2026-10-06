using FieldOps.Api.Authentication;
using FieldOps.Api.Authorization;
using FieldOps.Api.Contracts;
using FieldOps.Application.Features.ServiceRequests;
using FieldOps.Application.Features.WorkOrders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace FieldOps.Api.Controllers;

/// <summary>
/// The work order creation of the session organization (create-work-order FR-02 to FR-12). No endpoint accepts an
/// organization identifier; the organization and branch scope come from the session membership. The editor, draft
/// and create use the manage policy, the jobs list and detail the read policy.
/// </summary>
public sealed class WorkOrdersController(
    GetWorkOrderEditorHandler editorHandler,
    SaveWorkOrderDraftHandler draftHandler,
    CreateWorkOrderHandler createHandler,
    ListWorkOrdersHandler listHandler,
    GetWorkOrderHandler getHandler,
    IAuthorizationService authorization) : WorkOrderControllerBase
{
    [HttpGet("quotes/{quoteId:guid}/work-order")]
    [Authorize(Policy = WorkOrderPolicies.Manage)]
    [ProducesResponseType<WorkOrderEditor>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> GetEditor(Guid quoteId, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        return Map(await editorHandler.HandleAsync(Call(ticket), quoteId, cancellationToken));
    }

    [HttpPut("quotes/{quoteId:guid}/work-order/draft")]
    [Authorize(Policy = WorkOrderPolicies.Manage)]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxJsonBodyBytes)]
    [ProducesResponseType<WorkOrderEditor>(StatusCodes.Status201Created)]
    [ProducesResponseType<WorkOrderEditor>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SaveDraft(
        Guid quoteId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] WorkOrderRequestBody body,
        CancellationToken cancellationToken)
    {
        var failure = Begin(out var ticket);

        if (failure is not null)
        {
            return failure;
        }

        var result = await draftHandler.HandleAsync(Call(ticket), quoteId, body.UpdatedAt, body.ToText(), cancellationToken);

        if (result.Kind != ServiceRequestResultKind.Succeeded)
        {
            return MapFailure(result);
        }

        return result.Value!.Inserted
            ? Created($"/quotes/{quoteId}/work-order", result.Value.Editor)
            : Ok(result.Value.Editor);
    }

    [HttpPost("quotes/{quoteId:guid}/work-order")]
    [Authorize(Policy = WorkOrderPolicies.Manage)]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxJsonBodyBytes)]
    [ProducesResponseType<WorkOrderRef>(StatusCodes.Status201Created)]
    [ProducesResponseType<WorkOrderRef>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        Guid quoteId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] WorkOrderRequestBody body,
        CancellationToken cancellationToken)
    {
        var failure = Begin(out var ticket);

        if (failure is not null)
        {
            return failure;
        }

        var result = await createHandler.HandleAsync(Call(ticket), quoteId, body.UpdatedAt, body.ToText(), cancellationToken);

        if (result.Kind != ServiceRequestResultKind.Succeeded)
        {
            return MapFailure(result);
        }

        return result.Value!.Created
            ? Created($"/work-orders/{result.Value.Order.Id}", result.Value.Order)
            : Ok(result.Value.Order);
    }

    [HttpGet("work-orders")]
    [Authorize(Policy = WorkOrderPolicies.Read)]
    [ProducesResponseType<WorkOrderPage>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(int? page, int? pageSize, CancellationToken cancellationToken)
    {
        var failure = Begin(out var ticket);

        return failure ?? Map(await listHandler.HandleAsync(Call(ticket), page, pageSize, cancellationToken));
    }

    [HttpGet("work-orders/{id:guid}")]
    [Authorize(Policy = WorkOrderPolicies.Read)]
    [ProducesResponseType<WorkOrderDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        var canManage = (await authorization.AuthorizeAsync(User, WorkOrderPolicies.Manage)).Succeeded;
        var detail = await getHandler.HandleAsync(ticket.OrganizationId, ticket.MembershipId, id, canManage, cancellationToken);

        return detail is null ? NotFound() : Ok(detail);
    }
}
