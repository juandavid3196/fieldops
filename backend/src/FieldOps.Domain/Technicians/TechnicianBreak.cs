namespace FieldOps.Domain.Technicians;

public sealed class TechnicianBreak
{
    private TechnicianBreak()
    {
    }

    private TechnicianBreak(
        Guid id,
        Guid availabilityId,
        TimeOnly startTime,
        TimeOnly endTime)
    {
        Id = id;
        AvailabilityId = availabilityId;
        StartTime = startTime;
        EndTime = endTime;
    }

    public Guid Id { get; private set; }

    public Guid AvailabilityId { get; private set; }

    public TimeOnly StartTime { get; private set; }

    public TimeOnly EndTime { get; private set; }

    public static TechnicianBreak Create(
        Guid availabilityId,
        TimeOnly startTime,
        TimeOnly endTime)
    {
        if (availabilityId == Guid.Empty)
        {
            throw new ArgumentException(
                "Availability id is required.",
                nameof(availabilityId));
        }

        if (startTime >= endTime)
        {
            throw new ArgumentException(
                "Start time must be before end time.",
                nameof(startTime));
        }

        return new TechnicianBreak(
            Guid.NewGuid(),
            availabilityId,
            startTime,
            endTime);
    }
}
