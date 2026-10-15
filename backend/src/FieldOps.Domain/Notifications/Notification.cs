namespace FieldOps.Domain.Notifications;

public sealed class Notification
{
    private Notification()
    {
    }

    private Notification(
        Guid id,
        Guid organizationId,
        NotificationChannel channel,
        string templateCode)
    {
        Id = id;
        OrganizationId = organizationId;
        Channel = channel;
        TemplateCode = templateCode;
        Payload = "{}";
        Status = NotificationStatus.Pending;
        ScheduledAt = DateTimeOffset.UtcNow;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public Guid? RecipientUserId { get; private set; }

    public Guid? RecipientContactId { get; private set; }

    public NotificationChannel Channel { get; private set; }

    public string TemplateCode { get; private set; } = string.Empty;

    public string? Subject { get; private set; }

    public string Payload { get; private set; } = "{}";

    public NotificationStatus Status { get; private set; }

    public DateTimeOffset ScheduledAt { get; private set; }

    public DateTimeOffset? SentAt { get; private set; }

    public string? FailureReason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static Notification Create(
        Guid organizationId,
        NotificationChannel channel,
        string templateCode,
        Guid? recipientUserId = null,
        Guid? recipientContactId = null)
    {
        if (organizationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Organization id is required.",
                nameof(organizationId));
        }

        if (string.IsNullOrWhiteSpace(templateCode))
        {
            throw new ArgumentException(
                "Template code is required.",
                nameof(templateCode));
        }

        return new Notification(
            Guid.NewGuid(),
            organizationId,
            channel,
            templateCode.Trim())
        {
            RecipientUserId = recipientUserId,
            RecipientContactId = recipientContactId,
        };
    }

    /// <summary>
    /// Creates a sent in-app notification for a staff recipient; the snapshot content is never re-rendered
    /// (notifications-center BR-06). Time stamps all equal the event time.
    /// </summary>
    public static Notification CreateInApp(
        Guid organizationId,
        Guid recipientUserId,
        string templateCode,
        string subject,
        string payload,
        DateTimeOffset occurredAt)
    {
        if (recipientUserId == Guid.Empty)
        {
            throw new ArgumentException("Recipient user id is required.", nameof(recipientUserId));
        }

        if (string.IsNullOrWhiteSpace(subject))
        {
            throw new ArgumentException("Subject is required.", nameof(subject));
        }

        var notification = Create(
            organizationId,
            NotificationChannel.InApp,
            templateCode,
            recipientUserId);

        notification.Subject = subject.Trim();
        notification.Payload = string.IsNullOrWhiteSpace(payload) ? "{}" : payload;
        notification.Status = NotificationStatus.Sent;
        notification.ScheduledAt = occurredAt;
        notification.SentAt = occurredAt;
        notification.CreatedAt = occurredAt;

        return notification;
    }

    /// <summary>sent to read (BR-19). Idempotent; any other state is left untouched.</summary>
    public void MarkRead()
    {
        if (Status == NotificationStatus.Sent)
        {
            Status = NotificationStatus.Read;
        }
    }

    /// <summary>read to sent (BR-19). Idempotent; any other state is left untouched.</summary>
    public void MarkUnread()
    {
        if (Status == NotificationStatus.Read)
        {
            Status = NotificationStatus.Sent;
        }
    }
}
