using FieldOps.Application.Features.Access;
using FieldOps.Application.Features.OperationsDashboard;
using FieldOps.Application.Features.ServiceRequests;
using FieldOps.Application.Features.Team;
using FieldOps.Domain.Requests;
using FieldOps.Domain.Technicians;
using FieldOps.Domain.WorkOrders;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Persistence;

/// <summary>
/// Operations dashboard reads (operations-dashboard). No-tracking, read-only and always organization first, then the
/// effective branch set of the work order (BR-02). Every collection is loaded in one batched query per concern.
/// </summary>
internal sealed class OperationsDashboardStore(FieldOpsDbContext dbContext) : IOperationsDashboardStore
{
    private static readonly VisitStatus[] CompletedGroup = [VisitStatus.Completed, VisitStatus.NeedsCorrection, VisitStatus.Approved];

    public async Task<OperationsOptionsView> GetOptionsAsync(
        Guid organizationId, BranchScope scope, CancellationToken cancellationToken)
    {
        var organizationTimezone = await OrganizationTimezoneAsync(organizationId, cancellationToken);
        var branches = await VisibleBranches(organizationId, scope)
            .OrderBy(branch => branch.Name)
            .ThenBy(branch => branch.Id)
            .Select(branch => new DashboardBranchOption(branch.Id, branch.Name, branch.Timezone))
            .ToListAsync(cancellationToken);
        var branchIds = branches.Select(branch => branch.Id).ToArray();

        var technicians = await dbContext.TechnicianProfiles.AsNoTracking()
            .Where(profile => profile.OrganizationId == organizationId
                && profile.Status == TechnicianStatus.Active
                && branchIds.Contains(profile.BranchId))
            .OrderBy(profile => profile.FirstName)
            .ThenBy(profile => profile.LastName)
            .ThenBy(profile => profile.Id)
            .Select(profile => new { profile.Id, profile.FirstName, profile.LastName, profile.BranchId })
            .ToListAsync(cancellationToken);

        var categories = await dbContext.ServiceCategories.AsNoTracking()
            .Where(category => category.OrganizationId == organizationId && category.IsActive)
            .OrderBy(category => category.Name)
            .ThenBy(category => category.Id)
            .Select(category => new DashboardCategoryOption(category.Id, category.Name))
            .ToListAsync(cancellationToken);

        return new OperationsOptionsView(
            branches,
            organizationTimezone,
            [.. technicians.Select(item => new DashboardTechnicianOption(item.Id, FullName(item.FirstName, item.LastName), item.BranchId))],
            categories);
    }

    public async Task<DashboardScopeContext?> ResolveAsync(
        Guid organizationId, BranchScope scope, Guid? branchId, CancellationToken cancellationToken)
    {
        var organizationTimezone = await OrganizationTimezoneAsync(organizationId, cancellationToken);
        var branches = VisibleBranches(organizationId, scope);

        if (branchId is { } id)
        {
            var branch = await branches
                .Where(candidate => candidate.Id == id)
                .Select(candidate => new { candidate.Timezone })
                .SingleOrDefaultAsync(cancellationToken);

            return branch is null
                ? null
                : new DashboardScopeContext(BranchTime.ResolveZoneId(branch.Timezone, organizationTimezone), [id]);
        }

        var ids = await branches.OrderBy(branch => branch.Id).Select(branch => branch.Id).ToListAsync(cancellationToken);

        return new DashboardScopeContext(BranchTime.ResolveZoneId(null, organizationTimezone), ids);
    }

    public Task<bool> TechnicianInScopeAsync(
        Guid organizationId, IReadOnlyList<Guid> branchIds, Guid technicianId, CancellationToken cancellationToken)
    {
        var ids = branchIds.ToArray();

        return dbContext.TechnicianProfiles.AsNoTracking().AnyAsync(
            profile => profile.OrganizationId == organizationId
                && profile.Id == technicianId
                && profile.Status == TechnicianStatus.Active
                && ids.Contains(profile.BranchId),
            cancellationToken);
    }

    public Task<bool> CategoryIsActiveAsync(Guid organizationId, Guid categoryId, CancellationToken cancellationToken) =>
        dbContext.ServiceCategories.AsNoTracking().AnyAsync(
            category => category.OrganizationId == organizationId && category.Id == categoryId && category.IsActive,
            cancellationToken);

    public async Task<DashboardSource> LoadDashboardAsync(
        Guid organizationId, DashboardQuery query, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var zone = OrganizationTime.FindZone(query.TimezoneId);
        var dayStart = OrganizationTime.StartOfDayUtc(query.Date, zone);
        var dayEnd = OrganizationTime.StartOfDayUtc(query.Date.AddDays(1), zone);
        var branchIds = query.BranchIds.ToArray();

        var organization = await dbContext.Organizations.AsNoTracking()
            .Where(candidate => candidate.Id == organizationId)
            .Select(candidate => new { candidate.Timezone, candidate.WorkOrderPrefix })
            .SingleAsync(cancellationToken);

        var visits = await LoadDayVisitsAsync(organizationId, query, branchIds, dayStart, dayEnd, organization.WorkOrderPrefix, cancellationToken);

        // Same weekday one week earlier (BR-09): only the fields of the arrival KPI.
        var previousStart = OrganizationTime.StartOfDayUtc(query.Date.AddDays(-7), zone);
        var previousEnd = OrganizationTime.StartOfDayUtc(query.Date.AddDays(-6), zone);
        var previous = await ScopedVisits(organizationId, branchIds, query)
            .Where(visit => visit.Status != VisitStatus.Unscheduled
                && visit.Status != VisitStatus.Cancelled
                && visit.ScheduledStart >= previousStart
                && visit.ScheduledStart < previousEnd)
            .Select(visit => new { visit.Status, visit.ScheduledStart, visit.ArrivalWindowEnd, visit.ActualStartedAt })
            .ToListAsync(cancellationToken);

        var technicians = await LoadTechniciansAsync(
            organizationId, query, branchIds, organization.Timezone, dayStart, dayEnd, cancellationToken);
        var durations = await LoadDurationsAsync(organizationId, query, branchIds, zone, cancellationToken);
        var events = await LoadEventsAsync(organizationId, query, branchIds, dayStart, dayEnd, organization.WorkOrderPrefix, cancellationToken);

        return new DashboardSource(
            visits,
            [.. previous.Select(row => new DashboardArrival(row.Status, row.ScheduledStart!.Value, row.ArrivalWindowEnd, row.ActualStartedAt))],
            technicians,
            durations,
            events);
    }

    public async Task<PerformanceSource> LoadPerformanceAsync(
        Guid organizationId, DashboardQuery query, int days, CancellationToken cancellationToken)
    {
        var zone = OrganizationTime.FindZone(query.TimezoneId);
        var windowStart = OrganizationTime.StartOfDayUtc(query.Date.AddDays(1 - days), zone);
        var windowEnd = OrganizationTime.StartOfDayUtc(query.Date.AddDays(1), zone);
        var branchIds = query.BranchIds.ToArray();
        var scoped = ScopedVisits(organizationId, branchIds, query);

        // Jobs completed: visits scheduled in the period (BR-14).
        var inPeriod = scoped.Where(visit => visit.Status != VisitStatus.Unscheduled
            && visit.Status != VisitStatus.Cancelled
            && visit.ScheduledStart >= windowStart
            && visit.ScheduledStart < windowEnd);
        var total = await inPeriod.CountAsync(cancellationToken);
        var completed = await inPeriod.CountAsync(visit => CompletedGroup.Contains(visit.Status), cancellationToken);

        var completions = await scoped
            .Where(visit => CompletedGroup.Contains(visit.Status) && visit.ActualCompletedAt >= windowStart && visit.ActualCompletedAt < windowEnd)
            .Select(visit => visit.ActualCompletedAt!.Value)
            .ToListAsync(cancellationToken);

        var (fixedCount, fixTotal) = await FirstTimeFixAsync(organizationId, query, branchIds, windowStart, windowEnd, cancellationToken);

        var orders = ScopedOrders(organizationId, branchIds, query.CategoryId);
        var reviews = dbContext.InvoiceReviews.AsNoTracking()
            .Where(review => review.OrganizationId == organizationId
                && review.CreatedAt >= windowStart
                && review.CreatedAt < windowEnd
                && orders.Any(order => order.Id == review.WorkOrderId));

        if (query.TechnicianId is { } technicianId)
        {
            reviews = reviews.Where(review => review.TechnicianId == technicianId);
        }

        var ratings = await reviews.Select(review => (int)review.Rating).ToListAsync(cancellationToken);

        return new PerformanceSource(
            completed,
            total,
            fixedCount,
            fixTotal,
            ratings.Count,
            ratings.Count > 0 ? Math.Round((decimal)ratings.Sum() / ratings.Count, 1, MidpointRounding.AwayFromZero) : null,
            completions);
    }

    private async Task<(int Fixed, int Total)> FirstTimeFixAsync(
        Guid organizationId,
        DashboardQuery query,
        Guid[] branchIds,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        CancellationToken cancellationToken)
    {
        // One-time work orders finished in the scope whose latest completion falls in the period (BR-14).
        var candidates = ScopedOrders(organizationId, branchIds, query.CategoryId)
            .Where(order => order.JobType == WorkOrderJobTypes.OneTime
                && (order.Status == WorkOrderStatus.Completed || order.Status == WorkOrderStatus.ApprovedForBilling));

        if (query.TechnicianId is { } technicianId)
        {
            candidates = candidates.Where(order => dbContext.Visits.Any(visit =>
                visit.OrganizationId == organizationId
                && visit.WorkOrderId == order.Id
                && dbContext.VisitAssignments.Any(assignment =>
                    assignment.VisitId == visit.Id && assignment.TechnicianId == technicianId && assignment.UnassignedAt == null)));
        }

        var visits = await (
            from visit in dbContext.Visits.AsNoTracking()
            where visit.OrganizationId == organizationId
                && candidates.Any(order => order.Id == visit.WorkOrderId)
                && dbContext.Visits.Any(other => other.OrganizationId == organizationId
                    && other.WorkOrderId == visit.WorkOrderId
                    && other.ActualCompletedAt >= windowStart
                    && other.ActualCompletedAt < windowEnd)
            select new { visit.Id, visit.WorkOrderId, visit.Status, visit.ActualCompletedAt })
            .ToListAsync(cancellationToken);

        var visitIds = visits.Select(visit => visit.Id).ToArray();
        var corrected = await dbContext.VisitStatusHistories.AsNoTracking()
            .Where(history => visitIds.Contains(history.VisitId) && history.ToStatus == VisitStatus.NeedsCorrection)
            .Select(history => history.VisitId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var correctedSet = corrected.ToHashSet();

        var perOrder = visits
            .GroupBy(visit => visit.WorkOrderId)
            .Where(group => group.Max(visit => visit.ActualCompletedAt) is { } latest && latest >= windowStart && latest < windowEnd)
            .Select(group => group.Count(visit => visit.Status != VisitStatus.Cancelled) == 1
                && !group.Any(visit => correctedSet.Contains(visit.Id)))
            .ToList();

        return (perOrder.Count(isFixed => isFixed), perOrder.Count);
    }

    private async Task<List<DashboardVisit>> LoadDayVisitsAsync(
        Guid organizationId,
        DashboardQuery query,
        Guid[] branchIds,
        DateTimeOffset dayStart,
        DateTimeOffset dayEnd,
        string prefix,
        CancellationToken cancellationToken)
    {
        var rows = await (
            from visit in ScopedVisits(organizationId, branchIds, query)
            join order in dbContext.WorkOrders.AsNoTracking()
                on new { visit.OrganizationId, Id = visit.WorkOrderId } equals new { order.OrganizationId, order.Id }
            join customer in dbContext.Customers.AsNoTracking()
                on new { order.OrganizationId, Id = order.CustomerId } equals new { customer.OrganizationId, customer.Id }
            where visit.Status != VisitStatus.Unscheduled
                && visit.Status != VisitStatus.Cancelled
                && visit.ScheduledStart >= dayStart
                && visit.ScheduledStart < dayEnd
                && visit.ScheduledEnd != null
            select new
            {
                VisitId = visit.Id,
                WorkOrderId = order.Id,
                order.WorkOrderNumber,
                order.Title,
                order.BranchId,
                order.CustomerId,
                CustomerName = customer.DisplayName,
                order.EstimatedDurationMinutes,
                visit.Status,
                visit.ScheduledStart,
                visit.ScheduledEnd,
                visit.ArrivalWindowEnd,
                visit.ActualStartedAt,
                visit.ActualCompletedAt,
                visit.PauseSeconds,
            })
            .ToListAsync(cancellationToken);

        var visitIds = rows.Select(row => row.VisitId).ToArray();
        var assignees = await ActiveAssigneesAsync(organizationId, visitIds, cancellationToken);

        // Only on-the-way rows expose the primary active contact phone (tenant isolation, spec data rules).
        var onTheWayCustomers = rows.Where(row => row.Status == VisitStatus.OnTheWay).Select(row => row.CustomerId).Distinct().ToArray();
        var phones = onTheWayCustomers.Length == 0
            ? []
            : await dbContext.CustomerContacts.AsNoTracking()
                .Where(contact => contact.OrganizationId == organizationId
                    && onTheWayCustomers.Contains(contact.CustomerId)
                    && contact.IsPrimary
                    && contact.IsActive
                    && contact.Phone != null)
                .Select(contact => new { contact.CustomerId, contact.Phone })
                .ToListAsync(cancellationToken);

        return [.. rows.Select(row => new DashboardVisit(
            row.VisitId,
            row.WorkOrderId,
            row.WorkOrderNumber,
            RequestCardRules.DisplayNumber(prefix, row.WorkOrderNumber),
            row.Title,
            row.CustomerName,
            phones.FirstOrDefault(contact => contact.CustomerId == row.CustomerId)?.Phone,
            row.BranchId,
            row.Status,
            row.ScheduledStart!.Value,
            row.ScheduledEnd!.Value,
            row.ArrivalWindowEnd,
            row.ActualStartedAt,
            row.ActualCompletedAt,
            row.PauseSeconds,
            row.EstimatedDurationMinutes,
            assignees.GetValueOrDefault(row.VisitId) ?? []))];
    }

    /// <summary>Active assignments per visit with the technician full name, primary first.</summary>
    private async Task<Dictionary<Guid, List<DashboardAssignee>>> ActiveAssigneesAsync(
        Guid organizationId, Guid[] visitIds, CancellationToken cancellationToken)
    {
        if (visitIds.Length == 0)
        {
            return [];
        }

        var rows = await (
            from assignment in dbContext.VisitAssignments.AsNoTracking()
            join profile in dbContext.TechnicianProfiles.AsNoTracking() on assignment.TechnicianId equals profile.Id
            where visitIds.Contains(assignment.VisitId) && assignment.UnassignedAt == null && profile.OrganizationId == organizationId
            orderby assignment.IsPrimary descending, assignment.AssignedAt, assignment.Id
            select new { assignment.VisitId, profile.Id, profile.FirstName, profile.LastName })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(row => row.VisitId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(row => new DashboardAssignee(row.Id, FullName(row.FirstName, row.LastName))).ToList());
    }

    private async Task<List<DashboardTechnician>> LoadTechniciansAsync(
        Guid organizationId,
        DashboardQuery query,
        Guid[] branchIds,
        string? organizationTimezone,
        DateTimeOffset dayStart,
        DateTimeOffset dayEnd,
        CancellationToken cancellationToken)
    {
        var profilesQuery =
            from profile in dbContext.TechnicianProfiles.AsNoTracking()
            join branch in dbContext.Branches.AsNoTracking() on profile.BranchId equals branch.Id
            where profile.OrganizationId == organizationId
                && branch.OrganizationId == organizationId
                && profile.Status == TechnicianStatus.Active
                && branchIds.Contains(profile.BranchId)
            select new { profile.Id, profile.BranchId, profile.FirstName, profile.LastName, profile.Status, profile.ColorHex, BranchTimezone = branch.Timezone };

        if (query.TechnicianId is { } technicianId)
        {
            profilesQuery = profilesQuery.Where(profile => profile.Id == technicianId);
        }

        var profiles = await profilesQuery
            .OrderBy(profile => profile.FirstName)
            .ThenBy(profile => profile.LastName)
            .ThenBy(profile => profile.Id)
            .ToListAsync(cancellationToken);

        if (profiles.Count == 0)
        {
            return [];
        }

        var ids = profiles.Select(profile => profile.Id).ToArray();

        // Each technician keeps their own schedule zone (branch, then organization, then UTC); category filters never apply here (BR-05).
        var schedules = await TechnicianScheduleLoader.LoadAsync(
            dbContext,
            organizationId,
            [.. profiles.Select(profile => new ScheduleSubject(profile.Id, profile.Status, profile.BranchTimezone))],
            organizationTimezone,
            string.Empty,
            dayStart.AddDays(-1),
            dayEnd.AddDays(1),
            onlyActiveExceptions: true,
            cancellationToken);

        var skills = await (
            from link in dbContext.TechnicianSkills.AsNoTracking()
            join skill in dbContext.Skills.AsNoTracking() on link.SkillId equals skill.Id
            where ids.Contains(link.TechnicianId) && skill.OrganizationId == organizationId
            select new { link.TechnicianId, skill.Name, link.IsPrimary })
            .ToListAsync(cancellationToken);

        var assessments = await dbContext.Assessments.AsNoTracking()
            .Where(assessment => assessment.OrganizationId == organizationId
                && assessment.TechnicianId != null
                && ids.Contains(assessment.TechnicianId.Value)
                && assessment.Status == AssessmentStatus.Scheduled
                && assessment.ScheduledStart < dayEnd
                && assessment.ScheduledEnd > dayStart)
            .Select(assessment => new { TechnicianId = assessment.TechnicianId!.Value, assessment.ScheduledStart, assessment.ScheduledEnd })
            .ToListAsync(cancellationToken);

        return [.. profiles.Select(profile => new DashboardTechnician(
            profile.Id,
            profile.BranchId,
            FullName(profile.FirstName, profile.LastName),
            profile.ColorHex,
            schedules[profile.Id],
            [.. skills.Where(row => row.TechnicianId == profile.Id).Select(row => new DashboardSkill(row.Name, row.IsPrimary))],
            [.. assessments.Where(row => row.TechnicianId == profile.Id).Select(row => new DashboardInterval(row.ScheduledStart, row.ScheduledEnd))]))];
    }

    private async Task<List<DashboardDuration>> LoadDurationsAsync(
        Guid organizationId, DashboardQuery query, Guid[] branchIds, TimeZoneInfo zone, CancellationToken cancellationToken)
    {
        // The 7 local days ending on the selected date (BR-13).
        var windowStart = OrganizationTime.StartOfDayUtc(query.Date.AddDays(-6), zone);
        var windowEnd = OrganizationTime.StartOfDayUtc(query.Date.AddDays(1), zone);

        var rows = await (
            from visit in ScopedVisits(organizationId, branchIds, query)
            join order in dbContext.WorkOrders.AsNoTracking()
                on new { visit.OrganizationId, Id = visit.WorkOrderId } equals new { order.OrganizationId, order.Id }
            join category in dbContext.ServiceCategories.AsNoTracking()
                on new { order.OrganizationId, Id = order.ServiceCategoryId } equals new { category.OrganizationId, category.Id }
            where CompletedGroup.Contains(visit.Status)
                && visit.ActualStartedAt != null
                && visit.ActualCompletedAt >= windowStart
                && visit.ActualCompletedAt < windowEnd
                && order.EstimatedDurationMinutes != null
            select new
            {
                CategoryId = category.Id,
                CategoryName = category.Name,
                Estimated = order.EstimatedDurationMinutes!.Value,
                Started = visit.ActualStartedAt!.Value,
                Completed = visit.ActualCompletedAt!.Value,
                visit.PauseSeconds,
            })
            .ToListAsync(cancellationToken);

        return [.. rows.Select(row => new DashboardDuration(
            row.CategoryId, row.CategoryName, row.Estimated, row.Started, row.Completed, row.PauseSeconds))];
    }

    private async Task<List<DashboardEvent>> LoadEventsAsync(
        Guid organizationId,
        DashboardQuery query,
        Guid[] branchIds,
        DateTimeOffset dayStart,
        DateTimeOffset dayEnd,
        string prefix,
        CancellationToken cancellationToken)
    {
        var scoped = ScopedVisits(organizationId, branchIds, query);

        var statusRows = await (
            from history in dbContext.VisitStatusHistories.AsNoTracking()
            join visit in scoped on history.VisitId equals visit.Id
            join order in dbContext.WorkOrders.AsNoTracking()
                on new { visit.OrganizationId, Id = visit.WorkOrderId } equals new { order.OrganizationId, order.Id }
            where history.ChangedAt >= dayStart
                && history.ChangedAt < dayEnd
                && (history.ToStatus == VisitStatus.OnTheWay
                    || history.ToStatus == VisitStatus.Completed
                    || (history.ToStatus == VisitStatus.InProgress && history.FromStatus != VisitStatus.Paused))
            orderby history.ChangedAt descending, history.Id
            select new { VisitId = visit.Id, WorkOrderId = order.Id, order.WorkOrderNumber, order.Title, history.ToStatus, At = history.ChangedAt })
            .Take(OperationsDashboardMessages.MaxActivityEvents)
            .ToListAsync(cancellationToken);

        var assignmentRows = await (
            from assignment in dbContext.VisitAssignments.AsNoTracking()
            join visit in scoped on assignment.VisitId equals visit.Id
            join order in dbContext.WorkOrders.AsNoTracking()
                on new { visit.OrganizationId, Id = visit.WorkOrderId } equals new { order.OrganizationId, order.Id }
            join profile in dbContext.TechnicianProfiles.AsNoTracking() on assignment.TechnicianId equals profile.Id
            where assignment.IsPrimary
                && assignment.AssignedAt >= dayStart
                && assignment.AssignedAt < dayEnd
                && profile.OrganizationId == organizationId
            orderby assignment.AssignedAt descending, assignment.Id
            select new
            {
                VisitId = visit.Id,
                WorkOrderId = order.Id,
                order.WorkOrderNumber,
                order.Title,
                At = assignment.AssignedAt,
                profile.FirstName,
                profile.LastName,
                Reassigned = dbContext.VisitAssignments.Any(earlier =>
                    earlier.VisitId == assignment.VisitId && earlier.IsPrimary && earlier.AssignedAt < assignment.AssignedAt),
            })
            .Take(OperationsDashboardMessages.MaxActivityEvents)
            .ToListAsync(cancellationToken);

        var assignees = await ActiveAssigneesAsync(
            organizationId, [.. statusRows.Select(row => row.VisitId).Distinct()], cancellationToken);

        var events = new List<DashboardEvent>();

        events.AddRange(statusRows.Select(row => new DashboardEvent(
            row.ToStatus switch
            {
                VisitStatus.OnTheWay => "on_the_way",
                VisitStatus.Completed => "completed",
                _ => "started",
            },
            row.At,
            row.VisitId,
            row.WorkOrderId,
            RequestCardRules.DisplayNumber(prefix, row.WorkOrderNumber),
            row.Title,
            assignees.GetValueOrDefault(row.VisitId)?.FirstOrDefault()?.FullName)));
        events.AddRange(assignmentRows.Select(row => new DashboardEvent(
            row.Reassigned ? "reassigned" : "assigned",
            row.At,
            row.VisitId,
            row.WorkOrderId,
            RequestCardRules.DisplayNumber(prefix, row.WorkOrderNumber),
            row.Title,
            FullName(row.FirstName, row.LastName))));

        return events;
    }

    /// <summary>Work orders of the effective branches, not draft or cancelled, optionally of one service category (BR-02, BR-05).</summary>
    private IQueryable<FieldOps.Domain.WorkOrders.WorkOrder> ScopedOrders(Guid organizationId, Guid[] branchIds, Guid? categoryId)
    {
        var orders = dbContext.WorkOrders.AsNoTracking()
            .Where(order => order.OrganizationId == organizationId
                && branchIds.Contains(order.BranchId)
                && order.Status != WorkOrderStatus.Draft
                && order.Status != WorkOrderStatus.Cancelled);

        return categoryId is { } category ? orders.Where(order => order.ServiceCategoryId == category) : orders;
    }

    /// <summary>Visits of <see cref="ScopedOrders"/>, limited to the technician's active assignments when one is selected.</summary>
    private IQueryable<Visit> ScopedVisits(Guid organizationId, Guid[] branchIds, DashboardQuery query)
    {
        var orders = ScopedOrders(organizationId, branchIds, query.CategoryId);
        var visits = dbContext.Visits.AsNoTracking()
            .Where(visit => visit.OrganizationId == organizationId && orders.Any(order => order.Id == visit.WorkOrderId));

        return query.TechnicianId is { } technicianId
            ? visits.Where(visit => dbContext.VisitAssignments.Any(assignment =>
                assignment.VisitId == visit.Id && assignment.TechnicianId == technicianId && assignment.UnassignedAt == null))
            : visits;
    }

    private IQueryable<FieldOps.Domain.Branches.Branch> VisibleBranches(Guid organizationId, BranchScope scope)
    {
        var branches = dbContext.Branches.AsNoTracking()
            .Where(branch => branch.OrganizationId == organizationId && branch.IsActive);

        if (scope.All)
        {
            return branches;
        }

        var ids = scope.BranchIds.ToArray();

        return branches.Where(branch => ids.Contains(branch.Id));
    }

    private Task<string> OrganizationTimezoneAsync(Guid organizationId, CancellationToken cancellationToken) =>
        dbContext.Organizations.AsNoTracking()
            .Where(organization => organization.Id == organizationId)
            .Select(organization => organization.Timezone)
            .SingleAsync(cancellationToken);

    private static string FullName(string first, string last) => $"{first} {last}".Trim();
}
