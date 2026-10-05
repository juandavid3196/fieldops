namespace FieldOps.Domain.Requests;

public sealed class Assessment
{
    public const int PurposeMaxLength = 500;

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

    /// <summary>Why the visit is arranged (schedule-assessment BR-12); null for assessments created before the column existed.</summary>
    public string? Purpose { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Assessment Create(
        Guid organizationId,
        Guid requestId,
        DateTimeOffset scheduledStart,
        DateTimeOffset scheduledEnd,
        Guid createdByUserId,
        Guid? technicianId = null,
        string? purpose = null,
        string? internalNotes = null)
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
            createdByUserId)
        {
            TechnicianId = technicianId,
            Purpose = NormalizePurpose(purpose),
            InternalNotes = NormalizeNotes(internalNotes),
        };
    }

    /// <summary>Changes the slot, technician, purpose and internal notes of a scheduled assessment; the status is unchanged.</summary>
    public void Reschedule(
        DateTimeOffset scheduledStart,
        DateTimeOffset scheduledEnd,
        Guid? technicianId,
        string? purpose = null,
        string? internalNotes = null)
    {
        EnsureScheduled();

        if (scheduledStart >= scheduledEnd)
        {
            throw new ArgumentException(
                "Scheduled start must be before scheduled end.",
                nameof(scheduledStart));
        }

        ScheduledStart = scheduledStart;
        ScheduledEnd = scheduledEnd;
        TechnicianId = technicianId;
        Purpose = NormalizePurpose(purpose);
        InternalNotes = NormalizeNotes(internalNotes);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Cancel()
    {
        EnsureScheduled();
        Status = AssessmentStatus.Cancelled;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Complete()
    {
        EnsureScheduled();
        Status = AssessmentStatus.Completed;
        CompletedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CompletedAt.Value;
    }

    private static string? NormalizePurpose(string? purpose)
    {
        var value = string.IsNullOrWhiteSpace(purpose) ? null : purpose.Trim();

        if (value is { Length: > PurposeMaxLength })
        {
            throw new ArgumentException("Purpose is too long.", nameof(purpose));
        }

        return value;
    }

    private static string? NormalizeNotes(string? notes) => string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();

    private void EnsureScheduled()
    {
        if (Status != AssessmentStatus.Scheduled)
        {
            throw new InvalidOperationException("Only a scheduled assessment can change.");
        }
    }
}
