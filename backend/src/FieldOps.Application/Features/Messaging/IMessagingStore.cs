namespace FieldOps.Application.Features.Messaging;

/// <summary>
/// Persistence of technician-messages. Participation is computed on every call from the session organization, membership,
/// role, branch scope and (technicians) profile and assignments; no client identifier widens it (BR-01, BR-02).
/// </summary>
public interface IMessagingStore
{
    Task<MessagingStoreResult<MessagingSummary>> GetSummaryAsync(MessagingActor actor, CancellationToken cancellationToken);

    Task<MessagingStoreResult<ConversationPage>> ListAsync(
        MessagingActor actor, ConversationQuery query, CancellationToken cancellationToken);

    /// <summary>Returns the conversation id of the target, creating the row when needed (BR-11); never writes for a non-participant.</summary>
    Task<MessagingStoreResult<Guid>> ResolveAsync(MessagingActor actor, ResolveTarget target, CancellationToken cancellationToken);

    Task<MessagingStoreResult<ConversationDetail>> GetDetailAsync(
        MessagingActor actor, Guid conversationId, CancellationToken cancellationToken);

    Task<MessagingStoreResult<MessagePage>> ListMessagesAsync(
        MessagingActor actor, Guid conversationId, Guid? before, Guid? after, CancellationToken cancellationToken);

    /// <summary>Participation check only, run before the body is validated and the throttle is charged.</summary>
    Task<MessagingStoreResult<bool>> AuthorizeAsync(MessagingActor actor, Guid conversationId, CancellationToken cancellationToken);

    Task<MessagingStoreResult<SentMessage>> SendAsync(
        MessagingActor actor, Guid conversationId, Guid clientMessageId, string body, CancellationToken cancellationToken);

    Task<MessagingStoreResult<bool>> MarkReadAsync(MessagingActor actor, Guid conversationId, CancellationToken cancellationToken);
}

/// <summary>
/// At most 30 messages per minute per organization and user (BR-09). The rate limiter runs before authentication, so the
/// per-user part is enforced here.
/// </summary>
public interface IMessageSendThrottle
{
    /// <summary>Counts the attempt and returns the time until the user may try again, or null when it is allowed.</summary>
    TimeSpan? TryAcquire(Guid organizationId, Guid userId);
}

public static class MessageSendLimits
{
    public const int PerWindow = 30;

    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
}
