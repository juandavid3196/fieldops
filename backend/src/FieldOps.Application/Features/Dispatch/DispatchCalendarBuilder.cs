using FieldOps.Application.Features.ServiceRequests;
using FieldOps.Application.Features.Team;
using FieldOps.Domain.WorkOrders;

namespace FieldOps.Application.Features.Dispatch;

/// <summary>Builds the calendar response (dispatch-calendar BR-04 to BR-06) from plain data. Pure.</summary>
public static class DispatchCalendarBuilder
{
    /// <summary>Day: the local day of <paramref name="date"/>. Week: the Monday to Sunday containing it.</summary>
    public static (DateTimeOffset From, DateTimeOffset To, IReadOnlyList<DateOnly> Days) Range(
        string view, DateOnly date, TimeZoneInfo zone)
    {
        var first = view == "week" ? date.AddDays(-(((int)date.DayOfWeek + 6) % 7)) : date;
        var count = view == "week" ? 7 : 1;
        var days = Enumerable.Range(0, count).Select(offset => first.AddDays(offset)).ToList();

        return (OrganizationTime.StartOfDayUtc(first, zone), OrganizationTime.StartOfDayUtc(first.AddDays(count), zone), days);
    }

    public static DispatchCalendarView Build(CalendarSource source, CalendarQuery query)
    {
        var zone = OrganizationTime.FindZone(source.Timezone);
        var (_, _, days) = Range(query.View, query.Date, zone);
        var byId = source.Lanes.ToDictionary(lane => lane.Id);

        var shown = source.Visits
            .Where(visit => visit.Start < source.To && visit.End > source.From)
            .Select(visit => (Source: visit, Conflicts: ConflictsOf(visit, byId, zone)))
            .Where(item => query.Status switch
            {
                "unassigned" => item.Source.Status == VisitStatus.Scheduled,
                "assigned" => item.Source.Status == VisitStatus.Assigned,
                "conflicts" => item.Conflicts.Count > 0,
                _ => true,
            })
            .OrderBy(item => item.Source.Start)
            .ThenBy(item => item.Source.DisplayNumber, StringComparer.Ordinal)
            .ThenBy(item => item.Source.VisitId)
            .ToList();

        var holders = shown.SelectMany(item => item.Source.TechnicianIds).ToHashSet();
        var branchLanes = source.Lanes.Where(lane => lane.Tag is null).ToList();

        // Team ids that are not eligible (foreign, out of scope, other branch) are ignored; none left means All.
        var eligible = query.TechnicianIds.Where(id => branchLanes.Any(lane => lane.Id == id)).ToHashSet();

        var lanes = branchLanes
            .Where(lane => eligible.Count == 0 || eligible.Contains(lane.Id))
            .Where(lane => query.SkillId is not { } skillId || lane.Technician.SkillIds.Contains(skillId))
            .OrderBy(lane => lane.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(lane => lane.Id)
            .Concat(source.Lanes
                .Where(lane => lane.Tag is not null && holders.Contains(lane.Id))
                .OrderBy(lane => lane.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(lane => lane.Id))
            .Select(lane => BuildLane(lane, source, zone, days))
            .ToList();

        return new DispatchCalendarView(
            source.Timezone,
            OrganizationTime.ToZone(source.From, zone),
            OrganizationTime.ToZone(source.To, zone),
            days,
            lanes,
            [.. shown.Select(item => new CalendarVisitView(
                item.Source.VisitId,
                item.Source.WorkOrderId,
                item.Source.DisplayNumber,
                item.Source.Title,
                item.Source.Street,
                WorkOrderStatusCode(item.Source.Status),
                OrganizationTime.ToZone(item.Source.Start, zone),
                OrganizationTime.ToZone(item.Source.End, zone),
                item.Source.TechnicianIds,
                item.Source.PrimaryTechnicianId,
                item.Conflicts))]);
    }

    /// <summary>The current BR-10 conflicts of a scheduled or assigned visit; never read from audit rows.</summary>
    private static IReadOnlyList<DispatchConflict> ConflictsOf(
        CalendarVisitSource visit, IReadOnlyDictionary<Guid, CalendarLane> lanes, TimeZoneInfo zone)
    {
        if (visit.Status is not (VisitStatus.Scheduled or VisitStatus.Assigned) || visit.TechnicianIds.Count == 0)
        {
            return [];
        }

        var selection = visit.TechnicianIds
            .Where(lanes.ContainsKey)
            .Select(id => lanes[id].Technician)
            .ToList();

        return DispatchConflictClassifier.Classify(visit.Start, visit.End, visit.RequiredSkills, selection, zone, visit.VisitId).Conflicts;
    }

    private static CalendarTechnicianView BuildLane(
        CalendarLane lane, CalendarSource source, TimeZoneInfo zone, IReadOnlyList<DateOnly> days)
    {
        var technician = lane.Technician;
        var visits = technician.Commitments
            .Where(item => item.Kind == CommitmentKinds.Visit && item.Status is not null)
            .Select(item => new AssignedVisit(item.Status!.Value, item.Start, item.End, null))
            .ToList();
        var assessments = technician.Commitments
            .Where(item => item.Kind == CommitmentKinds.Assessment)
            .ToList();
        var schedule = technician.Schedule with { Visits = visits };

        var workload = AssessmentSlotEvaluator.Workload(
            schedule,
            [.. assessments.Select(item => new SlotCommitment(item.Start, item.End, CommitmentKinds.Assessment, "Assessment", null))],
            source.From,
            source.To);
        var available = TechnicianAvailabilityCalculator.AvailableMinutes(schedule, source.From, source.To);

        var calendar = AssessmentCalendarBuilder.Build(
            new CalendarLoad(technician.Schedule, [], null), days[0], days[^1], source.Timezone, zone);

        return new CalendarTechnicianView(
            lane.Id,
            lane.Name,
            RequestCardRules.Initials(lane.Name),
            lane.ColorHex,
            lane.PrimarySkill,
            lane.Tag,
            new DispatchLoad(workload.ScheduledMinutes, available, workload.State),
            [.. calendar.Days.Select(day => new DispatchCalendarDay(day.Date, day.Availability, day.Breaks, day.TimeOff))],
            [.. assessments
                .Where(item => item.Start < source.To && item.End > source.From)
                .OrderBy(item => item.Start)
                .Select(item => new CalendarRange(
                    OrganizationTime.ToZone(item.Start, zone), OrganizationTime.ToZone(item.End, zone)))]);
    }

    private static string WorkOrderStatusCode(VisitStatus status) =>
        FieldOps.Application.Features.WorkOrders.WorkOrderCodes.VisitStatusCode(status);
}
