using FieldOps.Domain.Invoices;

namespace FieldOps.UnitTests.Invoices;

public class InvoiceLineTests
{
    [Fact]
    public void Create_WithValidArguments_SetsDefaults()
    {
        var invoiceId = Guid.NewGuid();
        var sourceQuoteLineId = Guid.NewGuid();

        var line = InvoiceLine.Create(
            invoiceId,
            " Drain cleaning ",
            2,
            " hour ",
            75m,
            150m,
            15m,
            165m,
            sourceQuoteLineId: sourceQuoteLineId);

        Assert.NotEqual(Guid.Empty, line.Id);
        Assert.Equal(invoiceId, line.InvoiceId);
        Assert.Equal("Drain cleaning", line.Description);
        Assert.Equal(2, line.Quantity);
        Assert.Equal("hour", line.Unit);
        Assert.Equal(75m, line.UnitPrice);
        Assert.Equal(0m, line.TaxRate);
        Assert.Equal(150m, line.LineSubtotal);
        Assert.Equal(15m, line.LineTax);
        Assert.Equal(165m, line.LineTotal);
        Assert.Equal(0, line.SortOrder);
        Assert.Equal(sourceQuoteLineId, line.SourceQuoteLineId);
        Assert.Null(line.SourceVisitMaterialId);
    }

    [Fact]
    public void Create_WithNonPositiveQuantity_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => InvoiceLine.Create(
                Guid.NewGuid(),
                "Description",
                0,
                "hour",
                75m,
                0m,
                0m,
                0m));
    }

    [Fact]
    public void Create_WithNegativeUnitPrice_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => InvoiceLine.Create(
                Guid.NewGuid(),
                "Description",
                1,
                "hour",
                -1m,
                0m,
                0m,
                0m));
    }
}
