using FieldOps.Application.Features.AuditTrail;
using FieldOps.Application.Features.ServiceRequests;
using FieldOps.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Persistence;

/// <summary>
/// Read side of the audit trail (audit-trail) and the writer of <c>export.generated</c> (BR-16). Every read starts from
/// <see cref="AuditLogVisibility"/>; filtering, counting, sorting and paging run in SQL, so the command count does not
/// depend on the page size and only the requested rows are resolved in memory.
/// </summary>
internal sealed partial class AuditLogStore(FieldOpsDbContext dbContext) : IAuditLogStore, IExportAuditWriter
{
    private const int ResolveBatchSize = 500;

    private const string ExportEntityType = "export";

    public async Task<string> GetTimeZoneAsync(Guid organizationId, CancellationToken cancellationToken) =>
        await dbContext.Organizations.AsNoTracking()
            .Where(organization => organization.Id == organizationId)
            .Select(organization => organization.Timezone)
            .SingleAsync(cancellationToken);

    public async Task<bool> ActorExistsAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken) =>
        await dbContext.OrganizationUsers.AsNoTracking()
            .AnyAsync(member => member.OrganizationId == organizationId && member.UserId == userId, cancellationToken);

    public async Task<bool> BranchAvailableAsync(AuditViewer viewer, Guid branchId, CancellationToken cancellationToken) =>
        viewer.Scope.Contains(branchId)
        && await dbContext.Branches.AsNoTracking()
            .AnyAsync(branch => branch.OrganizationId == viewer.OrganizationId && branch.Id == branchId, cancellationToken);

    public async Task<AuditResolvedPage> ListAsync(AuditViewer viewer, AuditQuery query, CancellationToken cancellationToken)
    {
        var filtered = await FilteredAsync(viewer, query, cancellationToken);
        var total = await filtered.CountAsync(cancellationToken);
        var rows = await Select(Sorted(filtered, query)
                .Skip((query.Page - 1) * query.PageSize)
                .Take(query.PageSize))
            .ToListAsync(cancellationToken);

        return new AuditResolvedPage(await ResolveAsync(viewer, rows.Select(ToRecord).ToList(), cancellationToken), total);
    }

    public async Task<IReadOnlyList<AuditResolved>?> ExportAsync(
        AuditViewer viewer, AuditQuery query, int maxRows, CancellationToken cancellationToken)
    {
        var filtered = await FilteredAsync(viewer, query, cancellationToken);

        if (await filtered.CountAsync(cancellationToken) > maxRows)
        {
            return null;
        }

        var records = (await Select(Sorted(filtered, query)).ToListAsync(cancellationToken)).Select(ToRecord).ToList();
        var resolved = new List<AuditResolved>(records.Count);

        foreach (var batch in records.Chunk(ResolveBatchSize))
        {
            resolved.AddRange(await ResolveAsync(viewer, batch, cancellationToken));
        }

        return resolved;
    }

    public async Task<AuditSummaryCounts> SummaryAsync(
        AuditViewer viewer, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var high = AuditCatalog.HighCodes;
        var today = AuditLogVisibility.Apply(dbContext.AuditLogs.AsNoTracking(), viewer)
            .Where(audit => audit.OccurredAt >= from && audit.OccurredAt < to);

        var row = await today
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Events = group.Count(),
                Users = group.Where(audit => audit.ActorUserId != null).Select(audit => audit.ActorUserId).Distinct().Count(),
                High = group.Count(audit => high.Contains(audit.Action)),
                Access = group.Count(audit => audit.Action.StartsWith("auth.") || audit.Action.StartsWith("user.")),
            })
            .SingleOrDefaultAsync(cancellationToken);

        return row is null ? new AuditSummaryCounts(0, 0, 0, 0) : new AuditSummaryCounts(row.Events, row.Users, row.High, row.Access);
    }

    public async Task<AuditFilterData> FilterDataAsync(AuditViewer viewer, CancellationToken cancellationToken)
    {
        var members = await (
            from member in dbContext.OrganizationUsers.AsNoTracking()
            join user in dbContext.Users.AsNoTracking() on member.UserId equals user.Id
            where member.OrganizationId == viewer.OrganizationId
            select new { user.Id, user.FirstName, user.LastName })
            .Distinct()
            .ToListAsync(cancellationToken);

        var users = members
            .Select(member => new AuditUserOption(member.Id.ToString(), FullName(member.FirstName, member.LastName)))
            .OrderBy(option => option.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(option => option.Id, StringComparer.Ordinal)
            .Append(new AuditUserOption(AuditLogRules.SystemActor, AuditSourceResolver.System))
            .ToList();

        var branches = dbContext.Branches.AsNoTracking().Where(branch => branch.OrganizationId == viewer.OrganizationId);

        if (!viewer.Scope.All)
        {
            var scoped = viewer.Scope.BranchIds.ToArray();
            branches = branches.Where(branch => scoped.Contains(branch.Id));
        }

        return new AuditFilterData(
            users,
            await branches
                .OrderBy(branch => branch.Name)
                .ThenBy(branch => branch.Id)
                .Select(branch => new AuditBranchOption(branch.Id, branch.Name, branch.IsActive))
                .ToListAsync(cancellationToken));
    }

    public async Task<AuditResolvedDetail?> GetAsync(AuditViewer viewer, long id, CancellationToken cancellationToken)
    {
        var row = await Select(AuditLogVisibility.Apply(dbContext.AuditLogs.AsNoTracking(), viewer)
                .Where(audit => audit.Id == id))
            .SingleOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        var record = ToRecord(row);
        var resolved = (await ResolveAsync(viewer, [record], cancellationToken))[0];
        string? roleName = null;

        if (record.ActorUserId is { } actorId)
        {
            roleName = await (
                from member in dbContext.OrganizationUsers.AsNoTracking()
                join role in dbContext.Roles.AsNoTracking() on member.RoleId equals role.Id
                where member.OrganizationId == viewer.OrganizationId && member.UserId == actorId
                select role.Name)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return new AuditResolvedDetail(resolved, roleName, await TargetAsync(viewer, record, cancellationToken));
    }

    // BR-16: one insert and one save; a failure propagates so the export answers 500 with no file.
    public async Task RecordAsync(MembershipCall call, ExportAuditEntry entry, CancellationToken cancellationToken)
    {
        dbContext.AuditLogs.Add(AuditLog.Create(
            call.OrganizationId,
            AuditCatalog.ExportGenerated,
            ExportEntityType,
            actorUserId: call.UserId,
            entityId: null,
            branchId: entry.BranchId,
            ipAddress: call.IpAddress,
            metadata: entry.Metadata));

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    internal static string FullName(string firstName, string? lastName) =>
        string.IsNullOrWhiteSpace(lastName) ? firstName : $"{firstName} {lastName}";

    private static IQueryable<AuditRow> Select(IQueryable<AuditLog> query) =>
        query.Select(audit => new AuditRow
        {
            Id = audit.Id,
            OccurredAt = audit.OccurredAt,
            ActorUserId = audit.ActorUserId,
            Action = audit.Action,
            EntityType = audit.EntityType,
            EntityId = audit.EntityId,
            BranchId = audit.BranchId,
            BeforeData = audit.BeforeData,
            AfterData = audit.AfterData,
            Metadata = audit.Metadata,
            IpAddress = audit.IpAddress,
        });

    private static AuditRecord ToRecord(AuditRow row) =>
        new(
            row.Id,
            row.OccurredAt,
            row.ActorUserId,
            row.Action,
            row.EntityType,
            row.EntityId,
            row.BranchId,
            row.BeforeData,
            row.AfterData,
            row.Metadata,
            row.IpAddress?.ToString());

    // BR-05: impact orders High > Medium > Low for desc; ties by occurred_at desc then id desc.
    private static IOrderedQueryable<AuditLog> Sorted(IQueryable<AuditLog> query, AuditQuery filter)
    {
        var high = AuditCatalog.HighCodes;
        var medium = AuditCatalog.MediumCodes;

        if (filter.SortByImpact)
        {
            return (filter.Descending
                    ? query.OrderByDescending(audit => high.Contains(audit.Action) ? 2 : medium.Contains(audit.Action) ? 1 : 0)
                    : query.OrderBy(audit => high.Contains(audit.Action) ? 2 : medium.Contains(audit.Action) ? 1 : 0))
                .ThenByDescending(audit => audit.OccurredAt)
                .ThenByDescending(audit => audit.Id);
        }

        return (filter.Descending
                ? query.OrderByDescending(audit => audit.OccurredAt)
                : query.OrderBy(audit => audit.OccurredAt))
            .ThenByDescending(audit => audit.Id);
    }

    private sealed class AuditRow
    {
        public long Id { get; init; }

        public DateTimeOffset OccurredAt { get; init; }

        public Guid? ActorUserId { get; init; }

        public string Action { get; init; } = string.Empty;

        public string EntityType { get; init; } = string.Empty;

        public Guid? EntityId { get; init; }

        public Guid? BranchId { get; init; }

        public string? BeforeData { get; init; }

        public string? AfterData { get; init; }

        public string Metadata { get; init; } = "{}";

        public System.Net.IPAddress? IpAddress { get; init; }
    }
}
