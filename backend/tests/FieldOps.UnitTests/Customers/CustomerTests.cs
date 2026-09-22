using FieldOps.Domain.Customers;

namespace FieldOps.UnitTests.Customers;

public class CustomerTests
{
    [Fact]
    public void Create_WithValidArguments_SetsDefaults()
    {
        var organizationId = Guid.NewGuid();

        var customer = Customer.Create(organizationId, CustomerType.Company, " Acme Corp ");

        Assert.NotEqual(Guid.Empty, customer.Id);
        Assert.Equal(organizationId, customer.OrganizationId);
        Assert.Equal(CustomerType.Company, customer.Type);
        Assert.Equal("Acme Corp", customer.DisplayName);
        Assert.True(customer.IsActive);
    }

    [Fact]
    public void Create_WithEmptyOrganizationId_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => Customer.Create(Guid.Empty, CustomerType.Person, "Jane Doe"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankDisplayName_Throws(string displayName)
    {
        Assert.Throws<ArgumentException>(
            () => Customer.Create(Guid.NewGuid(), CustomerType.Person, displayName));
    }
}
