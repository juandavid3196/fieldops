namespace FieldOps.Domain.WorkOrders;

public sealed class WorkOrder
{
    private WorkOrder()
    {
    }

    private WorkOrder(
        Guid id,
        Guid organizationId,
        Guid branchId,
        long workOrderNumber,
        Guid quoteVersionId,
        Guid customerId,
        Guid propertyId,
        string scopeSnapshot,
        Guid createdByUserId,
        WorkOrderFields fields)
    {
        Id = id;
        OrganizationId = organizationId;
        BranchId = branchId;
        WorkOrderNumber = workOrderNumber;
        QuoteVersionId = quoteVersionId;
        CustomerId = customerId;
        PropertyId = propertyId;
        ScopeSnapshot = scopeSnapshot;
        CreatedByUserId = createdByUserId;
        Status = WorkOrderStatus.Draft;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = Truncate(DateTimeOffset.UtcNow);
        Apply(fields);
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public Guid BranchId { get; private set; }

    public long WorkOrderNumber { get; private set; }

    public Guid QuoteVersionId { get; private set; }

    public Guid CustomerId { get; private set; }

    public Guid PropertyId { get; private set; }

    public WorkOrderStatus Status { get; private set; }

    public short Priority { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string JobType { get; private set; } = WorkOrderJobTypes.OneTime;

    public Guid ServiceCategoryId { get; private set; }

    public int? EstimatedDurationMinutes { get; private set; }

    public string? RecurrenceFrequency { get; private set; }

    public short? RecurrenceCount { get; private set; }

    public bool NotifyCustomerWhenScheduled { get; private set; }

    public bool SendTechnicianDetails { get; private set; }

    public bool SendArrivalReminder { get; private set; }

    public string ScopeSnapshot { get; private set; } = string.Empty;

    public string? InternalInstructions { get; private set; }

    public DateTimeOffset? PreferredStart { get; private set; }

    public DateTimeOffset? PreferredEnd { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static WorkOrder Create(
        Guid organizationId,
        Guid branchId,
        long workOrderNumber,
        Guid quoteVersionId,
        Guid customerId,
        Guid propertyId,
        string scopeSnapshot,
        Guid createdByUserId,
        WorkOrderFields fields)
    {
        ArgumentNullException.ThrowIfNull(fields);

        if (organizationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Organization id is required.",
                nameof(organizationId));
        }

        if (branchId == Guid.Empty)
        {
            throw new ArgumentException(
                "Branch id is required.",
                nameof(branchId));
        }

        if (workOrderNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(workOrderNumber),
                workOrderNumber,
                "Work order number must be greater than zero.");
        }

        if (quoteVersionId == Guid.Empty)
        {
            throw new ArgumentException(
                "Quote version id is required.",
                nameof(quoteVersionId));
        }

        if (customerId == Guid.Empty)
        {
            throw new ArgumentException(
                "Customer id is required.",
                nameof(customerId));
        }

        if (propertyId == Guid.Empty)
        {
            throw new ArgumentException(
                "Property id is required.",
                nameof(propertyId));
        }

        if (string.IsNullOrWhiteSpace(scopeSnapshot))
        {
            throw new ArgumentException(
                "Scope snapshot is required.",
                nameof(scopeSnapshot));
        }

        if (createdByUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "Created by user id is required.",
                nameof(createdByUserId));
        }

        return new WorkOrder(
            Guid.NewGuid(),
            organizationId,
            branchId,
            workOrderNumber,
            quoteVersionId,
            customerId,
            propertyId,
            scopeSnapshot.Trim(),
            createdByUserId,
            fields);
    }

    /// <summary>Replaces every editable field of a draft (create-work-order BR-16) and sets a new concurrency value.</summary>
    public void ReplaceDraft(Guid branchId, WorkOrderFields fields, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(fields);

        if (Status != WorkOrderStatus.Draft)
        {
            throw new InvalidOperationException("Only a draft work order can be replaced.");
        }

        if (branchId == Guid.Empty)
        {
            throw new ArgumentException(
                "Branch id is required.",
                nameof(branchId));
        }

        BranchId = branchId;
        Apply(fields);
        Touch(now);
    }

    /// <summary>The draft becomes executable and waits to be scheduled (create-work-order BR-18).</summary>
    public void MarkReady(DateTimeOffset now)
    {
        if (Status != WorkOrderStatus.Draft)
        {
            throw new InvalidOperationException("Only a draft work order can become ready to schedule.");
        }

        Status = WorkOrderStatus.ReadyToSchedule;
        Touch(now);
    }

    /// <summary>A visit gained a schedule (dispatch-calendar BR-16): ready to schedule becomes scheduled.</summary>
    public void MarkScheduled(DateTimeOffset now)
    {
        if (Status != WorkOrderStatus.ReadyToSchedule)
        {
            throw new InvalidOperationException("Only a ready to schedule work order can become scheduled.");
        }

        Status = WorkOrderStatus.Scheduled;
        Touch(now);
    }

    /// <summary>A visit started its job (mobile-job-progress BR-04): a scheduled work order becomes in progress.</summary>
    public void MarkInProgress(DateTimeOffset now)
    {
        if (Status != WorkOrderStatus.Scheduled)
        {
            throw new InvalidOperationException("Only a scheduled work order can become in progress.");
        }

        Status = WorkOrderStatus.InProgress;
        Touch(now);
    }

    /// <summary>No non-cancelled visit keeps a schedule (dispatch-calendar BR-16).</summary>
    public void ReturnToReadyToSchedule(DateTimeOffset now)
    {
        if (Status != WorkOrderStatus.Scheduled)
        {
            throw new InvalidOperationException("Only a scheduled work order can return to ready to schedule.");
        }

        Status = WorkOrderStatus.ReadyToSchedule;
        Touch(now);
    }

    /// <summary>Sets a new concurrency value, truncated to microseconds (the PostgreSQL precision) and always different from the previous one.</summary>
    public void Touch(DateTimeOffset now)
    {
        var next = Truncate(now);

        if (next <= UpdatedAt)
        {
            next = UpdatedAt.AddTicks(10);
        }

        UpdatedAt = next;
    }

    private static DateTimeOffset Truncate(DateTimeOffset value)
    {
        var utc = value.UtcDateTime;

        return new DateTimeOffset(utc.Ticks - (utc.Ticks % 10), TimeSpan.Zero);
    }

    private void Apply(WorkOrderFields fields)
    {
        Title = fields.Title.Trim();
        JobType = fields.JobType;
        ServiceCategoryId = fields.ServiceCategoryId;
        Priority = fields.Priority;
        EstimatedDurationMinutes = fields.EstimatedDurationMinutes;
        RecurrenceFrequency = fields.RecurrenceFrequency;
        RecurrenceCount = fields.RecurrenceCount;
        InternalInstructions = string.IsNullOrWhiteSpace(fields.Instructions) ? null : fields.Instructions.Trim();
        PreferredStart = fields.PreferredStart;
        PreferredEnd = fields.PreferredEnd;
        NotifyCustomerWhenScheduled = fields.NotifyCustomerWhenScheduled;
        SendTechnicianDetails = fields.SendTechnicianDetails;
        SendArrivalReminder = fields.SendArrivalReminder;
    }
}

public static class WorkOrderJobTypes
{
    public const string OneTime = "one_time";

    public const string Recurring = "recurring";
}

/// <summary>The editable fields of a work order, already validated by the application layer.</summary>
public sealed record WorkOrderFields(
    string Title,
    string JobType,
    Guid ServiceCategoryId,
    short Priority,
    int? EstimatedDurationMinutes,
    string? RecurrenceFrequency,
    short? RecurrenceCount,
    string? Instructions,
    DateTimeOffset? PreferredStart,
    DateTimeOffset? PreferredEnd,
    bool NotifyCustomerWhenScheduled,
    bool SendTechnicianDetails,
    bool SendArrivalReminder);
