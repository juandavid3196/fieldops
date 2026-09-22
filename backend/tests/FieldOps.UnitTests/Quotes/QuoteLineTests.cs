using FieldOps.Domain.Catalog;
using FieldOps.Domain.Quotes;

namespace FieldOps.UnitTests.Quotes;

public class QuoteLineTests
{
    [Fact]
    public void Create_WithValidArguments_SetsDefaults()
    {
        var quoteVersionId = Guid.NewGuid();

        var line = QuoteLine.Create(
            quoteVersionId,
            CatalogItemType.Service,
            " Drain cleaning ",
            2,
            " hour ",
            75m,
            150m,
            15m,
            165m);

        Assert.NotEqual(Guid.Empty, line.Id);
        Assert.Equal(quoteVersionId, line.QuoteVersionId);
        Assert.Equal(CatalogItemType.Service, line.LineType);
        Assert.Equal("Drain cleaning", line.Description);
        Assert.Equal(2, line.Quantity);
        Assert.Equal("hour", line.Unit);
        Assert.Equal(0m, line.UnitCost);
        Assert.Equal(75m, line.UnitPrice);
        Assert.Equal(0m, line.TaxRate);
        Assert.Equal(150m, line.LineSubtotal);
        Assert.Equal(15m, line.LineTax);
        Assert.Equal(165m, line.LineTotal);
        Assert.Equal(0, line.SortOrder);
        Assert.Null(line.CatalogItemId);
    }

    [Fact]
    public void Create_WithNonPositiveQuantity_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => QuoteLine.Create(
                Guid.NewGuid(),
                CatalogItemType.Service,
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
            () => QuoteLine.Create(
                Guid.NewGuid(),
                CatalogItemType.Service,
                "Description",
                1,
                "hour",
                -1m,
                0m,
                0m,
                0m));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankDescription_Throws(string description)
    {
        Assert.Throws<ArgumentException>(
            () => QuoteLine.Create(
                Guid.NewGuid(),
                CatalogItemType.Service,
                description,
                1,
                "hour",
                75m,
                75m,
                0m,
                75m));
    }
}
