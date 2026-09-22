namespace FieldOps.Domain.Technicians;

public sealed class TechnicianWeeklyAvailability
{
    private TechnicianWeeklyAvailability()
    {
    }

    private TechnicianWeeklyAvailability(
        Guid id,
        Guid technicianId,
        short dayOfWeek,
        TimeOnly startTime,
        TimeOnly endTime)
    {
        Id = id;
        TechnicianId = technicianId;
        DayOfWeek = dayOfWeek;
        StartTime = startTime;
        EndTime = endTime;
        CapacityPercent = 100;
    }

    public Guid Id { get; private set; }

    public Guid TechnicianId { get; private set; }

    public short DayOfWeek { get; private set; }

    public TimeOnly StartTime { get; private set; }

    public TimeOnly EndTime { get; private set; }

    public short CapacityPercent { get; private set; }

    public static TechnicianWeeklyAvailability Create(
        Guid technicianId,
        short dayOfWeek,
        TimeOnly startTime,
        TimeOnly endTime,
        short capacityPercent = 100)
    {
        if (technicianId == Guid.Empty)
        {
            throw new ArgumentException(
                "Technician id is required.",
                nameof(technicianId));
        }

        if (dayOfWeek is < 0 or > 6)
        {
            throw new ArgumentOutOfRangeException(
                nameof(dayOfWeek),
                dayOfWeek,
                "Day of week must be between 0 and 6.");
        }

        if (capacityPercent is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capacityPercent),
                capacityPercent,
                "Capacity percent must be between 1 and 100.");
        }

        if (startTime >= endTime)
        {
            throw new ArgumentException(
                "Start time must be before end time.",
                nameof(startTime));
        }

        return new TechnicianWeeklyAvailability(
            Guid.NewGuid(),
            technicianId,
            dayOfWeek,
            startTime,
            endTime)
        {
            CapacityPercent = capacityPercent,
        };
    }
}
