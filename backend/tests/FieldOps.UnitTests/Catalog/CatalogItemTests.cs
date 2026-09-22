using FieldOps.Domain.Catalog;

namespace FieldOps.UnitTests.Catalog;

public class CatalogItemTests
{
    [Fact]
    public void Create_WithValidArguments_SetsDefaults()
    {
        var organizationId = Guid.NewGuid();

        var item = CatalogItem.Create(
            organizationId,
            CatalogItemType.Service,
            " Drain cleaning ",
            125.00m);

        Assert.NotEqual(Guid.Empty, item.Id);
        Assert.Equal(organizationId, item.OrganizationId);
        Assert.Equal(CatalogItemType.Service, item.Type);
        Assert.Equal("Drain cleaning", item.Name);
        Assert.Equal("unit", item.Unit);
        Assert.Equal(0m, item.UnitCost);
        Assert.Equal(125.00m, item.UnitPrice);
        Assert.Equal(0m, item.TaxRate);
        Assert.Null(item.CategoryId);
        Assert.True(item.IsActive);
    }

    [Fact]
    public void Create_WithEmptyOrganizationId_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => CatalogItem.Create(Guid.Empty, CatalogItemType.Product, "Filter", 10m));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankName_Throws(string name)
    {
        Assert.Throws<ArgumentException>(
            () => CatalogItem.Create(Guid.NewGuid(), CatalogItemType.Product, name, 10m));
    }

    [Fact]
    public void Create_WithNegativeUnitPrice_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CatalogItem.Create(Guid.NewGuid(), CatalogItemType.Product, "Filter", -1m));
    }
}
