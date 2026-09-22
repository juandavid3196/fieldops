using FieldOps.Domain.Customers;

namespace FieldOps.UnitTests.Customers;

public class PropertyTests
{
    [Fact]
    public void Create_WithValidArguments_SetsDefaults()
    {
        var organizationId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        var property = Property.Create(
            organizationId,
            customerId,
            " Main Warehouse ",
            " 123 Main St ",
            " Springfield ",
            " us ");

        Assert.NotEqual(Guid.Empty, property.Id);
        Assert.Equal(organizationId, property.OrganizationId);
        Assert.Equal(customerId, property.CustomerId);
        Assert.Equal("Main Warehouse", property.Name);
        Assert.Equal("123 Main St", property.AddressLine1);
        Assert.Equal("Springfield", property.City);
        Assert.Equal("us", property.CountryCode);
        Assert.Null(property.BranchId);
        Assert.True(property.IsActive);
    }

    [Theory]
    [InlineData("", "123 Main St", "Springfield", "US")]
    [InlineData("Name", "", "Springfield", "US")]
    [InlineData("Name", "123 Main St", "", "US")]
    [InlineData("Name", "123 Main St", "Springfield", "")]
    public void Create_WithMissingRequiredField_Throws(
        string name,
        string addressLine1,
        string city,
        string countryCode)
    {
        Assert.Throws<ArgumentException>(
            () => Property.Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                name,
                addressLine1,
                city,
                countryCode));
    }
}
