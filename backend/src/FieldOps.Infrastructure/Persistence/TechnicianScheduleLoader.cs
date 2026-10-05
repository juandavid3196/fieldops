using FieldOps.Application.Features.Team;
using FieldOps.Domain.Technicians;
using FieldOps.Domain.WorkOrders;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Persistence;

/// <summary>The profile data the loader needs to build one <see cref="TechnicianSchedule"/>.</summary>
internal sealed record ScheduleSubject(Guid Id, TechnicianStatus Status, string? BranchTimezone);

/// <summary>
/// Loads the plain schedule data of technician profiles (weekly windows, breaks, exceptions and assigned visits)
/// without tracking. Shared by the Team pages and the assessment planner; the caller supplies the range.
/// </summary>
internal static class TechnicianScheduleLoader
{
    private static readonly VisitStatus[] OnJobStatuses = [VisitStatus.OnTheWay, VisitStatus.InProgress, VisitStatus.Paused];

    /// <param name="onlyActiveExceptions">
    /// The Team pages keep their historical behavior (false); the assessment flow considers active exceptions only.
    /// </param>
    public static async Task<Dictionary<Guid, TechnicianSchedule>> LoadAsync(
        FieldOpsDbContext dbContext,
        Guid organizationId,
        IReadOnlyList<ScheduleSubject> subjects,
        string? organizationTimezone,
        string workOrderPrefix,
        DateTimeOffset low,
        DateTimeOffset high,
        bool onlyActiveExceptions,
        CancellationToken cancellationToken)
    {
        var ids = subjects.Select(row => row.Id).ToArray();

        var slotRows = await dbContext.TechnicianWeeklyAvailabilities.AsNoTracking()
            .Where(slot => ids.Contains(slot.TechnicianId))
            .Select(slot => new { slot.Id, slot.TechnicianId, slot.DayOfWeek, slot.StartTime, slot.EndTime, slot.CapacityPercent })
            .ToListAsync(cancellationToken);

        var slotIds = slotRows.Select(slot => slot.Id).ToArray();
        var breakRows = await dbContext.TechnicianBreaks.AsNoTracking()
            .Where(item => slotIds.Contains(item.AvailabilityId))
            .Select(item => new { item.AvailabilityId, item.StartTime, item.EndTime })
            .ToListAsync(cancellationToken);

        var exceptions = dbContext.TechnicianExceptions.AsNoTracking()
            .Where(item => ids.Contains(item.TechnicianId) && item.EndsAt > low && item.StartsAt < high);

        if (onlyActiveExceptions)
        {
            exceptions = exceptions.Where(item => item.Status == TechnicianExceptionStatus.Active);
        }

        var exceptionRows = await exceptions
            .Select(item => new { item.TechnicianId, item.StartsAt, item.EndsAt, item.IsAvailable })
            .ToListAsync(cancellationToken);

        // Only on-job visits carry their scope text; counted visits are limited to the window.
        var visitRows = await (
            from assignment in dbContext.VisitAssignments.AsNoTracking()
            join visit in dbContext.Visits.AsNoTracking() on assignment.VisitId equals visit.Id
            join workOrder in dbContext.WorkOrders.AsNoTracking() on visit.WorkOrderId equals workOrder.Id
            where ids.Contains(assignment.TechnicianId)
                && assignment.UnassignedAt == null
                && visit.OrganizationId == organizationId
                && visit.Status != VisitStatus.Unscheduled
                && visit.Status != VisitStatus.Cancelled
                && (OnJobStatuses.Contains(visit.Status)
                    || (visit.ScheduledStart >= low && visit.ScheduledStart < high))
            select new
            {
                assignment.TechnicianId,
                visit.Status,
                visit.ScheduledStart,
                visit.ScheduledEnd,
                workOrder.WorkOrderNumber,
                Scope = OnJobStatuses.Contains(visit.Status) ? workOrder.ScopeSnapshot : null,
            })
            .ToListAsync(cancellationToken);

        var breaksBySlot = breakRows
            .GroupBy(item => item.AvailabilityId)
            .ToDictionary(group => group.Key, group => group.Select(item => new TimeRange(item.StartTime, item.EndTime)).ToList());

        return subjects.ToDictionary(
            row => row.Id,
            row => new TechnicianSchedule(
                row.Status,
                BranchTime.ResolveZoneId(row.BranchTimezone, organizationTimezone),
                [.. slotRows.Where(slot => slot.TechnicianId == row.Id).Select(slot => new AvailabilitySlot(
                    slot.DayOfWeek,
                    slot.StartTime,
                    slot.EndTime,
                    slot.CapacityPercent,
                    breaksBySlot.TryGetValue(slot.Id, out var breaks) ? breaks : []))],
                [.. exceptionRows.Where(item => item.TechnicianId == row.Id)
                    .Select(item => new AvailabilityOverride(item.StartsAt, item.EndsAt, item.IsAvailable))],
                [.. visitRows.Where(item => item.TechnicianId == row.Id).Select(item => new AssignedVisit(
                    item.Status,
                    item.ScheduledStart,
                    item.ScheduledEnd,
                    item.Scope is null ? null : $"{workOrderPrefix}-{item.WorkOrderNumber} – {FirstLine(item.Scope)}"))]));
    }

    private static string FirstLine(string text) => text.Split('\n', 2)[0].Trim();
}
