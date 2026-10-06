using FieldOps.Domain.Catalog;
using FieldOps.Domain.Quotes;

namespace FieldOps.UnitTests.Quotes;

public class QuoteLineTests
{
    private static QuoteLine Create(
        string name = "Drain cleaning",
        string? description = "Clear the main drain",
        decimal quantity = 2,
        string unit = "hour",
        decimal unitCost = 40m,
        decimal unitPrice = 75m) =>
        QuoteLine.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            CatalogItemType.Service,
            name,
            description,
            quantity,
            unit,
            unitCost,
            unitPrice,
            8.25m,
            150m,
            12.38m,
            162.38m,
            3,
            true);

    [Fact]
    public void Create_WithValidArguments_SetsSnapshotFields()
    {
        var organizationId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var catalogItemId = Guid.NewGuid();

        var line = QuoteLine.Create(
            organizationId,
            versionId,
            catalogItemId,
            CatalogItemType.Product,
            " Copper pipe ",
            " 3/4 inch ",
            2,
            " m ",
            4m,
            9.5m,
            8.25m,
            19m,
            1.57m,
            20.57m,
            3,
            true);

        Assert.NotEqual(Guid.Empty, line.Id);
        Assert.Equal(organizationId, line.OrganizationId);
        Assert.Equal(versionId, line.QuoteVersionId);
        Assert.Equal(catalogItemId, line.CatalogItemId);
        Assert.Equal(CatalogItemType.Product, line.LineType);
        Assert.Equal("Copper pipe", line.Name);
        Assert.Equal("3/4 inch", line.Description);
        Assert.Equal("m", line.Unit);
        Assert.Equal(4m, line.UnitCost);
        Assert.Equal(8.25m, line.TaxRate);
        Assert.Equal(3, line.SortOrder);
        Assert.True(line.IsOptional);
    }

    [Fact]
    public void Create_WithoutDescription_StoresEmptyText()
    {
        Assert.Equal(string.Empty, Create(description: null).Description);
    }

    [Theory]
    [InlineData("", "hour", 1, 1, 1)]
    [InlineData("   ", "hour", 1, 1, 1)]
    [InlineData("Name", "", 1, 1, 1)]
    [InlineData("Name", "hour", 0, 1, 1)]
    [InlineData("Name", "hour", 1, -1, 1)]
    [InlineData("Name", "hour", 1, 1, -1)]
    public void Create_WithInvalidField_Throws(string name, string unit, decimal quantity, decimal unitPrice, decimal unitCost)
    {
        Assert.ThrowsAny<ArgumentException>(
            () => Create(name: name, unit: unit, quantity: quantity, unitPrice: unitPrice, unitCost: unitCost));
    }
}
