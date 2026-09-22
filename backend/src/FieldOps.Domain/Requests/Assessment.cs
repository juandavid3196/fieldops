namespace FieldOps.Domain.Requests;

public sealed class Assessment
{
    private Assessment()
    {
    }

    private Assessment(
        Guid id,
        Guid organizationId,
        Guid requestId,
        DateTimeOffset scheduledStart,
        DateTimeOffset scheduledEnd,
        Guid createdByUserId)
    {
        Id = id;
        OrganizationId = organizationId;
        RequestId = requestId;
        ScheduledStart = scheduledStart;
        ScheduledEnd = scheduledEnd;
        CreatedByUserId = createdByUserId;
        Status = AssessmentStatus.Scheduled;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public Guid RequestId { get; private set; }

    public Guid? TechnicianId { get; private set; }

    public DateTimeOffset ScheduledStart { get; private set; }

    public DateTimeOffset ScheduledEnd { get; private set; }

    public AssessmentStatus Status { get; private set; }

    public string? Diagnosis { get; private set; }

    public string? RecommendedScope { get; private set; }

    public string? InternalNotes { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Assessment Create(
        Guid organizationId,
        Guid requestId,
        DateTimeOffset scheduledStart,
        DateTimeOffset scheduledEnd,
        Guid createdByUserId)
    {
        if (organizationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Organization id is required.",
                nameof(organizationId));
        }

        if (requestId == Guid.Empty)
        {
            throw new ArgumentException(
                "Request id is required.",
                nameof(requestId));
        }

        if (createdByUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "Created by user id is required.",
                nameof(createdByUserId));
        }

        if (scheduledStart >= scheduledEnd)
        {
            throw new ArgumentException(
                "Scheduled start must be before scheduled end.",
                nameof(scheduledStart));
        }

        return new Assessment(
            Guid.NewGuid(),
            organizationId,
            requestId,
            scheduledStart,
            scheduledEnd,
            createdByUserId);
    }
}
