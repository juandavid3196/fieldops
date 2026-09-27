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
            1);

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
            1);

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
            name!, "Legal", null, null, null, "UTC", "USD", 0m, "Q", "WO", "INV", 1));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankTimezone_Throws(string timezone)
    {
        Assert.Throws<ArgumentException>(() => Organization.Create(
            "Acme", "Legal", null, null, null, timezone, "USD", 0m, "Q", "WO", "INV", 1));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankCurrency_Throws(string currency)
    {
        Assert.Throws<ArgumentException>(() => Organization.Create(
            "Acme", "Legal", null, null, null, "UTC", currency, 0m, "Q", "WO", "INV", 1));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankPrefixes_Throws(string prefix)
    {
        Assert.Throws<ArgumentException>(() => Organization.Create(
            "Acme", "Legal", null, null, null, "UTC", "USD", 0m, prefix, "WO", "INV", 1));
        Assert.Throws<ArgumentException>(() => Organization.Create(
            "Acme", "Legal", null, null, null, "UTC", "USD", 0m, "Q", prefix, "INV", 1));
        Assert.Throws<ArgumentException>(() => Organization.Create(
            "Acme", "Legal", null, null, null, "UTC", "USD", 0m, "Q", "WO", prefix, 1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_WithNextInvoiceNumberBelowOne_Throws(long nextInvoiceNumber)
    {
        Assert.Throws<ArgumentException>(() => Organization.Create(
            "Acme", "Legal", null, null, null, "UTC", "USD", 0m, "Q", "WO", "INV", nextInvoiceNumber));
    }
}
