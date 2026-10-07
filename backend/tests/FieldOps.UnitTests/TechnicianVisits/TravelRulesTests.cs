using System.Globalization;
using FieldOps.Application.Features.TechnicianVisits;
using FieldOps.Domain.WorkOrders;

namespace FieldOps.UnitTests.TechnicianVisits;

/// <summary>Start travel and arrive guards (mobile-job-details BR-07, BR-09) and the travel entry close clamp.</summary>
public class TravelRulesTests
{
    private const string Zone = "America/Chicago";

    // 2026-06-10 12:00 local (CDT, UTC-5): the local day is [05:00Z, 05:00Z next day).
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-10T17:00:00Z", CultureInfo.InvariantCulture);

    [Theory]
    [InlineData(VisitStatus.Assigned, "2026-06-10T18:00:00Z", false, TravelDecision.Proceed)]
    [InlineData(VisitStatus.Assigned, "2026-06-10T05:00:00Z", false, TravelDecision.Proceed)]
    [InlineData(VisitStatus.Assigned, "2026-06-11T04:59:59Z", false, TravelDecision.Proceed)]
    [InlineData(VisitStatus.Assigned, "2026-06-10T04:59:59Z", false, TravelDecision.NotToday)]
    [InlineData(VisitStatus.Assigned, "2026-06-11T05:00:00Z", false, TravelDecision.NotToday)]
    [InlineData(VisitStatus.Assigned, null, false, TravelDecision.NotToday)]
    [InlineData(VisitStatus.Assigned, "2026-06-10T18:00:00Z", true, TravelDecision.AnotherActive)]
    [InlineData(VisitStatus.Assigned, "2026-06-12T18:00:00Z", true, TravelDecision.NotToday)]
    [InlineData(VisitStatus.OnTheWay, "2026-06-01T18:00:00Z", true, TravelDecision.Unchanged)]
    [InlineData(VisitStatus.Scheduled, "2026-06-10T18:00:00Z", true, TravelDecision.StatusInvalid)]
    [InlineData(VisitStatus.InProgress, "2026-06-10T18:00:00Z", false, TravelDecision.StatusInvalid)]
    [InlineData(VisitStatus.Paused, "2026-06-10T18:00:00Z", false, TravelDecision.StatusInvalid)]
    [InlineData(VisitStatus.Completed, "2026-06-10T18:00:00Z", false, TravelDecision.StatusInvalid)]
    [InlineData(VisitStatus.NeedsCorrection, "2026-06-10T18:00:00Z", false, TravelDecision.StatusInvalid)]
    [InlineData(VisitStatus.Approved, "2026-06-10T18:00:00Z", false, TravelDecision.StatusInvalid)]
    public void Start_AppliesTheGuardsInOrder(VisitStatus status, string? start, bool anotherActive, TravelDecision expected)
    {
        DateTimeOffset? scheduledStart = start is null ? null : DateTimeOffset.Parse(start, CultureInfo.InvariantCulture);

        Assert.Equal(expected, TravelRules.Start(status, scheduledStart, Now, Zone, anotherActive));
    }

    [Theory]
    [InlineData(VisitStatus.OnTheWay, TravelEntryState.OpenOwn, TravelDecision.Proceed)]
    [InlineData(VisitStatus.OnTheWay, TravelEntryState.Closed, TravelDecision.Unchanged)]
    [InlineData(VisitStatus.OnTheWay, TravelEntryState.None, TravelDecision.StatusInvalid)]
    [InlineData(VisitStatus.OnTheWay, TravelEntryState.OpenOther, TravelDecision.StatusInvalid)]
    [InlineData(VisitStatus.Assigned, TravelEntryState.OpenOwn, TravelDecision.StatusInvalid)]
    [InlineData(VisitStatus.InProgress, TravelEntryState.Closed, TravelDecision.StatusInvalid)]
    public void Arrive_RequiresOnTheWayAndTheCallersTravelEntry(VisitStatus status, TravelEntryState entry, TravelDecision expected) =>
        Assert.Equal(expected, TravelRules.Arrive(status, entry));

    [Fact]
    public void Close_KeepsTheEndAfterTheStartForTheStartedAtEndedAtCheck()
    {
        var started = DateTimeOffset.Parse("2026-06-10T17:00:00Z", CultureInfo.InvariantCulture);
        var immediate = VisitTimeEntry.Create(Guid.NewGuid(), Guid.NewGuid(), started, VisitTimeEntryType.Travel);
        var later = VisitTimeEntry.Create(Guid.NewGuid(), Guid.NewGuid(), started, VisitTimeEntryType.Travel);

        immediate.Close(started);
        later.Close(started.AddMinutes(12));

        Assert.Equal(started.AddTicks(10), immediate.EndedAt);
        Assert.Equal(started.AddMinutes(12), later.EndedAt);
        Assert.Throws<InvalidOperationException>(() => later.Close(started.AddMinutes(13)));
    }
}
