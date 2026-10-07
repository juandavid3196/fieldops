using System.Net;
using FieldOps.Api.Authentication;
using FieldOps.Api.Authorization;
using FieldOps.Api.Contracts;
using FieldOps.Application.Features.TechnicianVisits;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace FieldOps.Api.Controllers;

/// <summary>
/// The signed-in technician's own visits (technician-todays-jobs). No parameter carries a technician or
/// organization identifier; both come from the session and the caller's linked profile.
/// </summary>
[Route("technician")]
public sealed class TechnicianController(
    GetTodayVisitsHandler todayHandler,
    GetTechnicianVisitHandler visitHandler,
    StartTravelHandler startTravelHandler,
    ArriveHandler arriveHandler,
    GetVisitAssessmentPhotoHandler photoHandler,
    TechnicianVisitProgressHandler progressHandler) : ControllerBase
{
    public const int MaxJsonBodyBytes = 64 * 1024;

    // 10 MiB of image plus the multipart envelope; a larger body is the existing 413.
    public const int MaxEvidenceRequestBytes = 11_534_336;

    public const string FileKey = "file";

    public const string TypeKey = "type";

    private const string ImageContentSecurityPolicy = "default-src 'none'; sandbox";

    [HttpGet("today")]
    [Authorize(Policy = TeamPolicies.Self)]
    [ProducesResponseType<TodayJobs>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Today(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        var result = await todayHandler.HandleAsync(ticket.OrganizationId, ticket.MembershipId, cancellationToken);

        return result.Kind == TechnicianVisitResultKind.Succeeded ? Ok(result.Value) : MapFailure(result);
    }

    [HttpGet("visits/{visitId:guid}")]
    [Authorize(Policy = TeamPolicies.Self)]
    [ProducesResponseType<TechnicianVisitDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetVisit(Guid visitId, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        var result = await visitHandler.HandleAsync(ticket.OrganizationId, ticket.MembershipId, visitId, cancellationToken);

        return result.Kind == TechnicianVisitResultKind.Succeeded ? Ok(result.Value) : MapFailure(result);
    }

    [HttpPost("visits/{visitId:guid}/start-travel")]
    [Authorize(Policy = TeamPolicies.Self)]
    [ProducesResponseType<TravelResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> StartTravel(Guid visitId, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        var result = await startTravelHandler.HandleAsync(Call(ticket), visitId, cancellationToken);

        return result.Kind == TechnicianVisitResultKind.Succeeded ? Ok(result.Value) : MapFailure(result);
    }

    [HttpPost("visits/{visitId:guid}/arrive")]
    [Authorize(Policy = TeamPolicies.Self)]
    [ProducesResponseType<TravelResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Arrive(Guid visitId, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        var result = await arriveHandler.HandleAsync(Call(ticket), visitId, cancellationToken);

        return result.Kind == TechnicianVisitResultKind.Succeeded ? Ok(result.Value) : MapFailure(result);
    }

    [HttpGet("visits/{visitId:guid}/assessment-photos/{photoId:guid}")]
    [Authorize(Policy = TeamPolicies.Self)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAssessmentPhoto(Guid visitId, Guid photoId, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        var result = await photoHandler.HandleAsync(
            ticket.OrganizationId, ticket.MembershipId, visitId, photoId, cancellationToken);

        if (result.Kind != TechnicianVisitResultKind.Succeeded)
        {
            return MapFailure(result);
        }

        // BR-06: the stored image type, inline, never sniffed.
        Response.Headers.ContentDisposition = "inline";
        Response.Headers.XContentTypeOptions = "nosniff";

        return File(result.Value!.Content, result.Value.MimeType);
    }

    [HttpPost("visits/{visitId:guid}/start-job")]
    [Authorize(Policy = TeamPolicies.Self)]
    [ProducesResponseType<VisitActionResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> StartJob(Guid visitId, CancellationToken cancellationToken) =>
        await TransitionAsync(visitId, progressHandler.StartJobAsync, cancellationToken);

    [HttpPost("visits/{visitId:guid}/pause")]
    [Authorize(Policy = TeamPolicies.Self)]
    [ProducesResponseType<VisitActionResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Pause(Guid visitId, CancellationToken cancellationToken) =>
        await TransitionAsync(visitId, progressHandler.PauseAsync, cancellationToken);

    [HttpPost("visits/{visitId:guid}/resume")]
    [Authorize(Policy = TeamPolicies.Self)]
    [ProducesResponseType<VisitActionResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Resume(Guid visitId, CancellationToken cancellationToken) =>
        await TransitionAsync(visitId, progressHandler.ResumeAsync, cancellationToken);

    [HttpPatch("visits/{visitId:guid}/tasks/{taskId:guid}")]
    [Authorize(Policy = TeamPolicies.Self)]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxJsonBodyBytes)]
    [ProducesResponseType<TechnicianVisitDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateTask(
        Guid visitId,
        Guid taskId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] UpdateVisitTaskBody body,
        CancellationToken cancellationToken) =>
        await EditAsync(
            call => progressHandler.UpdateTaskAsync(
                call, visitId, taskId, new UpdateTaskInput(body.IsCompleted, body.Notes), cancellationToken),
            created: false);

    [HttpPost("visits/{visitId:guid}/tasks")]
    [Authorize(Policy = TeamPolicies.Self)]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxJsonBodyBytes)]
    [ProducesResponseType<TechnicianVisitDetail>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddTask(
        Guid visitId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] AddVisitTaskBody body,
        CancellationToken cancellationToken) =>
        await EditAsync(call => progressHandler.AddTaskAsync(call, visitId, body.Label, cancellationToken), created: true, visitId);

    [HttpPut("visits/{visitId:guid}/planned-materials/{plannedMaterialId:guid}")]
    [Authorize(Policy = TeamPolicies.Self)]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxJsonBodyBytes)]
    [ProducesResponseType<TechnicianVisitDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SetPlannedMaterial(
        Guid visitId,
        Guid plannedMaterialId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] PlannedMaterialUsedBody body,
        CancellationToken cancellationToken) =>
        await EditAsync(
            call => progressHandler.SetPlannedMaterialAsync(call, visitId, plannedMaterialId, body.UsedQuantity, cancellationToken),
            created: false);

    [HttpPost("visits/{visitId:guid}/materials")]
    [Authorize(Policy = TeamPolicies.Self)]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxJsonBodyBytes)]
    [ProducesResponseType<TechnicianVisitDetail>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddMaterial(
        Guid visitId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] AddVisitMaterialBody body,
        CancellationToken cancellationToken) =>
        await EditAsync(
            call => progressHandler.AddMaterialAsync(
                call,
                visitId,
                new AddMaterialInput(body.Quantity, body.CatalogItemId, body.Description, body.Unit),
                cancellationToken),
            created: true,
            visitId);

    [HttpPut("visits/{visitId:guid}/materials/{materialId:guid}")]
    [Authorize(Policy = TeamPolicies.Self)]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxJsonBodyBytes)]
    [ProducesResponseType<TechnicianVisitDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SetMaterial(
        Guid visitId,
        Guid materialId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] VisitMaterialQuantityBody body,
        CancellationToken cancellationToken) =>
        await EditAsync(
            call => progressHandler.SetMaterialAsync(call, visitId, materialId, body.Quantity, cancellationToken),
            created: false);

    [HttpGet("visits/{visitId:guid}/material-catalog")]
    [Authorize(Policy = TeamPolicies.Self)]
    [ProducesResponseType<IReadOnlyList<MaterialCatalogItem>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SearchMaterialCatalog(Guid visitId, string? search, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        var result = await progressHandler.SearchMaterialCatalogAsync(
            ticket.OrganizationId, ticket.MembershipId, visitId, search, cancellationToken);

        return result.Kind == TechnicianVisitResultKind.Succeeded ? Ok(result.Value) : MapFailure(result);
    }

    [HttpPost("visits/{visitId:guid}/evidence")]
    [Authorize(Policy = TeamPolicies.Self)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxEvidenceRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxEvidenceRequestBytes)]
    [ProducesResponseType<TechnicianVisitDetail>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status415UnsupportedMediaType)]
    public async Task<IActionResult> UploadEvidence(Guid visitId, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        var call = Call(ticket);

        // Authorization, primary control and status are decided before the request body is read (Tenant isolation).
        var precheck = await progressHandler.PrecheckEvidenceAsync(call, visitId, cancellationToken);

        if (precheck.Kind != TechnicianVisitResultKind.Succeeded)
        {
            return MapFailure(precheck);
        }

        var (file, type, malformed) = await ReadFilePartAsync(cancellationToken);

        if (malformed)
        {
            return BadRequest();
        }

        if (file is null)
        {
            return FileProblem();
        }

        var content = await ReadAtMostAsync(file, VisitEvidenceContentValidator.MaxBytes + 1L, cancellationToken);
        var result = await progressHandler.AddEvidenceAsync(
            call, visitId, type, file.FileName, file.ContentType, content, cancellationToken);

        return result.Kind == TechnicianVisitResultKind.Succeeded
            ? Created($"/technician/visits/{visitId}", result.Value)
            : MapFailure(result);
    }

    [HttpGet("visits/{visitId:guid}/evidence/{evidenceId:guid}")]
    [Authorize(Policy = TeamPolicies.Self)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetEvidence(Guid visitId, Guid evidenceId, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        var result = await progressHandler.GetEvidenceAsync(
            ticket.OrganizationId, ticket.MembershipId, visitId, evidenceId, cancellationToken);

        if (result.Kind != TechnicianVisitResultKind.Succeeded)
        {
            return MapFailure(result);
        }

        // BR-12: the stored image type, inline, never sniffed, never rendered as active content.
        Response.Headers.ContentDisposition = "inline";
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.ContentSecurityPolicy = ImageContentSecurityPolicy;

        return File(result.Value!.Content, result.Value.MimeType);
    }

    [HttpDelete("visits/{visitId:guid}/evidence/{evidenceId:guid}")]
    [Authorize(Policy = TeamPolicies.Self)]
    [ProducesResponseType<TechnicianVisitDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteEvidence(Guid visitId, Guid evidenceId, CancellationToken cancellationToken) =>
        await EditAsync(call => progressHandler.DeleteEvidenceAsync(call, visitId, evidenceId, cancellationToken), created: false);

    [HttpPut("visits/{visitId:guid}/notes")]
    [Authorize(Policy = TeamPolicies.Self)]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxJsonBodyBytes)]
    [ProducesResponseType<TechnicianVisitDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SetNotes(
        Guid visitId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] TechnicianNotesBody body,
        CancellationToken cancellationToken) =>
        await EditAsync(call => progressHandler.SetNotesAsync(call, visitId, body.Notes, cancellationToken), created: false);

    private async Task<IActionResult> TransitionAsync(
        Guid visitId,
        Func<TravelCall, Guid, CancellationToken, Task<TechnicianVisitResult<VisitActionResult>>> run,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        var result = await run(Call(ticket), visitId, cancellationToken);

        return result.Kind == TechnicianVisitResultKind.Succeeded ? Ok(result.Value) : MapFailure(result);
    }

    // The JSON mutations: no-store, the model-state check of a non-[ApiController], the session, then the use case.
    private async Task<IActionResult> EditAsync(
        Func<TravelCall, Task<TechnicianVisitResult<TechnicianVisitDetail>>> run, bool created, Guid? visitId = null)
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

        var result = await run(Call(ticket));

        if (result.Kind != TechnicianVisitResultKind.Succeeded)
        {
            return MapFailure(result);
        }

        return created ? Created($"/technician/visits/{visitId}", result.Value) : Ok(result.Value);
    }

    // The form is read here, not by model binding, so an oversized body (BadHttpRequestException, 413)
    // reaches BadHttpRequestExceptionHandler while a malformed multipart body is a plain 400.
    private async Task<(IFormFile? File, string? Type, bool Malformed)> ReadFilePartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var form = await Request.ReadFormAsync(cancellationToken);

            return (form.Files.GetFile(FileKey), form[TypeKey].ToString(), false);
        }
        catch (InvalidDataException)
        {
            return (null, null, true);
        }
    }

    // Copies at most limit bytes so an oversized part is detected without buffering more than needed.
    private static async Task<byte[]> ReadAtMostAsync(IFormFile file, long limit, CancellationToken cancellationToken)
    {
        var buffer = new MemoryStream();
        var chunk = new byte[81920];
        long total = 0;

        await using var source = file.OpenReadStream();

        while (total < limit)
        {
            var toRead = (int)Math.Min(chunk.Length, limit - total);
            var read = await source.ReadAsync(chunk.AsMemory(0, toRead), cancellationToken);

            if (read == 0)
            {
                break;
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
            total += read;
        }

        return buffer.ToArray();
    }

    private static ObjectResult FileProblem() =>
        FieldErrors(
            new Dictionary<string, string[]>(StringComparer.Ordinal) { [FileKey] = [VisitEvidenceContentValidator.Message] });

    private static ObjectResult FieldErrors(IReadOnlyDictionary<string, string[]> errors) =>
        new(new ValidationProblemDetails(new Dictionary<string, string[]>(errors, StringComparer.Ordinal))
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "One or more validation errors occurred.",
        })
        {
            StatusCode = StatusCodes.Status400BadRequest,
        };

    private bool HasUnsupportedContentType() =>
        ModelState.Values
            .SelectMany(entry => entry.Errors)
            .Any(error => error.Exception is UnsupportedContentTypeException);

    private TravelCall Call(SessionTicket ticket) =>
        new(ticket.OrganizationId, ticket.MembershipId, ticket.UserId, ClientIpAddress());

    private IPAddress? ClientIpAddress()
    {
        var address = HttpContext.Connection.RemoteIpAddress;

        return address is { IsIPv4MappedToIPv6: true } ? address.MapToIPv4() : address;
    }

    // The title follows the code (BR-01, BR-02, BR-07, BR-09); a missing code is the identical 404 of every unavailable visit.
    private IActionResult MapFailure<T>(TechnicianVisitResult<T> result)
    {
        if (result.Kind == TechnicianVisitResultKind.Invalid)
        {
            return FieldErrors(result.Errors!);
        }

        var status = result.Kind switch
        {
            TechnicianVisitResultKind.Forbidden => StatusCodes.Status403Forbidden,
            TechnicianVisitResultKind.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status404NotFound,
        };
        var problem = new ProblemDetails
        {
            Status = status,
            Title = result.Message ?? TechnicianVisitMessages.Title(result.Code),
        };

        if (result.Code is not null)
        {
            problem.Extensions["code"] = result.Code;
        }

        return new ObjectResult(problem) { StatusCode = status };
    }
}
