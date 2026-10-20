namespace FieldOps.Domain.Messaging;

/// <summary>One row per conversation and user; <c>LastReadAt</c> never moves backwards (technician-messages BR-10).</summary>
public sealed class ConversationReadState
{
    private ConversationReadState()
    {
    }

    private ConversationReadState(Guid organizationId, Guid conversationId, Guid userId, DateTimeOffset lastReadAt)
    {
        OrganizationId = organizationId;
        ConversationId = conversationId;
        UserId = userId;
        LastReadAt = lastReadAt;
    }

    public Guid OrganizationId { get; private set; }

    public Guid ConversationId { get; private set; }

    public Guid UserId { get; private set; }

    public DateTimeOffset LastReadAt { get; private set; }

    public static ConversationReadState Create(Guid organizationId, Guid conversationId, Guid userId, DateTimeOffset lastReadAt)
    {
        if (organizationId == Guid.Empty || conversationId == Guid.Empty || userId == Guid.Empty)
        {
            throw new ArgumentException("Read state identifiers are required.");
        }

        return new ConversationReadState(organizationId, conversationId, userId, lastReadAt);
    }
}
