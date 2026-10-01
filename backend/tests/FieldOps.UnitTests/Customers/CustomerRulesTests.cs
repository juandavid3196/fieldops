using FieldOps.Application.Features.Customers;
using FieldOps.Domain.Customers;

namespace FieldOps.UnitTests.Customers;

public class CustomerRulesTests
{
    [Theory]
    [InlineData("5125557832", "5125557832")]
    [InlineData("(512) 555-7832", "5125557832")]
    [InlineData("+1 512.555.7832", "5125557832")]
    [InlineData("15125557832", "5125557832")]
    [InlineData("+44 20 7946 0958", "442079460958")]
    [InlineData("  ", null)]
    [InlineData(null, null)]
    public void TryNormalizePhone_ValidInput_ReturnsDigitsOnly(string? raw, string? expected)
    {
        Assert.True(CustomerNormalizer.TryNormalizePhone(raw, out var digits));
        Assert.Equal(expected, digits);
    }

    [Theory]
    [InlineData("123456789")]
    [InlineData("1234567890123456")]
    [InlineData("512-555-78x2")]
    [InlineData("512+5557832")]
    [InlineData("++15125557832")]
    public void TryNormalizePhone_InvalidInput_ReturnsFalse(string raw) =>
        Assert.False(CustomerNormalizer.TryNormalizePhone(raw, out _));

    [Theory]
    [InlineData("  Ada@Example.COM ", "ada@example.com", true)]
    [InlineData("ada@example", "ada@example", false)]
    [InlineData("ada example@x.co", "ada example@x.co", false)]
    public void Email_NormalizesAndValidates(string raw, string expected, bool valid)
    {
        var normalized = CustomerNormalizer.NormalizeEmail(raw);

        Assert.Equal(expected, normalized);
        Assert.Equal(valid, CustomerNormalizer.IsValidEmail(normalized));
        Assert.Equal("(512) 555-7832", CustomerNormalizer.FormatPhone("5125557832"));
    }

    [Theory]
    [InlineData(true, "tx", "78701-1234", true)]
    [InlineData(true, "ZZ", null, false)]
    [InlineData(true, null, "7870", false)]
    [InlineData(false, "Ontario", "M5V 2T6", true)]
    [InlineData(false, "LONG", null, false)]
    [InlineData(false, null, "01234567890123456789012345678901", false)]
    public void Validate_StateAndPostalCodeFollowTheOrganizationCountry(
        bool unitedStates, string? state, string? postal, bool valid)
    {
        state = state == "LONG" ? new string('s', 101) : state;
        var values = CustomerRules.Validate(Input(state, postal), null, unitedStates, out var errors);

        Assert.Equal(valid, values is not null);
        Assert.Equal(!valid, errors.ContainsKey(CustomerRules.StateRegionKey) || errors.ContainsKey(CustomerRules.PostalCodeKey));

        if (values is { StateRegion: not null } && unitedStates)
        {
            Assert.Equal("TX", values.StateRegion);
        }
    }

    [Fact]
    public void Validate_EditKeepsTheTypeAndResidentialIgnoresCompanyAndTitle()
    {
        var residential = CustomerRules.Validate(
            Input("TX", null) with { Type = "commercial", CompanyName = "Ignored", Title = "Boss" },
            CustomerType.Person,
            true,
            out _);

        Assert.NotNull(residential);
        Assert.Equal(CustomerType.Person, residential.Type);
        Assert.Equal("Ada Lovelace", residential.DisplayName);
        Assert.Null(residential.CompanyName);
        Assert.Null(residential.Title);

        var commercial = CustomerRules.Validate(
            Input("TX", null) with { CompanyName = " Acme ", Title = " Boss " }, CustomerType.Company, true, out _);

        Assert.Equal("Acme", commercial!.DisplayName);
        Assert.Equal("Boss", commercial.Title);
    }

    [Fact]
    public void Status_LifecycleAndDisplayFollowPrecedence()
    {
        Assert.Equal("lead", CustomerStatus.Lifecycle(true, false));
        Assert.Equal("active", CustomerStatus.Lifecycle(true, true));
        Assert.Equal("archived", CustomerStatus.Lifecycle(false, true));
        Assert.Equal("lead", CustomerStatus.Display(true, false, false));
        Assert.Equal("active", CustomerStatus.Display(true, true, false));
        Assert.Equal("overdue", CustomerStatus.Display(true, false, true));
        Assert.Equal("archived", CustomerStatus.Display(false, true, true));
    }

    [Fact]
    public void MonthBounds_UseTheOrganizationTimeZoneAndFallBackToUtc()
    {
        var now = new DateTimeOffset(2026, 3, 1, 3, 0, 0, TimeSpan.Zero);

        // 03:00 UTC on March 1 is still February 28 in Chicago (UTC-6 before the March 8 DST change).
        var (start, next) = CustomerMonth.Bounds("America/Chicago", now);
        Assert.Equal(new DateTimeOffset(2026, 2, 1, 6, 0, 0, TimeSpan.Zero), start);
        Assert.Equal(new DateTimeOffset(2026, 3, 1, 6, 0, 0, TimeSpan.Zero), next);

        var (utcStart, _) = CustomerMonth.Bounds("Not/AZone", now);
        Assert.Equal(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero), utcStart);
    }

    [Fact]
    public void CustomerContact_RequiresAtLeastOnePreferredChannel()
    {
        var contact = CustomerContact.CreatePrimary(Guid.NewGuid(), Guid.NewGuid(), "Ada", "L", "a@b.co", null, null, false, true);

        Assert.True(contact.IsPrimary);
        Assert.Throws<ArgumentException>(
            () => CustomerContact.CreatePrimary(Guid.NewGuid(), Guid.NewGuid(), "Ada", "L", "a@b.co", null, null, false, false));
        Assert.Throws<ArgumentException>(
            () => contact.Update("Ada", "L", "a@b.co", null, null, false, false, DateTimeOffset.UtcNow));
        Assert.False(contact.Update("Ada", "L", "a@b.co", null, null, false, true, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Customer_UpdateAndSetActiveReportChangesOnly()
    {
        var branchId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow.AddDays(1);
        var customer = Customer.Create(Guid.NewGuid(), branchId, CustomerType.Person, "Ada Lovelace", "a@b.co");

        Assert.False(customer.Update(branchId, CustomerType.Person, " Ada Lovelace ", "a@b.co", null, " ", now));
        Assert.True(customer.Update(Guid.NewGuid(), CustomerType.Person, "Ada Lovelace", "a@b.co", null, null, now));
        Assert.True(customer.Update(customer.BranchId, CustomerType.Company, "Ada Lovelace", "a@b.co", null, null, now));
        Assert.Equal(CustomerType.Company, customer.Type);
        Assert.Equal(now, customer.UpdatedAt);
        Assert.False(customer.SetActive(true, now));
        Assert.True(customer.SetActive(false, now));
        Assert.False(customer.IsActive);
        Assert.Equal("gold", CustomerTag.NormalizeName("  Gold "));
    }

    private static CustomerInput Input(string? state, string? postal) =>
        new(
            "residential", null, "Ada", "Lovelace", null, "ada@example.com", null, true, false,
            "1 Main St", "Austin", state, postal, null, null);
}
