namespace FieldOps.Application.Features.Team;

public static class AvailabilityCellStates
{
    public const string Available = "available";

    public const string TimeOff = "time_off";

    public const string Unavailable = "unavailable";
}

public sealed record GridCell(DateOnly Date, string State, TimeOnly? Start, TimeOnly? End);

public sealed record GridRow(IReadOnlyList<GridCell> Days, int WeeklyMinutes);

/// <summary>Pure rules of full-day time off ranges (team-availability-skills BR-14, BR-15).</summary>
public static class TimeOffRules
{
    /// <summary>
    /// True when both bounds are local midnights of the zone; <paramref name="end"/> is the last covered local date.
    /// </summary>
    public static bool TryFullDayRange(
        DateTimeOffset startsAt, DateTimeOffset endsAt, TimeZoneInfo zone, out DateOnly start, out DateOnly end)
    {
        start = BranchTime.LocalDate(startsAt, zone);
        end = default;

        var endBoundary = BranchTime.LocalDate(endsAt, zone);

        if (BranchTime.ToInstant(start, TimeOnly.MinValue, zone) != startsAt
            || BranchTime.ToInstant(endBoundary, TimeOnly.MinValue, zone) != endsAt)
        {
            return false;
        }

        end = endBoundary.AddDays(-1);

        return end >= start;
    }

    /// <summary>
    /// team-availability-skills BR-27: an unavailable full-day range of more than one local day. The caller checks the status.
    /// </summary>
    public static bool IsMultiDay(bool isAvailable, DateTimeOffset startsAt, DateTimeOffset endsAt, TimeZoneInfo zone) =>
        !isAvailable && TryFullDayRange(startsAt, endsAt, zone, out var start, out var end) && end > start;

    /// <summary>The instants of the inclusive local date range: start midnight and the midnight after the end date.</summary>
    public static (DateTimeOffset StartsAt, DateTimeOffset EndsAt) Instants(DateOnly start, DateOnly end, TimeZoneInfo zone) =>
        (BranchTime.ToInstant(start, TimeOnly.MinValue, zone), BranchTime.ToInstant(end.AddDays(1), TimeOnly.MinValue, zone));
}

/// <summary>
/// Builds the seven cells and the weekly minutes of one technician row (team-availability-skills BR-04, BR-05).
/// Pure: the caller supplies the effective weekly slots and the active exceptions.
/// </summary>
public static class TeamAvailabilityGrid
{
    public static GridRow Build(
        DateOnly weekStart,
        string zoneId,
        IReadOnlyList<AvailabilitySlot> slots,
        IReadOnlyList<AvailabilityOverride> activeExceptions)
    {
        var zone = BranchTime.FindZone(zoneId);
        var cells = new List<GridCell>(7);
        var minutes = 0;

        for (var offset = 0; offset < 7; offset++)
        {
            var date = weekStart.AddDays(offset);
            var dayStart = BranchTime.ToInstant(date, TimeOnly.MinValue, zone);
            var dayEnd = BranchTime.ToInstant(date.AddDays(1), TimeOnly.MinValue, zone);

            if (activeExceptions.Any(item => !item.IsAvailable && item.StartsAt <= dayStart && item.EndsAt >= dayEnd))
            {
                cells.Add(new GridCell(date, AvailabilityCellStates.TimeOff, null, null));

                continue;
            }

            var slot = slots
                .Where(item => item.DayOfWeek == (int)date.DayOfWeek)
                .OrderBy(item => item.Start)
                .ThenBy(item => item.End)
                .FirstOrDefault();

            if (slot is null)
            {
                cells.Add(new GridCell(date, AvailabilityCellStates.Unavailable, null, null));

                continue;
            }

            cells.Add(new GridCell(date, AvailabilityCellStates.Available, slot.Start, slot.End));
            minutes += (int)(slot.End - slot.Start).TotalMinutes;
        }

        return new GridRow(cells, minutes);
    }

    /// <summary>BR-06: whether a row matches the availability filter (<c>all</c>, <c>available</c>, <c>time_off</c>, <c>none</c>).</summary>
    public static bool Matches(string availability, GridRow row) =>
        availability switch
        {
            "available" => row.Days.Any(cell => cell.State == AvailabilityCellStates.Available),
            "time_off" => row.Days.Any(cell => cell.State == AvailabilityCellStates.TimeOff),
            "none" => row.Days.All(cell => cell.State != AvailabilityCellStates.Available),
            _ => true,
        };
}
