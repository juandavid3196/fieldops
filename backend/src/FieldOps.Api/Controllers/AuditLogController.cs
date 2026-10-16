using FieldOps.Api.Authentication;
using FieldOps.Api.Authorization;
using FieldOps.Application.Features.AuditTrail;
using FieldOps.Application.Features.BillingReview;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FieldOps.Api.Controllers;

/// <summary>
/// The read-only audit trail of the session organization (audit-trail FR-01 to FR-05). No endpoint accepts an organization
/// identifier and the role comes from the validated session. Only GET routes exist: a PUT, PATCH or DELETE on these paths is
/// answered 405 by endpoint routing, and nothing updates or deletes audit rows (BR-17).
/// </summary>
[Route("audit-log")]
public sealed class AuditLogController(
    ListAuditLogHandler listHandler,
    GetAuditSummaryHandler summaryHandler,
    GetAuditFiltersHandler filtersHandler,
    GetAuditEventHandler eventHandler,
    ExportAuditLogHandler exportHandler) : WorkOrderControllerBase
{
    [HttpGet]
    [Authorize(Policy = AuditLogPolicies.Read)]
    [ProducesResponseType<AuditPage>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(
        string? from,
        string? to,
        string? q,
        string? actorId,
        [FromQuery(Name = "action")] string? actionCode,
        string? entityType,
        string? entityId,
        string? branchId,
        string? impact,
        string? sort,
        string? direction,
        string? page,
        string? pageSize,
        CancellationToken cancellationToken)
    {
        var failure = Begin(out var ticket);

        return failure ?? Respond(
            await listHandler.HandleAsync(
                Call(ticket),
                RoleCode(),
                new AuditLogQueryText(from, to, q, actorId, actionCode, entityType, entityId, branchId, impact, sort, direction, page, pageSize),
                cancellationToken),
            value => Ok(value));
    }

    [HttpGet("summary")]
    [Authorize(Policy = AuditLogPolicies.Read)]
    [ProducesResponseType<AuditSummary>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Summary(CancellationToken cancellationToken)
    {
        var failure = Begin(out var ticket);

        return failure ?? Ok(await summaryHandler.HandleAsync(Call(ticket), RoleCode(), cancellationToken));
    }

    [HttpGet("filters")]
    [Authorize(Policy = AuditLogPolicies.Read)]
    [ProducesResponseType<AuditFilters>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Filters(CancellationToken cancellationToken)
    {
        var failure = Begin(out var ticket);

        return failure ?? Ok(await filtersHandler.HandleAsync(Call(ticket), RoleCode(), cancellationToken));
    }

    [HttpGet("export")]
    [Authorize(Policy = AuditLogPolicies.Read)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Export(
        string? from,
        string? to,
        string? q,
        string? actorId,
        [FromQuery(Name = "action")] string? actionCode,
        string? entityType,
        string? entityId,
        string? branchId,
        string? impact,
        string? sort,
        string? direction,
        CancellationToken cancellationToken)
    {
        var failure = Begin(out var ticket);

        return failure ?? Respond(
            await exportHandler.HandleAsync(
                Call(ticket),
                RoleCode(),
                new AuditLogQueryText(from, to, q, actorId, actionCode, entityType, entityId, branchId, impact, sort, direction, null, null),
                cancellationToken),
            file =>
            {
                Response.Headers.XContentTypeOptions = "nosniff";

                return File(file.Content, "text/csv; charset=utf-8", file.FileName);
            });
    }

    [HttpGet("{id:long}")]
    [Authorize(Policy = AuditLogPolicies.Read)]
    [ProducesResponseType<AuditDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(long id, CancellationToken cancellationToken)
    {
        var failure = Begin(out var ticket);

        if (failure is not null)
        {
            return failure;
        }

        var detail = await eventHandler.HandleAsync(Call(ticket), RoleCode(), id, cancellationToken);

        return detail is null ? NotFound() : Ok(detail);
    }

    // The role of the session validated on this request (the current one, never a stale ticket value).
    private string RoleCode() => SessionCookieEvents.GetValidatedSession(HttpContext)?.Role.Code ?? string.Empty;

    private IActionResult Respond<T>(BillingOutcome<T> outcome, Func<T, IActionResult> success) =>
        outcome switch
        {
            BillingOutcome<T>.Succeeded succeeded => success(succeeded.Value),
            BillingOutcome<T>.Invalid invalid => new ObjectResult(new ValidationProblemDetails(
                new Dictionary<string, string[]>(invalid.Errors, StringComparer.Ordinal))
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "One or more validation errors occurred.",
            })
            {
                StatusCode = StatusCodes.Status400BadRequest,
            },
            BillingOutcome<T>.Conflict conflict => ProblemWith(StatusCodes.Status409Conflict, conflict.Title, conflict.Code),
            _ => ProblemWith(StatusCodes.Status404NotFound, "Not found.", AuditLogMessages.BranchNotFoundCode),
        };

    // Every 404 and 409 carries a machine-readable code.
    private static ObjectResult ProblemWith(int status, string title, string code)
    {
        var problem = new ProblemDetails { Status = status, Title = title };
        problem.Extensions["code"] = code;

        return new ObjectResult(problem) { StatusCode = status };
    }
}
