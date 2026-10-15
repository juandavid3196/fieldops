using FieldOps.Application.Features.Access;
using FieldOps.Application.Features.ServiceRequests;

namespace FieldOps.Application.Features.Notifications;

internal static class NotificationSupport
{
    public static async Task<NotificationViewer> ViewerAsync(
        IBranchScopeResolver scopes, MembershipCall call, string roleCode, CancellationToken cancellationToken) =>
        new(
            call.OrganizationId,
            call.UserId,
            roleCode,
            await scopes.ResolveAsync(call.OrganizationId, call.MembershipId, cancellationToken));

    public static async Task<NotificationSummary> SummaryAsync(
        INotificationInboxStore store, NotificationViewer viewer, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var zone = OrganizationTime.FindZone(await store.GetTimeZoneAsync(viewer.OrganizationId, cancellationToken));
        var (from, to) = NotificationRules.Today(OrganizationTime.LocalDate(timeProvider.GetUtcNow(), zone), zone);

        return await store.SummaryAsync(viewer, from, to, cancellationToken);
    }
}

public sealed class ListNotificationsHandler(INotificationInboxStore store, IBranchScopeResolver scopes, TimeProvider timeProvider)
{
    public async Task<ServiceRequestResult<NotificationPage>> HandleAsync(
        MembershipCall call, string roleCode, NotificationListRequest request, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (!NotificationRules.TryParseTab(request.Tab, out var unreadOnly))
        {
            errors["tab"] = ["Tab must be all or unread."];
        }

        var search = request.Q?.Trim();

        if (search is { Length: > NotificationRules.SearchMaxLength })
        {
            errors["q"] = [$"Search must be {NotificationRules.SearchMaxLength} characters or fewer."];
        }

        if (!NotificationRules.TryParseType(request.Type, out var type))
        {
            errors["type"] = ["Type is not valid."];
        }

        if (!NotificationRules.TryParseDate(request.Date, out var days))
        {
            errors["date"] = ["Date must be any, today, 7d or 30d."];
        }

        var page = request.Page ?? 1;
        var pageSize = request.PageSize ?? NotificationRules.DefaultPageSize;

        if (page < 1)
        {
            errors["page"] = ["Page must be 1 or greater."];
        }

        if (pageSize is < 1 or > NotificationRules.MaxPageSize)
        {
            errors["pageSize"] = ["Page size must be between 1 and 100."];
        }

        if (errors.Count > 0)
        {
            return ServiceRequestResult<NotificationPage>.Invalid(errors);
        }

        var viewer = await NotificationSupport.ViewerAsync(scopes, call, roleCode, cancellationToken);
        Guid? branchId = null;

        if (!string.IsNullOrWhiteSpace(request.BranchId))
        {
            // A malformed id is as unknown as any other: one identical not found (BR-11).
            if (!Guid.TryParse(request.BranchId, out var parsed)
                || !await store.BranchAvailableAsync(viewer, parsed, cancellationToken))
            {
                return ServiceRequestResult<NotificationPage>.NotFound();
            }

            branchId = parsed;
        }

        var timeZone = await store.GetTimeZoneAsync(call.OrganizationId, cancellationToken);
        var zone = OrganizationTime.FindZone(timeZone);
        var window = NotificationRules.Window(days, OrganizationTime.LocalDate(timeProvider.GetUtcNow(), zone), zone);
        var filter = new NotificationFilter(
            unreadOnly,
            string.IsNullOrEmpty(search) ? null : search,
            type,
            branchId,
            window?.From,
            window?.To,
            page,
            pageSize);

        return ServiceRequestResult<NotificationPage>.Ok(await store.ListAsync(viewer, filter, timeZone, cancellationToken));
    }
}

public sealed class GetNotificationsSummaryHandler(INotificationInboxStore store, IBranchScopeResolver scopes, TimeProvider timeProvider)
{
    public async Task<NotificationSummary> HandleAsync(MembershipCall call, string roleCode, CancellationToken cancellationToken)
    {
        var viewer = await NotificationSupport.ViewerAsync(scopes, call, roleCode, cancellationToken);

        return await NotificationSupport.SummaryAsync(store, viewer, timeProvider, cancellationToken);
    }
}

public sealed class GetNotificationsOptionsHandler(INotificationInboxStore store, IBranchScopeResolver scopes)
{
    public async Task<NotificationOptions> HandleAsync(MembershipCall call, string roleCode, CancellationToken cancellationToken) =>
        await store.OptionsAsync(
            await NotificationSupport.ViewerAsync(scopes, call, roleCode, cancellationToken), cancellationToken);
}

public sealed class MarkNotificationReadHandler(INotificationInboxStore store, IBranchScopeResolver scopes, TimeProvider timeProvider)
{
    public Task<ServiceRequestResult<NotificationMarkResult>> ReadAsync(
        MembershipCall call, string roleCode, Guid id, CancellationToken cancellationToken) =>
        SetAsync(call, roleCode, id, true, cancellationToken);

    public Task<ServiceRequestResult<NotificationMarkResult>> UnreadAsync(
        MembershipCall call, string roleCode, Guid id, CancellationToken cancellationToken) =>
        SetAsync(call, roleCode, id, false, cancellationToken);

    private async Task<ServiceRequestResult<NotificationMarkResult>> SetAsync(
        MembershipCall call, string roleCode, Guid id, bool read, CancellationToken cancellationToken)
    {
        var viewer = await NotificationSupport.ViewerAsync(scopes, call, roleCode, cancellationToken);

        if (!await store.SetReadAsync(viewer, id, read, cancellationToken))
        {
            return ServiceRequestResult<NotificationMarkResult>.NotFound();
        }

        return ServiceRequestResult<NotificationMarkResult>.Ok(new NotificationMarkResult(
            await NotificationSupport.SummaryAsync(store, viewer, timeProvider, cancellationToken)));
    }
}

public sealed class ReadAllNotificationsHandler(INotificationInboxStore store, IBranchScopeResolver scopes, TimeProvider timeProvider)
{
    public async Task<NotificationReadAllResult> HandleAsync(MembershipCall call, string roleCode, CancellationToken cancellationToken)
    {
        var viewer = await NotificationSupport.ViewerAsync(scopes, call, roleCode, cancellationToken);
        var updated = await store.ReadAllAsync(viewer, timeProvider.GetUtcNow(), cancellationToken);

        return new NotificationReadAllResult(
            updated, await NotificationSupport.SummaryAsync(store, viewer, timeProvider, cancellationToken));
    }
}
