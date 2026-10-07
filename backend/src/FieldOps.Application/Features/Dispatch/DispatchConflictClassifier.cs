using System.Globalization;
using FieldOps.Application.Features.ServiceRequests;
using FieldOps.Application.Features.Team;
using FieldOps.Domain.WorkOrders;

namespace FieldOps.Application.Features.Dispatch;

/// <summary>The BR-10 result of one slot and selection: the conflicts, the skills check and the checks per technician.</summary>
public sealed record DispatchClassification(
    IReadOnlyList<DispatchConflict> Conflicts, DispatchCheck Skills, IReadOnlyList<TechnicianChecks> Checks);

/// <summary>
/// The single definition of the dispatch-calendar BR-10 conflicts, BR-09 impact and ranking. Pure: plain data in,
/// plain data out; the caller supplies the display zone, the day range and the visit being dispatched.
/// </summary>
public static class DispatchConflictClassifier
{
    private static readonly VisitStatus[] FreeStatuses =
        [VisitStatus.Unscheduled, VisitStatus.Cancelled, VisitStatus.Completed, VisitStatus.Approved];

    /// <summary>
    /// Conflicts of the slot <c>[start, end)</c> for the selected technicians. <c>missing_skills</c> is reported once
    /// for the selection; each technician gets one availability code (time off, then break, then outside
    /// availability) and one entry per overlapping commitment.
    /// </summary>
    public static DispatchClassification Classify(
        DateTimeOffset start,
        DateTimeOffset end,
        IReadOnlyList<DispatchSkill> required,
        IReadOnlyList<DispatchTechnician> selection,
        TimeZoneInfo displayZone,
        Guid? excludeVisitId)
    {
        var conflicts = new List<DispatchConflict>();
        var skills = EvaluateSkills(required, selection);

        if (!skills.Passed)
        {
            conflicts.Add(new DispatchConflict(null, DispatchCodes.MissingSkills, null, null, skills.Label));
        }

        var checks = new List<TechnicianChecks>(selection.Count);

        foreach (var technician in selection)
        {
            var availability = Availability(technician, start, end, displayZone);
            var overlaps = Overlaps(technician, start, end, displayZone, excludeVisitId);

            if (availability.Conflict is { } availabilityConflict)
            {
                conflicts.Add(availabilityConflict);
            }

            conflicts.AddRange(overlaps);

            checks.Add(new TechnicianChecks(
                technician.Id,
                availability.Check,
                overlaps.Count == 0
                    ? new DispatchCheck(true, null, "No overlapping jobs")
                    : new DispatchCheck(false, DispatchCodes.Overlap, overlaps[0].Label)));
        }

        return new DispatchClassification(conflicts, skills, checks);
    }

    /// <summary>
    /// BR-09: the load of the technician's local day after the assignment (jobs, scheduled and available minutes)
    /// and the previous and next commitment of that day. The visit being dispatched is excluded, the proposed one added.
    /// </summary>
    public static TechnicianImpact Impact(
        DispatchTechnician technician,
        DateTimeOffset start,
        DateTimeOffset end,
        DateTimeOffset dayFrom,
        DateTimeOffset dayTo,
        TimeZoneInfo displayZone,
        Guid? excludeVisitId)
    {
        var visits = technician.Commitments
            .Where(item => item.Kind == CommitmentKinds.Visit && item.VisitId != excludeVisitId && item.Status is not null)
            .Select(item => new AssignedVisit(item.Status!.Value, item.Start, item.End, null))
            .Append(new AssignedVisit(VisitStatus.Scheduled, start, end, null))
            .ToList();
        var workload = AssessmentSlotEvaluator.Workload(
            technician.Schedule with { Visits = visits }, AssessmentCommitments(technician), dayFrom, dayTo);
        var available = TechnicianAvailabilityCalculator.AvailableMinutes(technician.Schedule, dayFrom, dayTo);

        var neighbors = technician.Commitments
            .Where(item => IsBusy(item, excludeVisitId) && item.Start < dayTo && item.End > dayFrom)
            .ToList();
        var previous = neighbors.Where(item => item.End <= start).OrderByDescending(item => item.End).FirstOrDefault();
        var next = neighbors.Where(item => item.Start >= end).OrderBy(item => item.Start).FirstOrDefault();

        return new TechnicianImpact(
            technician.Id,
            workload.Jobs,
            workload.ScheduledMinutes,
            available,
            previous is null ? "No previous job" : $"Previous job: {Subject(previous)} ends {Time(previous.End, displayZone)}",
            next is null ? "No next job" : $"Next job: {Subject(next)} starts {Time(next.Start, displayZone)}");
    }

    /// <summary>
    /// BR-09 ranking of the candidates for the slot, each evaluated alone (skills included). Best match is the one
    /// candidate without conflicts and the lowest resulting load, ties by name.
    /// </summary>
    public static IReadOnlyList<TechnicianRanking> Rank(
        DateTimeOffset start,
        DateTimeOffset end,
        IReadOnlyList<DispatchSkill> required,
        IReadOnlyList<DispatchTechnician> candidates,
        DateTimeOffset dayFrom,
        DateTimeOffset dayTo,
        TimeZoneInfo displayZone,
        Guid? excludeVisitId)
    {
        var rows = candidates
            .Select(candidate =>
            {
                var conflictCount = Classify(start, end, required, [candidate], displayZone, excludeVisitId).Conflicts.Count;
                var impact = Impact(candidate, start, end, dayFrom, dayTo, displayZone, excludeVisitId);
                int? percent = impact.AvailableMinutes > 0
                    ? (int)Math.Round(impact.ScheduledMinutes / impact.AvailableMinutes * 100, MidpointRounding.AwayFromZero)
                    : null;

                return (candidate.Id, candidate.Name, ConflictCount: conflictCount, Percent: percent);
            })
            .ToList();

        var best = rows
            .Where(row => row.ConflictCount == 0)
            .OrderBy(row => row.Percent ?? int.MaxValue)
            .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.Id)
            .Select(row => (Guid?)row.Id)
            .FirstOrDefault();

        return
        [
            .. rows
                .OrderBy(row => row.ConflictCount)
                .ThenBy(row => row.Percent ?? int.MaxValue)
                .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.Id)
                .Select(row => new TechnicianRanking(row.Id, row.Id == best, row.ConflictCount, row.Percent)),
        ];
    }

    /// <summary>"h:mm – h:mm a"; both meridiems are shown when the range crosses noon.</summary>
    public static string Range(DateTimeOffset start, DateTimeOffset end, TimeZoneInfo zone)
    {
        var from = TimeZoneInfo.ConvertTime(start, zone);
        var to = TimeZoneInfo.ConvertTime(end, zone);

        return Range(TimeOnly.FromDateTime(from.DateTime), TimeOnly.FromDateTime(to.DateTime));
    }

    private static string Range(TimeOnly from, TimeOnly to)
    {
        var sameMeridiem = (from.Hour < 12) == (to.Hour < 12);

        return sameMeridiem
            ? $"{from.ToString("h:mm", CultureInfo.InvariantCulture)} – {to.ToString("h:mm tt", CultureInfo.InvariantCulture)}"
            : $"{from.ToString("h:mm tt", CultureInfo.InvariantCulture)} – {to.ToString("h:mm tt", CultureInfo.InvariantCulture)}";
    }

    private static string Time(DateTimeOffset instant, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTime(instant, zone).ToString("h:mm tt", CultureInfo.InvariantCulture);

    private static string Subject(DispatchCommitment commitment) =>
        commitment.Kind == CommitmentKinds.Assessment ? "Assessment" : $"#{commitment.DisplayNumber}";

    private static IReadOnlyList<SlotCommitment> AssessmentCommitments(DispatchTechnician technician) =>
        [.. technician.Commitments
            .Where(item => item.Kind == CommitmentKinds.Assessment)
            .Select(item => new SlotCommitment(item.Start, item.End, CommitmentKinds.Assessment, "Assessment", null))];

    private static bool IsBusy(DispatchCommitment item, Guid? excludeVisitId) =>
        item.Kind == CommitmentKinds.Assessment
        || (item.VisitId != excludeVisitId && item.Status is { } status && !FreeStatuses.Contains(status));

    private static DispatchCheck EvaluateSkills(IReadOnlyList<DispatchSkill> required, IReadOnlyList<DispatchTechnician> selection)
    {
        if (required.Count == 0)
        {
            return new DispatchCheck(true, null, "No required skills");
        }

        if (selection.Count == 0)
        {
            return new DispatchCheck(true, null, "Select technicians to check skills.");
        }

        var covered = selection.SelectMany(technician => technician.SkillIds).ToHashSet();
        var missing = required
            .Where(skill => !covered.Contains(skill.Id))
            .OrderBy(skill => skill.Name, StringComparer.OrdinalIgnoreCase)
            .Select(skill => skill.Name)
            .ToList();

        return missing.Count > 0
            ? new DispatchCheck(false, DispatchCodes.MissingSkills, $"Missing skills: {string.Join(", ", missing)}")
            : new DispatchCheck(
                true,
                null,
                $"Skills match ({string.Join(", ", required.Select(skill => skill.Name).OrderBy(name => name, StringComparer.OrdinalIgnoreCase))})");
    }

    /// <summary>Time off, then break, then outside availability; the first match wins (BR-10).</summary>
    private static (DispatchCheck Check, DispatchConflict? Conflict) Availability(
        DispatchTechnician technician, DateTimeOffset start, DateTimeOffset end, TimeZoneInfo displayZone)
    {
        var schedule = technician.Schedule;

        var timeOff = schedule.Exceptions
            .Where(item => !item.IsAvailable && item.StartsAt < end && item.EndsAt > start)
            .OrderBy(item => item.StartsAt)
            .Cast<AvailabilityOverride?>()
            .FirstOrDefault();

        if (timeOff is { } off)
        {
            return Failed(DispatchCodes.TimeOff, "Time off", technician.Id, off.StartsAt, off.EndsAt, displayZone);
        }

        var scheduleZone = BranchTime.FindZone(schedule.ZoneId);
        var date = BranchTime.LocalDate(start, scheduleZone);

        foreach (var slot in schedule.Slots.Where(item => item.DayOfWeek == (int)date.DayOfWeek))
        {
            foreach (var item in slot.Breaks.OrderBy(item => item.Start))
            {
                var breakStart = BranchTime.ToInstant(date, item.Start, scheduleZone);
                var breakEnd = BranchTime.ToInstant(date, item.End, scheduleZone);

                if (breakStart < end && breakEnd > start)
                {
                    return Failed(
                        DispatchCodes.Break,
                        $"On break {Range(item.Start, item.End)}",
                        technician.Id,
                        breakStart,
                        breakEnd,
                        displayZone);
                }
            }
        }

        var inside = TechnicianAvailabilityCalculator.AvailabilityRanges(schedule, start, end)
            .Any(range => range.Start <= start && range.End >= end);

        return inside
            ? (new DispatchCheck(true, null, "Available"), null)
            : Failed(DispatchCodes.OutsideAvailability, "Outside availability", technician.Id, null, null, displayZone);
    }

    private static (DispatchCheck Check, DispatchConflict? Conflict) Failed(
        string code,
        string label,
        Guid technicianId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        TimeZoneInfo displayZone) =>
        (
            new DispatchCheck(false, code, label),
            new DispatchConflict(
                technicianId,
                code,
                from is { } start ? TimeZoneInfo.ConvertTime(start, displayZone) : null,
                to is { } finish ? TimeZoneInfo.ConvertTime(finish, displayZone) : null,
                label));

    private static List<DispatchConflict> Overlaps(
        DispatchTechnician technician,
        DateTimeOffset start,
        DateTimeOffset end,
        TimeZoneInfo displayZone,
        Guid? excludeVisitId) =>
        [.. technician.Commitments
            .Where(item => IsBusy(item, excludeVisitId) && item.Start < end && item.End > start)
            .OrderBy(item => item.Start)
            .ThenBy(item => item.End)
            .Select(item => new DispatchConflict(
                technician.Id,
                DispatchCodes.Overlap,
                TimeZoneInfo.ConvertTime(item.Start, displayZone),
                TimeZoneInfo.ConvertTime(item.End, displayZone),
                item.Kind == CommitmentKinds.Assessment
                    ? $"Overlaps assessment {Range(item.Start, item.End, displayZone)}"
                    : $"Overlaps #{item.DisplayNumber} {Range(item.Start, item.End, displayZone)}"))];
}
