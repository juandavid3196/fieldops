using System.Globalization;
using System.Text.Json;
using FieldOps.Application.Features.ServiceRequests;

namespace FieldOps.Application.Features.AuditTrail;

/// <summary>The report types of an <c>export.generated</c> row (BR-16).</summary>
public static class ExportReportTypes
{
    public const string AuditLog = "audit_log";

    public const string Invoices = "invoices";

    public const string Payments = "payments";

    public const string BillingReview = "billing_review";

    public const string BusinessOverview = "business_overview";

    public const string CatalogItems = "catalog_items";

    /// <summary>Prefix of the reports workspace export types: <c>report_&lt;reportKey&gt;</c> (reports-workspace BR-16).</summary>
    public const string ReportPrefix = "report_";

    public static string Report(string reportKey) => ReportPrefix + reportKey;
}

/// <summary>One <c>export.generated</c> row to insert: the report type, the export's single branch filter and the metadata JSON.</summary>
public sealed record ExportAuditEntry(string ReportType, Guid? BranchId, string Metadata);

/// <summary>
/// Writes the <c>export.generated</c> row after an export's CSV is fully built and before it is returned (BR-16). A failure
/// propagates, so the export answers 500 with no file.
/// </summary>
public interface IExportAuditWriter
{
    Task RecordAsync(MembershipCall call, ExportAuditEntry entry, CancellationToken cancellationToken);
}

/// <summary>Builds the BR-16 metadata from normalized values only: never the search text, names, emails or free text.</summary>
public static class ExportAudit
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <param name="reportType">One of <see cref="ExportReportTypes"/>.</param>
    /// <param name="filters">Normalized filter values (ids, codes, dates, sort) and <c>hasSearch</c>.</param>
    /// <param name="rowCount">The number of CSV data rows, header excluded.</param>
    /// <param name="format">The file format (<c>csv</c> or <c>pdf</c>) of a reports workspace export; omitted when null.</param>
    public static string Metadata(string reportType, IReadOnlyDictionary<string, object?> filters, int rowCount, string? format = null)
    {
        var metadata = new Dictionary<string, object?> { ["reportType"] = reportType };

        if (format is not null)
        {
            metadata["format"] = format;
        }

        metadata["filters"] = filters.ToDictionary(pair => pair.Key, pair => Normalize(pair.Value), StringComparer.Ordinal);
        metadata["rowCount"] = rowCount;

        return JsonSerializer.Serialize(metadata, JsonOptions);
    }

    public static ExportAuditEntry Entry(
        string reportType, Guid? branchId, IReadOnlyDictionary<string, object?> filters, int rowCount, string? format = null) =>
        new(reportType, branchId, Metadata(reportType, filters, rowCount, format));

    private static object? Normalize(object? value) =>
        value switch
        {
            DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Guid id => id.ToString(),
            Enum option => JsonNamingPolicy.CamelCase.ConvertName(option.ToString()),
            _ => value,
        };
}
