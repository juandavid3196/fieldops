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
            " Suite 200 ",
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
        Assert.Equal("Suite 200", branch.AddressLine2);
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
            OrganizationId, "Main", "MAIN", null, null, null, null, null, null, null, null, null, "{}");

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
            OrganizationId, "Main", "MAIN", null, null, null, null, null, null, null, null, null, businessHours!));
    }

    [Fact]
    public void UpdateDetails_WithValidValues_TrimsAndSetsFieldsAndTimestamp()
    {
        var branch = Branch.Create(
            OrganizationId, "Main", "MAIN", null, null, null, null, null, null, null, null, null, "{}");
        var updatedAt = DateTimeOffset.UtcNow.AddMinutes(5);

        branch.UpdateDetails(
            " Updated Branch ",
            " upd ",
            " new@acme.com ",
            " +1 555 111 2222 ",
            " 1 New St ",
            " Suite 3 ",
            " Denver ",
            " CO ",
            " 80202 ",
            " US ",
            " America/Denver ",
            """{"tuesday":{"start":"09:00","end":"18:00"}}""",
            updatedAt);

        Assert.Equal("Updated Branch", branch.Name);
        Assert.Equal("upd", branch.Code);
        Assert.Equal("new@acme.com", branch.Email);
        Assert.Equal("Suite 3", branch.AddressLine2);
        Assert.Equal("""{"tuesday":{"start":"09:00","end":"18:00"}}""", branch.BusinessHours);
        Assert.Equal(updatedAt, branch.UpdatedAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void UpdateDetails_WithBlankName_Throws(string? name)
    {
        var branch = Branch.Create(
            OrganizationId, "Main", "MAIN", null, null, null, null, null, null, null, null, null, "{}");

        Assert.Throws<ArgumentException>(() => branch.UpdateDetails(
            name!, "MAIN", null, null, null, null, null, null, null, null, null, "{}", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Deactivate_WhenActive_ReturnsTrueAndSetsInactive()
    {
        var branch = Branch.Create(
            OrganizationId, "Main", "MAIN", null, null, null, null, null, null, null, null, null, "{}");
        var updatedAt = DateTimeOffset.UtcNow.AddMinutes(1);

        var changed = branch.Deactivate(updatedAt);

        Assert.True(changed);
        Assert.False(branch.IsActive);
        Assert.Equal(updatedAt, branch.UpdatedAt);
    }

    [Fact]
    public void Deactivate_WhenAlreadyInactive_ReturnsFalseAndLeavesTimestampUnchanged()
    {
        var branch = Branch.Create(
            OrganizationId, "Main", "MAIN", null, null, null, null, null, null, null, null, null, "{}");
        branch.Deactivate(DateTimeOffset.UtcNow);
        var updatedAtBefore = branch.UpdatedAt;

        var changed = branch.Deactivate(DateTimeOffset.UtcNow.AddMinutes(5));

        Assert.False(changed);
        Assert.False(branch.IsActive);
        Assert.Equal(updatedAtBefore, branch.UpdatedAt);
    }

    [Fact]
    public void Reactivate_WhenInactive_ReturnsTrueAndSetsActive()
    {
        var branch = Branch.Create(
            OrganizationId, "Main", "MAIN", null, null, null, null, null, null, null, null, null, "{}");
        branch.Deactivate(DateTimeOffset.UtcNow);
        var updatedAt = DateTimeOffset.UtcNow.AddMinutes(10);

        var changed = branch.Reactivate(updatedAt);

        Assert.True(changed);
        Assert.True(branch.IsActive);
        Assert.Equal(updatedAt, branch.UpdatedAt);
    }

    [Fact]
    public void Reactivate_WhenAlreadyActive_ReturnsFalse()
    {
        var branch = Branch.Create(
            OrganizationId, "Main", "MAIN", null, null, null, null, null, null, null, null, null, "{}");

        var changed = branch.Reactivate(DateTimeOffset.UtcNow);

        Assert.False(changed);
        Assert.True(branch.IsActive);
    }
}
