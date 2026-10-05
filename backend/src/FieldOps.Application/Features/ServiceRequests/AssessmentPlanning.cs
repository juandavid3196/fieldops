using FieldOps.Application.Features.Team;

namespace FieldOps.Application.Features.ServiceRequests;

public sealed record PlannerSlot(
    string State, DateTimeOffset? From, DateTimeOffset? To, DateTimeOffset? AvailableAfter, bool Blocking);

public sealed record PlannerWorkload(int? Percent, string State);

public sealed record PlannerTechnician(
    Guid Id, string Name, string Initials, string? PrimarySkill, PlannerSlot Slot, PlannerWorkload Workload);

/// <summary>Response of GET /service-requests/{id}/assessment/planner (FR-03).</summary>
public sealed record AssessmentPlanner(string Timezone, Guid BranchId, IReadOnlyList<PlannerTechnician> Technicians);

/// <summary>One eligible technician as loaded by the store: plain data for the pure evaluator.</summary>
public sealed record PlannerCandidate(
    Guid Id,
    string Name,
    string? PrimarySkill,
    TechnicianSchedule Schedule,
    IReadOnlyList<SlotCommitment> Commitments);

/// <summary>The eligible technicians of the effective branch. Commitments already exclude the assessment being rescheduled.</summary>
public sealed record PlannerLoad(Guid BranchId, IReadOnlyList<PlannerCandidate> Candidates);

public sealed record CalendarRange(DateTimeOffset Start, DateTimeOffset End);

public sealed record CalendarDay(
    DateOnly Date,
    IReadOnlyList<CalendarRange> Availability,
    IReadOnlyList<CalendarRange> TimeOff,
    IReadOnlyList<CalendarRange> Breaks);

public sealed record CalendarEvent(string Kind, DateTimeOffset Start, DateTimeOffset End, string Label, bool IsCurrent);

/// <summary>Response of GET /service-requests/{id}/assessment/calendar (FR-04).</summary>
public sealed record AssessmentCalendar(string Timezone, IReadOnlyList<CalendarDay> Days, IReadOnlyList<CalendarEvent> Events);

/// <summary>The schedule and commitments of the selected technician; the current assessment is flagged, not excluded.</summary>
public sealed record CalendarLoad(
    TechnicianSchedule Schedule, IReadOnlyList<SlotCommitment> Commitments, Guid? CurrentAssessmentId);

/// <summary>Builds the read-only calendar of one technician in organization time (BR-07). Pure.</summary>
public static class AssessmentCalendarBuilder
{
    public static AssessmentCalendar Build(
        CalendarLoad load, DateOnly from, DateOnly to, string timezoneId, TimeZoneInfo organizationZone)
    {
        var scheduleZone = BranchTime.FindZone(load.Schedule.ZoneId);
        var rangeStart = OrganizationTime.StartOfDayUtc(from, organizationZone);
        var rangeEnd = OrganizationTime.StartOfDayUtc(to.AddDays(1), organizationZone);
        var breaks = BreaksBetween(load.Schedule, scheduleZone, rangeStart, rangeEnd);
        var days = new List<CalendarDay>();

        for (var date = from; date <= to; date = date.AddDays(1))
        {
            var dayStart = OrganizationTime.StartOfDayUtc(date, organizationZone);
            var dayEnd = OrganizationTime.StartOfDayUtc(date.AddDays(1), organizationZone);

            days.Add(new CalendarDay(
                date,
                [.. TechnicianAvailabilityCalculator.AvailabilityRanges(load.Schedule, dayStart, dayEnd)
                    .Select(item => Zoned(item.Start, item.End, organizationZone))],
                [.. Clip(
                    load.Schedule.Exceptions.Where(item => !item.IsAvailable).Select(item => (item.StartsAt, item.EndsAt)),
                    dayStart,
                    dayEnd,
                    organizationZone)],
                [.. Clip(breaks, dayStart, dayEnd, organizationZone)]));
        }

        var events = load.Commitments
            .Where(item => item.Start < rangeEnd && item.End > rangeStart)
            .OrderBy(item => item.Start)
            .ThenBy(item => item.End)
            .Select(item => new CalendarEvent(
                item.Kind,
                TimeZoneInfo.ConvertTime(item.Start, organizationZone),
                TimeZoneInfo.ConvertTime(item.End, organizationZone),
                item.Label,
                item.AssessmentId is { } id && id == load.CurrentAssessmentId))
            .ToList();

        return new AssessmentCalendar(timezoneId, days, events);
    }

    private static List<(DateTimeOffset Start, DateTimeOffset End)> BreaksBetween(
        TechnicianSchedule schedule, TimeZoneInfo zone, DateTimeOffset from, DateTimeOffset to)
    {
        var result = new List<(DateTimeOffset Start, DateTimeOffset End)>();

        for (var date = BranchTime.LocalDate(from, zone).AddDays(-1);
            date <= BranchTime.LocalDate(to, zone).AddDays(1);
            date = date.AddDays(1))
        {
            foreach (var slot in schedule.Slots.Where(slot => slot.DayOfWeek == (int)date.DayOfWeek))
            {
                result.AddRange(slot.Breaks.Select(item =>
                    (BranchTime.ToInstant(date, item.Start, zone), BranchTime.ToInstant(date, item.End, zone))));
            }
        }

        return result;
    }

    private static IEnumerable<CalendarRange> Clip(
        IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> ranges,
        DateTimeOffset from,
        DateTimeOffset to,
        TimeZoneInfo zone) =>
        ranges
            .Select(item => (Start: item.Start < from ? from : item.Start, End: item.End > to ? to : item.End))
            .Where(item => item.End > item.Start)
            .OrderBy(item => item.Start)
            .Select(item => Zoned(item.Start, item.End, zone));

    private static CalendarRange Zoned(DateTimeOffset start, DateTimeOffset end, TimeZoneInfo zone) =>
        new(TimeZoneInfo.ConvertTime(start, zone), TimeZoneInfo.ConvertTime(end, zone));
}
