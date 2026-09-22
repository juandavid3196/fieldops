using FieldOps.Domain.WorkOrders;

namespace FieldOps.UnitTests.WorkOrders;

public class VisitTimeEntryTests
{
    [Fact]
    public void Create_WithValidArguments_SetsProperties()
    {
        var visitId = Guid.NewGuid();
        var technicianId = Guid.NewGuid();
        var startedAt = DateTimeOffset.UtcNow;

        var entry = VisitTimeEntry.Create(visitId, technicianId, startedAt, VisitTimeEntryType.Work);

        Assert.Equal(visitId, entry.VisitId);
        Assert.Equal(technicianId, entry.TechnicianId);
        Assert.Equal(startedAt, entry.StartedAt);
        Assert.Equal(VisitTimeEntryType.Work, entry.EntryType);
        Assert.Null(entry.EndedAt);
    }

    [Fact]
    public void Create_WithEndedAtNotAfterStartedAt_Throws()
    {
        var startedAt = DateTimeOffset.UtcNow;

        Assert.Throws<ArgumentException>(
            () => VisitTimeEntry.Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                startedAt,
                VisitTimeEntryType.Pause,
                endedAt: startedAt.AddMinutes(-5)));
    }

    [Fact]
    public void Create_WithEmptyTechnicianId_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => VisitTimeEntry.Create(
                Guid.NewGuid(),
                Guid.Empty,
                DateTimeOffset.UtcNow,
                VisitTimeEntryType.Travel));
    }
}
