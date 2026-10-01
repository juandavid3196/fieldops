using FieldOps.Application.Features.Customers;

namespace FieldOps.UnitTests.Customers;

public class CustomerActivityActionsTests
{
    // BR-17: only the ten whitelisted actions may reach the Activity tab, and only property actions carry a property name.
    [Theory]
    [InlineData("customer.created", true, false)]
    [InlineData("customer.updated", true, false)]
    [InlineData("customer.archived", true, false)]
    [InlineData("customer.reactivated", true, false)]
    [InlineData("property.created", true, true)]
    [InlineData("property.updated", true, true)]
    [InlineData("property.primary_changed", true, true)]
    [InlineData("property.archived", true, true)]
    [InlineData("property.reactivated", true, true)]
    [InlineData("customer_note.created", true, false)]
    [InlineData("customer.imported", false, false)]
    [InlineData("customer_tag.created", false, false)]
    [InlineData("property.deleted", false, false)]
    public void Actions_AreWhitelistedAndNameAPropertyOnlyForPropertyActions(string action, bool whitelisted, bool namesProperty)
    {
        Assert.Equal(whitelisted, CustomerActivityActions.IsWhitelisted(action));
        Assert.Equal(namesProperty, CustomerActivityActions.NamesProperty(action) && whitelisted);
    }
}
