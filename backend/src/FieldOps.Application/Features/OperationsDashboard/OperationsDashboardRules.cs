using FieldOps.Application.Features.ServiceRequests;
using FieldOps.Application.Features.Team;
using FieldOps.Application.Features.WorkOrders;
using FieldOps.Domain.WorkOrders;

namespace FieldOps.Application.Features.OperationsDashboard;

/// <summary>Delay and risk of one visit (operations-dashboard BR-07).</summary>
public sealed record VisitTiming(bool IsDelayed, int? DelayMinutes, bool IsAtRisk);

public static class AttentionTypes
{
    public const string Delayed = "delayed";

    public const string Unassigned = "unassigned";

    public const string OverEstimate = "over_estimate";

    public const string OverCapacity = "over_capacity";
}

/// <summary>Pure calculations of the operations dashboard: no EF, no clock (the caller supplies instants).</summary>
public static class OperationsDashboardRules
{
    private static readonly TimeSpan ArrivalGrace = TimeSpan.FromMinutes(15);

    private static readonly TimeSpan AtRiskWindow = TimeSpan.FromMinutes(60);

    /// <summary>BR-04: now for today, the end of the day for the past, the start of the day for the future.</summary>
    public static DateTimeOffset Reference(
        DateOnly date, DateOnly today, DateTimeOffset now, DateTimeOffset dayStart, DateTimeOffset dayEnd) =>
        date == today ? now : date < today ? dayEnd : dayStart;

    /// <summary>BR-04: any date from today minus 365 to today plus 365 days.</summary>
    public static bool DateInRange(DateOnly date, DateOnly today) =>
        date >= today.AddDays(-OperationsDashboardMessages.MaxDateOffsetDays)
        && date <= today.AddDays(OperationsDashboardMessages.MaxDateOffsetDays);

    /// <summary>BR-06: scheduled (scheduled, assigned), active (on the way, in progress, paused), completed group.</summary>
    public static string StatusGroup(VisitStatus status) => status switch
    {
        VisitStatus.Scheduled or VisitStatus.Assigned => "scheduled",
        VisitStatus.OnTheWay or VisitStatus.InProgress or VisitStatus.Paused => "active",
        VisitStatus.Completed or VisitStatus.NeedsCorrection or VisitStatus.Approved => "completed",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Not a day visit status."),
    };

    /// <summary>BR-07: a delay is whole minutes rounded down; a delay of 0 minutes is not delayed.</summary>
    public static VisitTiming Classify(
        VisitStatus status,
        DateTimeOffset scheduledStart,
        DateTimeOffset scheduledEnd,
        DateTimeOffset? arrivalWindowEnd,
        DateTimeOffset reference)
    {
        var limit = status switch
        {
            VisitStatus.Scheduled or VisitStatus.Assigned or VisitStatus.OnTheWay => arrivalWindowEnd ?? scheduledStart,
            VisitStatus.InProgress or VisitStatus.Paused => scheduledEnd,
            _ => (DateTimeOffset?)null,
        };

        if (limit is { } found && reference > found)
        {
            var minutes = (int)Math.Floor((reference - found).TotalMinutes);

            if (minutes >= 1)
            {
                return new VisitTiming(true, minutes, false);
            }
        }

        var untilStart = scheduledStart - reference;

        return new VisitTiming(false, null, status == VisitStatus.Scheduled && untilStart >= TimeSpan.Zero && untilStart <= AtRiskWindow);
    }

    /// <summary>BR-10: started + paused seconds + planned length, for visits in progress or paused.</summary>
    public static DateTimeOffset? ProjectedEnd(
        VisitStatus status, DateTimeOffset? startedAt, int pauseSeconds, DateTimeOffset scheduledStart, DateTimeOffset scheduledEnd) =>
        status is VisitStatus.InProgress or VisitStatus.Paused && startedAt is { } started
            ? started.AddSeconds(pauseSeconds) + (scheduledEnd - scheduledStart)
            : null;

    /// <summary>BR-09: eligible and on-time arrivals for the supplied reference instant.</summary>
    public static (int Eligible, int OnTime) OnTime(IEnumerable<DashboardArrival> visits, DateTimeOffset reference)
    {
        var eligible = 0;
        var onTime = 0;

        foreach (var visit in visits)
        {
            var limit = visit.ArrivalWindowEnd ?? visit.ScheduledStart + ArrivalGrace;

            if (visit.ActualStartedAt is { } started)
            {
                eligible++;

                if (started <= limit)
                {
                    onTime++;
                }
            }
            else if ((visit.Status is VisitStatus.Scheduled or VisitStatus.Assigned or VisitStatus.OnTheWay) && limit < reference)
            {
                eligible++;
            }
        }

        return (eligible, onTime);
    }

    /// <summary>Integer percent, rounded half away from zero; null when the total is 0.</summary>
    public static int? Percent(double part, double total) =>
        total > 0 ? (int)Math.Round(part / total * 100, MidpointRounding.AwayFromZero) : null;

    /// <summary>BR-11: whole minutes of elapsed work beyond the estimate for a visit in progress or paused.</summary>
    public static int? OverEstimateMinutes(
        VisitStatus status, DateTimeOffset? startedAt, int pauseSeconds, int? estimatedMinutes, DateTimeOffset reference)
    {
        if (status is not (VisitStatus.InProgress or VisitStatus.Paused) || startedAt is not { } started || estimatedMinutes is not { } estimate)
        {
            return null;
        }

        var elapsed = (reference - started).TotalMinutes - (pauseSeconds / 60d);
        var over = (int)Math.Floor(elapsed - estimate);

        return over >= 1 ? over : null;
    }

    /// <summary>
    /// BR-12: the earliest free time left today, or null (also when the technician is available now). Completed
    /// visits free the time; scheduled assessments are busy like visits.
    /// </summary>
    public static DateTimeOffset? NextAvailableToday(
        TechnicianSchedule schedule, IReadOnlyList<DashboardInterval> assessments, string currentStatus, DateTimeOffset now)
    {
        if (currentStatus == TodayStatuses.Available)
        {
            return null;
        }

        var busy = schedule.Visits
            .Where(visit => visit.Status != VisitStatus.NeedsCorrection)
            .Concat(assessments.Select(item => new AssignedVisit(VisitStatus.Scheduled, item.Start, item.End, null)))
            .ToList();
        var next = TechnicianAvailabilityCalculator.NextAvailable(schedule with { Visits = busy }, currentStatus, now);
        var (_, dayEnd) = BranchTime.Day(now, BranchTime.FindZone(schedule.ZoneId));

        return next.At is { } at && at < dayEnd ? at : null;
    }

    /// <summary>BR-14: the completions per local day of the period, zero-filled.</summary>
    public static IReadOnlyList<DailyCompletedView> Daily(
        IEnumerable<DateTimeOffset> completions, DateOnly from, int days, TimeZoneInfo zone)
    {
        var counts = completions
            .GroupBy(instant => OrganizationTime.LocalDate(instant, zone))
            .ToDictionary(group => group.Key, group => group.Count());

        return [.. Enumerable.Range(0, days)
            .Select(offset => from.AddDays(offset))
            .Select(date => new DailyCompletedView(date, counts.GetValueOrDefault(date)))];
    }
}

/// <summary>Assembles the dashboard response from plain store data (BR-05 to BR-15). Pure.</summary>
public static class OperationsDashboardBuilder
{
    public static OperationsDashboardView Build(DashboardSource source, string timezoneId, DateOnly date, DateTimeOffset now)
    {
        var zone = OrganizationTime.FindZone(timezoneId);
        var dayStart = OrganizationTime.StartOfDayUtc(date, zone);
        var dayEnd = OrganizationTime.StartOfDayUtc(date.AddDays(1), zone);
        var today = OrganizationTime.LocalDate(now, zone);
        var isToday = date == today;
        var reference = OperationsDashboardRules.Reference(date, today, now, dayStart, dayEnd);

        var rows = source.Visits
            .OrderBy(visit => visit.ScheduledStart)
            .ThenBy(visit => visit.WorkOrderNumber)
            .ThenBy(visit => visit.VisitId)
            .Select(visit => (
                Visit: visit,
                Timing: OperationsDashboardRules.Classify(
                    visit.Status, visit.ScheduledStart, visit.ScheduledEnd, visit.ArrivalWindowEnd, reference)))
            .ToList();

        var technicians = BuildTechnicians(source.Technicians, dayStart, dayEnd);
        var totalScheduled = technicians.Sum(item => item.Workload.ScheduledMinutes);
        var totalAvailable = technicians.Sum(item => item.Available);

        return new OperationsDashboardView(
            timezoneId,
            date,
            OrganizationTime.ToZone(reference, zone),
            new DashboardKpisView(
                new JobsKpiView(
                    rows.Count,
                    rows.Count(row => OperationsDashboardRules.StatusGroup(row.Visit.Status) == "completed"),
                    rows.Count(row => OperationsDashboardRules.StatusGroup(row.Visit.Status) == "active"),
                    rows.Count(row => OperationsDashboardRules.StatusGroup(row.Visit.Status) == "scheduled")),
                rows.Count(row => row.Visit.Status == VisitStatus.Scheduled),
                rows.Count(row => row.Timing.IsDelayed || row.Timing.IsAtRisk),
                new UtilizationKpiView(
                    (int)Math.Round(totalScheduled, MidpointRounding.AwayFromZero),
                    (int)Math.Round(totalAvailable, MidpointRounding.AwayFromZero),
                    OperationsDashboardRules.Percent(totalScheduled, totalAvailable)),
                BuildOnTime(source, reference, date, zone)),
            [.. rows.Select(row => ToView(row.Visit, row.Timing, zone))],
            date > today ? [] : BuildAttention(rows, technicians, reference, zone),
            [.. technicians
                .OrderByDescending(item => WorkloadKey(item.Workload))
                .ThenBy(item => item.Source.FullName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Source.Id)
                .Select(item => ToRow(item, isToday ? now : null))],
            BuildDurations(source.Durations),
            [.. source.Events
                .OrderByDescending(item => item.At)
                .ThenBy(item => item.VisitId)
                .Take(OperationsDashboardMessages.MaxActivityEvents)
                .Select(item => new ActivityView(
                    item.Type,
                    OrganizationTime.ToZone(item.At, zone),
                    item.VisitId,
                    item.WorkOrderId,
                    item.DisplayNumber,
                    item.Title,
                    item.TechnicianName))]);
    }

    private sealed record TechnicianLoad(DashboardTechnician Source, PeriodWorkload Workload, double Available);

    private static List<TechnicianLoad> BuildTechnicians(
        IReadOnlyList<DashboardTechnician> technicians, DateTimeOffset dayStart, DateTimeOffset dayEnd) =>
        [.. technicians.Select(technician =>
        {
            // The same workload the dispatch calendar lane uses: visits plus scheduled assessments (team BR-04/BR-06).
            var workload = AssessmentSlotEvaluator.Workload(
                technician.Schedule,
                [.. technician.Assessments.Select(item => new SlotCommitment(
                    item.Start, item.End, CommitmentKinds.Assessment, "Assessment", null))],
                dayStart,
                dayEnd);

            return new TechnicianLoad(
                technician, workload, TechnicianAvailabilityCalculator.AvailableMinutes(technician.Schedule, dayStart, dayEnd));
        })];

    // No availability with scheduled work sorts first, then by percent; nothing scheduled sorts last.
    private static int WorkloadKey(PeriodWorkload workload) =>
        workload.Percent ?? (workload.State == WorkloadStates.NoAvailability ? int.MaxValue : -1);

    private static OnTimeKpiView BuildOnTime(
        DashboardSource source, DateTimeOffset reference, DateOnly date, TimeZoneInfo zone)
    {
        var current = OperationsDashboardRules.OnTime(
            source.Visits.Select(visit => new DashboardArrival(
                visit.Status, visit.ScheduledStart, visit.ArrivalWindowEnd, visit.ActualStartedAt)),
            reference);

        // Last week: the same weekday, with the end of that day as the reference (BR-09).
        var lastWeekEnd = OrganizationTime.StartOfDayUtc(date.AddDays(-6), zone);
        var previous = OperationsDashboardRules.OnTime(source.PreviousWeek, lastWeekEnd);
        var percent = OperationsDashboardRules.Percent(current.OnTime, current.Eligible);
        var previousPercent = current.Eligible > 0 ? OperationsDashboardRules.Percent(previous.OnTime, previous.Eligible) : null;

        return new OnTimeKpiView(percent, current.Eligible, previousPercent);
    }

    private static DayVisitView ToView(DashboardVisit visit, VisitTiming timing, TimeZoneInfo zone) =>
        new(
            visit.VisitId,
            visit.WorkOrderId,
            visit.DisplayNumber,
            visit.Title,
            visit.CustomerName,
            visit.Status == VisitStatus.OnTheWay ? visit.CustomerPhone : null,
            visit.BranchId,
            WorkOrderCodes.VisitStatusCode(visit.Status),
            OperationsDashboardRules.StatusGroup(visit.Status),
            timing.IsDelayed,
            timing.DelayMinutes,
            timing.IsAtRisk,
            OrganizationTime.ToZone(visit.ScheduledStart, zone),
            OrganizationTime.ToZone(visit.ScheduledEnd, zone),
            visit.ActualCompletedAt is { } completed && OperationsDashboardRules.StatusGroup(visit.Status) == "completed"
                ? OrganizationTime.ToZone(completed, zone)
                : null,
            OperationsDashboardRules.ProjectedEnd(
                visit.Status, visit.ActualStartedAt, visit.PauseSeconds, visit.ScheduledStart, visit.ScheduledEnd) is { } projected
                ? OrganizationTime.ToZone(projected, zone)
                : null,
            visit.Assignees.Count > 0 ? new DashboardPerson(visit.Assignees[0].Id, visit.Assignees[0].FullName) : null,
            Math.Max(visit.Assignees.Count - 1, 0));

    // BR-11 order: delayed (delay descending), unassigned (start), over estimate, over-capacity technicians.
    private static List<AttentionView> BuildAttention(
        List<(DashboardVisit Visit, VisitTiming Timing)> rows,
        List<TechnicianLoad> technicians,
        DateTimeOffset reference,
        TimeZoneInfo zone)
    {
        var items = new List<AttentionView>();

        items.AddRange(rows
            .Where(row => row.Timing.IsDelayed)
            .OrderByDescending(row => row.Timing.DelayMinutes)
            .ThenBy(row => row.Visit.ScheduledStart)
            .Select(row => ForVisit(row.Visit, AttentionTypes.Delayed, row.Visit.ScheduledStart, row.Timing.DelayMinutes, zone)));

        items.AddRange(rows
            .Where(row => row.Visit.Status == VisitStatus.Scheduled && !row.Timing.IsDelayed)
            .OrderBy(row => row.Visit.ScheduledStart)
            .Select(row => ForVisit(row.Visit, AttentionTypes.Unassigned, row.Visit.ScheduledStart, null, zone)));

        items.AddRange(rows
            .Select(row => (row.Visit, Over: OperationsDashboardRules.OverEstimateMinutes(
                row.Visit.Status, row.Visit.ActualStartedAt, row.Visit.PauseSeconds, row.Visit.EstimatedMinutes, reference)))
            .Where(item => item.Over is not null)
            .OrderByDescending(item => item.Over)
            .ThenBy(item => item.Visit.ActualStartedAt)
            .Select(item => ForVisit(item.Visit, AttentionTypes.OverEstimate, item.Visit.ActualStartedAt, item.Over, zone)));

        items.AddRange(technicians
            .Where(item => item.Workload.Percent is > 100)
            .OrderByDescending(item => item.Workload.Percent)
            .ThenBy(item => item.Source.FullName, StringComparer.OrdinalIgnoreCase)
            .Select(item => new AttentionView(
                AttentionTypes.OverCapacity,
                null,
                null,
                null,
                null,
                null,
                item.Source.BranchId,
                null,
                null,
                new AttentionTechnicianView(
                    item.Source.Id,
                    item.Source.FullName,
                    (int)Math.Round(item.Workload.ScheduledMinutes, MidpointRounding.AwayFromZero),
                    (int)Math.Round(item.Available, MidpointRounding.AwayFromZero),
                    item.Workload.Percent!.Value))));

        return items;
    }

    private static AttentionView ForVisit(
        DashboardVisit visit, string type, DateTimeOffset? at, int? minutes, TimeZoneInfo zone) =>
        new(
            type,
            visit.VisitId,
            visit.WorkOrderId,
            visit.DisplayNumber,
            visit.Title,
            WorkOrderCodes.VisitStatusCode(visit.Status),
            visit.BranchId,
            at is { } instant ? OrganizationTime.ToZone(instant, zone) : null,
            minutes,
            null);

    private static TechnicianRowView ToRow(TechnicianLoad item, DateTimeOffset? now)
    {
        var technician = item.Source;
        var skills = technician.Skills
            .OrderByDescending(skill => skill.IsPrimary)
            .ThenBy(skill => skill.Name, StringComparer.OrdinalIgnoreCase)
            .Select(skill => skill.Name)
            .ToList();
        var status = now is { } instant ? TechnicianAvailabilityCalculator.ResolveStatus(technician.Schedule, instant) : null;

        return new TechnicianRowView(
            technician.Id,
            technician.FullName,
            RequestCardRules.Initials(technician.FullName),
            technician.ColorHex,
            [.. skills.Take(2)],
            Math.Max(skills.Count - 2, 0),
            status,
            (int)Math.Round(item.Workload.ScheduledMinutes, MidpointRounding.AwayFromZero),
            (int)Math.Round(item.Available, MidpointRounding.AwayFromZero),
            item.Workload.Percent,
            status is not null && now is { } at
                ? OperationsDashboardRules.NextAvailableToday(technician.Schedule, technician.Assessments, status, at)
                : null);
    }

    private static DurationsView BuildDurations(IReadOnlyList<DashboardDuration> durations)
    {
        var categories = durations
            .GroupBy(item => (item.CategoryId, item.CategoryName))
            .Select(group => new DurationCategoryView(
                group.Key.CategoryId,
                group.Key.CategoryName,
                group.Count(),
                (int)Math.Round(group.Average(item => (double)item.EstimatedMinutes), MidpointRounding.AwayFromZero),
                (int)Math.Round(
                    group.Average(item => Math.Max(0d, (item.CompletedAt - item.StartedAt).TotalMinutes - item.PauseSeconds / 60d)),
                    MidpointRounding.AwayFromZero)))
            .OrderByDescending(item => item.VisitCount)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.CategoryId)
            .Take(OperationsDashboardMessages.MaxDurationCategories)
            .ToList();

        return new DurationsView(durations.Count, categories);
    }
}
