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

    public DateTimeOffset? PreferredStart { get; private set; }

    public DateTimeOffset? PreferredEnd { get; private set; }

    public DateTimeOffset? ArrivalWindowStart { get; private set; }

    public DateTimeOffset? ArrivalWindowEnd { get; private set; }

    public string? DispatchNote { get; private set; }

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
        DateTimeOffset? scheduledEnd = null,
        DateTimeOffset? preferredStart = null,
        DateTimeOffset? preferredEnd = null)
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

        if ((preferredStart is null) != (preferredEnd is null)
            || (preferredStart is not null && preferredStart >= preferredEnd))
        {
            throw new ArgumentException(
                "Preferred start and end must be set together, start before end.",
                nameof(preferredStart));
        }

        return new Visit(
            Guid.NewGuid(),
            organizationId,
            workOrderId,
            visitNumber)
        {
            ScheduledStart = scheduledStart,
            ScheduledEnd = scheduledEnd,
            PreferredStart = preferredStart,
            PreferredEnd = preferredEnd,
        };
    }

    /// <summary>Sets the dispatch fields (dispatch-calendar BR-14); the arrival window must contain the start.</summary>
    public void ApplyDispatch(
        DateTimeOffset start,
        DateTimeOffset end,
        DateTimeOffset arrivalWindowStart,
        DateTimeOffset arrivalWindowEnd,
        string? dispatchNote,
        DateTimeOffset now)
    {
        if (start >= end)
        {
            throw new ArgumentException("Scheduled start must be before scheduled end.", nameof(start));
        }

        if (arrivalWindowStart > start || start > arrivalWindowEnd)
        {
            throw new ArgumentException("The arrival window must contain the scheduled start.", nameof(arrivalWindowStart));
        }

        ScheduledStart = start;
        ScheduledEnd = end;
        ArrivalWindowStart = arrivalWindowStart;
        ArrivalWindowEnd = arrivalWindowEnd;
        DispatchNote = string.IsNullOrWhiteSpace(dispatchNote) ? null : dispatchNote.Trim();
        Touch(now);
    }

    public void SetStatus(VisitStatus status)
    {
        Status = status;
    }

    /// <summary>Sets a new concurrency value, truncated to microseconds (the PostgreSQL precision) and always different from the previous one.</summary>
    public void Touch(DateTimeOffset now)
    {
        var utc = now.UtcDateTime;
        var next = new DateTimeOffset(utc.Ticks - (utc.Ticks % 10), TimeSpan.Zero);

        if (next <= UpdatedAt)
        {
            next = UpdatedAt.AddTicks(10);
        }

        UpdatedAt = next;
    }
}
