namespace FieldOps.Application.Features.Dispatch;

/// <summary>
/// The preferred window of the next recurring occurrence (dispatch-calendar BR-18). Pure: the local date and the
/// local window times of visit <i>k</i> plus the interval, converted in the branch zone.
/// </summary>
public static class RecurrenceCalculator
{
    /// <summary>
    /// The next occurrence's window as UTC instants. Weekly adds 7 days, biweekly 14, monthly one calendar month and
    /// quarterly three, clamped to the last day of the month. A local time skipped by a DST change uses the offset an
    /// hour later; an ambiguous one (clock set back) uses its first occurrence.
    /// </summary>
    public static (DateTimeOffset Start, DateTimeOffset End)? NextWindow(
        string? frequency, DateOnly date, TimeOnly windowStart, TimeOnly windowEnd, TimeZoneInfo zone)
    {
        DateOnly? next = frequency switch
        {
            "weekly" => date.AddDays(7),
            "biweekly" => date.AddDays(14),
            "monthly" => date.AddMonths(1),
            "quarterly" => date.AddMonths(3),
            _ => null,
        };

        if (next is not { } target || windowEnd <= windowStart)
        {
            return null;
        }

        return (ToInstant(target, windowStart, zone), ToInstant(target, windowEnd, zone));
    }

    /// <summary>The UTC instant of a local date and time with the DST rules of <see cref="NextWindow"/>.</summary>
    public static DateTimeOffset ToInstant(DateOnly date, TimeOnly time, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);

        if (zone.IsInvalidTime(local))
        {
            return new DateTimeOffset(local, zone.GetUtcOffset(local.AddHours(1))).ToUniversalTime();
        }

        if (zone.IsAmbiguousTime(local))
        {
            return new DateTimeOffset(local, zone.GetAmbiguousTimeOffsets(local).Max()).ToUniversalTime();
        }

        return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
    }
}
