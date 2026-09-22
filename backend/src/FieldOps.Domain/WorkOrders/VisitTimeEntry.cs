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
}
