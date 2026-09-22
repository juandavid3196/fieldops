using FieldOps.Domain.WorkOrders;

namespace FieldOps.UnitTests.WorkOrders;

public class WorkOrderChecklistTemplateTests
{
    [Fact]
    public void Create_WithValidArguments_SetsDefaults()
    {
        var workOrderId = Guid.NewGuid();

        var template = WorkOrderChecklistTemplate.Create(workOrderId, " Verify power off ");

        Assert.NotEqual(Guid.Empty, template.Id);
        Assert.Equal(workOrderId, template.WorkOrderId);
        Assert.Equal("Verify power off", template.Label);
        Assert.True(template.IsRequired);
        Assert.Equal(0, template.SortOrder);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankLabel_Throws(string label)
    {
        Assert.Throws<ArgumentException>(
            () => WorkOrderChecklistTemplate.Create(Guid.NewGuid(), label));
    }
}
