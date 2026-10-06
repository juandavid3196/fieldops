using System.Net;
using FieldOps.Api.Authentication;
using FieldOps.Api.Authorization;
using FieldOps.Api.Contracts;
using FieldOps.Application.Features.Quotes;
using FieldOps.Application.Features.ServiceRequests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace FieldOps.Api.Controllers;

/// <summary>
/// The quote builder of the session organization (quote-builder FR-02 to FR-16). No endpoint accepts an
/// organization identifier; the organization and branch scope come from the session membership. Reads use the
/// requests-pipeline view policy, calculate and every mutation its manage policy.
/// </summary>
/// <remarks>
/// Not an [ApiController], like <see cref="ServiceRequestsController"/>: model state is checked here. A quote
/// outside the caller's scope or organization is a plain 404. Every response is no-store.
/// </remarks>
[Route("quotes")]
public sealed class QuotesController(
    CreateQuoteHandler createHandler,
    GetQuoteHandler getHandler,
    GetQuoteVersionHandler versionHandler,
    CalculateQuoteHandler calculateHandler,
    SaveQuoteDraftHandler saveHandler,
    SendQuoteHandler sendHandler,
    ReviseQuoteHandler reviseHandler,
    DiscardQuoteDraftHandler discardHandler,
    ResendQuoteEmailHandler resendHandler,
    IAuthorizationService authorization) : ControllerBase
{
    public const int MaxJsonBodyBytes = 64 * 1024;

    [HttpPost]
    [Authorize(Policy = ServiceRequestPolicies.Manage)]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxJsonBodyBytes)]
    [ProducesResponseType<QuoteDetail>(StatusCodes.Status201Created)]
    [ProducesResponseType<QuoteDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] CreateQuoteBody body,
        CancellationToken cancellationToken)
    {
        var failure = BeginMutation(out var ticket);

        if (failure is not null)
        {
            return failure;
        }

        var result = await createHandler.HandleAsync(Call(ticket), body.RequestId, cancellationToken);

        if (result.Kind != ServiceRequestResultKind.Succeeded)
        {
            return MapFailure(result);
        }

        return result.Value!.Created
            ? Created($"/quotes/{result.Value.Detail.Id}", result.Value.Detail)
            : Ok(result.Value.Detail);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = ServiceRequestPolicies.View)]
    [ProducesResponseType<QuoteDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetQuote(Guid id, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        var canManage = (await authorization.AuthorizeAsync(User, ServiceRequestPolicies.Manage)).Succeeded;
        var canManageWorkOrders = (await authorization.AuthorizeAsync(User, WorkOrderPolicies.Manage)).Succeeded;
        var detail = await getHandler.HandleAsync(
            ticket.OrganizationId, ticket.MembershipId, id, canManage, canManageWorkOrders, cancellationToken);

        return detail is null ? NotFound() : Ok(detail);
    }

    [HttpGet("{id:guid}/versions/{versionNo:int}")]
    [Authorize(Policy = ServiceRequestPolicies.View)]
    [ProducesResponseType<QuoteVersionView>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetVersion(Guid id, int versionNo, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (!SessionClaims.TryRead(User, out var ticket))
        {
            return Unauthorized();
        }

        var version = await versionHandler.HandleAsync(ticket.OrganizationId, ticket.MembershipId, id, versionNo, cancellationToken);

        return version is null ? NotFound() : Ok(version);
    }

    [HttpPost("{id:guid}/calculate")]
    [Authorize(Policy = ServiceRequestPolicies.Manage)]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxJsonBodyBytes)]
    [ProducesResponseType<QuoteCalculation>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Calculate(
        Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] DraftBody body,
        CancellationToken cancellationToken)
    {
        var failure = BeginMutation(out var ticket);

        return failure ?? Map(await calculateHandler.HandleAsync(Call(ticket), id, body.ToText(), cancellationToken));
    }

    [HttpPut("{id:guid}/draft")]
    [Authorize(Policy = ServiceRequestPolicies.Manage)]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxJsonBodyBytes)]
    [ProducesResponseType<QuoteDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SaveDraft(
        Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] SaveDraftBody body,
        CancellationToken cancellationToken)
    {
        var failure = BeginMutation(out var ticket);

        return failure ?? Map(await saveHandler.HandleAsync(Call(ticket), id, body.UpdatedAt, body.ToText(), cancellationToken));
    }

    [HttpPost("{id:guid}/send")]
    [Authorize(Policy = ServiceRequestPolicies.Manage)]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxJsonBodyBytes)]
    [ProducesResponseType<QuoteSendResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Send(
        Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] SendQuoteBody body,
        CancellationToken cancellationToken)
    {
        var failure = BeginMutation(out var ticket);

        return failure
            ?? Map(await sendHandler.HandleAsync(Call(ticket), id, body.UpdatedAt, body.EmailMessage, body.ToText(), cancellationToken));
    }

    [HttpPost("{id:guid}/revise")]
    [Authorize(Policy = ServiceRequestPolicies.Manage)]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxJsonBodyBytes)]
    [ProducesResponseType<QuoteDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Revise(
        Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] QuoteVersionActionBody body,
        CancellationToken cancellationToken)
    {
        var failure = BeginMutation(out var ticket);

        return failure ?? Map(await reviseHandler.HandleAsync(Call(ticket), id, body.UpdatedAt, cancellationToken));
    }

    [HttpPost("{id:guid}/discard-draft")]
    [Authorize(Policy = ServiceRequestPolicies.Manage)]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxJsonBodyBytes)]
    [ProducesResponseType<QuoteDiscarded>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DiscardDraft(
        Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] QuoteVersionActionBody body,
        CancellationToken cancellationToken)
    {
        var failure = BeginMutation(out var ticket);

        return failure ?? Map(await discardHandler.HandleAsync(Call(ticket), id, body.UpdatedAt, cancellationToken));
    }

    [HttpPost("{id:guid}/resend-email")]
    [Authorize(Policy = ServiceRequestPolicies.Manage)]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxJsonBodyBytes)]
    [ProducesResponseType<QuoteSendResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ResendEmail(
        Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] ResendQuoteEmailBody body,
        CancellationToken cancellationToken)
    {
        var failure = BeginMutation(out var ticket);

        return failure ?? Map(await resendHandler.HandleAsync(Call(ticket), id, body.UpdatedAt, body.EmailMessage, cancellationToken));
    }

    /// <summary>no-store, the body check of a non-[ApiController] and the session; a result means the request ends here.</summary>
    private IActionResult? BeginMutation(out SessionTicket ticket)
    {
        Response.Headers.CacheControl = "no-store";
        ticket = default;

        if (!ModelState.IsValid)
        {
            return HasUnsupportedContentType() ? new UnsupportedMediaTypeResult() : BadRequest();
        }

        return SessionClaims.TryRead(User, out ticket) ? null : Unauthorized();
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
            ServiceRequestResultKind.Conflict => Conflict(ConflictProblem(result.Message, result.Code)),
            _ => throw new InvalidOperationException("Unknown quote result."),
        };

    // Every 409 carries a machine-readable code.
    private static ProblemDetails ConflictProblem(string? title, string? code)
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = title ?? QuoteMessages.QuoteChangedTitle,
        };
        problem.Extensions["code"] = code ?? QuoteMessages.QuoteChangedCode;

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
