namespace FieldOps.Domain.WorkOrders;

public sealed class VisitMaterial
{
    private VisitMaterial()
    {
    }

    private VisitMaterial(
        Guid id,
        Guid visitId,
        string description,
        decimal quantity,
        string unit)
    {
        Id = id;
        VisitId = visitId;
        Description = description;
        Quantity = quantity;
        Unit = unit;
        UnitCost = 0m;
        Billable = false;
    }

    public Guid Id { get; private set; }

    public Guid VisitId { get; private set; }

    public Guid? CatalogItemId { get; private set; }

    // The planned material this row records the used quantity of (mobile-job-progress SA-02); null for additional materials.
    public Guid? PlannedMaterialId { get; private set; }

    // Snapshotted independently from CatalogItem: kept even if the catalog
    // item's description, unit or cost changes later.
    public string Description { get; private set; } = string.Empty;

    public decimal Quantity { get; private set; }

    public string Unit { get; private set; } = string.Empty;

    public decimal UnitCost { get; private set; }

    public bool Billable { get; private set; }

    public static VisitMaterial Create(
        Guid visitId,
        string description,
        decimal quantity,
        string unit,
        Guid? catalogItemId = null,
        decimal unitCost = 0m,
        Guid? plannedMaterialId = null)
    {
        if (visitId == Guid.Empty)
        {
            throw new ArgumentException(
                "Visit id is required.",
                nameof(visitId));
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException(
                "Material description is required.",
                nameof(description));
        }

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                quantity,
                "Quantity must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(unit))
        {
            throw new ArgumentException(
                "Material unit is required.",
                nameof(unit));
        }

        // Version 7 ids sort by creation time, which is the order of the additional materials (no created_at column).
        return new VisitMaterial(
            Guid.CreateVersion7(),
            visitId,
            description.Trim(),
            quantity,
            unit.Trim())
        {
            CatalogItemId = catalogItemId,
            UnitCost = unitCost,
            PlannedMaterialId = plannedMaterialId,
        };
    }

    public void SetQuantity(decimal quantity)
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                quantity,
                "Quantity must be greater than zero.");
        }

        Quantity = quantity;
    }
}
