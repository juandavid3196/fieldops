using FieldOps.Domain.Catalog;

namespace FieldOps.UnitTests.Catalog;

public class ServiceCategoryTests
{
    [Fact]
    public void Create_WithValidArguments_SetsDefaults()
    {
        var organizationId = Guid.NewGuid();

        var category = ServiceCategory.Create(organizationId, " HVAC ");

        Assert.NotEqual(Guid.Empty, category.Id);
        Assert.Equal(organizationId, category.OrganizationId);
        Assert.Equal("HVAC", category.Name);
        Assert.True(category.IsActive);
    }

    [Fact]
    public void Create_WithEmptyOrganizationId_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => ServiceCategory.Create(Guid.Empty, "HVAC"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankName_Throws(string name)
    {
        Assert.Throws<ArgumentException>(
            () => ServiceCategory.Create(Guid.NewGuid(), name));
    }
}
