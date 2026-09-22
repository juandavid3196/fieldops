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
        Guid createdByUserId)
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
        Priority = 3;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
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
        Guid createdByUserId)
    {
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
            createdByUserId);
    }
}
