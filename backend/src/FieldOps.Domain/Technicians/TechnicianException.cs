namespace FieldOps.Domain.Technicians;

public sealed class TechnicianException
{
    private TechnicianException()
    {
    }

    private TechnicianException(
        Guid id,
        Guid technicianId,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt)
    {
        Id = id;
        TechnicianId = technicianId;
        StartsAt = startsAt;
        EndsAt = endsAt;
        IsAvailable = false;
    }

    public Guid Id { get; private set; }

    public Guid TechnicianId { get; private set; }

    public DateTimeOffset StartsAt { get; private set; }

    public DateTimeOffset EndsAt { get; private set; }

    public bool IsAvailable { get; private set; }

    public string? Reason { get; private set; }

    public static TechnicianException Create(
        Guid technicianId,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt)
    {
        if (technicianId == Guid.Empty)
        {
            throw new ArgumentException(
                "Technician id is required.",
                nameof(technicianId));
        }

        if (startsAt >= endsAt)
        {
            throw new ArgumentException(
                "Start date must be before end date.",
                nameof(startsAt));
        }

        return new TechnicianException(
            Guid.NewGuid(),
            technicianId,
            startsAt,
            endsAt);
    }
}
