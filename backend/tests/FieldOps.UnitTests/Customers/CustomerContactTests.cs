using FieldOps.Domain.Customers;

namespace FieldOps.UnitTests.Customers;

public class CustomerContactTests
{
    [Fact]
    public void Create_WithValidArguments_SetsDefaults()
    {
        var organizationId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        var contact = CustomerContact.Create(organizationId, customerId, " Jane ");

        Assert.NotEqual(Guid.Empty, contact.Id);
        Assert.Equal(organizationId, contact.OrganizationId);
        Assert.Equal(customerId, contact.CustomerId);
        Assert.Equal("Jane", contact.FirstName);
        Assert.False(contact.IsPrimary);
        Assert.True(contact.IsActive);
        Assert.Null(contact.PortalUserId);
    }

    [Fact]
    public void Create_WithEmptyCustomerId_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => CustomerContact.Create(Guid.NewGuid(), Guid.Empty, "Jane"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankFirstName_Throws(string firstName)
    {
        Assert.Throws<ArgumentException>(
            () => CustomerContact.Create(Guid.NewGuid(), Guid.NewGuid(), firstName));
    }
}
