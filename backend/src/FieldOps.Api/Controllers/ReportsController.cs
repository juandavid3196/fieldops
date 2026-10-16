using FieldOps.Api.Authentication;
using FieldOps.Api.Authorization;
using FieldOps.Application.Features.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FieldOps.Api.Controllers;

/// <summary>
/// The reports workspace of the session organization (reports-workspace FR-01 to FR-13). Read-only apart from the export audit
/// row; no endpoint accepts an organization identifier, the role comes from the validated session and every response is no-store.
/// </summary>
[Route("reports")]
[Authorize(Policy = ReportsPolicies.Read)]
public sealed class ReportsController(
    GetReportOptionsHandler optionsHandler,
    GetReportHandler reportHandler,
    ExportReportHandler exportHandler,
    ListReportExportsHandler historyHandler) : WorkOrderControllerBase
{
    [HttpGet("options")]
    [ProducesResponseType<ReportOptionsView>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Options(CancellationToken cancellationToken)
    {
        var failure = Begin(out var ticket);

        return failure ?? Respond(await optionsHandler.HandleAsync(Call(ticket), RoleCode(), cancellationToken), value => Ok(value));
    }

    [HttpGet("exports")]
    [ProducesResponseType<ReportHistoryView>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> History(CancellationToken cancellationToken)
    {
        var failure = Begin(out var ticket);

        return failure ?? Respond(await historyHandler.HandleAsync(Call(ticket), RoleCode(), cancellationToken), value => Ok(value));
    }

    [HttpGet("{reportKey}")]
    [ProducesResponseType<ReportView>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Report(
        string reportKey,
        string? from,
        string? to,
        string? branchId,
        string? status,
        string? categoryId,
        string? technicianId,
        string? bucket,
        [FromQuery(Name = "event")] string? eventCode,
        string? onTime,
        string? sort,
        string? direction,
        string? page,
        string? pageSize,
        CancellationToken cancellationToken)
    {
        var failure = Begin(out var ticket);

        return failure ?? Respond(
            await reportHandler.HandleAsync(
                Call(ticket),
                RoleCode(),
                reportKey,
                new ReportQueryText(from, to, branchId, status, categoryId, technicianId, bucket, eventCode, onTime, sort, direction, page, pageSize, null, null),
                cancellationToken),
            value => Ok(value));
    }

    [HttpGet("{reportKey}/export")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Export(
        string reportKey,
        string? from,
        string? to,
        string? branchId,
        string? status,
        string? categoryId,
        string? technicianId,
        string? bucket,
        [FromQuery(Name = "event")] string? eventCode,
        string? onTime,
        string? sort,
        string? direction,
        string? format,
        string? compare,
        CancellationToken cancellationToken)
    {
        var failure = Begin(out var ticket);

        return failure ?? Respond(
            await exportHandler.HandleAsync(
                Call(ticket),
                RoleCode(),
                reportKey,
                new ReportQueryText(from, to, branchId, status, categoryId, technicianId, bucket, eventCode, onTime, sort, direction, null, null, format, compare),
                cancellationToken),
            file =>
            {
                Response.Headers.XContentTypeOptions = "nosniff";

                return File(file.Content, file.ContentType, file.FileName);
            });
    }

    // The role of the session validated on this request (the current one, never a stale ticket value).
    private string RoleCode() => SessionCookieEvents.GetValidatedSession(HttpContext)?.Role.Code ?? string.Empty;

    private IActionResult Respond<T>(ReportOutcome<T> outcome, Func<T, IActionResult> success) =>
        outcome switch
        {
            ReportOutcome<T>.Succeeded succeeded => success(succeeded.Value),
            ReportOutcome<T>.Invalid invalid => new ObjectResult(new ValidationProblemDetails(
                new Dictionary<string, string[]>(invalid.Errors, StringComparer.Ordinal))
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "One or more validation errors occurred.",
            })
            {
                StatusCode = StatusCodes.Status400BadRequest,
            },
            ReportOutcome<T>.Conflict conflict => ProblemWith(StatusCodes.Status409Conflict, conflict.Title, conflict.Code),
            ReportOutcome<T>.NotFound notFound => ProblemWith(StatusCodes.Status404NotFound, "Not found.", notFound.Code),
            _ => Forbid(),
        };

    private static ObjectResult ProblemWith(int status, string title, string code)
    {
        var problem = new ProblemDetails { Status = status, Title = title };
        problem.Extensions["code"] = code;

        return new ObjectResult(problem) { StatusCode = status };
    }
}
