namespace FieldOps.Domain.Invoices;

public sealed class InvoiceLine
{
    private InvoiceLine()
    {
    }

    private InvoiceLine(
        Guid id,
        Guid invoiceId,
        string description,
        decimal quantity,
        string unit,
        decimal unitPrice,
        decimal lineSubtotal,
        decimal lineTax,
        decimal lineTotal)
    {
        Id = id;
        InvoiceId = invoiceId;
        Description = description;
        Quantity = quantity;
        Unit = unit;
        UnitPrice = unitPrice;
        TaxRate = 0m;
        LineSubtotal = lineSubtotal;
        LineTax = lineTax;
        LineTotal = lineTotal;
        SortOrder = 0;
    }

    public Guid Id { get; private set; }

    public Guid InvoiceId { get; private set; }

    public Guid? SourceQuoteLineId { get; private set; }

    public Guid? SourceVisitMaterialId { get; private set; }

    // Snapshotted independently from its source: kept even if the source
    // QuoteLine or VisitMaterial changes or is removed later.
    public string Description { get; private set; } = string.Empty;

    public decimal Quantity { get; private set; }

    public string Unit { get; private set; } = string.Empty;

    public decimal UnitPrice { get; private set; }

    public decimal TaxRate { get; private set; }

    public decimal LineSubtotal { get; private set; }

    public decimal LineTax { get; private set; }

    public decimal LineTotal { get; private set; }

    public int SortOrder { get; private set; }

    public static InvoiceLine Create(
        Guid invoiceId,
        string description,
        decimal quantity,
        string unit,
        decimal unitPrice,
        decimal lineSubtotal,
        decimal lineTax,
        decimal lineTotal,
        Guid? sourceQuoteLineId = null,
        Guid? sourceVisitMaterialId = null)
    {
        if (invoiceId == Guid.Empty)
        {
            throw new ArgumentException(
                "Invoice id is required.",
                nameof(invoiceId));
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException(
                "Invoice line description is required.",
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
                "Invoice line unit is required.",
                nameof(unit));
        }

        if (unitPrice < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(unitPrice),
                unitPrice,
                "Unit price cannot be negative.");
        }

        return new InvoiceLine(
            Guid.NewGuid(),
            invoiceId,
            description.Trim(),
            quantity,
            unit.Trim(),
            unitPrice,
            lineSubtotal,
            lineTax,
            lineTotal)
        {
            SourceQuoteLineId = sourceQuoteLineId,
            SourceVisitMaterialId = sourceVisitMaterialId,
        };
    }
}
