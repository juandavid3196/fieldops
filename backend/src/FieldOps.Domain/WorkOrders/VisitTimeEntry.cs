namespace FieldOps.Domain.WorkOrders;

public sealed class VisitTimeEntry
{
    private VisitTimeEntry()
    {
    }

    private VisitTimeEntry(
        Guid id,
        Guid visitId,
        Guid technicianId,
        DateTimeOffset startedAt,
        VisitTimeEntryType entryType)
    {
        Id = id;
        VisitId = visitId;
        TechnicianId = technicianId;
        StartedAt = startedAt;
        EntryType = entryType;
    }

    public Guid Id { get; private set; }

    public Guid VisitId { get; private set; }

    public Guid TechnicianId { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset? EndedAt { get; private set; }

    public VisitTimeEntryType EntryType { get; private set; }

    public static VisitTimeEntry Create(
        Guid visitId,
        Guid technicianId,
        DateTimeOffset startedAt,
        VisitTimeEntryType entryType,
        DateTimeOffset? endedAt = null)
    {
        if (visitId == Guid.Empty)
        {
            throw new ArgumentException(
                "Visit id is required.",
                nameof(visitId));
        }

        if (technicianId == Guid.Empty)
        {
            throw new ArgumentException(
                "Technician id is required.",
                nameof(technicianId));
        }

        if (endedAt is not null && startedAt >= endedAt)
        {
            throw new ArgumentException(
                "Started at must be before ended at.",
                nameof(startedAt));
        }

        return new VisitTimeEntry(
            Guid.NewGuid(),
            visitId,
            technicianId,
            startedAt,
            entryType)
        {
            EndedAt = endedAt,
        };
    }

    /// <summary>
    /// Closes an open entry. The end is at least one microsecond (the PostgreSQL precision) after the start, so a close
    /// right after the start still satisfies <c>started_at &lt; ended_at</c>.
    /// </summary>
    public void Close(DateTimeOffset endedAt)
    {
        if (EndedAt is not null)
        {
            throw new InvalidOperationException("The time entry is already closed.");
        }

        var earliest = StartedAt.AddTicks(10);

        EndedAt = endedAt < earliest ? earliest : endedAt;
    }
}
