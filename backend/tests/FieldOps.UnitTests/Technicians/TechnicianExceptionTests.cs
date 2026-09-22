using FieldOps.Domain.Technicians;

namespace FieldOps.UnitTests.Technicians;

public class TechnicianExceptionTests
{
    [Fact]
    public void Create_WithValidArguments_SetsDefaults()
    {
        var technicianId = Guid.NewGuid();
        var startsAt = DateTimeOffset.UtcNow;
        var endsAt = startsAt.AddHours(2);

        var exception = TechnicianException.Create(technicianId, startsAt, endsAt);

        Assert.Equal(technicianId, exception.TechnicianId);
        Assert.Equal(startsAt, exception.StartsAt);
        Assert.Equal(endsAt, exception.EndsAt);
        Assert.False(exception.IsAvailable);
        Assert.Null(exception.Reason);
    }

    [Fact]
    public void Create_WithEmptyTechnicianId_Throws()
    {
        var startsAt = DateTimeOffset.UtcNow;

        Assert.Throws<ArgumentException>(
            () => TechnicianException.Create(Guid.Empty, startsAt, startsAt.AddHours(1)));
    }

    [Fact]
    public void Create_WithStartNotBeforeEnd_Throws()
    {
        var startsAt = DateTimeOffset.UtcNow;

        Assert.Throws<ArgumentException>(
            () => TechnicianException.Create(Guid.NewGuid(), startsAt, startsAt.AddHours(-1)));
    }
}
