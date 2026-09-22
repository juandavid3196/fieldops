namespace FieldOps.Domain.WorkOrders;

public sealed class VisitIncident
{
    private VisitIncident()
    {
    }

    private VisitIncident(
        Guid id,
        Guid visitId,
        string type,
        string description,
        Guid createdByUserId)
    {
        Id = id;
        VisitId = visitId;
        Type = type;
        Description = description;
        CreatedByUserId = createdByUserId;
        AdditionalWorkRequested = false;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid VisitId { get; private set; }

    public string Type { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    public bool AdditionalWorkRequested { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static VisitIncident Create(
        Guid visitId,
        string type,
        string description,
        Guid createdByUserId)
    {
        if (visitId == Guid.Empty)
        {
            throw new ArgumentException(
                "Visit id is required.",
                nameof(visitId));
        }

        if (string.IsNullOrWhiteSpace(type))
        {
            throw new ArgumentException(
                "Incident type is required.",
                nameof(type));
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException(
                "Incident description is required.",
                nameof(description));
        }

        if (createdByUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "Created by user id is required.",
                nameof(createdByUserId));
        }

        return new VisitIncident(
            Guid.NewGuid(),
            visitId,
            type.Trim(),
            description.Trim(),
            createdByUserId);
    }
}
