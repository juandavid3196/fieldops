namespace FieldOps.Domain.Messaging;

/// <summary>An immutable text message of a conversation (technician-messages BR-03). Never updated or deleted by the feature.</summary>
public sealed class ConversationMessage
{
    public const int MaxBodyLength = 2000;

    private ConversationMessage()
    {
    }

    private ConversationMessage(
        Guid id, Guid organizationId, Guid conversationId, Guid authorUserId, Guid clientMessageId, string body, DateTimeOffset createdAt)
    {
        Id = id;
        OrganizationId = organizationId;
        ConversationId = conversationId;
        AuthorUserId = authorUserId;
        ClientMessageId = clientMessageId;
        Body = body;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public Guid ConversationId { get; private set; }

    public Guid AuthorUserId { get; private set; }

    public Guid ClientMessageId { get; private set; }

    public string Body { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>The body is trimmed; <c>createdAt</c> is truncated to microseconds, the precision PostgreSQL stores.</summary>
    public static ConversationMessage Create(
        Guid organizationId, Guid conversationId, Guid authorUserId, Guid clientMessageId, string body, DateTimeOffset now)
    {
        if (organizationId == Guid.Empty || conversationId == Guid.Empty || authorUserId == Guid.Empty || clientMessageId == Guid.Empty)
        {
            throw new ArgumentException("Message identifiers are required.");
        }

        var trimmed = body?.Trim();

        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxBodyLength)
        {
            throw new ArgumentException("Message body must be 1 to 2000 characters.", nameof(body));
        }

        return new ConversationMessage(
            Guid.NewGuid(),
            organizationId,
            conversationId,
            authorUserId,
            clientMessageId,
            trimmed,
            new DateTimeOffset(now.UtcTicks - (now.UtcTicks % 10), TimeSpan.Zero));
    }
}
