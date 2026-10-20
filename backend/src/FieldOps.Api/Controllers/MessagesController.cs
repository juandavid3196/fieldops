using System.Globalization;
using FieldOps.Api.Authentication;
using FieldOps.Api.Authorization;
using FieldOps.Api.Contracts;
using FieldOps.Application.Features.Messaging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FieldOps.Api.Controllers;

/// <summary>
/// technician-messages: internal Job and Team conversations shared by technicians and staff. The organization, user and role
/// come from the session; participation is computed by the use cases (BR-01, BR-02). Bodies and search text are never logged.
/// </summary>
[Route("messages")]
[Authorize(Policy = MessagingPolicies.Access)]
public sealed class MessagesController(MessagingHandler handler) : ControllerBase
{
    public const int MaxJsonBodyBytes = 64 * 1024;

    [HttpGet("summary")]
    [ProducesResponseType<MessagingSummary>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Summary(CancellationToken cancellationToken) =>
        Respond(await Run(actor => handler.SummaryAsync(actor, cancellationToken)));

    [HttpGet("conversations")]
    [ProducesResponseType<ConversationList>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(string? filter, string? search, string? cursor, CancellationToken cancellationToken) =>
        Respond(await Run(actor => handler.ListAsync(actor, filter, search, cursor, cancellationToken)));

    [HttpPost("conversations/resolve")]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxJsonBodyBytes)]
    [ProducesResponseType<ResolvedConversation>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Resolve([FromBody] ResolveConversationBody? body, CancellationToken cancellationToken) =>
        Respond(await Run(actor => handler.ResolveAsync(actor, body?.Type, body?.WorkOrderId, body?.BranchId, cancellationToken)));

    [HttpGet("conversations/{conversationId:guid}")]
    [ProducesResponseType<ConversationDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Detail(Guid conversationId, CancellationToken cancellationToken) =>
        Respond(await Run(actor => handler.DetailAsync(actor, conversationId, cancellationToken)));

    [HttpGet("conversations/{conversationId:guid}/messages")]
    [ProducesResponseType<MessagePage>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Messages(
        Guid conversationId, string? before, string? after, CancellationToken cancellationToken) =>
        Respond(await Run(actor => handler.MessagesAsync(actor, conversationId, before, after, cancellationToken)));

    [HttpPost("conversations/{conversationId:guid}/messages")]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxJsonBodyBytes)]
    [ProducesResponseType<MessageView>(StatusCodes.Status201Created)]
    [ProducesResponseType<MessageView>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Send(
        Guid conversationId, [FromBody] SendConversationMessageBody? body, CancellationToken cancellationToken)
    {
        var result = await Run(actor => handler.SendAsync(actor, conversationId, body?.ClientMessageId, body?.Body, cancellationToken));

        return result.Kind == MessagingResultKind.Succeeded
            ? StatusCode(result.Value!.Created ? StatusCodes.Status201Created : StatusCodes.Status200OK, result.Value.Message)
            : Respond(result);
    }

    [HttpPost("conversations/{conversationId:guid}/read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Read(Guid conversationId, CancellationToken cancellationToken)
    {
        var result = await Run(actor => handler.ReadAsync(actor, conversationId, cancellationToken));

        return result.Kind == MessagingResultKind.Succeeded ? NoContent() : Respond(result);
    }

    private async Task<MessagingResult<T>> Run<T>(Func<MessagingActor, Task<MessagingResult<T>>> run)
    {
        Response.Headers.CacheControl = "no-store";

        // A policy-authorized request always carries a readable ticket; anything else is the identical 404.
        return SessionClaims.TryRead(User, out var ticket)
            ? await run(new MessagingActor(ticket.OrganizationId, ticket.MembershipId, ticket.UserId))
            : MessagingResult<T>.NotFound();
    }

    private IActionResult Respond<T>(MessagingResult<T> result)
    {
        if (result.Kind == MessagingResultKind.Succeeded)
        {
            return Ok(result.Value);
        }

        if (result.Kind == MessagingResultKind.Invalid)
        {
            return new ObjectResult(new ValidationProblemDetails(new Dictionary<string, string[]>(result.Errors!, StringComparer.Ordinal))
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "One or more validation errors occurred.",
            })
            {
                StatusCode = StatusCodes.Status400BadRequest,
            };
        }

        var status = result.Kind switch
        {
            MessagingResultKind.Forbidden => StatusCodes.Status403Forbidden,
            MessagingResultKind.RateLimited => StatusCodes.Status429TooManyRequests,
            _ => StatusCodes.Status404NotFound,
        };
        var problem = new ProblemDetails { Status = status, Title = MessagingMessages.Title(result.Code) };

        if (result.Code is not null)
        {
            problem.Extensions["code"] = result.Code;
        }

        if (result.RetryAfter is { } retryAfter)
        {
            Response.Headers.RetryAfter = Math.Max(1, (long)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        }

        return new ObjectResult(problem) { StatusCode = status };
    }
}
