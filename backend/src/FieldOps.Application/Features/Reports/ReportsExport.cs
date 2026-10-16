using System.Globalization;
using FieldOps.Application.Features.BillingReview;

namespace FieldOps.Application.Features.Reports;

/// <summary>One lines of the PDF summary block; <see cref="Previous"/> is null when the comparison is off or not applicable.</summary>
public sealed record ReportPdfMetric(string Label, string Current, string? Previous);

public sealed record ReportPdfChart(string Title, IReadOnlyList<string> Header, IReadOnlyList<IReadOnlyList<string>> Rows);

public sealed record ReportPdfLine(string Label, string Value);

/// <summary>The neutral, already formatted document the PDF renderer lays out (reports-workspace BR-15).</summary>
public sealed record ReportPdfDocument(
    string OrganizationName,
    string ReportName,
    string Description,
    string Period,
    string Branch,
    string Generated,
    string Currency,
    IReadOnlyList<ReportPdfLine> Filters,
    IReadOnlyList<ReportPdfMetric> Summary,
    ReportPdfChart Chart,
    IReadOnlyList<ReportPdfLine> Definitions,
    string? Disclaimer,
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<string>> Rows,
    IReadOnlyList<string> Totals);

public interface IReportPdfRenderer
{
    byte[] Render(ReportPdfDocument document);
}

/// <summary>Everything an export needs: the report, its context, the whole sorted set and the applied comparison.</summary>
public sealed record ReportExportInput(
    ReportDefinition Definition, ReportContext Context, ReportResult Result, IReadOnlyList<ReportRow> Rows, bool Compare);

/// <summary>Value formatting shared by the CSV (BR-14) and the PDF (BR-15); every date and time is in the organization time zone.</summary>
internal static class ReportFormats
{
    public static string? Csv(ReportValueKind kind, object? value, TimeZoneInfo zone) =>
        Format(kind, value, zone, "yyyy-MM-dd", "yyyy-MM-dd HH:mm", false);

    public static string? Display(ReportValueKind kind, object? value, TimeZoneInfo zone, string? key = null)
    {
        if (key == "varianceMinutes" && value is int minutes)
        {
            return minutes == 0
                ? "On estimate"
                : string.Create(CultureInfo.InvariantCulture, $"{(minutes > 0 ? "+" : "−")}{Math.Abs(minutes)} min");
        }

        return Format(kind, value, zone, "MMM d, yyyy", "MMM d, yyyy h:mm tt", true);
    }

    private static string? Format(ReportValueKind kind, object? value, TimeZoneInfo zone, string dateFormat, string timeFormat, bool display)
    {
        if (value is null)
        {
            return null;
        }

        var invariant = CultureInfo.InvariantCulture;

        return kind switch
        {
            ReportValueKind.Date when value is DateOnly date => date.ToString(dateFormat, invariant),
            ReportValueKind.DateTime when value is DateTimeOffset instant =>
                TimeZoneInfo.ConvertTime(instant, zone).ToString(timeFormat, invariant),
            ReportValueKind.Money when value is decimal money => money.ToString("0.00", invariant),
            ReportValueKind.Percent1 when value is decimal percent => percent.ToString("0.0", invariant),
            ReportValueKind.Percent0 => Convert.ToDecimal(value, invariant).ToString("0", invariant),
            ReportValueKind.Hours => Math.Round(Convert.ToDecimal(value, invariant) / 60m, 0, MidpointRounding.AwayFromZero).ToString("0", invariant),
            ReportValueKind.Minutes when display => string.Create(invariant, $"{value} min"),
            ReportValueKind.Minutes or ReportValueKind.Count => Convert.ToString(value, invariant),
            _ => Convert.ToString(value, invariant),
        };
    }

    public static string MetricValue(ReportMetric metric, decimal? value, string currency)
    {
        if (value is not { } number)
        {
            return string.Empty;
        }

        return metric.Unit switch
        {
            "currency" => number.ToString("0.00", CultureInfo.InvariantCulture),
            "percent" => number.ToString(metric.Decimals > 0 ? "0." + new string('0', metric.Decimals) : "0", CultureInfo.InvariantCulture),
            _ => number.ToString("0", CultureInfo.InvariantCulture),
        };
    }
}

/// <summary>The CSV (BR-14) and PDF model (BR-15) of a report, built from the same sorted set as the data endpoint.</summary>
public static class ReportsExport
{
    public static string FileName(ReportDefinition definition, ReportContext context, string extension)
    {
        var stem = definition.Key.Replace('_', '-');

        return definition.UsesPeriod
            ? string.Create(CultureInfo.InvariantCulture, $"{stem}-{ReportRules.Day(context.Period.From)}-to-{ReportRules.Day(context.Period.To)}.{extension}")
            : string.Create(CultureInfo.InvariantCulture, $"{stem}-{ReportRules.Day(context.Today)}.{extension}");
    }

    public static ReportExportFile Csv(ReportExportInput input)
    {
        var definition = input.Definition;
        var zone = input.Context.Zone;
        var currency = input.Context.Organization.Currency;
        var money = definition.HasMoney;
        var rows = new List<string>();

        var header = new List<string?> { "Section" };
        header.AddRange(definition.Columns.Select(column => CsvCell.Escape(column.Label)));

        if (money)
        {
            header.Add("Currency");
        }

        rows.Add(Line(header));

        foreach (var row in input.Rows)
        {
            var cells = new List<string?> { "Rows" };

            cells.AddRange(definition.Columns.Select(column => Cell(column, row.Cells.GetValueOrDefault(column.Key), zone)));

            if (money)
            {
                cells.Add(CsvCell.Escape(Convert.ToString(row.Cells.GetValueOrDefault("currency") ?? currency, CultureInfo.InvariantCulture)));
            }

            rows.Add(Line(cells));
        }

        var totals = new List<string?> { "Totals" };

        totals.AddRange(definition.Columns.Select(column => Cell(column, input.Result.Totals.GetValueOrDefault(column.Key), zone)));

        if (money)
        {
            totals.Add(currency);
        }

        rows.Add(Line(totals));
        rows.Add(string.Empty);
        rows.Add(Line(["Section", "Metric", "Current", "Previous", "Unit"]));

        foreach (var metric in input.Result.Summary)
        {
            var unit = metric.Unit == "currency" ? currency : metric.Unit;

            rows.Add(Line(
            [
                "Summary",
                CsvCell.Escape(metric.Label),
                ReportFormats.MetricValue(metric, metric.Value, currency),
                input.Compare ? ReportFormats.MetricValue(metric, metric.Previous, currency) : null,
                unit,
            ]));
        }

        return new ReportExportFile(
            CsvCell.Document(rows), "text/csv; charset=utf-8", FileName(definition, input.Context, "csv"), input.Rows.Count);
    }

    // Cells are already escaped (free text) or raw (numbers, dates), so a row is a plain join.
    private static string Line(IEnumerable<string?> cells) => string.Join(',', cells.Select(cell => cell ?? string.Empty));

    // Free text is escaped against formula injection; numbers and dates are written as they are, so a negative profit stays a number.
    private static string? Cell(ReportColumn column, object? value, TimeZoneInfo zone)
    {
        var text = ReportFormats.Csv(column.Kind, value, zone);

        return column.Kind == ReportValueKind.Text ? CsvCell.Escape(text) : text;
    }

    public static ReportPdfDocument Pdf(ReportExportInput input, string organizationName)
    {
        var definition = input.Definition;
        var context = input.Context;
        var zone = context.Zone;
        var query = context.Query;
        var selectedBranch = query.BranchId is { } branchId ? context.BranchName(branchId, "Unavailable branch") : "All branches";
        var period = definition.UsesPeriod
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{context.Period.From.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)} – {context.Period.To.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)}")
            : "As of " + context.Today.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
        var generated = string.Create(
            CultureInfo.InvariantCulture,
            $"Generated {TimeZoneInfo.ConvertTime(context.Now, zone).ToString("MMM d, yyyy h:mm tt", CultureInfo.InvariantCulture)} {context.Organization.Timezone}");

        var filters = new List<ReportPdfLine>();

        if (query.Status != "all")
        {
            filters.Add(new ReportPdfLine("Status", ReportCatalog.StatusLabel(definition.Key, query.Status)));
        }

        if (query.CategoryId is { } categoryId)
        {
            filters.Add(new ReportPdfLine("Category", context.Directory.Categories.FirstOrDefault(item => item.Id == categoryId)?.Name ?? string.Empty));
        }

        if (query.TechnicianId is { } technicianId)
        {
            filters.Add(new ReportPdfLine("Technician", context.Directory.Technicians.FirstOrDefault(item => item.Id == technicianId)?.Name ?? string.Empty));
        }

        if (definition.Key == ReportKeys.InvoiceAging && query.Bucket != "all")
        {
            filters.Add(new ReportPdfLine("Bucket", query.Bucket));
        }

        if (definition.Key == ReportKeys.QuoteConversion && query.Event != "all")
        {
            filters.Add(new ReportPdfLine("Event", query.Event == "sent" ? "Sent" : "Accepted"));
        }

        if (definition.Key == ReportKeys.JobsOnTime && query.OnTime != "all")
        {
            filters.Add(new ReportPdfLine("On-time result", query.OnTime));
        }

        var numbers = input.Result.Chart.Kind is "category_bars" or "bucket_bars" ? "0.00" : "0";
        var chart = input.Result.Chart;

        return new ReportPdfDocument(
            organizationName,
            definition.Name,
            definition.Description,
            period,
            selectedBranch,
            generated,
            context.Organization.Currency,
            filters,
            [.. input.Result.Summary.Select(metric => new ReportPdfMetric(
                metric.Label,
                ReportFormats.MetricValue(metric, metric.Value, context.Organization.Currency) is { Length: > 0 } current ? current : "—",
                input.Compare && definition.UsesPeriod
                    ? ReportFormats.MetricValue(metric, metric.Previous, context.Organization.Currency) is { Length: > 0 } previous ? previous : "—"
                    : null))],
            new ReportPdfChart(
                definition.Name,
                ["Item", .. chart.Series.Select(series => series.Label)],
                [.. chart.Points.Select(point => (IReadOnlyList<string>)
                    [point.Label, .. chart.Series.Select(series => point.Values.GetValueOrDefault(series.Key).ToString(numbers, CultureInfo.InvariantCulture))])]),
            [.. definition.Definitions.Select(item => new ReportPdfLine(item.Term, item.Text))],
            definition.Disclaimer,
            [.. definition.Columns.Select(column => column.Label)],
            [.. input.Rows.Select(row => (IReadOnlyList<string>)
                [.. definition.Columns.Select(column => ReportFormats.Display(column.Kind, row.Cells.GetValueOrDefault(column.Key), zone, column.Key) ?? "—")])],
            [.. definition.Columns.Select(column => ReportFormats.Display(column.Kind, input.Result.Totals.GetValueOrDefault(column.Key), zone, column.Key) ?? string.Empty)]);
    }
}
