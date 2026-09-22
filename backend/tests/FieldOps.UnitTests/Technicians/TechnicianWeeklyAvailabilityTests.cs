using FieldOps.Domain.Technicians;

namespace FieldOps.UnitTests.Technicians;

public class TechnicianWeeklyAvailabilityTests
{
    [Fact]
    public void Create_WithValidArguments_SetsDefaultCapacity()
    {
        var technicianId = Guid.NewGuid();

        var availability = TechnicianWeeklyAvailability.Create(
            technicianId,
            1,
            new TimeOnly(9, 0),
            new TimeOnly(17, 0));

        Assert.Equal(technicianId, availability.TechnicianId);
        Assert.Equal((short)1, availability.DayOfWeek);
        Assert.Equal((short)100, availability.CapacityPercent);
    }

    [Theory]
    [InlineData((short)-1)]
    [InlineData((short)7)]
    public void Create_WithDayOfWeekOutOfRange_Throws(short dayOfWeek)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TechnicianWeeklyAvailability.Create(
                Guid.NewGuid(),
                dayOfWeek,
                new TimeOnly(9, 0),
                new TimeOnly(17, 0)));
    }

    [Theory]
    [InlineData((short)0)]
    [InlineData((short)101)]
    public void Create_WithCapacityOutOfRange_Throws(short capacityPercent)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TechnicianWeeklyAvailability.Create(
                Guid.NewGuid(),
                1,
                new TimeOnly(9, 0),
                new TimeOnly(17, 0),
                capacityPercent));
    }

    [Fact]
    public void Create_WithStartTimeNotBeforeEndTime_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => TechnicianWeeklyAvailability.Create(
                Guid.NewGuid(),
                1,
                new TimeOnly(17, 0),
                new TimeOnly(9, 0)));
    }
}
