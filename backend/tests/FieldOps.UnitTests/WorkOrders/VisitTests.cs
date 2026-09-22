using FieldOps.Domain.WorkOrders;

namespace FieldOps.UnitTests.WorkOrders;

public class VisitTests
{
    [Fact]
    public void Create_WithValidArguments_SetsDefaults()
    {
        var organizationId = Guid.NewGuid();
        var workOrderId = Guid.NewGuid();

        var visit = Visit.Create(organizationId, workOrderId, 1);

        Assert.NotEqual(Guid.Empty, visit.Id);
        Assert.Equal(organizationId, visit.OrganizationId);
        Assert.Equal(workOrderId, visit.WorkOrderId);
        Assert.Equal(1, visit.VisitNumber);
        Assert.Equal(VisitStatus.Unscheduled, visit.Status);
        Assert.Equal(0, visit.PauseSeconds);
        Assert.Null(visit.ScheduledStart);
        Assert.Null(visit.ScheduledEnd);
    }

    [Fact]
    public void Create_WithNonPositiveVisitNumber_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Visit.Create(Guid.NewGuid(), Guid.NewGuid(), 0));
    }

    [Fact]
    public void Create_WithScheduledStartNotBeforeEnd_Throws()
    {
        var now = DateTimeOffset.UtcNow;

        Assert.Throws<ArgumentException>(
            () => Visit.Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                1,
                scheduledStart: now,
                scheduledEnd: now.AddHours(-1)));
    }

    [Fact]
    public void Create_WithOnlyScheduledStart_Succeeds()
    {
        var now = DateTimeOffset.UtcNow;

        var visit = Visit.Create(Guid.NewGuid(), Guid.NewGuid(), 1, scheduledStart: now);

        Assert.Equal(now, visit.ScheduledStart);
        Assert.Null(visit.ScheduledEnd);
    }
}
