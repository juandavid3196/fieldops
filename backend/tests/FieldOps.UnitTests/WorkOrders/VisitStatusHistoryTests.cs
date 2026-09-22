using FieldOps.Domain.WorkOrders;

namespace FieldOps.UnitTests.WorkOrders;

public class VisitStatusHistoryTests
{
    [Fact]
    public void Create_WithValidArguments_SetsProperties()
    {
        var visitId = Guid.NewGuid();

        var history = VisitStatusHistory.Create(visitId, VisitStatus.Scheduled, VisitStatus.Assigned);

        Assert.Equal(visitId, history.VisitId);
        Assert.Equal(VisitStatus.Scheduled, history.FromStatus);
        Assert.Equal(VisitStatus.Assigned, history.ToStatus);
    }

    [Fact]
    public void Create_WithoutFromStatus_LeavesItNull()
    {
        var history = VisitStatusHistory.Create(Guid.NewGuid(), null, VisitStatus.Scheduled);

        Assert.Null(history.FromStatus);
    }

    [Fact]
    public void Create_WithEmptyVisitId_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => VisitStatusHistory.Create(Guid.Empty, null, VisitStatus.Scheduled));
    }
}
