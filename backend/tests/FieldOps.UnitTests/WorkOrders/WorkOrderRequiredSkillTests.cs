using FieldOps.Domain.WorkOrders;

namespace FieldOps.UnitTests.WorkOrders;

public class WorkOrderRequiredSkillTests
{
    [Fact]
    public void Create_WithValidArguments_SetsProperties()
    {
        var workOrderId = Guid.NewGuid();
        var skillId = Guid.NewGuid();

        var requiredSkill = WorkOrderRequiredSkill.Create(workOrderId, skillId, 3);

        Assert.Equal(workOrderId, requiredSkill.WorkOrderId);
        Assert.Equal(skillId, requiredSkill.SkillId);
        Assert.Equal((short)3, requiredSkill.MinimumProficiency);
    }

    [Fact]
    public void Create_WithoutMinimumProficiency_LeavesItNull()
    {
        var requiredSkill = WorkOrderRequiredSkill.Create(Guid.NewGuid(), Guid.NewGuid());

        Assert.Null(requiredSkill.MinimumProficiency);
    }

    [Theory]
    [InlineData((short)0)]
    [InlineData((short)6)]
    public void Create_WithMinimumProficiencyOutOfRange_Throws(short minimumProficiency)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => WorkOrderRequiredSkill.Create(Guid.NewGuid(), Guid.NewGuid(), minimumProficiency));
    }
}
