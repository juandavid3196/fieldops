namespace FieldOps.Domain.WorkOrders;

/// <summary>
/// A customer's request to move a scheduled visit (customer portal BR-32). It never changes the visit; it is pending
/// while the visit still starts at <see cref="OriginalScheduledStart"/>.
/// </summary>
public sealed class VisitRescheduleRequest
{
    public static readonly string[] TimeWindows = ["morning", "afternoon", "evening", "any"];

    private VisitRescheduleRequest()
    {
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public Guid VisitId { get; private set; }

    public Guid ContactId { get; private set; }

    public DateTimeOffset OriginalScheduledStart { get; private set; }

    public DateOnly PreferredDate { get; private set; }

    public string TimeWindow { get; private set; } = string.Empty;

    public string Reason { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public static VisitRescheduleRequest Create(
        Guid organizationId,
        Guid visitId,
        Guid contactId,
        DateTimeOffset originalScheduledStart,
        DateOnly preferredDate,
        string timeWindow,
        string reason,
        DateTimeOffset now)
    {
        if (organizationId == Guid.Empty || visitId == Guid.Empty || contactId == Guid.Empty)
        {
            throw new ArgumentException("Organization, visit and contact are required.");
        }

        if (!TimeWindows.Contains(timeWindow))
        {
            throw new ArgumentException("Time window is not valid.", nameof(timeWindow));
        }

        var text = reason?.Trim() ?? string.Empty;

        if (text.Length is 0 or > 500)
        {
            throw new ArgumentException("Reason must have 1 to 500 characters.", nameof(reason));
        }

        return new VisitRescheduleRequest
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            VisitId = visitId,
            ContactId = contactId,
            OriginalScheduledStart = originalScheduledStart,
            PreferredDate = preferredDate,
            TimeWindow = timeWindow,
            Reason = text,
            CreatedAt = now,
        };
    }
}
