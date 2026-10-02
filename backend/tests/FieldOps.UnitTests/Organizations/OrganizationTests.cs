using FieldOps.Domain.Organizations;

namespace FieldOps.UnitTests.Organizations;

public class OrganizationTests
{
    [Fact]
    public void Create_WithValidValues_TrimsAndUppercasesAndSetsDefaults()
    {
        var organization = Organization.Create(
            " Acme Field Services ",
            " Acme Field Services LLC ",
            " 12-3456789 ",
            " ops@acme.com ",
            " +1 555 123 4567 ",
            " America/Chicago ",
            "usd",
            7.25m,
            "q",
            "wo",
            "inv",
            1, "acme");

        Assert.Equal("Acme Field Services", organization.Name);
        Assert.Equal("Acme Field Services LLC", organization.LegalName);
        Assert.Equal("12-3456789", organization.TaxId);
        Assert.Equal("ops@acme.com", organization.Email);
        Assert.Equal("+1 555 123 4567", organization.Phone);
        Assert.Equal("America/Chicago", organization.Timezone);
        Assert.Equal("usd", organization.Currency);
        Assert.Equal(1, organization.NextQuoteNumber);
        Assert.Equal(1, organization.NextWorkOrderNumber);
        Assert.Equal(1, organization.NextInvoiceNumber);
        Assert.True(organization.IsActive);
    }

    [Fact]
    public void Create_WithoutOptionalValues_LeavesThemNull()
    {
        var organization = Organization.Create(
            "Acme",
            null,
            null,
            null,
            null,
            "UTC",
            "USD",
            0m,
            "Q",
            "WO",
            "INV",
            1, "acme");

        Assert.Null(organization.LegalName);
        Assert.Null(organization.TaxId);
        Assert.Null(organization.Email);
        Assert.Null(organization.Phone);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_WithBlankName_Throws(string? name)
    {
        Assert.Throws<ArgumentException>(() => Organization.Create(
            name!, "Legal", null, null, null, "UTC", "USD", 0m, "Q", "WO", "INV", 1, "acme"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankTimezone_Throws(string timezone)
    {
        Assert.Throws<ArgumentException>(() => Organization.Create(
            "Acme", "Legal", null, null, null, timezone, "USD", 0m, "Q", "WO", "INV", 1, "acme"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankCurrency_Throws(string currency)
    {
        Assert.Throws<ArgumentException>(() => Organization.Create(
            "Acme", "Legal", null, null, null, "UTC", currency, 0m, "Q", "WO", "INV", 1, "acme"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankPrefixes_Throws(string prefix)
    {
        Assert.Throws<ArgumentException>(() => Organization.Create(
            "Acme", "Legal", null, null, null, "UTC", "USD", 0m, prefix, "WO", "INV", 1, "acme"));
        Assert.Throws<ArgumentException>(() => Organization.Create(
            "Acme", "Legal", null, null, null, "UTC", "USD", 0m, "Q", prefix, "INV", 1, "acme"));
        Assert.Throws<ArgumentException>(() => Organization.Create(
            "Acme", "Legal", null, null, null, "UTC", "USD", 0m, "Q", "WO", prefix, 1, "acme"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_WithNextInvoiceNumberBelowOne_Throws(long nextInvoiceNumber)
    {
        Assert.Throws<ArgumentException>(() => Organization.Create(
            "Acme", "Legal", null, null, null, "UTC", "USD", 0m, "Q", "WO", "INV", nextInvoiceNumber, "acme"));
    }

    [Fact]
    public void UpdateSettings_WithValidValues_TrimsAndSetsFieldsAndTimestamp()
    {
        var organization = Organization.Create(
            "Acme", "Legal", null, null, null, "UTC", "USD", 0m, "Q", "WO", "INV", 1, "acme");
        var updatedAt = DateTimeOffset.UtcNow.AddMinutes(5);

        organization.UpdateSettings(
            " Acme Renamed ",
            " Acme Renamed LLC ",
            " 98-7654321 ",
            " new-ops@acme.com ",
            " +1 555 999 8888 ",
            " America/New_York ",
            "eur",
            9.5m,
            "q2",
            "wo2",
            "inv2",
            42,
            7,
            8,
            " acme.com ",
            " 1 Main St ",
            " Austin ",
            "TX",
            " 78701 ",
            "US",
            true,
            updatedAt);

        Assert.Equal("Acme Renamed", organization.Name);
        Assert.Equal("acme.com", organization.Website);
        Assert.Equal("Austin", organization.City);
        Assert.Equal(7, organization.NextQuoteNumber);
        Assert.Equal(8, organization.NextWorkOrderNumber);
        Assert.True(organization.PricesIncludeTax);
        Assert.Equal("Acme Renamed LLC", organization.LegalName);
        Assert.Equal("new-ops@acme.com", organization.Email);
        Assert.Equal("eur", organization.Currency);
        Assert.Equal(42, organization.NextInvoiceNumber);
        Assert.Equal(updatedAt, organization.UpdatedAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void UpdateSettings_WithBlankName_Throws(string name)
    {
        var organization = Organization.Create(
            "Acme", "Legal", null, null, null, "UTC", "USD", 0m, "Q", "WO", "INV", 1, "acme");

        Assert.Throws<ArgumentException>(() => organization.UpdateSettings(
            name, "Legal", null, null, null, "UTC", "USD", 0m, "Q", "WO", "INV", 1, 1, 1, null, "1 Main St", "Austin", null, "78701", "US", false, DateTimeOffset.UtcNow));
    }
}
