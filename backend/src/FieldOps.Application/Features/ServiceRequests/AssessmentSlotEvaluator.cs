using FieldOps.Application.Features.Team;
using FieldOps.Domain.WorkOrders;

namespace FieldOps.Application.Features.ServiceRequests;

public static class CommitmentKinds
{
    public const string Assessment = "assessment";

    public const string Visit = "visit";
}

public static class SlotStates
{
    public const string Available = "available";

    public const string AvailableAfter = "available_after";

    public const string OutsideAvailability = "outside_availability";

    public const string Conflict = "conflict";

    public const string TimeOff = "time_off";
}

/// <summary>A scheduled assessment or an active visit of one technician; the label never carries customer data.</summary>
public sealed record SlotCommitment(DateTimeOffset Start, DateTimeOffset End, string Kind, string Label, Guid? AssessmentId);

/// <summary>The BR-05 slot state; <see cref="From"/> and <see cref="To"/> are set for a conflict only.</summary>
public sealed record SlotState(
    string State, DateTimeOffset? From, DateTimeOffset? To, DateTimeOffset? AvailableAfter, bool Blocking);

/// <summary>
/// The single definition of the schedule-assessment BR-05 slot state and BR-06 workload, shared by the planner,
/// the calendar and the schedule/reschedule mutations. Pure: plain data in, plain data out.
/// </summary>
public static class AssessmentSlotEvaluator
{
    /// <summary>Time off or conflict (BR-05, first match wins) for the slot, otherwise null.</summary>
    public static SlotState? Blocking(
        TechnicianSchedule schedule,
        IReadOnlyList<SlotCommitment> commitments,
        DateTimeOffset start,
        DateTimeOffset end,
        Guid? excludeAssessmentId = null)
    {
        if (schedule.Exceptions.Any(item => !item.IsAvailable && item.StartsAt < end && item.EndsAt > start))
        {
            return new SlotState(SlotStates.TimeOff, null, null, null, true);
        }

        var conflict = commitments
            .Where(item => (excludeAssessmentId is null || item.AssessmentId != excludeAssessmentId)
                && item.Start < end && item.End > start)
            .OrderBy(item => item.Start)
            .ThenBy(item => item.End)
            .Cast<SlotCommitment?>()
            .FirstOrDefault();

        return conflict is { } found ? new SlotState(SlotStates.Conflict, found.Start, found.End, null, true) : null;
    }

    /// <summary>
    /// The state of the slot <c>[start, end)</c>. Weekly windows and breaks use the schedule (branch) zone through the
    /// calculator; "available after" works on whole hours of the organization local day.
    /// </summary>
    public static SlotState Evaluate(
        TechnicianSchedule schedule,
        IReadOnlyList<SlotCommitment> commitments,
        DateTimeOffset start,
        DateTimeOffset end,
        TimeZoneInfo organizationZone,
        Guid? excludeAssessmentId = null)
    {
        if (Blocking(schedule, commitments, start, end, excludeAssessmentId) is { } blocking)
        {
            return blocking;
        }

        if (IsInsideAvailability(schedule, start, end))
        {
            return new SlotState(SlotStates.Available, null, null, null, false);
        }

        var duration = end - start;
        var local = TimeZoneInfo.ConvertTime(start, organizationZone);
        var date = DateOnly.FromDateTime(local.DateTime);

        for (var hour = local.Hour + 1; hour < 24; hour++)
        {
            var wall = date.ToDateTime(new TimeOnly(hour, 0), DateTimeKind.Unspecified);

            if (organizationZone.IsInvalidTime(wall))
            {
                continue;
            }

            var candidateStart = new DateTimeOffset(wall, organizationZone.GetUtcOffset(wall)).ToUniversalTime();
            var candidateEnd = candidateStart + duration;

            if (DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(candidateEnd, organizationZone).DateTime) != date)
            {
                break;
            }

            if (Blocking(schedule, commitments, candidateStart, candidateEnd, excludeAssessmentId) is null
                && IsInsideAvailability(schedule, candidateStart, candidateEnd))
            {
                return new SlotState(SlotStates.AvailableAfter, null, null, candidateStart, false);
            }
        }

        return new SlotState(SlotStates.OutsideAvailability, null, null, null, false);
    }

    /// <summary>BR-04 order of the technician cards: slot state first.</summary>
    public static int Rank(string state) => state switch
    {
        SlotStates.Available => 0,
        SlotStates.AvailableAfter => 1,
        SlotStates.OutsideAvailability => 2,
        SlotStates.Conflict => 3,
        SlotStates.TimeOff => 4,
        _ => 5,
    };

    /// <summary>
    /// BR-06: the team workload formula over counted visits plus scheduled assessments starting in the period. Only
    /// assessment commitments are added; visits are already in <see cref="TechnicianSchedule.Visits"/>.
    /// </summary>
    public static PeriodWorkload Workload(
        TechnicianSchedule schedule,
        IReadOnlyList<SlotCommitment> commitments,
        DateTimeOffset from,
        DateTimeOffset to,
        Guid? excludeAssessmentId = null)
    {
        var counted = new List<AssignedVisit>(schedule.Visits);

        counted.AddRange(commitments
            .Where(item => item.Kind == CommitmentKinds.Assessment
                && (excludeAssessmentId is null || item.AssessmentId != excludeAssessmentId))
            .Select(item => new AssignedVisit(VisitStatus.Scheduled, item.Start, item.End, null)));

        return TechnicianAvailabilityCalculator.PeriodWorkloadFor(
            counted, from, to, TechnicianAvailabilityCalculator.AvailableMinutes(schedule, from, to));
    }

    private static bool IsInsideAvailability(TechnicianSchedule schedule, DateTimeOffset start, DateTimeOffset end) =>
        TechnicianAvailabilityCalculator.AvailabilityRanges(schedule, start, end)
            .Any(range => range.Start <= start && range.End >= end);
}
