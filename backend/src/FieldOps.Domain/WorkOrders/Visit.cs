namespace FieldOps.Domain.WorkOrders;

public sealed class Visit
{
    private Visit()
    {
    }

    private Visit(
        Guid id,
        Guid organizationId,
        Guid workOrderId,
        int visitNumber)
    {
        Id = id;
        OrganizationId = organizationId;
        WorkOrderId = workOrderId;
        VisitNumber = visitNumber;
        Status = VisitStatus.Unscheduled;
        PauseSeconds = 0;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public Guid WorkOrderId { get; private set; }

    public int VisitNumber { get; private set; }

    public VisitStatus Status { get; private set; }

    public DateTimeOffset? ScheduledStart { get; private set; }

    public DateTimeOffset? ScheduledEnd { get; private set; }

    public DateTimeOffset? ActualStartedAt { get; private set; }

    public DateTimeOffset? ActualCompletedAt { get; private set; }

    public int PauseSeconds { get; private set; }

    public string? CompletionSummary { get; private set; }

    public string? CompletionWithoutSignatureReason { get; private set; }

    public string? ReviewNotes { get; private set; }

    public Guid? ReviewedByUserId { get; private set; }

    public DateTimeOffset? ReviewedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Visit Create(
        Guid organizationId,
        Guid workOrderId,
        int visitNumber,
        DateTimeOffset? scheduledStart = null,
        DateTimeOffset? scheduledEnd = null)
    {
        if (organizationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Organization id is required.",
                nameof(organizationId));
        }

        if (workOrderId == Guid.Empty)
        {
            throw new ArgumentException(
                "Work order id is required.",
                nameof(workOrderId));
        }

        if (visitNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(visitNumber),
                visitNumber,
                "Visit number must be greater than zero.");
        }

        if (scheduledStart is not null
            && scheduledEnd is not null
            && scheduledStart >= scheduledEnd)
        {
            throw new ArgumentException(
                "Scheduled start must be before scheduled end.",
                nameof(scheduledStart));
        }

        return new Visit(
            Guid.NewGuid(),
            organizationId,
            workOrderId,
            visitNumber)
        {
            ScheduledStart = scheduledStart,
            ScheduledEnd = scheduledEnd,
        };
    }
}
