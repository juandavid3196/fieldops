using System.Globalization;
using FieldOps.Application.Features.Branches;

namespace FieldOps.Application.Features.Team;

/// <summary>
/// The recurring weekly availability of a technician (team-availability-skills BR-07). A profile that uses the branch
/// hours gets one window per open weekday of the home branch business hours at 100 % capacity and without breaks;
/// otherwise its own weekly rows. Pure.
/// </summary>
public static class EffectiveWeeklyAvailability
{
    public const int BranchCapacityPercent = 100;

    private static readonly Dictionary<string, int> DayNumbers = new(StringComparer.Ordinal)
    {
        ["sunday"] = 0,
        ["monday"] = 1,
        ["tuesday"] = 2,
        ["wednesday"] = 3,
        ["thursday"] = 4,
        ["friday"] = 5,
        ["saturday"] = 6,
    };

    /// <param name="usesBranchHours">True when the profile inherits the branch hours.</param>
    /// <param name="branchBusinessHoursJson">The stored <c>branches.business_hours</c> object.</param>
    /// <param name="ownSlots">The profile's own weekly rows with their breaks; used when it does not inherit.</param>
    public static IReadOnlyList<AvailabilitySlot> Resolve(
        bool usesBranchHours, string? branchBusinessHoursJson, IReadOnlyList<AvailabilitySlot> ownSlots)
    {
        if (!usesBranchHours)
        {
            return ownSlots;
        }

        var slots = new List<AvailabilitySlot>();

        foreach (var day in BusinessHoursRules.FromStored(string.IsNullOrWhiteSpace(branchBusinessHoursJson) ? "{}" : branchBusinessHoursJson))
        {
            if (!day.IsOpen
                || !DayNumbers.TryGetValue(day.DayOfWeek, out var number)
                || !TryTime(day.OpeningTime, out var start)
                || !TryTime(day.ClosingTime, out var end)
                || end <= start)
            {
                continue;
            }

            slots.Add(new AvailabilitySlot(number, start, end, BranchCapacityPercent, []));
        }

        return slots;
    }

    private static bool TryTime(string? text, out TimeOnly time) =>
        TimeOnly.TryParseExact(text, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
}
