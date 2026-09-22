using FieldOps.Domain.Catalog;

namespace FieldOps.Domain.Quotes;

public sealed class QuoteLine
{
    private QuoteLine()
    {
    }

    private QuoteLine(
        Guid id,
        Guid quoteVersionId,
        CatalogItemType lineType,
        string description,
        decimal quantity,
        string unit,
        decimal unitPrice,
        decimal lineSubtotal,
        decimal lineTax,
        decimal lineTotal)
    {
        Id = id;
        QuoteVersionId = quoteVersionId;
        LineType = lineType;
        Description = description;
        Quantity = quantity;
        Unit = unit;
        UnitCost = 0m;
        UnitPrice = unitPrice;
        TaxRate = 0m;
        LineSubtotal = lineSubtotal;
        LineTax = lineTax;
        LineTotal = lineTotal;
        SortOrder = 0;
    }

    public Guid Id { get; private set; }

    public Guid QuoteVersionId { get; private set; }

    public Guid? CatalogItemId { get; private set; }

    public CatalogItemType LineType { get; private set; }

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

    public static QuoteLine Create(
        Guid quoteVersionId,
        CatalogItemType lineType,
        string description,
        decimal quantity,
        string unit,
        decimal unitPrice,
        decimal lineSubtotal,
        decimal lineTax,
        decimal lineTotal)
    {
        if (quoteVersionId == Guid.Empty)
        {
            throw new ArgumentException(
                "Quote version id is required.",
                nameof(quoteVersionId));
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException(
                "Quote line description is required.",
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

        return new QuoteLine(
            Guid.NewGuid(),
            quoteVersionId,
            lineType,
            description.Trim(),
            quantity,
            unit.Trim(),
            unitPrice,
            lineSubtotal,
            lineTax,
            lineTotal);
    }
}
