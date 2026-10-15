using System.Text.Json;
using FieldOps.Application.Features.Notifications;
using FieldOps.Domain.Invoices;
using FieldOps.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Persistence;

/// <summary>
/// Read side of the notifications inbox (notifications-center). Every query starts from the visible set of BR-03:
/// session organization, the caller as recipient, the in-app channel, the event codes of the caller's current role and
/// a payload branch that is null or inside the caller's current scope. Filtering, counting, ordering and paging run in
/// SQL on <c>ix_notifications_recipient</c>; the command count does not depend on the page size (BR-24).
/// </summary>
internal sealed class NotificationInboxStore(FieldOpsDbContext dbContext) : INotificationInboxStore
{
    private const string BranchKey = "branchId";

    private const string DescriptionKey = "description";

    private const string EntityLabelKey = "entityLabel";

    private const string SecondaryLabelKey = "secondaryLabel";

    public async Task<string> GetTimeZoneAsync(Guid organizationId, CancellationToken cancellationToken) =>
        await dbContext.Organizations.AsNoTracking()
            .Where(organization => organization.Id == organizationId)
            .Select(organization => organization.Timezone)
            .SingleAsync(cancellationToken);

    public async Task<bool> BranchAvailableAsync(NotificationViewer viewer, Guid branchId, CancellationToken cancellationToken) =>
        viewer.Scope.Contains(branchId)
        && await dbContext.Branches.AsNoTracking().AnyAsync(
            branch => branch.OrganizationId == viewer.OrganizationId && branch.Id == branchId && branch.IsActive,
            cancellationToken);

    public async Task<NotificationOptions> OptionsAsync(NotificationViewer viewer, CancellationToken cancellationToken)
    {
        var branches = dbContext.Branches.AsNoTracking()
            .Where(branch => branch.OrganizationId == viewer.OrganizationId && branch.IsActive);

        if (!viewer.Scope.All)
        {
            var ids = viewer.Scope.BranchIds.ToArray();
            branches = branches.Where(branch => ids.Contains(branch.Id));
        }

        return new NotificationOptions(await branches
            .OrderBy(branch => branch.Name)
            .ThenBy(branch => branch.Id)
            .Select(branch => new NotificationBranchRef(branch.Id, branch.Name))
            .ToListAsync(cancellationToken));
    }

    public async Task<NotificationPage> ListAsync(
        NotificationViewer viewer, NotificationFilter filter, string timeZone, CancellationToken cancellationToken)
    {
        var visible = await VisibleAsync(viewer, false, cancellationToken);
        var filtered = Apply(visible, filter);

        var grouped = await filtered
            .GroupBy(notification => notification.Status == NotificationStatus.Read)
            .Select(group => new { IsRead = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);
        var counts = new NotificationCounts(
            grouped.Sum(group => group.Count),
            grouped.Where(group => !group.IsRead).Sum(group => group.Count));
        var total = filter.UnreadOnly ? counts.Unread : counts.All;

        var tab = filter.UnreadOnly ? filtered.Where(notification => notification.Status != NotificationStatus.Read) : filtered;
        var rows = await tab
            .OrderByDescending(notification => notification.CreatedAt)
            .ThenByDescending(notification => notification.Id)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(notification => new
            {
                notification.Id,
                notification.TemplateCode,
                notification.Subject,
                notification.Payload,
                notification.Status,
                notification.CreatedAt,
            })
            .ToListAsync(cancellationToken);

        var parsed = rows.Select(row => (Row: row, Payload: ParsePayload(row.Payload))).ToList();
        var branches = await LoadBranchesAsync(viewer, parsed.Select(item => item.Payload.BranchId), cancellationToken);
        var allowed = await ResolveTargetsAsync(viewer, parsed.Select(item => item.Payload), cancellationToken);

        var items = parsed.Select(item =>
        {
            var payload = item.Payload;
            var type = NotificationCatalog.TryGet(item.Row.TemplateCode, out var entry) ? entry.Type : string.Empty;
            NotificationBranchRef? branch = payload.BranchId is { } branchId && branches.TryGetValue(branchId, out var name)
                ? new NotificationBranchRef(branchId, name)
                : null;
            var target = payload.EntityId is { } entityId && allowed.Contains((payload.EntityKind, entityId))
                ? new NotificationTarget(payload.EntityKind, entityId)
                : null;

            return new NotificationItem(
                item.Row.Id,
                type,
                item.Row.TemplateCode,
                item.Row.Subject ?? string.Empty,
                payload.Description,
                payload.EntityKind,
                payload.EntityLabel,
                payload.SecondaryLabel,
                branch,
                item.Row.CreatedAt,
                item.Row.Status == NotificationStatus.Read,
                target);
        }).ToList();

        return new NotificationPage(items, total, filter.Page, filter.PageSize, counts, timeZone);
    }

    public async Task<NotificationSummary> SummaryAsync(
        NotificationViewer viewer, DateTimeOffset todayStart, DateTimeOffset tomorrowStart, CancellationToken cancellationToken)
    {
        var visible = await VisibleAsync(viewer, false, cancellationToken);
        var row = await visible
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Unread = group.Count(notification => notification.Status != NotificationStatus.Read),
                Today = group.Count(notification => notification.CreatedAt >= todayStart && notification.CreatedAt < tomorrowStart),
            })
            .SingleOrDefaultAsync(cancellationToken);

        return new NotificationSummary(row?.Unread ?? 0, row?.Today ?? 0);
    }

    public async Task<bool> SetReadAsync(
        NotificationViewer viewer, Guid notificationId, bool read, CancellationToken cancellationToken)
    {
        var notification = await (await VisibleAsync(viewer, true, cancellationToken))
            .SingleOrDefaultAsync(candidate => candidate.Id == notificationId, cancellationToken);

        if (notification is null)
        {
            return false;
        }

        if (read)
        {
            notification.MarkRead();
        }
        else
        {
            notification.MarkUnread();
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async Task<int> ReadAllAsync(NotificationViewer viewer, DateTimeOffset upTo, CancellationToken cancellationToken) =>
        await (await VisibleAsync(viewer, false, cancellationToken))
            .Where(notification => notification.Status != NotificationStatus.Read && notification.CreatedAt <= upTo)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(notification => notification.Status, NotificationStatus.Read),
                cancellationToken);

    // BR-03. The visible set is rebuilt from the caller's current role and branch scope on every request.
    private async Task<IQueryable<Notification>> VisibleAsync(
        NotificationViewer viewer, bool tracked, CancellationToken cancellationToken)
    {
        var organizationId = viewer.OrganizationId;
        var userId = viewer.UserId;
        var codes = NotificationCatalog.CodesForRole(viewer.RoleCode);

        var query = tracked ? dbContext.Notifications.AsQueryable() : dbContext.Notifications.AsNoTracking();
        query = query.Where(notification => notification.OrganizationId == organizationId
            && notification.RecipientUserId == userId
            && notification.Channel == NotificationChannel.InApp
            && codes.Contains(notification.TemplateCode));

        if (viewer.Scope.All)
        {
            return query;
        }

        var scoped = viewer.Scope.BranchIds.ToArray();
        var active = (await dbContext.Branches.AsNoTracking()
            .Where(branch => branch.OrganizationId == organizationId && branch.IsActive && scoped.Contains(branch.Id))
            .Select(branch => branch.Id)
            .ToListAsync(cancellationToken))
            .Select(id => id.ToString())
            .ToArray();

        return query.Where(notification =>
            FieldOpsDbContext.PayloadText(notification.Payload, BranchKey) == null
            || active.Contains(FieldOpsDbContext.PayloadText(notification.Payload, BranchKey)!));
    }

    private static IQueryable<Notification> Apply(IQueryable<Notification> query, NotificationFilter filter)
    {
        if (filter.Type is { } type)
        {
            var codes = NotificationCatalog.CodesForType(type);
            query = query.Where(notification => codes.Contains(notification.TemplateCode));
        }

        if (filter.BranchId is { } branchId)
        {
            var text = branchId.ToString();
            query = query.Where(notification => FieldOpsDbContext.PayloadText(notification.Payload, BranchKey) == text);
        }

        if (filter.From is { } from && filter.To is { } to)
        {
            query = query.Where(notification => notification.CreatedAt >= from && notification.CreatedAt < to);
        }

        if (filter.Search is { } search)
        {
            var pattern = "%" + search.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            query = query.Where(notification =>
                EF.Functions.ILike(notification.Subject!, pattern)
                || EF.Functions.ILike(FieldOpsDbContext.PayloadText(notification.Payload, DescriptionKey)!, pattern)
                || EF.Functions.ILike(FieldOpsDbContext.PayloadText(notification.Payload, EntityLabelKey)!, pattern)
                || EF.Functions.ILike(FieldOpsDbContext.PayloadText(notification.Payload, SecondaryLabelKey)!, pattern));
        }

        return query;
    }

    private async Task<Dictionary<Guid, string>> LoadBranchesAsync(
        NotificationViewer viewer, IEnumerable<Guid?> branchIds, CancellationToken cancellationToken)
    {
        var ids = branchIds.Where(id => id is not null).Select(id => id!.Value).Distinct().ToArray();

        return ids.Length == 0
            ? []
            : await dbContext.Branches.AsNoTracking()
                .Where(branch => branch.OrganizationId == viewer.OrganizationId && ids.Contains(branch.Id))
                .ToDictionaryAsync(branch => branch.Id, branch => branch.Name, cancellationToken);
    }

    // BR-12: one batched existence and branch-scope query per entity kind present on the page and readable by the role.
    private async Task<HashSet<(string Kind, Guid Id)>> ResolveTargetsAsync(
        NotificationViewer viewer, IEnumerable<PayloadSnapshot> payloads, CancellationToken cancellationToken)
    {
        var organizationId = viewer.OrganizationId;
        var all = viewer.Scope.All;
        var scoped = viewer.Scope.BranchIds.ToArray();
        var allowed = new HashSet<(string, Guid)>();
        var byKind = payloads
            .Where(payload => payload.EntityId is not null && NotificationCatalog.CanReadKind(viewer.RoleCode, payload.EntityKind))
            .GroupBy(payload => payload.EntityKind)
            .ToDictionary(group => group.Key, group => group.Select(payload => payload.EntityId!.Value).Distinct().ToArray());

        foreach (var (kind, ids) in byKind)
        {
            List<Guid> found;

            switch (kind)
            {
                case NotificationEntityKinds.Request:
                    found = await dbContext.ServiceRequests.AsNoTracking()
                        .Where(request => request.OrganizationId == organizationId
                            && ids.Contains(request.Id)
                            && (all || request.BranchId == null || scoped.Contains(request.BranchId.Value)))
                        .Select(request => request.Id)
                        .ToListAsync(cancellationToken);
                    break;
                case NotificationEntityKinds.Customer:
                    found = await dbContext.Customers.AsNoTracking()
                        .Where(customer => customer.OrganizationId == organizationId
                            && ids.Contains(customer.Id)
                            && (all || scoped.Contains(customer.BranchId)))
                        .Select(customer => customer.Id)
                        .ToListAsync(cancellationToken);
                    break;
                case NotificationEntityKinds.Quote:
                    found = await (
                        from quote in dbContext.Quotes.AsNoTracking()
                        join request in dbContext.ServiceRequests.AsNoTracking()
                            on new { quote.OrganizationId, Id = quote.RequestId } equals new { request.OrganizationId, request.Id }
                        where quote.OrganizationId == organizationId
                            && ids.Contains(quote.Id)
                            && (all || request.BranchId == null || scoped.Contains(request.BranchId.Value))
                        select quote.Id)
                        .ToListAsync(cancellationToken);
                    break;
                case NotificationEntityKinds.Visit:
                    found = await (
                        from visit in dbContext.Visits.AsNoTracking()
                        join order in dbContext.WorkOrders.AsNoTracking()
                            on new { visit.OrganizationId, Id = visit.WorkOrderId } equals new { order.OrganizationId, order.Id }
                        where visit.OrganizationId == organizationId
                            && ids.Contains(visit.Id)
                            && (all || scoped.Contains(order.BranchId))
                        select visit.Id)
                        .ToListAsync(cancellationToken);
                    break;
                case NotificationEntityKinds.WorkOrder:
                    found = await dbContext.WorkOrders.AsNoTracking()
                        .Where(order => order.OrganizationId == organizationId
                            && ids.Contains(order.Id)
                            && (all || scoped.Contains(order.BranchId)))
                        .Select(order => order.Id)
                        .ToListAsync(cancellationToken);
                    break;
                case NotificationEntityKinds.Invoice:
                    found = await dbContext.Invoices.AsNoTracking()
                        .Where(invoice => invoice.OrganizationId == organizationId
                            && ids.Contains(invoice.Id)
                            && invoice.Status != InvoiceStatus.Void
                            && (all || scoped.Contains(invoice.BranchId)))
                        .Select(invoice => invoice.Id)
                        .ToListAsync(cancellationToken);
                    break;
                default:
                    continue;
            }

            foreach (var id in found)
            {
                allowed.Add((kind, id));
            }
        }

        return allowed;
    }

    private static PayloadSnapshot ParsePayload(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            return new PayloadSnapshot(
                Text(root, "entityKind") ?? string.Empty,
                Id(root, "entityId"),
                Text(root, EntityLabelKey) ?? string.Empty,
                Text(root, SecondaryLabelKey),
                Text(root, DescriptionKey) ?? string.Empty,
                Id(root, BranchKey));
        }
        catch (JsonException)
        {
            return new PayloadSnapshot(string.Empty, null, string.Empty, null, string.Empty, null);
        }
    }

    private static string? Text(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static Guid? Id(JsonElement root, string name) =>
        Guid.TryParse(Text(root, name), out var id) ? id : null;

    private sealed record PayloadSnapshot(
        string EntityKind, Guid? EntityId, string EntityLabel, string? SecondaryLabel, string Description, Guid? BranchId);
}
