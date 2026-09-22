namespace FieldOps.Domain.Requests;

public sealed class RequestMessage
{
    private RequestMessage()
    {
    }

    private RequestMessage(
        Guid id,
        Guid organizationId,
        Guid requestId,
        MessageVisibility visibility,
        string body)
    {
        Id = id;
        OrganizationId = organizationId;
        RequestId = requestId;
        Visibility = visibility;
        Body = body;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public Guid RequestId { get; private set; }

    public Guid? AuthorUserId { get; private set; }

    public Guid? AuthorContactId { get; private set; }

    public MessageVisibility Visibility { get; private set; }

    public string Body { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public static RequestMessage Create(
        Guid organizationId,
        Guid requestId,
        MessageVisibility visibility,
        string body)
    {
        if (organizationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Organization id is required.",
                nameof(organizationId));
        }

        if (requestId == Guid.Empty)
        {
            throw new ArgumentException(
                "Request id is required.",
                nameof(requestId));
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            throw new ArgumentException(
                "Message body is required.",
                nameof(body));
        }

        return new RequestMessage(
            Guid.NewGuid(),
            organizationId,
            requestId,
            visibility,
            body.Trim());
    }
}
