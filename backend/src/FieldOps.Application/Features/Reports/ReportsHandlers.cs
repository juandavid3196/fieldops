using System.Text.Json;
using FieldOps.Application.Features.Access;
using FieldOps.Application.Features.AuditTrail;
using FieldOps.Application.Features.BusinessOverview;
using FieldOps.Application.Features.ServiceRequests;

namespace FieldOps.Application.Features.Reports;

internal sealed record ReportPrepared(ReportDefinition Definition, IReportDataProvider Provider, ReportContext Context);

internal static class ReportsSupport
{
    /// <summary>
    /// The order of reports-workspace BR-02/BR-03: unknown key 404, role 403, query 400, then the branch and technician 404, all
    /// before any report data is read. The organization and the scope are the session's.
    /// </summary>
    public static async Task<ReportOutcome<ReportPrepared>> PrepareAsync(
        IReportsStore store,
        IBranchScopeResolver scopes,
        IEnumerable<IReportDataProvider> providers,
        MembershipCall call,
        string role,
        string reportKey,
        ReportQueryText text,
        bool export,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (ReportCatalog.Find(reportKey) is not { } definition)
        {
            return new ReportOutcome<ReportPrepared>.NotFound(ReportMessages.ReportNotFoundCode);
        }

        if (!definition.Roles.Contains(role, StringComparer.Ordinal))
        {
            return new ReportOutcome<ReportPrepared>.Forbidden();
        }

        var (parsed, errors) = ReportRules.Parse(definition, text, export);
        var organization = await store.GetOrganizationAsync(call.OrganizationId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var zone = OrganizationTime.FindZone(organization.Timezone);
        var today = OrganizationTime.LocalDate(now, zone);

        if (parsed is not null)
        {
            foreach (var (key, value) in BusinessOverviewRules.ValidateRange(parsed.From, parsed.To, today))
            {
                errors[key] = value;
            }
        }

        if (errors.Count > 0 || parsed is null)
        {
            return new ReportOutcome<ReportPrepared>.Invalid(errors);
        }

        var scope = await scopes.ResolveAsync(call.OrganizationId, call.MembershipId, cancellationToken);
        var directory = await store.GetDirectoryAsync(call.OrganizationId, scope, cancellationToken);

        if (parsed.CategoryId is { } categoryId && directory.Categories.All(category => category.Id != categoryId))
        {
            return new ReportOutcome<ReportPrepared>.Invalid(
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["categoryId"] = [ReportMessages.CategoryInvalid] });
        }

        if (parsed.BranchId is { } branchId && directory.Branches.All(branch => branch.Id != branchId))
        {
            return new ReportOutcome<ReportPrepared>.NotFound(ReportMessages.BranchNotFoundCode);
        }

        if (parsed.TechnicianId is { } technicianId && directory.Technicians.All(technician => technician.Id != technicianId))
        {
            return new ReportOutcome<ReportPrepared>.NotFound(ReportMessages.TechnicianNotFoundCode);
        }

        var provider = providers.Single(candidate => candidate.Key == definition.Key);
        var branchIds = parsed.BranchId is { } selected ? [selected] : directory.Branches.Select(branch => branch.Id).ToList();
        var context = new ReportContext(
            call,
            role,
            organization,
            zone,
            now,
            today,
            BusinessOverviewRules.ResolvePeriod(parsed.From, parsed.To),
            parsed,
            scope,
            branchIds,
            directory);

        return new ReportOutcome<ReportPrepared>.Succeeded(new ReportPrepared(definition, provider, context));
    }

    public static ReportOutcome<T> Failure<T>(ReportOutcome<ReportPrepared> outcome) =>
        outcome switch
        {
            ReportOutcome<ReportPrepared>.Invalid invalid => new ReportOutcome<T>.Invalid(invalid.Errors),
            ReportOutcome<ReportPrepared>.NotFound notFound => new ReportOutcome<T>.NotFound(notFound.Code),
            _ => new ReportOutcome<T>.Forbidden(),
        };
}

/// <summary>GET /reports/options (BR-01, BR-03): the visible reports and the in-scope branches, categories and technicians.</summary>
public sealed class GetReportOptionsHandler(IReportsStore store, IBranchScopeResolver scopes, TimeProvider timeProvider)
{
    public async Task<ReportOutcome<ReportOptionsView>> HandleAsync(MembershipCall call, string role, CancellationToken cancellationToken)
    {
        var visible = ReportCatalog.VisibleTo(role);

        if (visible.Count == 0)
        {
            return new ReportOutcome<ReportOptionsView>.Forbidden();
        }

        var organization = await store.GetOrganizationAsync(call.OrganizationId, cancellationToken);
        var scope = await scopes.ResolveAsync(call.OrganizationId, call.MembershipId, cancellationToken);
        var directory = await store.GetDirectoryAsync(call.OrganizationId, scope, cancellationToken);
        var today = OrganizationTime.LocalDate(timeProvider.GetUtcNow(), OrganizationTime.FindZone(organization.Timezone));

        return new ReportOutcome<ReportOptionsView>.Succeeded(new ReportOptionsView(
            organization.Timezone,
            organization.Currency,
            today,
            BusinessOverviewMessages.MarginTarget,
            [.. visible.Select(definition => new ReportOption(definition.Key, definition.Group, definition.Name, definition.Description))],
            directory.Branches,
            directory.Categories,
            directory.Technicians));
    }
}

/// <summary>GET /reports/{reportKey} (FR-04 to FR-11): the summary, chart, one sorted page and the totals of the whole filtered set.</summary>
public sealed class GetReportHandler(
    IReportsStore store, IBranchScopeResolver scopes, IEnumerable<IReportDataProvider> providers, TimeProvider timeProvider)
{
    public async Task<ReportOutcome<ReportView>> HandleAsync(
        MembershipCall call, string role, string reportKey, ReportQueryText text, CancellationToken cancellationToken)
    {
        var prepared = await ReportsSupport.PrepareAsync(store, scopes, providers, call, role, reportKey, text, false, timeProvider, cancellationToken);

        if (prepared is not ReportOutcome<ReportPrepared>.Succeeded ok)
        {
            return ReportsSupport.Failure<ReportView>(prepared);
        }

        var (definition, provider, context) = ok.Value;
        var query = context.Query;
        var result = await provider.LoadAsync(context, cancellationToken);
        var sorted = ReportRules.Sort(result.Rows, query.Sort, query.Descending);
        var page = sorted.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize);
        var period = definition.UsesPeriod ? context.Period : null;

        return new ReportOutcome<ReportView>.Succeeded(new ReportView(
            definition.Key,
            period?.From,
            period?.To,
            period?.PreviousFrom,
            period?.PreviousTo,
            context.Today,
            context.Organization.Timezone,
            context.Organization.Currency,
            period?.Granularity,
            BusinessOverviewMessages.MarginTarget,
            result.Summary,
            new ReportNotes(result.OtherCurrencyCount),
            result.Chart,
            [.. page.Select(Item)],
            result.Totals,
            sorted.Count,
            query.Page,
            query.PageSize));
    }

    private static Dictionary<string, object?> Item(ReportRow row)
    {
        var item = new Dictionary<string, object?>(StringComparer.Ordinal) { ["id"] = row.Id };

        foreach (var (key, value) in row.Cells)
        {
            item[key] = value;
        }

        item["links"] = row.Links;

        return item;
    }
}

/// <summary>
/// GET /reports/{reportKey}/export (FR-12, BR-14 to BR-16): the whole filtered set as CSV or PDF. A set over the limit is a conflict
/// that writes nothing; otherwise the file is built, then <c>export.generated</c> is recorded, then the file is returned. A failed
/// insert propagates, so the export answers 500 with no file.
/// </summary>
public sealed class ExportReportHandler(
    IReportsStore store,
    IBranchScopeResolver scopes,
    IEnumerable<IReportDataProvider> providers,
    IExportAuditWriter audit,
    IReportPdfRenderer pdf,
    ReportExportLimits limits,
    TimeProvider timeProvider)
{
    public async Task<ReportOutcome<ReportExportFile>> HandleAsync(
        MembershipCall call, string role, string reportKey, ReportQueryText text, CancellationToken cancellationToken)
    {
        var prepared = await ReportsSupport.PrepareAsync(store, scopes, providers, call, role, reportKey, text, true, timeProvider, cancellationToken);

        if (prepared is not ReportOutcome<ReportPrepared>.Succeeded ok)
        {
            return ReportsSupport.Failure<ReportExportFile>(prepared);
        }

        var (definition, provider, context) = ok.Value;
        var query = context.Query;
        var isPdf = query.Format == "pdf";
        var result = await provider.LoadAsync(context, cancellationToken);
        var limit = isPdf ? limits.Pdf : limits.Csv;

        if (result.Rows.Count > limit)
        {
            return new ReportOutcome<ReportExportFile>.Conflict(
                ReportMessages.ExportTooLargeCode, isPdf ? ReportMessages.PdfTooLarge : ReportMessages.CsvTooLarge);
        }

        var input = new ReportExportInput(definition, context, result, ReportRules.Sort(result.Rows, query.Sort, query.Descending), query.Compare);
        var file = isPdf
            ? new ReportExportFile(
                pdf.Render(ReportsExport.Pdf(input, context.Organization.Name)),
                "application/pdf",
                ReportsExport.FileName(definition, context, "pdf"),
                result.Rows.Count)
            : ReportsExport.Csv(input);

        await audit.RecordAsync(
            call,
            ExportAudit.Entry(
                ReportCatalog.ReportTypeOf(definition.Key),
                query.BranchId,
                NormalizedFilters(definition, context),
                file.RowCount,
                query.Format),
            cancellationToken);

        return new ReportOutcome<ReportExportFile>.Succeeded(file);
    }

    // BR-16: normalized dates, ids and codes of the applied state only; no names or free text.
    private static Dictionary<string, object?> NormalizedFilters(ReportDefinition definition, ReportContext context)
    {
        var query = context.Query;
        var filters = new Dictionary<string, object?>(StringComparer.Ordinal);

        if (definition.UsesPeriod)
        {
            filters["from"] = query.From;
            filters["to"] = query.To;
        }
        else
        {
            filters["asOf"] = context.Today;
        }

        filters["branchId"] = query.BranchId;
        filters["status"] = query.Status;

        if (definition.Key is ReportKeys.JobProfitability or ReportKeys.QuoteConversion or ReportKeys.JobsOnTime)
        {
            filters["categoryId"] = query.CategoryId;
        }

        if (definition.Key is ReportKeys.JobProfitability or ReportKeys.JobsOnTime or ReportKeys.TechnicianUtilization)
        {
            filters["technicianId"] = query.TechnicianId;
        }

        switch (definition.Key)
        {
            case ReportKeys.InvoiceAging:
                filters["bucket"] = query.Bucket;
                break;
            case ReportKeys.QuoteConversion:
                filters["event"] = query.Event;
                break;
            case ReportKeys.JobsOnTime:
                filters["onTime"] = query.OnTime;
                break;
        }

        filters["sort"] = query.Sort;
        filters["direction"] = query.Descending ? "desc" : "asc";
        filters["compare"] = query.Compare;

        return filters;
    }
}

/// <summary>GET /reports/exports (FR-13, BR-17): the caller's own report exports of the last 90 days, newest first.</summary>
public sealed class ListReportExportsHandler(IReportsStore store, TimeProvider timeProvider)
{
    public const int HistoryDays = 90;

    public const int MaxItems = 20;

    public async Task<ReportOutcome<ReportHistoryView>> HandleAsync(MembershipCall call, string role, CancellationToken cancellationToken)
    {
        var types = ReportCatalog.ExportTypesFor(role);

        if (types.Length == 0)
        {
            return new ReportOutcome<ReportHistoryView>.Forbidden();
        }

        var since = timeProvider.GetUtcNow().AddDays(-HistoryDays);
        var records = await store.ListExportsAsync(call.OrganizationId, call.UserId, since, types, MaxItems, cancellationToken);
        var items = new List<ReportHistoryItem>();

        foreach (var record in records)
        {
            if (Item(record) is { } item)
            {
                items.Add(item);
            }
        }

        return new ReportOutcome<ReportHistoryView>.Succeeded(new ReportHistoryView(items));
    }

    private static ReportHistoryItem? Item(ReportExportRecord record)
    {
        try
        {
            using var document = JsonDocument.Parse(record.Metadata);
            var root = document.RootElement;
            var type = root.GetProperty("reportType").GetString() ?? string.Empty;
            var key = type.StartsWith(ExportReportTypes.ReportPrefix, StringComparison.Ordinal) ? type[ExportReportTypes.ReportPrefix.Length..] : null;

            if (ReportCatalog.Find(key) is not { } definition)
            {
                return null;
            }

            return new ReportHistoryItem(
                record.Id,
                record.OccurredAt,
                definition.Key,
                definition.Name,
                root.TryGetProperty("format", out var format) ? format.GetString() ?? string.Empty : string.Empty,
                root.TryGetProperty("rowCount", out var count) && count.TryGetInt32(out var rows) ? rows : 0,
                root.TryGetProperty("filters", out var filters) ? filters.Clone() : JsonDocument.Parse("{}").RootElement.Clone());
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }
}
