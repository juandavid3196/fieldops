using System.Globalization;
using FieldOps.Application.Features.Access;
using FieldOps.Application.Features.BillingReview;
using FieldOps.Application.Features.Notifications;
using FieldOps.Application.Features.ServiceRequests;

namespace FieldOps.Application.Features.AuditTrail;

internal sealed record AuditPrepared(AuditViewer Viewer, AuditQuery Query, string TimeZone, DateOnly Today);

internal static class AuditLogSupport
{
    public static async Task<AuditViewer> ViewerAsync(
        IBranchScopeResolver scopes, MembershipCall call, string roleCode, CancellationToken cancellationToken) =>
        new(
            call.OrganizationId,
            call.UserId,
            roleCode,
            await scopes.ResolveAsync(call.OrganizationId, call.MembershipId, cancellationToken));

    /// <summary>
    /// Parses the query (400 first), then validates the actor (400) and the branch (404) against the session organization
    /// and the caller scope. The organization is always the session's.
    /// </summary>
    public static async Task<BillingOutcome<AuditPrepared>> PrepareAsync(
        IAuditLogStore store,
        IBranchScopeResolver scopes,
        MembershipCall call,
        string roleCode,
        AuditLogQueryText text,
        bool paged,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var timeZone = await store.GetTimeZoneAsync(call.OrganizationId, cancellationToken);
        var zone = OrganizationTime.FindZone(timeZone);
        var today = OrganizationTime.LocalDate(timeProvider.GetUtcNow(), zone);

        switch (AuditLogRules.Parse(text, roleCode, today, zone, paged))
        {
            case BillingOutcome<AuditQuery>.Invalid invalid:
                return new BillingOutcome<AuditPrepared>.Invalid(invalid.Errors);
            case BillingOutcome<AuditQuery>.Succeeded parsed:
                var viewer = await ViewerAsync(scopes, call, roleCode, cancellationToken);
                var query = parsed.Value;

                if (query.ActorId is { } actorId && !await store.ActorExistsAsync(call.OrganizationId, actorId, cancellationToken))
                {
                    return new BillingOutcome<AuditPrepared>.Invalid(
                        new Dictionary<string, string[]>(StringComparer.Ordinal) { ["actorId"] = ["Actor is not valid."] });
                }

                if (query.BranchId is { } branchId && !await store.BranchAvailableAsync(viewer, branchId, cancellationToken))
                {
                    return new BillingOutcome<AuditPrepared>.NotFound();
                }

                return new BillingOutcome<AuditPrepared>.Succeeded(new AuditPrepared(viewer, query, timeZone, today));
            default:
                return new BillingOutcome<AuditPrepared>.NotFound();
        }
    }

    /// <summary>Normalized filter values of the export row (BR-16): ids, codes, dates and sort; <c>hasSearch</c> instead of the text.</summary>
    public static Dictionary<string, object?> NormalizedFilters(AuditQuery query) =>
        new(StringComparer.Ordinal)
        {
            ["from"] = query.From,
            ["to"] = query.To,
            ["actorId"] = query.ActorSystem ? AuditLogRules.SystemActor : query.ActorId?.ToString(),
            ["action"] = query.Action,
            ["entityType"] = query.EntityType,
            ["entityId"] = query.EntityId,
            ["branchId"] = query.BranchId,
            ["impact"] = query.Impact is { } impact ? AuditCatalog.ImpactCode(impact) : null,
            ["sort"] = query.SortByImpact ? AuditLogMessages.SortImpact : AuditLogMessages.SortOccurredAt,
            ["direction"] = query.Descending ? "desc" : "asc",
            ["hasSearch"] = query.Search is not null,
        };
}

/// <summary>GET /audit-log (FR-01).</summary>
public sealed class ListAuditLogHandler(IAuditLogStore store, IBranchScopeResolver scopes, TimeProvider timeProvider)
{
    public async Task<BillingOutcome<AuditPage>> HandleAsync(
        MembershipCall call, string roleCode, AuditLogQueryText text, CancellationToken cancellationToken)
    {
        var prepared = await AuditLogSupport.PrepareAsync(store, scopes, call, roleCode, text, true, timeProvider, cancellationToken);

        switch (prepared)
        {
            case BillingOutcome<AuditPrepared>.Succeeded ok:
                var page = await store.ListAsync(ok.Value.Viewer, ok.Value.Query, cancellationToken);

                return new BillingOutcome<AuditPage>.Succeeded(new AuditPage(
                    [.. page.Rows.Select(AuditPresenter.Item)],
                    page.Total,
                    ok.Value.Query.Page,
                    ok.Value.Query.PageSize,
                    ok.Value.TimeZone));
            case BillingOutcome<AuditPrepared>.Invalid invalid:
                return new BillingOutcome<AuditPage>.Invalid(invalid.Errors);
            default:
                return new BillingOutcome<AuditPage>.NotFound();
        }
    }
}

/// <summary>GET /audit-log/summary (FR-02): the current local day within the caller's visibility, independent of filters.</summary>
public sealed class GetAuditSummaryHandler(IAuditLogStore store, IBranchScopeResolver scopes, TimeProvider timeProvider)
{
    public async Task<AuditSummary> HandleAsync(MembershipCall call, string roleCode, CancellationToken cancellationToken)
    {
        var viewer = await AuditLogSupport.ViewerAsync(scopes, call, roleCode, cancellationToken);
        var timeZone = await store.GetTimeZoneAsync(call.OrganizationId, cancellationToken);
        var zone = OrganizationTime.FindZone(timeZone);
        var (from, to) = NotificationRules.Today(OrganizationTime.LocalDate(timeProvider.GetUtcNow(), zone), zone);
        var counts = await store.SummaryAsync(viewer, from, to, cancellationToken);

        return new AuditSummary(counts.Events, counts.ActiveUsers, counts.HighImpact, counts.AccessChanges, timeZone);
    }
}

/// <summary>GET /audit-log/filters (FR-03).</summary>
public sealed class GetAuditFiltersHandler(IAuditLogStore store, IBranchScopeResolver scopes)
{
    public async Task<AuditFilters> HandleAsync(MembershipCall call, string roleCode, CancellationToken cancellationToken)
    {
        var viewer = await AuditLogSupport.ViewerAsync(scopes, call, roleCode, cancellationToken);
        var data = await store.FilterDataAsync(viewer, cancellationToken);
        var visible = AuditAccess.VisibleEntries(roleCode);

        return new AuditFilters(
            [.. visible.OrderBy(entry => entry.Label, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.Code, StringComparer.Ordinal)
                .Select(entry => new AuditFilterOption(entry.Code, entry.Label))],
            [.. visible.Select(entry => entry.EntityType)
                .Distinct(StringComparer.Ordinal)
                .Select(type => new AuditFilterOption(type, AuditCatalog.EntityLabel(type)))
                .OrderBy(option => option.Label, StringComparer.OrdinalIgnoreCase)],
            data.Users,
            data.Branches);
    }
}

/// <summary>GET /audit-log/{id} (FR-04): an invisible or unknown id is one identical not found.</summary>
public sealed class GetAuditEventHandler(IAuditLogStore store, IBranchScopeResolver scopes)
{
    public async Task<AuditDetail?> HandleAsync(MembershipCall call, string roleCode, long id, CancellationToken cancellationToken)
    {
        var viewer = await AuditLogSupport.ViewerAsync(scopes, call, roleCode, cancellationToken);
        var detail = await store.GetAsync(viewer, id, cancellationToken);

        return detail is null
            ? null
            : AuditPresenter.Detail(detail, await store.GetTimeZoneAsync(call.OrganizationId, cancellationToken));
    }
}

/// <summary>
/// GET /audit-log/export (FR-05, BR-14): the list query without paging. More than 5,000 matches is a conflict that writes
/// nothing; otherwise the CSV is built, then <c>export.generated</c> is recorded, then the file is returned (BR-16).
/// </summary>
public sealed class ExportAuditLogHandler(
    IAuditLogStore store, IBranchScopeResolver scopes, IExportAuditWriter audit, TimeProvider timeProvider)
{
    public const int MaxExportRows = 5000;

    public const string ExportTooLargeCode = "export_too_large";

    public const string ExportTooLargeTitle = "Narrow the filters to export 5,000 rows or fewer.";

    public async Task<BillingOutcome<AuditCsvFile>> HandleAsync(
        MembershipCall call, string roleCode, AuditLogQueryText text, CancellationToken cancellationToken)
    {
        var prepared = await AuditLogSupport.PrepareAsync(store, scopes, call, roleCode, text, false, timeProvider, cancellationToken);

        switch (prepared)
        {
            case BillingOutcome<AuditPrepared>.Succeeded ok:
                var rows = await store.ExportAsync(ok.Value.Viewer, ok.Value.Query, MaxExportRows, cancellationToken);

                if (rows is null)
                {
                    return new BillingOutcome<AuditCsvFile>.Conflict(ExportTooLargeCode, ExportTooLargeTitle);
                }

                var items = rows.Select(AuditPresenter.Item).ToList();
                var file = new AuditCsvFile(
                    $"audit-log-{ok.Value.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.csv",
                    AuditPresenter.Csv(items, ok.Value.TimeZone));

                await audit.RecordAsync(
                    call,
                    ExportAudit.Entry(
                        ExportReportTypes.AuditLog,
                        ok.Value.Query.BranchId,
                        AuditLogSupport.NormalizedFilters(ok.Value.Query),
                        items.Count),
                    cancellationToken);

                return new BillingOutcome<AuditCsvFile>.Succeeded(file);
            case BillingOutcome<AuditPrepared>.Invalid invalid:
                return new BillingOutcome<AuditCsvFile>.Invalid(invalid.Errors);
            case BillingOutcome<AuditPrepared>.Conflict conflict:
                return new BillingOutcome<AuditCsvFile>.Conflict(conflict.Code, conflict.Title);
            default:
                return new BillingOutcome<AuditCsvFile>.NotFound();
        }
    }
}
