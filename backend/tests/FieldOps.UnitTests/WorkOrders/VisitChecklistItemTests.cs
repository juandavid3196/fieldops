using FieldOps.Domain.WorkOrders;

namespace FieldOps.UnitTests.WorkOrders;

public class VisitChecklistItemTests
{
    [Fact]
    public void Create_WithValidArguments_SetsDefaults()
    {
        var visitId = Guid.NewGuid();
        var templateItemId = Guid.NewGuid();

        var item = VisitChecklistItem.Create(visitId, " Confirm power off ", templateItemId);

        Assert.NotEqual(Guid.Empty, item.Id);
        Assert.Equal(visitId, item.VisitId);
        Assert.Equal(templateItemId, item.TemplateItemId);
        Assert.Equal("Confirm power off", item.Label);
        Assert.True(item.IsRequired);
        Assert.False(item.IsCompleted);
        Assert.Equal(0, item.SortOrder);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankLabel_Throws(string label)
    {
        Assert.Throws<ArgumentException>(
            () => VisitChecklistItem.Create(Guid.NewGuid(), label));
    }
}
