using FieldOps.Api.Authentication;
using FieldOps.Api.Authorization;
using FieldOps.Api.Contracts;
using FieldOps.Application.Features.ChecklistTemplates;
using FieldOps.Application.Features.ServiceRequests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace FieldOps.Api.Controllers;

/// <summary>Checklist templates of the session organization (create-work-order BR-11); the manage policy covers both endpoints.</summary>
[Route("checklist-templates")]
public sealed class ChecklistTemplatesController(
    ListChecklistTemplatesHandler listHandler,
    CreateChecklistTemplateHandler createHandler) : WorkOrderControllerBase
{
    [HttpGet]
    [Authorize(Policy = WorkOrderPolicies.Manage)]
    [ProducesResponseType<IReadOnlyList<ChecklistTemplateView>>(StatusCodes.Status200OK)]
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

    [HttpPost]
    [Authorize(Policy = WorkOrderPolicies.Manage)]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxJsonBodyBytes)]
    [ProducesResponseType<ChecklistTemplateView>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Create(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] ChecklistTemplateBody body,
        CancellationToken cancellationToken)
    {
        var failure = Begin(out var ticket);

        if (failure is not null)
        {
            return failure;
        }

        var result = await createHandler.HandleAsync(Call(ticket), body.ToText(), cancellationToken);

        return result.Kind == ServiceRequestResultKind.Succeeded
            ? Created($"/checklist-templates/{result.Value!.Id}", result.Value)
            : MapFailure(result);
    }
}
