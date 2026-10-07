using FieldOps.Application.Features.Team;
using FieldOps.Domain.WorkOrders;

namespace FieldOps.Application.Features.TechnicianVisits;

public enum TravelDecision
{
    /// <summary>The transition applies.</summary>
    Proceed,

    /// <summary>The visit already is in the requested state: <c>200 changed = false</c>, no write.</summary>
    Unchanged,

    StatusInvalid,

    NotToday,

    AnotherActive,
}

/// <summary>The latest travel time entry of the visit as seen by the caller (BR-04, BR-09).</summary>
public enum TravelEntryState
{
    None,

    OpenOwn,

    OpenOther,

    Closed,
}

/// <summary>Pure guards of Start travel (BR-07) and arrive (BR-09), evaluated on the locked, re-read state.</summary>
public static class TravelRules
{
    /// <summary>Idempotent repeat, then status, then the visit day in the profile zone, then another active visit of the caller.</summary>
    public static TravelDecision Start(
        VisitStatus status, DateTimeOffset? scheduledStart, DateTimeOffset now, string zoneId, bool anotherActive)
    {
        if (status == VisitStatus.OnTheWay)
        {
            return TravelDecision.Unchanged;
        }

        if (status != VisitStatus.Assigned)
        {
            return TravelDecision.StatusInvalid;
        }

        var (dayStart, dayEnd) = BranchTime.Day(now, BranchTime.FindZone(zoneId));

        if (scheduledStart is not { } start || start < dayStart || start >= dayEnd)
        {
            return TravelDecision.NotToday;
        }

        return anotherActive ? TravelDecision.AnotherActive : TravelDecision.Proceed;
    }

    public static TravelDecision Arrive(VisitStatus status, TravelEntryState entry) =>
        status != VisitStatus.OnTheWay
            ? TravelDecision.StatusInvalid
            : entry switch
            {
                TravelEntryState.OpenOwn => TravelDecision.Proceed,
                TravelEntryState.Closed => TravelDecision.Unchanged,
                _ => TravelDecision.StatusInvalid,
            };
}
