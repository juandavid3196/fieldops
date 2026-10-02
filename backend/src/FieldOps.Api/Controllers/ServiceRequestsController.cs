using System.Net;
using FieldOps.Api.Authentication;
using FieldOps.Api.Authorization;
using FieldOps.Api.Contracts;
using FieldOps.Application.Features.PublicRequests;
using FieldOps.Application.Features.ServiceRequests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Net.Http.Headers;

namespace FieldOps.Api.Controllers;

/// <summary>
/// The requests pipeline of the session organization (FR-02 to FR-18). No endpoint accepts an organization
/// identifier; the organization and branch scope come from the session membership.
/// </summary>
/// <remarks>
/// Not an [ApiController], like <see cref="CustomersController"/>: model state is checked here. A request
/// outside the caller's scope or organization is a plain 404. Every mutation returns the request detail.
/// </remarks>
[Route("service-requests")]
public sealed class ServiceRequestsController(
    ListServiceRequestPipelineHandler pipelineHandler,
    GetServiceRequestMetricsHandler metricsHandler,
    GetServiceRequestOptionsHandler optionsHandler,
    GetRequestCustomerOptionsHandler customerOptionsHandler,
    GetServiceRequestHandler detailHandler,
    GetRequestAttachmentHandler attachmentHandler,
    ServiceRequestActionHandler actions,
    CreateInternalRequestHandler createHandler) : ControllerBase
{
    public const int MaxJsonBodyBytes = 64 * 1024;

    // 26 MB: the 25 MB per-call total plus multipart overhead (BR-16).
    public const int MaxUploadRequestBytes = 27_262_976;

    public const string AttachmentsPartName = "attachments";

    [HttpGet("pipeline")]
    [Authorize(Policy = ServiceRequestPolicies.View)]
    [ProducesResponseType<PipelineView>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Pipeline(
        [FromQuery] string? assigneeUserId,
        [FromQuery] string? categoryId,
        [FromQuery] string? urgency,
        [FromQuery] string? source,
        [FromQuery] string? created,
        [FromQuery] string? search,
        [FromQuery] string? status,
        [FromQuery] string? offset,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        int? parsedOffset = null;

        if (!string.IsNullOrWhiteSpace(offset))
        {
            parsedOffset = int.TryParse(offset, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var value)
                ? value
                : -1;
        }

        var result = await pipelineHandler.HandleAsync(
            ticket.OrganizationId,
            ticket.MembershipId,
            new PipelineQueryText(assigneeUserId, categoryId, urgency, source, created, search, status, parsedOffset),
            cancellationToken);

        return Map(result);
    }

    [HttpGet("metrics")]
    [Authorize(Policy = ServiceRequestPolicies.View)]
    [ProducesResponseType<RequestMetrics>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Metrics(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        return Ok(await metricsHandler.HandleAsync(ticket.OrganizationId, ticket.MembershipId, cancellationToken));
    }

    [HttpGet("options")]
    [Authorize(Policy = ServiceRequestPolicies.View)]
    [ProducesResponseType<RequestOptions>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Options(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        return Ok(await optionsHandler.HandleAsync(ticket.OrganizationId, ticket.MembershipId, cancellationToken));
    }

    [HttpGet("customer-options")]
    [Authorize(Policy = ServiceRequestPolicies.Manage)]
    [ProducesResponseType<CustomerOptions>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CustomerOptionsList([FromQuery] string? search, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        return Map(await customerOptionsHandler.HandleAsync(
            ticket.OrganizationId, ticket.MembershipId, search, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = ServiceRequestPolicies.View)]
    [ProducesResponseType<RequestDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDetail(Guid id, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        var detail = await detailHandler.HandleAsync(ticket.OrganizationId, ticket.MembershipId, id, cancellationToken);

        return detail is null ? NotFound() : Ok(detail);
    }

    [HttpGet("{id:guid}/attachments/{attachmentId:guid}")]
    [Authorize(Policy = ServiceRequestPolicies.View)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadAttachment(Guid id, Guid attachmentId, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "private, no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        var download = await attachmentHandler.HandleAsync(
            ticket.OrganizationId, ticket.MembershipId, id, attachmentId, cancellationToken);

        if (download is null)
        {
            return NotFound();
        }

        // BR-16: images render inline, everything else downloads; the stored name is already sanitized.
        var disposition = new ContentDispositionHeaderValue(
            download.MimeType.StartsWith("image/", StringComparison.Ordinal) ? "inline" : "attachment");
        disposition.SetHttpFileName(download.FileName);
        Response.Headers.ContentDisposition = disposition.ToString();
        Response.Headers.XContentTypeOptions = "nosniff";

        return File(download.Content, download.MimeType);
    }

    [HttpPost]
    [Authorize(Policy = ServiceRequestPolicies.Manage)]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxJsonBodyBytes)]
    [ProducesResponseType<RequestDetail>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Create(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] CreateInternalRequestBody body,
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

        var result = await createHandler.HandleAsync(Call(ticket), body.ToText(), cancellationToken);

        return result.Kind == ServiceRequestResultKind.Succeeded
            ? Created($"/service-requests/{result.Value!.Id}", result.Value)
            : MapFailure(result);
    }

    [HttpPost("{id:guid}/start-review")]
    [Authorize(Policy = ServiceRequestPolicies.Manage)]
    [ProducesResponseType<RequestDetail>(StatusCodes.Status200OK)]
    public Task<IActionResult> StartReview(Guid id, CancellationToken cancellationToken) =>
        RunAsync(call => actions.StartReviewAsync(call, id, cancellationToken));

    [HttpPut("{id:guid}/assignee")]
    [Authorize(Policy = ServiceRequestPolicies.Manage)]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxJsonBodyBytes)]
    [ProducesResponseType<RequestDetail>(StatusCodes.Status200OK)]
    public Task<IActionResult> Assign(
        Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] AssignRequestBody body,
        CancellationToken cancellationToken) =>
        RunBodyAsync(call => actions.AssignAsync(call, id, body.AssigneeUserId, cancellationToken));

    [HttpPut("{id:guid}/priority")]
    [Authorize(Policy = ServiceRequestPolicies.Manage)]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxJsonBodyBytes)]
    [ProducesResponseType<RequestDetail>(StatusCodes.Status200OK)]
    public Task<IActionResult> Priority(
        Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] PriorityRequestBody body,
        CancellationToken cancellationToken) =>
        RunBodyAsync(call => actions.ChangePriorityAsync(call, id, body.Urgency, cancellationToken));

    [HttpPut("{id:guid}/branch")]
    [Authorize(Policy = ServiceRequestPolicies.Manage)]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxJsonBodyBytes)]
    [ProducesResponseType<RequestDetail>(StatusCodes.Status200OK)]
    public Task<IActionResult> Branch(
        Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] BranchRequestBody body,
        CancellationToken cancellationToken) =>
        RunBodyAsync(call => actions.SetBranchAsync(call, id, body.BranchId, cancellationToken));

    [HttpPost("{id:guid}/notes")]
    [Authorize(Policy = ServiceRequestPolicies.Manage)]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxJsonBodyBytes)]
    [ProducesResponseType<RequestDetail>(StatusCodes.Status200OK)]
    public Task<IActionResult> AddNote(
        Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] MessageRequestBody body,
        CancellationToken cancellationToken) =>
        RunBodyAsync(call => actions.AddNoteAsync(call, id, body.Body, cancellationToken));

    [HttpPost("{id:guid}/information-requests")]
    [Authorize(Policy = ServiceRequestPolicies.Manage)]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxJsonBodyBytes)]
    [ProducesResponseType<RequestDetail>(StatusCodes.Status200OK)]
    public Task<IActionResult> RequestInformation(
        Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] MessageRequestBody body,
        CancellationToken cancellationToken) =>
        RunBodyAsync(call => actions.RequestInformationAsync(call, id, body.Body, cancellationToken));

    [HttpPost("{id:guid}/customer-responses")]
    [Authorize(Policy = ServiceRequestPolicies.Manage)]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxJsonBodyBytes)]
    [ProducesResponseType<RequestDetail>(StatusCodes.Status200OK)]
    public Task<IActionResult> LogResponse(
        Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] MessageRequestBody body,
        CancellationToken cancellationToken) =>
        RunBodyAsync(call => actions.LogResponseAsync(call, id, body.Body, cancellationToken));

    [HttpPost("{id:guid}/assessment")]
    [Authorize(Policy = ServiceRequestPolicies.Manage)]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxJsonBodyBytes)]
    [ProducesResponseType<RequestDetail>(StatusCodes.Status200OK)]
    public Task<IActionResult> ScheduleAssessment(
        Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] ScheduleAssessmentRequestBody body,
        CancellationToken cancellationToken) =>
        RunBodyAsync(call => actions.ScheduleAssessmentAsync(
            call, id, body.Start, body.End, body.TechnicianId, body.BranchId, cancellationToken));

    [HttpPut("{id:guid}/assessment")]
    [Authorize(Policy = ServiceRequestPolicies.Manage)]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxJsonBodyBytes)]
    [ProducesResponseType<RequestDetail>(StatusCodes.Status200OK)]
    public Task<IActionResult> RescheduleAssessment(
        Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] RescheduleAssessmentRequestBody body,
        CancellationToken cancellationToken) =>
        RunBodyAsync(call => actions.RescheduleAssessmentAsync(
            call, id, body.Start, body.End, body.TechnicianId, cancellationToken));

    [HttpPost("{id:guid}/assessment/cancel")]
    [Authorize(Policy = ServiceRequestPolicies.Manage)]
    [ProducesResponseType<RequestDetail>(StatusCodes.Status200OK)]
    public Task<IActionResult> CancelAssessment(Guid id, CancellationToken cancellationToken) =>
        RunAsync(call => actions.CancelAssessmentAsync(call, id, cancellationToken));

    [HttpPost("{id:guid}/assessment/complete")]
    [Authorize(Policy = ServiceRequestPolicies.Manage)]
    [ProducesResponseType<RequestDetail>(StatusCodes.Status200OK)]
    public Task<IActionResult> CompleteAssessment(Guid id, CancellationToken cancellationToken) =>
        RunAsync(call => actions.CompleteAssessmentAsync(call, id, cancellationToken));

    [HttpPost("{id:guid}/ready-for-quote")]
    [Authorize(Policy = ServiceRequestPolicies.Manage)]
    [ProducesResponseType<RequestDetail>(StatusCodes.Status200OK)]
    public Task<IActionResult> ReadyForQuote(Guid id, CancellationToken cancellationToken) =>
        RunAsync(call => actions.MarkReadyForQuoteAsync(call, id, cancellationToken));

    [HttpPost("{id:guid}/move-to-review")]
    [Authorize(Policy = ServiceRequestPolicies.Manage)]
    [ProducesResponseType<RequestDetail>(StatusCodes.Status200OK)]
    public Task<IActionResult> MoveToReview(Guid id, CancellationToken cancellationToken) =>
        RunAsync(call => actions.MoveToReviewAsync(call, id, cancellationToken));

    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = ServiceRequestPolicies.Manage)]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxJsonBodyBytes)]
    [ProducesResponseType<RequestDetail>(StatusCodes.Status200OK)]
    public Task<IActionResult> CancelRequest(
        Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] CancelRequestBody body,
        CancellationToken cancellationToken) =>
        RunBodyAsync(call => actions.CancelRequestAsync(call, id, body.Reason, cancellationToken));

    // The form is read here, not by model binding, so an oversized body (BadHttpRequestException, 413)
    // reaches BadHttpRequestExceptionHandler while a malformed multipart body is a plain 400.
    [HttpPost("{id:guid}/attachments")]
    [Authorize(Policy = ServiceRequestPolicies.Manage)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxUploadRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadRequestBytes)]
    [ProducesResponseType<RequestDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status415UnsupportedMediaType)]
    public async Task<IActionResult> UploadAttachments(Guid id, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        List<UploadedFile> files;

        try
        {
            var form = await Request.ReadFormAsync(cancellationToken);
            files = await ReadFilesAsync(form, cancellationToken);
        }
        catch (InvalidDataException)
        {
            return BadRequest();
        }

        return Map(await actions.AddAttachmentsAsync(Call(ticket), id, files, cancellationToken));
    }

    private static async Task<List<UploadedFile>> ReadFilesAsync(IFormCollection form, CancellationToken cancellationToken)
    {
        var parts = form.Files.GetFiles(AttachmentsPartName);
        var files = new List<UploadedFile>(parts.Count);

        for (var index = 0; index < parts.Count; index++)
        {
            // Beyond the maximum the count error is reported without reading content.
            if (index >= AttachmentContentInspector.MaxFiles)
            {
                files.Add(new UploadedFile(parts[index].FileName, []));
                continue;
            }

            // At most limit + 1 bytes, so an oversized part is detected without buffering more than needed.
            var buffer = new MemoryStream();
            var chunk = new byte[81920];
            long total = 0;
            var limit = AttachmentContentInspector.MaxFileBytes + 1L;

            await using var source = parts[index].OpenReadStream();

            while (total < limit)
            {
                var read = await source.ReadAsync(
                    chunk.AsMemory(0, (int)Math.Min(chunk.Length, limit - total)), cancellationToken);

                if (read == 0)
                {
                    break;
                }

                await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
                total += read;
            }

            files.Add(new UploadedFile(parts[index].FileName, buffer.ToArray()));
        }

        return files;
    }

    private async Task<IActionResult> RunAsync(Func<MembershipCall, Task<ServiceRequestResult<RequestDetail>>> run)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        return Map(await run(Call(ticket)));
    }

    private Task<IActionResult> RunBodyAsync(Func<MembershipCall, Task<ServiceRequestResult<RequestDetail>>> run)
    {
        if (!ModelState.IsValid)
        {
            Response.Headers.CacheControl = "no-store";

            return Task.FromResult<IActionResult>(HasUnsupportedContentType() ? new UnsupportedMediaTypeResult() : BadRequest());
        }

        return RunAsync(run);
    }

    private MembershipCall Call(SessionTicket ticket) =>
        new(ticket.OrganizationId, ticket.MembershipId, ticket.UserId, GetClientIpAddress());

    private IActionResult Map<T>(ServiceRequestResult<T> result) =>
        result.Kind == ServiceRequestResultKind.Succeeded ? Ok(result.Value) : MapFailure(result);

    private IActionResult MapFailure<T>(ServiceRequestResult<T> result) =>
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
            ServiceRequestResultKind.Conflict => Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = result.Message ?? ServiceRequestMessages.ConflictTitle,
            }),
            _ => throw new InvalidOperationException("Unknown service request result."),
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
