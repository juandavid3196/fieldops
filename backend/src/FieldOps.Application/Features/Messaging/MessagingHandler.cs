using FieldOps.Domain.Messaging;

namespace FieldOps.Application.Features.Messaging;

/// <summary>
/// The technician-messages use cases (BR-01 to BR-11). Authorization is always decided by the store from the session identifiers;
/// no log call here receives a message body or a search text (BR-18).
/// </summary>
public sealed class MessagingHandler(IMessagingStore store, IMessageSendThrottle throttle)
{
    public async Task<MessagingResult<MessagingSummary>> SummaryAsync(MessagingActor actor, CancellationToken cancellationToken) =>
        MessagingResult<MessagingSummary>.From(await store.GetSummaryAsync(actor, cancellationToken));

    public async Task<MessagingResult<ConversationList>> ListAsync(
        MessagingActor actor, string? filter, string? search, string? cursor, CancellationToken cancellationToken)
    {
        var normalizedFilter = string.IsNullOrWhiteSpace(filter) ? MessagingFilters.All : filter.Trim();
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (!MessagingFilters.IsValid(normalizedFilter))
        {
            errors[MessagingFields.Filter] = [MessagingMessages.FilterInvalid];
        }

        var (normalizedSearch, searchValid) = MessagingRules.NormalizeSearch(search);

        if (!searchValid)
        {
            errors[MessagingFields.Search] = [MessagingMessages.SearchInvalid];
        }

        var hash = MessagingRules.QueryHash(normalizedFilter, normalizedSearch);
        ConversationListCursor? after = null;

        if (errors.Count == 0 && !string.IsNullOrEmpty(cursor))
        {
            after = MessagingRules.DecodeCursor(cursor, hash);

            if (after is null)
            {
                errors[MessagingFields.Cursor] = [MessagingMessages.CursorInvalid];
            }
        }

        if (errors.Count > 0)
        {
            return MessagingResult<ConversationList>.Invalid(errors);
        }

        var result = await store.ListAsync(actor, new ConversationQuery(normalizedFilter, normalizedSearch, after), cancellationToken);

        return result.Status == MessagingStatus.Ok
            ? MessagingResult<ConversationList>.Ok(
                new ConversationList(
                    result.Value!.Items,
                    result.Value.Next is { } next ? MessagingRules.EncodeCursor(hash, next) : null))
            : MessagingResult<ConversationList>.From(new MessagingStoreResult<ConversationList>(result.Status));
    }

    public async Task<MessagingResult<ResolvedConversation>> ResolveAsync(
        MessagingActor actor, string? type, Guid? workOrderId, Guid? branchId, CancellationToken cancellationToken)
    {
        ResolveTarget? target = type switch
        {
            ConversationTypes.Job when workOrderId is { } id && id != Guid.Empty => new ResolveTarget(ConversationTypes.Job, id),
            ConversationTypes.Team when branchId is { } id && id != Guid.Empty => new ResolveTarget(ConversationTypes.Team, id),
            _ => null,
        };

        if (target is null)
        {
            return MessagingResult<ResolvedConversation>.Invalid(MessagingFields.Type, MessagingMessages.TypeInvalid);
        }

        var result = await store.ResolveAsync(actor, target, cancellationToken);

        return result.Status == MessagingStatus.Ok
            ? MessagingResult<ResolvedConversation>.Ok(new ResolvedConversation(result.Value))
            : MessagingResult<ResolvedConversation>.From(new MessagingStoreResult<ResolvedConversation>(result.Status));
    }

    public async Task<MessagingResult<ConversationDetail>> DetailAsync(
        MessagingActor actor, Guid conversationId, CancellationToken cancellationToken) =>
        MessagingResult<ConversationDetail>.From(await store.GetDetailAsync(actor, conversationId, cancellationToken));

    public async Task<MessagingResult<MessagePage>> MessagesAsync(
        MessagingActor actor, Guid conversationId, string? before, string? after, CancellationToken cancellationToken)
    {
        Guid? beforeId = null;
        Guid? afterId = null;
        var invalid = !string.IsNullOrEmpty(before) && !string.IsNullOrEmpty(after);

        if (!invalid && !string.IsNullOrEmpty(before))
        {
            invalid = !Guid.TryParse(before, out var parsed);
            beforeId = parsed;
        }

        if (!invalid && !string.IsNullOrEmpty(after))
        {
            invalid = !Guid.TryParse(after, out var parsed);
            afterId = parsed;
        }

        if (invalid)
        {
            // Authorization first: a non-participant never learns whether the cursor was well-formed.
            var access = await store.AuthorizeAsync(actor, conversationId, cancellationToken);

            return access.Status == MessagingStatus.Ok
                ? MessagingResult<MessagePage>.Invalid(MessagingFields.Cursor, MessagingMessages.CursorInvalid)
                : MessagingResult<MessagePage>.From(new MessagingStoreResult<MessagePage>(access.Status));
        }

        return MessagingResult<MessagePage>.From(
            await store.ListMessagesAsync(actor, conversationId, beforeId, afterId, cancellationToken));
    }

    public async Task<MessagingResult<SentMessage>> SendAsync(
        MessagingActor actor, Guid conversationId, string? clientMessageId, string? body, CancellationToken cancellationToken)
    {
        // Authorize, then validate, then throttle, then write (BR-09).
        var access = await store.AuthorizeAsync(actor, conversationId, cancellationToken);

        if (access.Status != MessagingStatus.Ok)
        {
            return MessagingResult<SentMessage>.From(new MessagingStoreResult<SentMessage>(access.Status));
        }

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var trimmed = body?.Trim();

        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > ConversationMessage.MaxBodyLength)
        {
            errors[MessagingFields.Body] = [MessagingMessages.BodyInvalid];
        }

        if (!Guid.TryParse(clientMessageId, out var clientId) || clientId == Guid.Empty)
        {
            errors[MessagingFields.ClientMessageId] = [MessagingMessages.ClientMessageIdInvalid];
        }

        if (errors.Count > 0)
        {
            return MessagingResult<SentMessage>.Invalid(errors);
        }

        if (throttle.TryAcquire(actor.OrganizationId, actor.UserId) is { } retryAfter)
        {
            return MessagingResult<SentMessage>.RateLimited(retryAfter);
        }

        return MessagingResult<SentMessage>.From(await store.SendAsync(actor, conversationId, clientId, trimmed!, cancellationToken));
    }

    public async Task<MessagingResult<bool>> ReadAsync(MessagingActor actor, Guid conversationId, CancellationToken cancellationToken) =>
        MessagingResult<bool>.From(await store.MarkReadAsync(actor, conversationId, cancellationToken));
}
