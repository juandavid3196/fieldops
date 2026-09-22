using FieldOps.Domain.Technicians;

namespace FieldOps.UnitTests.Technicians;

public class TechnicianSkillTests
{
    [Fact]
    public void Create_WithValidArguments_SetsProperties()
    {
        var technicianId = Guid.NewGuid();
        var skillId = Guid.NewGuid();

        var technicianSkill = TechnicianSkill.Create(technicianId, skillId, 4, 3.5m);

        Assert.Equal(technicianId, technicianSkill.TechnicianId);
        Assert.Equal(skillId, technicianSkill.SkillId);
        Assert.Equal((short)4, technicianSkill.Proficiency);
        Assert.Equal(3.5m, technicianSkill.YearsExperience);
    }

    [Fact]
    public void Create_WithoutOptionalValues_LeavesThemNull()
    {
        var technicianSkill = TechnicianSkill.Create(Guid.NewGuid(), Guid.NewGuid());

        Assert.Null(technicianSkill.Proficiency);
        Assert.Null(technicianSkill.YearsExperience);
    }

    [Fact]
    public void Create_WithEmptyTechnicianId_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => TechnicianSkill.Create(Guid.Empty, Guid.NewGuid()));
    }

    [Theory]
    [InlineData((short)0)]
    [InlineData((short)6)]
    public void Create_WithProficiencyOutOfRange_Throws(short proficiency)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TechnicianSkill.Create(Guid.NewGuid(), Guid.NewGuid(), proficiency));
    }
}
