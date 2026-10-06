namespace FieldOps.Domain.WorkOrders;

/// <summary>Operational planning of a material for a work order (create-work-order BR-12): no price, never billable, no stock effect.</summary>
public sealed class WorkOrderPlannedMaterial
{
    public const decimal MaxQuantity = 99_999.999m;

    private WorkOrderPlannedMaterial()
    {
    }

    private WorkOrderPlannedMaterial(
        Guid id,
        Guid organizationId,
        Guid workOrderId,
        string description,
        decimal quantity,
        string unit,
        string source)
    {
        Id = id;
        OrganizationId = organizationId;
        WorkOrderId = workOrderId;
        Description = description;
        Quantity = quantity;
        Unit = unit;
        Source = source;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public Guid WorkOrderId { get; private set; }

    public Guid? QuoteLineId { get; private set; }

    public Guid? CatalogItemId { get; private set; }

    public string Description { get; private set; } = string.Empty;

    public decimal Quantity { get; private set; }

    public string Unit { get; private set; } = string.Empty;

    public string Source { get; private set; } = string.Empty;

    public int SortOrder { get; private set; }

    public static WorkOrderPlannedMaterial Create(
        Guid organizationId,
        Guid workOrderId,
        Guid? quoteLineId,
        Guid? catalogItemId,
        string description,
        decimal quantity,
        string unit,
        string source,
        int sortOrder)
    {
        if (organizationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Organization id is required.",
                nameof(organizationId));
        }

        if (workOrderId == Guid.Empty)
        {
            throw new ArgumentException(
                "Work order id is required.",
                nameof(workOrderId));
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException(
                "Description is required.",
                nameof(description));
        }

        if (quantity <= 0 || quantity > MaxQuantity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                quantity,
                "Quantity must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(unit))
        {
            throw new ArgumentException(
                "Unit is required.",
                nameof(unit));
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            throw new ArgumentException(
                "Source is required.",
                nameof(source));
        }

        return new WorkOrderPlannedMaterial(
            Guid.NewGuid(),
            organizationId,
            workOrderId,
            description.Trim(),
            quantity,
            unit.Trim(),
            source.Trim())
        {
            QuoteLineId = quoteLineId,
            CatalogItemId = catalogItemId,
            SortOrder = sortOrder,
        };
    }
}
