namespace FieldOps.Domain.Technicians;

public static class TechnicianExceptionStatus
{
    public const string Active = "active";

    public const string Cancelled = "cancelled";
}

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
        Status = TechnicianExceptionStatus.Active;
    }

    public Guid Id { get; private set; }

    public Guid TechnicianId { get; private set; }

    public DateTimeOffset StartsAt { get; private set; }

    public DateTimeOffset EndsAt { get; private set; }

    public bool IsAvailable { get; private set; }

    public string? Reason { get; private set; }

    public string Status { get; private set; } = TechnicianExceptionStatus.Active;

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>The exception version; also its optimistic concurrency token.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsActive => Status == TechnicianExceptionStatus.Active;

    public static TechnicianException Create(
        Guid technicianId,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt,
        bool isAvailable = false,
        string? reason = null,
        DateTimeOffset? now = null)
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

        var stamp = now ?? DateTimeOffset.UtcNow;

        return new TechnicianException(
            Guid.NewGuid(),
            technicianId,
            startsAt,
            endsAt)
        {
            IsAvailable = isAvailable,
            Reason = reason,
            CreatedAt = stamp,
            UpdatedAt = stamp,
        };
    }

    /// <summary>Replaces the editable fields; false (nothing touched) when they are all unchanged.</summary>
    public bool Update(
        DateTimeOffset startsAt, DateTimeOffset endsAt, bool isAvailable, string reason, DateTimeOffset now)
    {
        if (startsAt >= endsAt)
        {
            throw new ArgumentException("Start date must be before end date.", nameof(startsAt));
        }

        if (StartsAt == startsAt && EndsAt == endsAt && IsAvailable == isAvailable && Reason == reason)
        {
            return false;
        }

        StartsAt = startsAt;
        EndsAt = endsAt;
        IsAvailable = isAvailable;
        Reason = reason;
        UpdatedAt = now;

        return true;
    }

    /// <summary>Cancels an active exception; false when it is already cancelled.</summary>
    public bool Cancel(DateTimeOffset now) => Move(TechnicianExceptionStatus.Cancelled, now);

    /// <summary>Activates a cancelled exception; false when it is already active.</summary>
    public bool Activate(DateTimeOffset now) => Move(TechnicianExceptionStatus.Active, now);

    private bool Move(string status, DateTimeOffset now)
    {
        if (Status == status)
        {
            return false;
        }

        Status = status;
        UpdatedAt = now;

        return true;
    }
}
