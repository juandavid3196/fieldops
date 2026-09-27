using FieldOps.Domain.Branches;

namespace FieldOps.UnitTests.Branches;

public class BranchTests
{
    private static readonly Guid OrganizationId = Guid.NewGuid();

    [Fact]
    public void Create_WithValidValues_TrimsAndSetsFields()
    {
        var branch = Branch.Create(
            OrganizationId,
            " Main Branch ",
            " main ",
            " branch@acme.com ",
            " +1 555 987 6543 ",
            " 123 Main St ",
            " Chicago ",
            " IL ",
            " 60601 ",
            " US ",
            " America/Chicago ",
            """{"monday":{"start":"08:00","end":"17:00"}}""");

        Assert.Equal("Main Branch", branch.Name);
        Assert.Equal("main", branch.Code);
        Assert.Equal("branch@acme.com", branch.Email);
        Assert.Equal("+1 555 987 6543", branch.Phone);
        Assert.Equal("123 Main St", branch.AddressLine1);
        Assert.Null(branch.AddressLine2);
        Assert.Equal("Chicago", branch.City);
        Assert.Equal("IL", branch.StateRegion);
        Assert.Equal("60601", branch.PostalCode);
        Assert.Equal("US", branch.CountryCode);
        Assert.Equal("America/Chicago", branch.Timezone);
        Assert.Equal("""{"monday":{"start":"08:00","end":"17:00"}}""", branch.BusinessHours);
        Assert.True(branch.IsActive);
    }

    [Fact]
    public void Create_WithoutOptionalValues_LeavesThemNull()
    {
        var branch = Branch.Create(
            OrganizationId, "Main", "MAIN", null, null, null, null, null, null, null, null, "{}");

        Assert.Null(branch.Email);
        Assert.Null(branch.Phone);
        Assert.Null(branch.AddressLine1);
        Assert.Null(branch.City);
        Assert.Null(branch.StateRegion);
        Assert.Null(branch.PostalCode);
        Assert.Null(branch.CountryCode);
        Assert.Null(branch.Timezone);
        Assert.Equal("{}", branch.BusinessHours);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_WithBlankBusinessHours_Throws(string? businessHours)
    {
        Assert.Throws<ArgumentException>(() => Branch.Create(
            OrganizationId, "Main", "MAIN", null, null, null, null, null, null, null, null, businessHours!));
    }
}
