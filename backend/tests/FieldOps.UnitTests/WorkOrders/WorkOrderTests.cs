using FieldOps.Domain.WorkOrders;

namespace FieldOps.UnitTests.WorkOrders;

public class WorkOrderTests
{
    private static WorkOrderFields Fields(string title = " Replace HVAC unit ") =>
        new(
            title,
            WorkOrderJobTypes.OneTime,
            Guid.NewGuid(),
            3,
            null,
            null,
            null,
            null,
            null,
            null,
            true,
            true,
            true);

    [Fact]
    public void Create_WithValidArguments_SetsDefaults()
    {
        var organizationId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var quoteVersionId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var propertyId = Guid.NewGuid();
        var createdByUserId = Guid.NewGuid();

        var workOrder = WorkOrder.Create(
            organizationId,
            branchId,
            1,
            quoteVersionId,
            customerId,
            propertyId,
            " Replace HVAC unit ",
            createdByUserId,
            Fields());

        Assert.NotEqual(Guid.Empty, workOrder.Id);
        Assert.Equal(organizationId, workOrder.OrganizationId);
        Assert.Equal(branchId, workOrder.BranchId);
        Assert.Equal(1, workOrder.WorkOrderNumber);
        Assert.Equal(quoteVersionId, workOrder.QuoteVersionId);
        Assert.Equal(customerId, workOrder.CustomerId);
        Assert.Equal(propertyId, workOrder.PropertyId);
        Assert.Equal("Replace HVAC unit", workOrder.ScopeSnapshot);
        Assert.Equal("Replace HVAC unit", workOrder.Title);
        Assert.Equal(WorkOrderStatus.Draft, workOrder.Status);
        Assert.Equal((short)3, workOrder.Priority);
    }

    [Fact]
    public void ReplaceDraftAndMarkReady_ChangeFieldsAndAlwaysMoveTheConcurrencyValue()
    {
        var workOrder = WorkOrder.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            1,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Scope",
            Guid.NewGuid(),
            Fields());
        var first = workOrder.UpdatedAt;
        var newBranch = Guid.NewGuid();

        workOrder.ReplaceDraft(newBranch, Fields("New title"), first);

        Assert.Equal(newBranch, workOrder.BranchId);
        Assert.Equal("New title", workOrder.Title);
        Assert.True(workOrder.UpdatedAt > first);

        workOrder.MarkReady(first);

        Assert.Equal(WorkOrderStatus.ReadyToSchedule, workOrder.Status);
        Assert.Throws<InvalidOperationException>(() => workOrder.ReplaceDraft(newBranch, Fields(), first));
        Assert.Throws<InvalidOperationException>(() => workOrder.MarkReady(first));
    }

    [Fact]
    public void Create_WithNonPositiveWorkOrderNumber_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => WorkOrder.Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                0,
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                "Scope",
                Guid.NewGuid(),
                Fields()));
    }

    [Fact]
    public void Create_WithEmptyPropertyId_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => WorkOrder.Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                1,
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.Empty,
                "Scope",
                Guid.NewGuid(),
                Fields()));
    }

    [Fact]
    public void Create_WithBlankScopeSnapshot_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => WorkOrder.Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                1,
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                "   ",
                Guid.NewGuid(),
                Fields()));
    }
}
