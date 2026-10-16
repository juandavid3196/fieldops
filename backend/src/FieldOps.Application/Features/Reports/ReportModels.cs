using System.Text.Json;
using System.Text.Json.Serialization;
using FieldOps.Application.Features.Access;
using FieldOps.Application.Features.BusinessOverview;
using FieldOps.Application.Features.ServiceRequests;

namespace FieldOps.Application.Features.Reports;

/// <summary>Raw query values; every value is optional text so a malformed value becomes a 400 (reports-workspace BR-07).</summary>
public sealed record ReportQueryText(
    string? From,
    string? To,
    string? BranchId,
    string? Status,
    string? CategoryId,
    string? TechnicianId,
    string? Bucket,
    string? Event,
    string? OnTime,
    string? Sort,
    string? Direction,
    string? Page,
    string? PageSize,
    string? Format,
    string? Compare);

/// <summary>A validated query (BR-03 to BR-07). The filters that do not apply to the report keep their defaults.</summary>
public sealed record ReportQuery(
    DateOnly From,
    DateOnly To,
    Guid? BranchId,
    string Status,
    Guid? CategoryId,
    Guid? TechnicianId,
    string Bucket,
    string Event,
    string OnTime,
    string Sort,
    bool Descending,
    int Page,
    int PageSize,
    string? Format,
    bool Compare);

public sealed record ReportOrganization(
    string Name, string Timezone, string Currency, string WorkOrderPrefix, string InvoicePrefix, string QuotePrefix, string RequestPrefix);

public sealed record ReportBranch(Guid Id, string Name, bool IsActive);

public sealed record ReportCategory(Guid Id, string Name, bool IsActive);

public sealed record ReportTechnicianOption(Guid Id, string Name, bool IsActive, Guid BranchId);

/// <summary>The branches and technicians in the caller's scope and the categories of the organization (BR-03).</summary>
public sealed record ReportDirectory(
    IReadOnlyList<ReportBranch> Branches, IReadOnlyList<ReportCategory> Categories, IReadOnlyList<ReportTechnicianOption> Technicians);

public sealed record ReportOption(string Key, string Group, string Name, string Description);

public sealed record ReportOptionsView(
    string Timezone,
    string Currency,
    DateOnly Today,
    decimal MarginTarget,
    IReadOnlyList<ReportOption> Reports,
    IReadOnlyList<ReportBranch> Branches,
    IReadOnlyList<ReportCategory> Categories,
    IReadOnlyList<ReportTechnicianOption> Technicians);

/// <param name="Decimals">The precision of the CSV and PDF value; not part of the response.</param>
public sealed record ReportMetric(string Key, string Label, string Unit, decimal? Value, decimal? Previous, [property: JsonIgnore] int Decimals = 0);

public sealed record ReportSeries(string Key, string Label);

public sealed record ReportPoint(string Label, string? Start, IReadOnlyDictionary<string, decimal> Values);

public sealed record ReportChart(string Kind, IReadOnlyList<ReportSeries> Series, IReadOnlyList<ReportPoint> Points);

public sealed record ReportNotes(int? OtherCurrencyCount);

/// <summary>The ids of the entities a row may open; null when the entity is not openable by the caller (BR-13).</summary>
public sealed record ReportLinks(Guid? Job, Guid? Visit, Guid? Customer, Guid? Invoice, Guid? Quote, Guid? Request, Guid? Technician)
{
    public static ReportLinks None { get; } = new(null, null, null, null, null, null, null);
}

/// <summary>
/// One report row. <see cref="Cells"/> are the response fields; <see cref="Sort"/> holds the sort value of every allowlisted
/// key whose value differs from its cell. A null sort value sorts last in either direction (BR-07).
/// </summary>
public sealed record ReportRow(
    Guid Id,
    string TieName,
    long TieNumber,
    IReadOnlyDictionary<string, object?> Cells,
    ReportLinks Links,
    IReadOnlyDictionary<string, object?> Sort)
{
    public object? SortValue(string key) => Sort.TryGetValue(key, out var value) ? value : Cells.GetValueOrDefault(key);
}

/// <summary>The whole filtered set of a report before sorting and paging.</summary>
public sealed record ReportResult(
    IReadOnlyList<ReportMetric> Summary,
    int? OtherCurrencyCount,
    ReportChart Chart,
    IReadOnlyList<ReportRow> Rows,
    IReadOnlyDictionary<string, object?> Totals);

public sealed record ReportView(
    string ReportKey,
    DateOnly? From,
    DateOnly? To,
    DateOnly? PreviousFrom,
    DateOnly? PreviousTo,
    DateOnly AsOf,
    string Timezone,
    string Currency,
    string? Granularity,
    decimal MarginTarget,
    IReadOnlyList<ReportMetric> Summary,
    ReportNotes Notes,
    ReportChart Chart,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Items,
    IReadOnlyDictionary<string, object?> Totals,
    int Total,
    int Page,
    int PageSize);

public sealed record ReportExportFile(byte[] Content, string ContentType, string FileName, int RowCount);

public sealed record ReportHistoryItem(
    long Id, DateTimeOffset OccurredAt, string ReportKey, string ReportName, string Format, int RowCount, JsonElement Filters);

public sealed record ReportHistoryView(IReadOnlyList<ReportHistoryItem> Items);

/// <summary>An audit row of the caller's own report exports (BR-17).</summary>
public sealed record ReportExportRecord(long Id, DateTimeOffset OccurredAt, string Metadata);

/// <summary>Everything a provider needs: the validated query, the resolved period, the scope and the caller (BR-03, BR-04).</summary>
public sealed record ReportContext(
    MembershipCall Call,
    string Role,
    ReportOrganization Organization,
    TimeZoneInfo Zone,
    DateTimeOffset Now,
    DateOnly Today,
    BusinessPeriod Period,
    ReportQuery Query,
    BranchScope Scope,
    IReadOnlyList<Guid> BranchIds,
    ReportDirectory Directory)
{
    public DateTimeOffset CurrentStart => OrganizationTime.StartOfDayUtc(Period.From, Zone);

    public DateTimeOffset CurrentEnd => OrganizationTime.StartOfDayUtc(Period.To.AddDays(1), Zone);

    public DateTimeOffset PreviousStart => OrganizationTime.StartOfDayUtc(Period.PreviousFrom, Zone);

    /// <summary>BR-11: the reference instant of arrivals is the earlier of now and the end of the period.</summary>
    public DateTimeOffset Reference => Now < CurrentEnd ? Now : CurrentEnd;

    public string BranchName(Guid branchId, string fallback = "")
    {
        var branch = Directory.Branches.FirstOrDefault(item => item.Id == branchId);

        return branch is null ? fallback : branch.IsActive ? branch.Name : branch.Name + " (Inactive)";
    }
}

public interface IReportDataProvider
{
    string Key { get; }

    Task<ReportResult> LoadAsync(ReportContext context, CancellationToken cancellationToken);
}

/// <summary>Outcome of a reports use case; a 403 is distinct from a 404 (BR-02).</summary>
public abstract record ReportOutcome<T>
{
    public sealed record Succeeded(T Value) : ReportOutcome<T>;

    public sealed record Forbidden : ReportOutcome<T>;

    public sealed record NotFound(string Code) : ReportOutcome<T>;

    public sealed record Invalid(IReadOnlyDictionary<string, string[]> Errors) : ReportOutcome<T>;

    public sealed record Conflict(string Code, string Title) : ReportOutcome<T>;
}

/// <summary>The row limits of the CSV (5,000) and PDF (1,000) exports (BR-14, BR-15); a singleton so tests can lower them.</summary>
public sealed record ReportExportLimits(int Csv, int Pdf)
{
    public static ReportExportLimits Default { get; } = new(5000, 1000);
}

public static class ReportMessages
{
    public const string ReportNotFoundCode = "report_not_found";

    public const string BranchNotFoundCode = "branch_not_found";

    public const string TechnicianNotFoundCode = "technician_not_found";

    public const string ExportTooLargeCode = "export_too_large";

    public const string CsvTooLarge = "Narrow the filters to export 5,000 rows or fewer.";

    public const string PdfTooLarge = "Narrow the filters to export 1,000 rows or fewer.";

    public const string StatusInvalid = "Select a valid status.";

    public const string BucketInvalid = "Select a valid aging bucket.";

    public const string EventInvalid = "Select a valid event.";

    public const string OnTimeInvalid = "Select a valid on-time result.";

    public const string CategoryInvalid = "Select a valid service category.";

    public const string TechnicianInvalid = "Select a valid technician.";

    public const string SortInvalid = "Select a valid sort column.";

    public const string DirectionInvalid = "Direction must be asc or desc.";

    public const string PageInvalid = "Page must be 1 or greater.";

    public const string PageSizeInvalid = "Page size must be 10, 25 or 50.";

    public const string FormatInvalid = "Format must be csv or pdf.";
}

/// <summary>Persistence port of the reports workspace. Every method is scoped to one organization and read-only, except nothing writes here.</summary>
public interface IReportsStore
{
    Task<ReportOrganization> GetOrganizationAsync(Guid organizationId, CancellationToken cancellationToken);

    Task<ReportDirectory> GetDirectoryAsync(Guid organizationId, BranchScope scope, CancellationToken cancellationToken);

    Task<ProfitabilitySource> LoadProfitabilityAsync(ReportContext context, CancellationToken cancellationToken);

    Task<AgingSource> LoadAgingAsync(ReportContext context, CancellationToken cancellationToken);

    Task<QuotesSource> LoadQuotesAsync(ReportContext context, CancellationToken cancellationToken);

    Task<JobsSource> LoadJobsAsync(ReportContext context, CancellationToken cancellationToken);

    Task<UtilizationSource> LoadUtilizationAsync(ReportContext context, CancellationToken cancellationToken);

    /// <summary>The caller's own <c>export.generated</c> rows of the given report types since <paramref name="since"/>, newest first.</summary>
    Task<IReadOnlyList<ReportExportRecord>> ListExportsAsync(
        Guid organizationId, Guid userId, DateTimeOffset since, IReadOnlyList<string> reportTypes, int take, CancellationToken cancellationToken);
}
