using FieldOps.Application.Features.Access;
using FieldOps.Application.Features.ServiceRequests;
using FieldOps.Application.Features.Team;
using FieldOps.Domain.WorkOrders;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Persistence;

/// <summary>
/// Participation history of one technician (technician-profile-overview-history BR-12 to BR-17). The technician is
/// resolved through the organization and branch scope first; visits are then reached only through work orders of that
/// organization and scope. Filtering, sorting and paging run in the database with a fixed number of queries.
/// </summary>
internal sealed class TechnicianJobHistoryStore(FieldOpsDbContext dbContext) : ITechnicianJobHistoryStore
{
    public async Task<JobHistoryData?> GetAsync(
        Guid organizationId,
        BranchScope scope,
        Guid technicianId,
        JobHistoryQuery query,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var profiles = dbContext.TechnicianProfiles.AsNoTracking()
            .Where(profile => profile.OrganizationId == organizationId && profile.Id == technicianId);

        if (!scope.All)
        {
            var ids = scope.BranchIds.ToArray();
            profiles = profiles.Where(profile => ids.Contains(profile.BranchId));
        }

        var home = await (
            from profile in profiles
            join branch in dbContext.Branches.AsNoTracking() on profile.BranchId equals branch.Id
            select branch.Timezone)
            .ToListAsync(cancellationToken);

        if (home.Count == 0)
        {
            return null;
        }

        var organization = await dbContext.Organizations.AsNoTracking()
            .Where(item => item.Id == organizationId)
            .Select(item => new { item.Timezone, item.WorkOrderPrefix })
            .SingleAsync(cancellationToken);

        var zoneId = BranchTime.ResolveZoneId(home[0], organization.Timezone);
        var zone = BranchTime.FindZone(zoneId);
        var (from, today) = JobHistoryRules.Bounds(query.Period, now, zone);

        // Local-day bounds as instants: DST-aware and open at the top so future-dated rows are excluded.
        var upper = BranchTime.ToInstant(today.AddDays(1), TimeOnly.MinValue, zone);
        DateTimeOffset? lower = from is { } start ? BranchTime.ToInstant(start, TimeOnly.MinValue, zone) : null;

        var rows = PeriodRows(organizationId, scope, technicianId, now, zoneId, lower, upper);

        var summaryRows = await rows
            .Select(row => new
            {
                row.Outcome,
                row.Seconds,
                row.ScheduledStart,
                row.ArrivalWindowEnd,
                row.ActualStartedAt,
                row.ActualCompletedAt,
                row.PauseSeconds,
            })
            .ToListAsync(cancellationToken);

        var filtered = rows;

        if (query.Status is { } status)
        {
            var code = (int)status + 1;
            filtered = filtered.Where(row => row.Outcome == code);
        }

        if (query.Search is { } term)
        {
            var pattern = RequestSearch.ContainsPattern(term);
            var prefix = organization.WorkOrderPrefix;

            filtered = filtered.Where(row =>
                EF.Functions.ILike(prefix + "-" + row.WorkOrderNumber.ToString(), pattern)
                || EF.Functions.ILike(row.Title, pattern)
                || EF.Functions.ILike(row.PropertyName, pattern)
                || EF.Functions.ILike(row.City, pattern));
        }

        var total = await filtered.CountAsync(cancellationToken);
        var items = new List<JobHistoryRow>();

        if (query.Skip < total)
        {
            var ordered = query.Newest
                ? filtered.OrderByDescending(row => row.LocalDay).ThenByDescending(row => row.ScheduledStart).ThenByDescending(row => row.VisitId)
                : filtered.OrderBy(row => row.LocalDay).ThenBy(row => row.ScheduledStart).ThenBy(row => row.VisitId);

            var page = await ordered
                .Skip((int)query.Skip)
                .Take(query.PageSize)
                .Select(row => new
                {
                    row.VisitId,
                    row.WorkOrderId,
                    row.WorkOrderNumber,
                    row.Title,
                    row.VisitNumber,
                    row.VisitCount,
                    row.PropertyName,
                    row.City,
                    row.Instant,
                    row.Seconds,
                    row.Outcome,
                })
                .ToListAsync(cancellationToken);

            items = [.. page.Select(row => new JobHistoryRow(
                row.VisitId,
                row.WorkOrderId,
                RequestCardRules.DisplayNumber(organization.WorkOrderPrefix, row.WorkOrderNumber),
                row.Title,
                row.VisitNumber,
                row.VisitCount,
                row.PropertyName,
                row.City,
                BranchTime.LocalDate(row.Instant, zone),
                Minutes(row.Seconds),
                ToOutcome(row.Outcome)))];
        }

        return new JobHistoryData(
            zoneId,
            today,
            from,
            [.. summaryRows.Select(row => new JobHistorySummaryRow(
                ToOutcome(row.Outcome),
                Minutes(row.Seconds),
                row.ScheduledStart,
                row.ArrivalWindowEnd,
                row.ActualStartedAt,
                row.ActualCompletedAt,
                row.PauseSeconds))],
            items,
            total);
    }

    private static int Minutes(double seconds) => (int)Math.Floor(seconds / 60d);

    private static JobOutcome ToOutcome(int code) => (JobOutcome)(code - 1);

    /// <summary>
    /// BR-12 to BR-15: one row per participating visit of the organization's in-scope work orders with the outcome
    /// (1 completed, 2 reassigned, 3 cancelled; unfinished assigned visits are excluded), the participation instant and
    /// the technician's own work and travel seconds. The period bounds apply to the participation instant.
    /// </summary>
    private IQueryable<PeriodRow> PeriodRows(
        Guid organizationId,
        BranchScope scope,
        Guid technicianId,
        DateTimeOffset now,
        string zoneId,
        DateTimeOffset? lower,
        DateTimeOffset upper)
    {
        var workOrders = dbContext.WorkOrders.AsNoTracking().Where(order => order.OrganizationId == organizationId);

        if (!scope.All)
        {
            var ids = scope.BranchIds.ToArray();
            workOrders = workOrders.Where(order => ids.Contains(order.BranchId));
        }

        var query =
            from visit in dbContext.Visits.AsNoTracking()
            join order in workOrders on visit.WorkOrderId equals order.Id
            join property in dbContext.Properties.AsNoTracking() on order.PropertyId equals property.Id
            where visit.OrganizationId == organizationId
                && property.OrganizationId == organizationId
                && (dbContext.VisitAssignments.Any(item => item.VisitId == visit.Id && item.TechnicianId == technicianId)
                    || dbContext.VisitTimeEntries.Any(item => item.VisitId == visit.Id && item.TechnicianId == technicianId))
            let hasActive = dbContext.VisitAssignments.Any(
                item => item.VisitId == visit.Id && item.TechnicianId == technicianId && item.UnassignedAt == null)
            let instant = visit.ScheduledStart
                ?? visit.ActualStartedAt
                ?? dbContext.VisitTimeEntries
                    .Where(item => item.VisitId == visit.Id && item.TechnicianId == technicianId)
                    .Min(item => (DateTimeOffset?)item.StartedAt)
                ?? dbContext.VisitAssignments
                    .Where(item => item.VisitId == visit.Id && item.TechnicianId == technicianId)
                    .Max(item => (DateTimeOffset?)item.AssignedAt)
                ?? visit.CreatedAt
            let outcome = visit.Status == VisitStatus.Cancelled
                ? 3
                : hasActive
                    ? (visit.Status == VisitStatus.Completed
                        || visit.Status == VisitStatus.NeedsCorrection
                        || visit.Status == VisitStatus.Approved ? 1 : 0)
                    : 2
            where outcome != 0 && instant < upper
            select new PeriodRow
            {
                VisitId = visit.Id,
                WorkOrderId = order.Id,
                WorkOrderNumber = order.WorkOrderNumber,
                Title = order.Title,
                VisitNumber = visit.VisitNumber,
                VisitCount = dbContext.Visits.Count(other => other.WorkOrderId == order.Id),
                PropertyName = property.Name,
                City = property.City,
                ScheduledStart = visit.ScheduledStart,
                ArrivalWindowEnd = visit.ArrivalWindowEnd,
                ActualStartedAt = visit.ActualStartedAt,
                ActualCompletedAt = visit.ActualCompletedAt,
                PauseSeconds = visit.PauseSeconds,
                Instant = instant,
                LocalDay = FieldOpsDbContext.LocalTimestamp(zoneId, instant).Date,
                Outcome = outcome,
                Seconds = dbContext.VisitTimeEntries
                    .Where(item => item.VisitId == visit.Id
                        && item.TechnicianId == technicianId
                        && (item.EntryType == VisitTimeEntryType.Work || item.EntryType == VisitTimeEntryType.Travel))
                    .Sum(item => ((item.EndedAt ?? now) - item.StartedAt).TotalSeconds),
            };

        return lower is { } floor ? query.Where(row => row.Instant >= floor) : query;
    }

    private sealed class PeriodRow
    {
        public Guid VisitId { get; init; }

        public Guid WorkOrderId { get; init; }

        public long WorkOrderNumber { get; init; }

        public string Title { get; init; } = string.Empty;

        public int VisitNumber { get; init; }

        public int VisitCount { get; init; }

        public string PropertyName { get; init; } = string.Empty;

        public string City { get; init; } = string.Empty;

        public DateTimeOffset? ScheduledStart { get; init; }

        public DateTimeOffset? ArrivalWindowEnd { get; init; }

        public DateTimeOffset? ActualStartedAt { get; init; }

        public DateTimeOffset? ActualCompletedAt { get; init; }

        public int PauseSeconds { get; init; }

        public DateTimeOffset Instant { get; init; }

        public DateTime LocalDay { get; init; }

        public int Outcome { get; init; }

        public double Seconds { get; init; }
    }
}
