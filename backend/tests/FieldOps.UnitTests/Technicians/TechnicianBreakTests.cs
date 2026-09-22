using FieldOps.Domain.Technicians;

namespace FieldOps.UnitTests.Technicians;

public class TechnicianBreakTests
{
    [Fact]
    public void Create_WithValidArguments_SetsProperties()
    {
        var availabilityId = Guid.NewGuid();

        var technicianBreak = TechnicianBreak.Create(
            availabilityId,
            new TimeOnly(12, 0),
            new TimeOnly(13, 0));

        Assert.Equal(availabilityId, technicianBreak.AvailabilityId);
        Assert.Equal(new TimeOnly(12, 0), technicianBreak.StartTime);
        Assert.Equal(new TimeOnly(13, 0), technicianBreak.EndTime);
    }

    [Fact]
    public void Create_WithEmptyAvailabilityId_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => TechnicianBreak.Create(Guid.Empty, new TimeOnly(12, 0), new TimeOnly(13, 0)));
    }

    [Fact]
    public void Create_WithStartTimeNotBeforeEndTime_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => TechnicianBreak.Create(Guid.NewGuid(), new TimeOnly(13, 0), new TimeOnly(12, 0)));
    }
}
