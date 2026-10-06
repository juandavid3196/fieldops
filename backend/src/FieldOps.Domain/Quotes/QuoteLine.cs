using FieldOps.Domain.Catalog;

namespace FieldOps.Domain.Quotes;

/// <summary>A snapshot line of a quote version: texts, prices, costs and amounts are independent of the catalog.</summary>
public sealed class QuoteLine
{
    private QuoteLine()
    {
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public Guid QuoteVersionId { get; private set; }

    public Guid? CatalogItemId { get; private set; }

    public CatalogItemType LineType { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    public decimal Quantity { get; private set; }

    public string Unit { get; private set; } = string.Empty;

    public decimal UnitCost { get; private set; }

    public decimal UnitPrice { get; private set; }

    public decimal TaxRate { get; private set; }

    public decimal LineSubtotal { get; private set; }

    public decimal LineTax { get; private set; }

    public decimal LineTotal { get; private set; }

    public int SortOrder { get; private set; }

    public bool IsOptional { get; private set; }

    public static QuoteLine Create(
        Guid organizationId,
        Guid quoteVersionId,
        Guid? catalogItemId,
        CatalogItemType lineType,
        string name,
        string? description,
        decimal quantity,
        string unit,
        decimal unitCost,
        decimal unitPrice,
        decimal taxRate,
        decimal lineSubtotal,
        decimal lineTax,
        decimal lineTotal,
        int sortOrder,
        bool isOptional)
    {
        if (organizationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Organization id is required.",
                nameof(organizationId));
        }

        if (quoteVersionId == Guid.Empty)
        {
            throw new ArgumentException(
                "Quote version id is required.",
                nameof(quoteVersionId));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "Quote line name is required.",
                nameof(name));
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
                "Quote line unit is required.",
                nameof(unit));
        }

        if (unitPrice < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(unitPrice),
                unitPrice,
                "Unit price cannot be negative.");
        }

        if (unitCost < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(unitCost),
                unitCost,
                "Unit cost cannot be negative.");
        }

        return new QuoteLine
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            QuoteVersionId = quoteVersionId,
            CatalogItemId = catalogItemId,
            LineType = lineType,
            Name = name.Trim(),
            Description = description?.Trim() ?? string.Empty,
            Quantity = quantity,
            Unit = unit.Trim(),
            UnitCost = unitCost,
            UnitPrice = unitPrice,
            TaxRate = taxRate,
            LineSubtotal = lineSubtotal,
            LineTax = lineTax,
            LineTotal = lineTotal,
            SortOrder = sortOrder,
            IsOptional = isOptional,
        };
    }
}
