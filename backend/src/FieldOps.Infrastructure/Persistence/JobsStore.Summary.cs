using FieldOps.Application.Features.Access;
using FieldOps.Application.Features.ServiceRequests;
using FieldOps.Application.Features.WorkOrders;
using FieldOps.Domain.WorkOrders;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Persistence;

internal sealed partial class JobsStore
{
    /// <summary>
    /// BR-11: the four metrics of the effective branch set. The time zone work is done here in C# (the selected
    /// branch zone, else the organization zone), and the queries receive the day window and the delay limit as values.
    /// </summary>
    public async Task<JobsSummary> SummaryAsync(
        Guid organizationId, BranchScope scope, Guid? branchId, CancellationToken cancellationToken)
    {
        var organization = await ReadOrganizationAsync(organizationId, cancellationToken);
        var branchZone = branchId is { } selected
            ? await dbContext.Branches.AsNoTracking()
                .Where(branch => branch.OrganizationId == organizationId && branch.Id == selected)
                .Select(branch => branch.Timezone)
                .SingleOrDefaultAsync(cancellationToken)
            : null;
        var timezone = string.IsNullOrWhiteSpace(branchZone) ? organization.Timezone : branchZone;
        var zone = OrganizationTime.FindZone(timezone);
        var now = timeProvider.GetUtcNow();
        var today = OrganizationTime.LocalDate(now, zone);
        var start = OrganizationTime.StartOfDayUtc(today, zone);
        var end = OrganizationTime.StartOfDayUtc(today.AddDays(1), zone);

        var orders = ScopedOrders(organizationId, scope, branchId);

        var jobs = await orders
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Active = group.Count(order => order.Status == WorkOrderStatus.Scheduled || order.Status == WorkOrderStatus.InProgress),
                InProgress = group.Count(order => order.Status == WorkOrderStatus.InProgress),
                Ready = group.Count(order => order.Status == WorkOrderStatus.ReadyToSchedule),
            })
            .FirstOrDefaultAsync(cancellationToken);

        var todayVisits =
            from visit in dbContext.Visits.AsNoTracking()
            join order in orders on visit.WorkOrderId equals order.Id
            where visit.OrganizationId == organizationId
                && order.Status != WorkOrderStatus.Cancelled
                && visit.ScheduledStart >= start
                && visit.ScheduledStart < end
                && visit.Status != VisitStatus.Unscheduled
                && visit.Status != VisitStatus.Cancelled
            select visit;

        var metrics = await todayVisits
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Total = group.Count(),
                Assigned = group.Count(visit => visit.Status == VisitStatus.Assigned
                    || visit.Status == VisitStatus.OnTheWay
                    || visit.Status == VisitStatus.InProgress
                    || visit.Status == VisitStatus.Paused),
                Completed = group.Count(visit => visit.Status == VisitStatus.Completed
                    || visit.Status == VisitStatus.NeedsCorrection
                    || visit.Status == VisitStatus.Approved),
            })
            .FirstOrDefaultAsync(cancellationToken);

        var attention = await todayVisits
            .Where(JobsRules.NeedsAttention(now))
            .Select(visit => visit.WorkOrderId)
            .Distinct()
            .CountAsync(cancellationToken);

        return new JobsSummary(
            timezone,
            new JobsActiveMetric(jobs?.Active ?? 0, jobs?.InProgress ?? 0),
            jobs?.Ready ?? 0,
            new JobsTodayMetric(metrics?.Total ?? 0, metrics?.Assigned ?? 0, metrics?.Completed ?? 0),
            attention);
    }
}
