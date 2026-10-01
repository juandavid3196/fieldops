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

    // BR-07: every guard of set primary, archive and reactivate by state; a guarded call changes nothing.
    [Theory]
    [InlineData("active", "set-primary", PropertyTransition.Applied, true, true)]
    [InlineData("primary", "set-primary", PropertyTransition.AlreadyPrimary, true, true)]
    [InlineData("archived", "set-primary", PropertyTransition.NotActive, false, false)]
    [InlineData("active", "archive", PropertyTransition.Applied, false, false)]
    [InlineData("primary", "archive", PropertyTransition.PrimaryCannotBeArchived, true, true)]
    [InlineData("archived", "archive", PropertyTransition.AlreadyArchived, false, false)]
    [InlineData("archived", "reactivate", PropertyTransition.Applied, true, false)]
    [InlineData("active", "reactivate", PropertyTransition.AlreadyActive, true, false)]
    [InlineData("primary", "reactivate", PropertyTransition.AlreadyActive, true, true)]
    public void StateChange_AppliesOnlyWhenTheGuardAllows(
        string state, string action, PropertyTransition expected, bool isActive, bool isPrimary)
    {
        var created = DateTimeOffset.UtcNow.AddDays(-1);
        var property = Property.Create(Guid.NewGuid(), Guid.NewGuid(), "Site", "1 Main St", "Austin", "US", isPrimary: state == "primary");

        if (state == "archived")
        {
            Assert.Equal(PropertyTransition.Applied, property.Archive(created));
        }

        var stamp = property.UpdatedAt;
        var now = DateTimeOffset.UtcNow.AddMinutes(5);

        var result = action switch
        {
            "set-primary" => property.SetPrimary(now),
            "archive" => property.Archive(now),
            _ => property.Reactivate(now),
        };

        Assert.Equal(expected, result);
        Assert.Equal((isActive, isPrimary), (property.IsActive, property.IsPrimary));
        Assert.Equal(expected == PropertyTransition.Applied ? now : stamp, property.UpdatedAt);
    }

    // BR-05, AC-08: edit reports the names of the changed fields; a no-op reports none and leaves updated_at alone.
    [Fact]
    public void Edit_ReportsChangedFieldNamesAndNoOpKeepsUpdatedAt()
    {
        var branch = Guid.NewGuid();
        var property = Property.Create(Guid.NewGuid(), Guid.NewGuid(), "Site", "1 Main St", "Austin", "US", branch, "TX", "78701", "Gate");
        var stamp = property.UpdatedAt;
        var now = stamp.AddMinutes(10);

        Assert.Empty(property.Edit(" Site ", "1 Main St", " ", "Austin", "TX", "78701", branch, "Gate", now));
        Assert.Equal(stamp, property.UpdatedAt);

        var changed = property.Edit("New", "1 Main St", "Bay 2", "Dallas", null, "78701", Guid.NewGuid(), "Gate", now);

        Assert.Equal(["name", "addressLine2", "city", "stateRegion", "branchId"], changed);
        Assert.Equal(now, property.UpdatedAt);
        Assert.Equal(("New", "Bay 2", "Dallas", null), (property.Name, property.AddressLine2, property.City, property.StateRegion));
    }
}
