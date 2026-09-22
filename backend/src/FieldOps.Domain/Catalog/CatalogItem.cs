namespace FieldOps.Domain.Catalog;

public sealed class CatalogItem
{
    private CatalogItem()
    {
    }

    private CatalogItem(
        Guid id,
        Guid organizationId,
        CatalogItemType type,
        string name,
        decimal unitPrice)
    {
        Id = id;
        OrganizationId = organizationId;
        Type = type;
        Name = name;
        Unit = "unit";
        UnitCost = 0m;
        UnitPrice = unitPrice;
        TaxRate = 0m;
        IsActive = true;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public Guid? CategoryId { get; private set; }

    public CatalogItemType Type { get; private set; }

    public string? Sku { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public string Unit { get; private set; } = "unit";

    public decimal UnitCost { get; private set; }

    public decimal UnitPrice { get; private set; }

    public decimal TaxRate { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static CatalogItem Create(
        Guid organizationId,
        CatalogItemType type,
        string name,
        decimal unitPrice)
    {
        if (organizationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Organization id is required.",
                nameof(organizationId));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "Catalog item name is required.",
                nameof(name));
        }

        if (unitPrice < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(unitPrice),
                unitPrice,
                "Unit price cannot be negative.");
        }

        return new CatalogItem(
            Guid.NewGuid(),
            organizationId,
            type,
            name.Trim(),
            unitPrice);
    }
}
