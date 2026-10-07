namespace FieldOps.Domain.WorkOrders;

public sealed class VisitStatusHistory
{
    private VisitStatusHistory()
    {
    }

    private VisitStatusHistory(
        Guid id,
        Guid visitId,
        VisitStatus? fromStatus,
        VisitStatus toStatus,
        Guid? changedByUserId,
        DateTimeOffset changedAt)
    {
        Id = id;
        VisitId = visitId;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        ChangedByUserId = changedByUserId;
        ChangedAt = changedAt;
    }

    public Guid Id { get; private set; }

    public Guid VisitId { get; private set; }

    public VisitStatus? FromStatus { get; private set; }

    public VisitStatus ToStatus { get; private set; }

    public Guid? ChangedByUserId { get; private set; }

    public string? Reason { get; private set; }

    public DateTimeOffset ChangedAt { get; private set; }

    public static VisitStatusHistory Create(
        Guid visitId,
        VisitStatus? fromStatus,
        VisitStatus toStatus,
        Guid? changedByUserId = null,
        DateTimeOffset? changedAt = null)
    {
        if (visitId == Guid.Empty)
        {
            throw new ArgumentException(
                "Visit id is required.",
                nameof(visitId));
        }

        return new VisitStatusHistory(
            Guid.NewGuid(),
            visitId,
            fromStatus,
            toStatus,
            changedByUserId,
            changedAt ?? DateTimeOffset.UtcNow);
    }
}
