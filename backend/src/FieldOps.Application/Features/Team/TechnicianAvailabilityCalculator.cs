using FieldOps.Domain.Technicians;
using FieldOps.Domain.WorkOrders;

namespace FieldOps.Application.Features.Team;

public enum TeamPeriod
{
    Today,
    Week,
}

public readonly record struct TimeRange(TimeOnly Start, TimeOnly End);

/// <summary>One weekly window (<c>day_of_week</c> 0 = Sunday) with the breaks inside it.</summary>
public sealed record AvailabilitySlot(
    int DayOfWeek, TimeOnly Start, TimeOnly End, int CapacityPercent, IReadOnlyList<TimeRange> Breaks);

public sealed record AvailabilityOverride(DateTimeOffset StartsAt, DateTimeOffset EndsAt, bool IsAvailable);

/// <summary>A visit with an active assignment of the technician. The label is loaded for on-job statuses only.</summary>
public sealed record AssignedVisit(
    VisitStatus Status, DateTimeOffset? ScheduledStart, DateTimeOffset? ScheduledEnd, string? Label);

/// <summary>Everything the calculator needs about one profile; plain data loaded without tracking.</summary>
public sealed record TechnicianSchedule(
    TechnicianStatus Status,
    string ZoneId,
    IReadOnlyList<AvailabilitySlot> Slots,
    IReadOnlyList<AvailabilityOverride> Exceptions,
    IReadOnlyList<AssignedVisit> Visits);

public static class TodayStatuses
{
    public const string Available = "available";

    public const string OnJob = "on_job";

    public const string Break = "break";

    public const string TimeOff = "time_off";

    public const string Off = "off";

    public const string Inactive = "inactive";

    public const string Suspended = "suspended";
}

public static class WorkloadStates
{
    public const string Percent = "percent";

    public const string NoAvailability = "no_availability";

    public const string None = "none";
}

public sealed record PeriodWorkload(int Jobs, double ScheduledMinutes, int? Percent, string State, bool AtCapacity);

public sealed record TeamDerivation(
    string TodayStatus,
    PeriodWorkload Workload,
    NextAvailableView NextAvailable,
    string? CurrentJobLabel);

/// <summary>Time zone and local calendar helpers (BR-03). Pure; the caller supplies the instant.</summary>
public static class BranchTime
{
    /// <summary>Branch time zone, then organization time zone, then UTC.</summary>
    public static string ResolveZoneId(string? branchTimezone, string? organizationTimezone)
    {
        foreach (var candidate in new[] { branchTimezone, organizationTimezone })
        {
            if (!string.IsNullOrWhiteSpace(candidate) && TryFind(candidate.Trim(), out _))
            {
                return candidate.Trim();
            }
        }

        return "UTC";
    }

    public static TimeZoneInfo FindZone(string? zoneId) =>
        !string.IsNullOrWhiteSpace(zoneId) && TryFind(zoneId, out var zone) ? zone : TimeZoneInfo.Utc;

    public static DateOnly LocalDate(DateTimeOffset instant, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);

    /// <summary>The UTC instant of a local date and time; a time skipped by DST uses the offset an hour later.</summary>
    public static DateTimeOffset ToInstant(DateOnly date, TimeOnly time, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        var offset = zone.IsInvalidTime(local) ? zone.GetUtcOffset(local.AddHours(1)) : zone.GetUtcOffset(local);

        return new DateTimeOffset(local, offset).ToUniversalTime();
    }

    /// <summary>Local midnight to the next local midnight, as UTC instants.</summary>
    public static (DateTimeOffset Start, DateTimeOffset End) Day(DateTimeOffset now, TimeZoneInfo zone)
    {
        var date = LocalDate(now, zone);

        return (ToInstant(date, TimeOnly.MinValue, zone), ToInstant(date.AddDays(1), TimeOnly.MinValue, zone));
    }

    /// <summary>Monday 00:00 to the next Monday 00:00 local, as UTC instants.</summary>
    public static (DateTimeOffset Start, DateTimeOffset End) Week(DateTimeOffset now, TimeZoneInfo zone)
    {
        var date = LocalDate(now, zone);
        var sinceMonday = ((int)date.DayOfWeek + 6) % 7;
        var monday = date.AddDays(-sinceMonday);

        return (ToInstant(monday, TimeOnly.MinValue, zone), ToInstant(monday.AddDays(7), TimeOnly.MinValue, zone));
    }

    public static (DateTimeOffset Start, DateTimeOffset End) Period(TeamPeriod period, DateTimeOffset now, TimeZoneInfo zone) =>
        period == TeamPeriod.Week ? Week(now, zone) : Day(now, zone);

    private static bool TryFind(string zoneId, out TimeZoneInfo zone)
    {
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);

            return true;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            zone = TimeZoneInfo.Utc;

            return false;
        }
    }
}

/// <summary>
/// Derives today's status, period workload and next available time of a profile (BR-04 to BR-07) from
/// plain data and a supplied clock. Pure: no EF, no <see cref="TimeProvider"/>.
/// </summary>
public static class TechnicianAvailabilityCalculator
{
    private readonly record struct Span(DateTimeOffset Start, DateTimeOffset End, double Weight);

    private static readonly NextAvailableView NoNext = new("none", null);

    public static TeamDerivation Derive(TechnicianSchedule schedule, TeamPeriod period, DateTimeOffset now)
    {
        var status = ResolveStatus(schedule, now);

        if (status is TodayStatuses.Inactive or TodayStatuses.Suspended)
        {
            return new TeamDerivation(
                status, new PeriodWorkload(0, 0, null, WorkloadStates.None, false), NoNext, null);
        }

        var zone = BranchTime.FindZone(schedule.ZoneId);
        var (from, to) = BranchTime.Period(period, now, zone);
        var workload = PeriodWorkloadFor(schedule.Visits, from, to, AvailableMinutes(schedule, from, to));

        return new TeamDerivation(status, workload, NextAvailable(schedule, status, now), CurrentJobLabel(schedule));
    }

    /// <summary>BR-05: the first matching status wins.</summary>
    public static string ResolveStatus(TechnicianSchedule schedule, DateTimeOffset now)
    {
        if (schedule.Status == TechnicianStatus.Inactive)
        {
            return TodayStatuses.Inactive;
        }

        if (schedule.Status == TechnicianStatus.Suspended)
        {
            return TodayStatuses.Suspended;
        }

        if (schedule.Exceptions.Any(item => !item.IsAvailable && item.StartsAt <= now && now < item.EndsAt))
        {
            return TodayStatuses.TimeOff;
        }

        if (schedule.Visits.Any(visit => visit.Status is VisitStatus.OnTheWay or VisitStatus.InProgress or VisitStatus.Paused))
        {
            return TodayStatuses.OnJob;
        }

        var zone = BranchTime.FindZone(schedule.ZoneId);
        var today = BranchTime.LocalDate(now, zone);

        foreach (var slot in schedule.Slots.Where(slot => slot.DayOfWeek == (int)today.DayOfWeek))
        {
            if (slot.Breaks.Any(item =>
                BranchTime.ToInstant(today, item.Start, zone) <= now && now < BranchTime.ToInstant(today, item.End, zone)))
            {
                return TodayStatuses.Break;
            }
        }

        var (dayStart, dayEnd) = BranchTime.Day(now, zone);

        return Availability(schedule, zone, dayStart, dayEnd).Any(span => span.Start <= now && now < span.End)
            ? TodayStatuses.Available
            : TodayStatuses.Off;
    }

    /// <summary>BR-04: window minutes weighted by capacity, plus exception-added minutes at 100 %.</summary>
    public static double AvailableMinutes(TechnicianSchedule schedule, DateTimeOffset from, DateTimeOffset to)
    {
        var zone = BranchTime.FindZone(schedule.ZoneId);

        return Availability(schedule, zone, from, to).Sum(span => (span.End - span.Start).TotalMinutes * span.Weight);
    }

    /// <summary>BR-06: visits starting inside the period, their clipped minutes and the resulting workload.</summary>
    public static PeriodWorkload PeriodWorkloadFor(
        IReadOnlyList<AssignedVisit> visits, DateTimeOffset from, DateTimeOffset to, double availableMinutes)
    {
        var counted = visits
            .Where(visit => visit.Status is not (VisitStatus.Unscheduled or VisitStatus.Cancelled)
                && visit.ScheduledStart is { } start && start >= from && start < to)
            .ToList();

        var scheduled = counted.Sum(visit =>
        {
            if (visit.ScheduledEnd is not { } end)
            {
                return 0d;
            }

            var clippedStart = visit.ScheduledStart!.Value < from ? from : visit.ScheduledStart.Value;
            var clippedEnd = end > to ? to : end;

            return clippedEnd > clippedStart ? (clippedEnd - clippedStart).TotalMinutes : 0d;
        });

        if (availableMinutes <= 0)
        {
            return scheduled > 0
                ? new PeriodWorkload(counted.Count, scheduled, null, WorkloadStates.NoAvailability, true)
                : new PeriodWorkload(counted.Count, scheduled, null, WorkloadStates.None, false);
        }

        var percent = (int)Math.Round(scheduled / availableMinutes * 100, MidpointRounding.AwayFromZero);

        return new PeriodWorkload(counted.Count, scheduled, percent, WorkloadStates.Percent, percent >= 100);
    }

    /// <summary>BR-07: now, the first free minute today, the next window within 7 days, or none.</summary>
    public static NextAvailableView NextAvailable(TechnicianSchedule schedule, string status, DateTimeOffset now)
    {
        if (status is TodayStatuses.Inactive or TodayStatuses.Suspended)
        {
            return NoNext;
        }

        if (status == TodayStatuses.Available)
        {
            return new NextAvailableView("now", null);
        }

        var zone = BranchTime.FindZone(schedule.ZoneId);
        var (_, dayEnd) = BranchTime.Day(now, zone);
        var earliest = CeilToMinute(now);

        // A visit without an end keeps the technician busy for the rest of the day.
        var busy = schedule.Visits
            .Where(visit => visit.Status is not (VisitStatus.Unscheduled or VisitStatus.Cancelled
                or VisitStatus.Completed or VisitStatus.Approved) && visit.ScheduledStart is not null)
            .Select(visit => (Start: visit.ScheduledStart!.Value, End: visit.ScheduledEnd ?? dayEnd))
            .ToList();

        foreach (var span in Availability(schedule, zone, earliest, dayEnd).OrderBy(span => span.Start))
        {
            foreach (var free in Subtract(span.Start, span.End, busy).OrderBy(piece => piece.Start))
            {
                if (free.End > free.Start)
                {
                    return new NextAvailableView("time", free.Start);
                }
            }
        }

        var next = Availability(schedule, zone, dayEnd, dayEnd.AddDays(7))
            .OrderBy(span => span.Start)
            .Cast<Span?>()
            .FirstOrDefault();

        return next is { } window ? new NextAvailableView("time", window.Start) : NoNext;
    }

    /// <summary>The label of the first visit that makes the profile "On job", or null.</summary>
    public static string? CurrentJobLabel(TechnicianSchedule schedule) =>
        schedule.Visits
            .Where(visit => visit.Status is VisitStatus.OnTheWay or VisitStatus.InProgress or VisitStatus.Paused)
            .OrderBy(visit => visit.ScheduledStart)
            .Select(visit => visit.Label)
            .FirstOrDefault();

    private static DateTimeOffset CeilToMinute(DateTimeOffset instant)
    {
        var ticks = instant.UtcTicks;
        var rounded = (ticks + TimeSpan.TicksPerMinute - 1) / TimeSpan.TicksPerMinute * TimeSpan.TicksPerMinute;

        return new DateTimeOffset(rounded, TimeSpan.Zero);
    }

    private static List<Span> Availability(
        TechnicianSchedule schedule, TimeZoneInfo zone, DateTimeOffset from, DateTimeOffset to)
    {
        var windows = new List<(DateTimeOffset Start, DateTimeOffset End)>();
        var spans = new List<Span>();

        // One day of margin each side: a window near midnight can start before `from` in UTC terms.
        for (var date = BranchTime.LocalDate(from, zone).AddDays(-1);
            date <= BranchTime.LocalDate(to, zone).AddDays(1);
            date = date.AddDays(1))
        {
            foreach (var slot in schedule.Slots.Where(slot => slot.DayOfWeek == (int)date.DayOfWeek))
            {
                var start = BranchTime.ToInstant(date, slot.Start, zone);
                var end = BranchTime.ToInstant(date, slot.End, zone);

                if (end <= start)
                {
                    continue;
                }

                windows.Add((start, end));

                var breaks = slot.Breaks
                    .Select(item => (Start: BranchTime.ToInstant(date, item.Start, zone), End: BranchTime.ToInstant(date, item.End, zone)))
                    .ToList();

                spans.AddRange(Subtract(start, end, breaks)
                    .Select(piece => new Span(piece.Start, piece.End, slot.CapacityPercent / 100d)));
            }
        }

        foreach (var added in schedule.Exceptions.Where(item => item.IsAvailable))
        {
            spans.AddRange(Subtract(added.StartsAt, added.EndsAt, windows)
                .Select(piece => new Span(piece.Start, piece.End, 1d)));
        }

        var cuts = schedule.Exceptions
            .Where(item => !item.IsAvailable)
            .Select(item => (Start: item.StartsAt, End: item.EndsAt))
            .ToList();

        var result = new List<Span>();

        foreach (var span in spans)
        {
            foreach (var piece in Subtract(span.Start, span.End, cuts))
            {
                var start = piece.Start < from ? from : piece.Start;
                var end = piece.End > to ? to : piece.End;

                if (end > start)
                {
                    result.Add(new Span(start, end, span.Weight));
                }
            }
        }

        return result;
    }

    private static List<(DateTimeOffset Start, DateTimeOffset End)> Subtract(
        DateTimeOffset start,
        DateTimeOffset end,
        IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> cuts)
    {
        var pieces = new List<(DateTimeOffset Start, DateTimeOffset End)> { (start, end) };

        foreach (var cut in cuts)
        {
            var next = new List<(DateTimeOffset Start, DateTimeOffset End)>();

            foreach (var piece in pieces)
            {
                if (cut.End <= piece.Start || cut.Start >= piece.End)
                {
                    next.Add(piece);

                    continue;
                }

                if (cut.Start > piece.Start)
                {
                    next.Add((piece.Start, cut.Start));
                }

                if (cut.End < piece.End)
                {
                    next.Add((cut.End, piece.End));
                }
            }

            pieces = next;
        }

        return pieces;
    }
}
