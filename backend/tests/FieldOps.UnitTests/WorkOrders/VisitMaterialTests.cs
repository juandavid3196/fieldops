using FieldOps.Domain.WorkOrders;

namespace FieldOps.UnitTests.WorkOrders;

public class VisitMaterialTests
{
    [Fact]
    public void Create_WithValidArguments_SetsDefaults()
    {
        var visitId = Guid.NewGuid();

        var material = VisitMaterial.Create(visitId, " Copper pipe ", 3, " ft ");

        Assert.NotEqual(Guid.Empty, material.Id);
        Assert.Equal(visitId, material.VisitId);
        Assert.Equal("Copper pipe", material.Description);
        Assert.Equal(3, material.Quantity);
        Assert.Equal("ft", material.Unit);
        Assert.Equal(0m, material.UnitCost);
        Assert.False(material.Billable);
        Assert.Null(material.CatalogItemId);
    }

    [Fact]
    public void Create_WithNonPositiveQuantity_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => VisitMaterial.Create(Guid.NewGuid(), "Description", 0, "ft"));
    }

    [Theory]
    [InlineData("", "ft")]
    [InlineData("Description", "")]
    public void Create_WithBlankRequiredField_Throws(string description, string unit)
    {
        Assert.Throws<ArgumentException>(
            () => VisitMaterial.Create(Guid.NewGuid(), description, 1, unit));
    }
}
