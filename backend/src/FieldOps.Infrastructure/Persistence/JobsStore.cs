using FieldOps.Application.Features.Access;
using FieldOps.Application.Features.ServiceRequests;
using FieldOps.Application.Features.WorkOrders;
using FieldOps.Domain.WorkOrders;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Persistence;

/// <summary>
/// Read model of the jobs workspace (jobs-management). Every query filters by organization first, then by the branch
/// scope, never includes drafts, and runs a bounded number of commands regardless of the page size (BR-21).
/// The current visit and the operational status are derived once, in SQL.
/// </summary>
internal sealed partial class JobsStore(FieldOpsDbContext dbContext, TimeProvider timeProvider) : IJobsStore
{
    public async Task<bool> BranchAvailableAsync(
        Guid organizationId, BranchScope scope, Guid branchId, CancellationToken cancellationToken)
    {
        if (!scope.Contains(branchId))
        {
            return false;
        }

        return await dbContext.Branches.AsNoTracking()
            .AnyAsync(branch => branch.OrganizationId == organizationId && branch.Id == branchId && branch.IsActive, cancellationToken);
    }

    public async Task<JobsOptions> OptionsAsync(
        Guid organizationId, BranchScope scope, bool canManage, CancellationToken cancellationToken)
    {
        var organization = await ReadOrganizationAsync(organizationId, cancellationToken);
        var branches = dbContext.Branches.AsNoTracking()
            .Where(branch => branch.OrganizationId == organizationId && branch.IsActive);

        if (!scope.All)
        {
            var ids = scope.BranchIds.ToArray();
            branches = branches.Where(branch => ids.Contains(branch.Id));
        }

        var rows = await branches
            .OrderBy(branch => branch.Name)
            .ThenBy(branch => branch.Id)
            .Select(branch => new { branch.Id, branch.Name, branch.Timezone })
            .ToListAsync(cancellationToken);

        return new JobsOptions(
            rows.Select(row => new JobsOptionBranch(row.Id, row.Name, row.Timezone)).ToList(),
            organization.Timezone,
            canManage);
    }

    public async Task<JobsPage> ListAsync(
        Guid organizationId, BranchScope scope, JobsFilter filter, bool canManage, CancellationToken cancellationToken)
    {
        var organization = await ReadOrganizationAsync(organizationId, cancellationToken);
        var rows = Rows(organizationId, scope, filter, organization.WorkOrderPrefix);
        var counts = await CountTabsAsync(rows, cancellationToken);
        var statuses = JobsRules.OrderStatuses(filter.Tab);
        var inTab = rows.Where(row => statuses.Contains(row.Status));
        var skip = (filter.Page - 1) * filter.PageSize;
        List<JobData> data;
        int total;

        if (filter.Status is { } status)
        {
            var derived = Derive(inTab).Where(job => job.JobStatus == status);
            total = await derived.CountAsync(cancellationToken);
            data = await SortDerived(derived, filter.Sort).Skip(skip).Take(filter.PageSize).ToListAsync(cancellationToken);
        }
        else
        {
            total = await inTab.CountAsync(cancellationToken);

            if (filter.Sort is JobsSort.Updated or JobsSort.Schedule)
            {
                data = await SortDerived(Derive(inTab), filter.Sort).Skip(skip).Take(filter.PageSize).ToListAsync(cancellationToken);
            }
            else
            {
                // Only the requested page pays for the derivation.
                var ids = await SortRows(inTab, filter.Sort).Skip(skip).Take(filter.PageSize)
                    .Select(row => row.Id)
                    .ToListAsync(cancellationToken);
                var found = ids.Count == 0
                    ? []
                    : await Derive(rows.Where(row => ids.Contains(row.Id))).ToListAsync(cancellationToken);
                var position = ids.Select((id, index) => (id, index)).ToDictionary(pair => pair.id, pair => pair.index);
                data = found.OrderBy(job => position[job.Id]).ToList();
            }
        }

        var technicians = await ReadTechniciansAsync(organizationId, data, cancellationToken);
        var items = data.Select(job => ToRow(job, organization, technicians, canManage)).ToList();

        return new JobsPage(items, total, counts);
    }

    private static JobRow ToRow(
        JobData job, OrganizationInfo organization, Dictionary<Guid, JobTechnician> technicians, bool canManage) =>
        new(
            job.Id,
            RequestCardRules.DisplayNumber(organization.WorkOrderPrefix, job.WorkOrderNumber),
            job.Title,
            job.CustomerName,
            new JobRowProperty(job.PropertyName, job.PostalCode, job.City),
            new JobRowBranch(job.BranchId, job.BranchName),
            job.BranchTimezone ?? organization.Timezone,
            JobsRules.StatusCode(job.JobStatus),
            WorkOrderCodes.PriorityCode(job.Priority),
            job.VisitId is { } visitId ? new JobCurrentVisit(visitId, job.VisitStart, job.VisitEnd) : null,
            job.VisitId is { } id && technicians.TryGetValue(id, out var technician) ? technician : null,
            job.UpdatedAt,
            job.CreatedAt,
            JobsRules.Actions(job.OrderStatus, job.VisitId is not null, job.VisitStart is not null, canManage));

    /// <summary>One command: the primary, still-assigned technician of each page visit.</summary>
    private async Task<Dictionary<Guid, JobTechnician>> ReadTechniciansAsync(
        Guid organizationId, List<JobData> data, CancellationToken cancellationToken)
    {
        var visitIds = data.Where(job => job.VisitId != null).Select(job => job.VisitId!.Value).ToArray();

        if (visitIds.Length == 0)
        {
            return [];
        }

        var rows = await (
            from assignment in dbContext.VisitAssignments.AsNoTracking()
            join technician in dbContext.TechnicianProfiles.AsNoTracking() on assignment.TechnicianId equals technician.Id
            where visitIds.Contains(assignment.VisitId)
                && assignment.IsPrimary
                && assignment.UnassignedAt == null
                && technician.OrganizationId == organizationId
            select new { assignment.VisitId, technician.FirstName, technician.LastName, technician.ColorHex })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(row => row.VisitId)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var first = group.First();
                    var fullName = $"{first.FirstName} {first.LastName}".Trim();

                    return new JobTechnician(fullName, RequestCardRules.Initials(fullName), first.ColorHex);
                });
    }

    /// <summary>Tab counts apply branch, search and priority but not the status filter (BR-03). One command.</summary>
    private static async Task<JobTabCounts> CountTabsAsync(IQueryable<OrderRow> rows, CancellationToken cancellationToken)
    {
        var counts = await rows
            .GroupBy(_ => 1)
            .Select(group => new
            {
                All = group.Count(),
                Active = group.Count(row => row.Status == WorkOrderStatus.Scheduled || row.Status == WorkOrderStatus.InProgress),
                Unscheduled = group.Count(row => row.Status == WorkOrderStatus.ReadyToSchedule),
                Completed = group.Count(row => row.Status == WorkOrderStatus.Completed || row.Status == WorkOrderStatus.ApprovedForBilling),
            })
            .FirstOrDefaultAsync(cancellationToken);

        return counts is null ? new JobTabCounts(0, 0, 0, 0) : new JobTabCounts(counts.All, counts.Active, counts.Unscheduled, counts.Completed);
    }

    private static IOrderedQueryable<OrderRow> SortRows(IQueryable<OrderRow> rows, JobsSort sort) => sort switch
    {
        JobsSort.Oldest => rows.OrderBy(row => row.CreatedAt).ThenByDescending(row => row.WorkOrderNumber),
        JobsSort.Priority => rows.OrderBy(row => row.Priority)
            .ThenByDescending(row => row.CreatedAt)
            .ThenByDescending(row => row.WorkOrderNumber),
        _ => rows.OrderByDescending(row => row.CreatedAt).ThenByDescending(row => row.WorkOrderNumber),
    };

    private static IOrderedQueryable<JobData> SortDerived(IQueryable<JobData> jobs, JobsSort sort) => sort switch
    {
        JobsSort.Oldest => jobs.OrderBy(job => job.CreatedAt).ThenByDescending(job => job.WorkOrderNumber),
        JobsSort.Updated => jobs.OrderByDescending(job => job.UpdatedAt).ThenByDescending(job => job.WorkOrderNumber),
        JobsSort.Schedule => jobs.OrderBy(job => job.VisitStart == null)
            .ThenBy(job => job.VisitStart)
            .ThenByDescending(job => job.WorkOrderNumber),
        JobsSort.Priority => jobs.OrderBy(job => job.Priority)
            .ThenByDescending(job => job.CreatedAt)
            .ThenByDescending(job => job.WorkOrderNumber),
        _ => jobs.OrderByDescending(job => job.CreatedAt).ThenByDescending(job => job.WorkOrderNumber),
    };

    /// <summary>Non-draft orders of the organization inside the scope and the selected branch (BR-02).</summary>
    private IQueryable<WorkOrder> ScopedOrders(Guid organizationId, BranchScope scope, Guid? branchId)
    {
        var orders = dbContext.WorkOrders.AsNoTracking()
            .Where(order => order.OrganizationId == organizationId && order.Status != WorkOrderStatus.Draft);

        if (!scope.All)
        {
            var ids = scope.BranchIds.ToArray();
            orders = orders.Where(order => ids.Contains(order.BranchId));
        }

        return branchId is { } selected ? orders.Where(order => order.BranchId == selected) : orders;
    }

    /// <summary>The filtered base: scope, branch, search (BR-08) and priority (BR-07) over the order and its labels.</summary>
    private IQueryable<OrderRow> Rows(Guid organizationId, BranchScope scope, JobsFilter filter, string prefix)
    {
        var orders = ScopedOrders(organizationId, scope, filter.BranchId);

        if (filter.Priority is { } range)
        {
            var min = range.Min;
            var max = range.Max;
            orders = orders.Where(order => order.Priority >= min && order.Priority <= max);
        }

        var rows =
            from order in orders
            join customer in dbContext.Customers.AsNoTracking()
                on new { order.OrganizationId, Id = order.CustomerId } equals new { customer.OrganizationId, customer.Id }
            join property in dbContext.Properties.AsNoTracking()
                on new { order.OrganizationId, Id = order.PropertyId } equals new { property.OrganizationId, property.Id }
            join branch in dbContext.Branches.AsNoTracking()
                on new { order.OrganizationId, Id = order.BranchId } equals new { branch.OrganizationId, branch.Id }
            select new OrderRow
            {
                Id = order.Id,
                OrganizationId = order.OrganizationId,
                WorkOrderNumber = order.WorkOrderNumber,
                Title = order.Title,
                Priority = order.Priority,
                Status = order.Status,
                CreatedAt = order.CreatedAt,
                UpdatedAt = order.UpdatedAt,
                BranchId = order.BranchId,
                BranchName = branch.Name,
                BranchTimezone = branch.Timezone,
                CustomerName = customer.DisplayName,
                PropertyName = property.Name,
                AddressLine1 = property.AddressLine1,
                PostalCode = property.PostalCode,
                City = property.City,
            };

        var term = filter.Search?.TrimStart('#').Trim();

        if (string.IsNullOrEmpty(term))
        {
            return rows;
        }

        var pattern = RequestSearch.ContainsPattern(term);
        var number = RequestSearch.TryParseNumber(term, prefix, out var parsed) ? parsed : -1L;

        return rows.Where(row =>
            row.WorkOrderNumber == number
            || EF.Functions.ILike(row.Title, pattern)
            || EF.Functions.ILike(row.CustomerName, pattern)
            || EF.Functions.ILike(row.PropertyName, pattern)
            || EF.Functions.ILike(row.AddressLine1, pattern)
            || EF.Functions.ILike(row.City, pattern)
            || (row.PostalCode != null && EF.Functions.ILike(row.PostalCode, pattern)));
    }

    /// <summary>
    /// BR-04 and BR-05 in SQL: the current visit (lowest open visit number, else highest non-cancelled, else highest)
    /// through a lateral top-1, and the operational status as one conditional over the order and that visit.
    /// </summary>
    private IQueryable<JobData> Derive(IQueryable<OrderRow> rows) =>
        from row in rows
        from visit in dbContext.Visits.AsNoTracking()
            .Where(candidate => candidate.OrganizationId == row.OrganizationId && candidate.WorkOrderId == row.Id)
            .OrderBy(candidate =>
                candidate.Status != VisitStatus.Completed
                && candidate.Status != VisitStatus.NeedsCorrection
                && candidate.Status != VisitStatus.Approved
                && candidate.Status != VisitStatus.Cancelled
                    ? 0
                    : candidate.Status != VisitStatus.Cancelled ? 1 : 2)
            .ThenBy(candidate =>
                candidate.Status != VisitStatus.Completed
                && candidate.Status != VisitStatus.NeedsCorrection
                && candidate.Status != VisitStatus.Approved
                && candidate.Status != VisitStatus.Cancelled
                    ? candidate.VisitNumber
                    : -candidate.VisitNumber)
            .Take(1)
            .DefaultIfEmpty()
        from latest in dbContext.Visits.AsNoTracking()
            .Where(candidate => candidate.OrganizationId == row.OrganizationId && candidate.WorkOrderId == row.Id)
            .OrderByDescending(candidate => candidate.UpdatedAt)
            .Take(1)
            .Select(candidate => (DateTimeOffset?)candidate.UpdatedAt)
            .DefaultIfEmpty()
        select new JobData
        {
            Id = row.Id,
            WorkOrderNumber = row.WorkOrderNumber,
            Title = row.Title,
            Priority = row.Priority,
            OrderStatus = row.Status,
            CreatedAt = row.CreatedAt,
            BranchId = row.BranchId,
            BranchName = row.BranchName,
            BranchTimezone = row.BranchTimezone,
            CustomerName = row.CustomerName,
            PropertyName = row.PropertyName,
            PostalCode = row.PostalCode,
            City = row.City,
            VisitId = visit == null ? null : (Guid?)visit.Id,
            VisitStart = visit == null ? null : visit.ScheduledStart,
            VisitEnd = visit == null ? null : visit.ScheduledEnd,
            UpdatedAt = latest != null && latest > row.UpdatedAt ? latest.Value : row.UpdatedAt,
            JobStatus = row.Status == WorkOrderStatus.ReadyToSchedule
                ? JobStatus.ReadyToSchedule
                : row.Status == WorkOrderStatus.Scheduled
                    ? (visit.Status == VisitStatus.Assigned
                        ? JobStatus.Assigned
                        : visit.Status == VisitStatus.OnTheWay ? JobStatus.OnTheWay : JobStatus.Scheduled)
                    : row.Status == WorkOrderStatus.InProgress
                        ? (visit.Status == VisitStatus.Paused ? JobStatus.Paused : JobStatus.InProgress)
                        : row.Status == WorkOrderStatus.Completed
                            ? (dbContext.Visits.Any(candidate => candidate.OrganizationId == row.OrganizationId
                                && candidate.WorkOrderId == row.Id
                                && candidate.Status == VisitStatus.NeedsCorrection)
                                ? JobStatus.NeedsCorrection
                                : JobStatus.Completed)
                            : row.Status == WorkOrderStatus.ApprovedForBilling
                                ? JobStatus.ApprovedForBilling
                                : JobStatus.Cancelled,
        };

    private async Task<OrganizationInfo> ReadOrganizationAsync(Guid organizationId, CancellationToken cancellationToken) =>
        await dbContext.Organizations.AsNoTracking()
            .Where(organization => organization.Id == organizationId)
            .Select(organization => new OrganizationInfo(organization.Timezone, organization.WorkOrderPrefix))
            .SingleAsync(cancellationToken);

    private sealed record OrganizationInfo(string Timezone, string WorkOrderPrefix);

    private sealed class OrderRow
    {
        public Guid Id { get; set; }

        public Guid OrganizationId { get; set; }

        public long WorkOrderNumber { get; set; }

        public string Title { get; set; } = string.Empty;

        public short Priority { get; set; }

        public WorkOrderStatus Status { get; set; }

        public DateTimeOffset CreatedAt { get; set; }

        public DateTimeOffset UpdatedAt { get; set; }

        public Guid BranchId { get; set; }

        public string BranchName { get; set; } = string.Empty;

        public string? BranchTimezone { get; set; }

        public string CustomerName { get; set; } = string.Empty;

        public string PropertyName { get; set; } = string.Empty;

        public string AddressLine1 { get; set; } = string.Empty;

        public string? PostalCode { get; set; }

        public string City { get; set; } = string.Empty;
    }

    private sealed class JobData
    {
        public Guid Id { get; set; }

        public long WorkOrderNumber { get; set; }

        public string Title { get; set; } = string.Empty;

        public short Priority { get; set; }

        public WorkOrderStatus OrderStatus { get; set; }

        public JobStatus JobStatus { get; set; }

        public DateTimeOffset CreatedAt { get; set; }

        public DateTimeOffset UpdatedAt { get; set; }

        public Guid BranchId { get; set; }

        public string BranchName { get; set; } = string.Empty;

        public string? BranchTimezone { get; set; }

        public string CustomerName { get; set; } = string.Empty;

        public string PropertyName { get; set; } = string.Empty;

        public string? PostalCode { get; set; }

        public string City { get; set; } = string.Empty;

        public Guid? VisitId { get; set; }

        public DateTimeOffset? VisitStart { get; set; }

        public DateTimeOffset? VisitEnd { get; set; }
    }
}
