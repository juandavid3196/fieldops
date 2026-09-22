using FieldOps.Domain.Technicians;

namespace FieldOps.UnitTests.Technicians;

public class SkillTests
{
    [Fact]
    public void Create_WithValidArguments_SetsDefaults()
    {
        var organizationId = Guid.NewGuid();

        var skill = Skill.Create(organizationId, " Electrical ");

        Assert.NotEqual(Guid.Empty, skill.Id);
        Assert.Equal(organizationId, skill.OrganizationId);
        Assert.Equal("Electrical", skill.Name);
        Assert.True(skill.IsActive);
    }

    [Fact]
    public void Create_WithEmptyOrganizationId_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => Skill.Create(Guid.Empty, "Electrical"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankName_Throws(string name)
    {
        Assert.Throws<ArgumentException>(
            () => Skill.Create(Guid.NewGuid(), name));
    }
}
