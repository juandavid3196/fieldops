namespace FieldOps.Domain.WorkOrders;

public sealed class VisitAssignment
{
    private VisitAssignment()
    {
    }

    private VisitAssignment(
        Guid id,
        Guid visitId,
        Guid technicianId,
        Guid assignedByUserId)
    {
        Id = id;
        VisitId = visitId;
        TechnicianId = technicianId;
        AssignedByUserId = assignedByUserId;
        IsPrimary = true;
        AssignedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid VisitId { get; private set; }

    public Guid TechnicianId { get; private set; }

    public Guid AssignedByUserId { get; private set; }

    public bool IsPrimary { get; private set; }

    public DateTimeOffset AssignedAt { get; private set; }

    public DateTimeOffset? UnassignedAt { get; private set; }

    public static VisitAssignment Create(
        Guid visitId,
        Guid technicianId,
        Guid assignedByUserId)
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

        if (assignedByUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "Assigned by user id is required.",
                nameof(assignedByUserId));
        }

        return new VisitAssignment(
            Guid.NewGuid(),
            visitId,
            technicianId,
            assignedByUserId);
    }
}
