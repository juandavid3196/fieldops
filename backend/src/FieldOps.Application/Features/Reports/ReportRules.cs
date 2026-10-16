using System.Globalization;
using FieldOps.Application.Features.BusinessOverview;
using FieldOps.Application.Features.Quotes;

namespace FieldOps.Application.Features.Reports;

/// <summary>Pure rules of the reports workspace: query parsing (BR-03 to BR-07), stable sorting and the shared metric helpers.</summary>
public static class ReportRules
{
    public static readonly int[] PageSizes = [10, 25, 50];

    public static readonly string[] Buckets = ["all", "current", "1_30", "31_60", "60_plus"];

    public static readonly string[] Events = ["all", "sent", "accepted"];

    public static readonly string[] OnTimeResults = ["all", "on_time", "late", "not_started", "not_due"];

    /// <summary>
    /// Parses the text values of a data (or export) request. The range is checked against today by
    /// <see cref="BusinessOverviewRules.ValidateRange"/>, and the category and branch against the directory by the caller.
    /// </summary>
    public static (ReportQuery? Query, Dictionary<string, string[]> Errors) Parse(ReportDefinition definition, ReportQueryText text, bool export)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var from = Date(text.From, "from", errors);
        var to = Date(text.To, "to", errors);
        var branchId = Id(text.BranchId, "branchId", BusinessOverviewMessages.BranchInvalid, errors);
        var categoryId = Id(text.CategoryId, "categoryId", ReportMessages.CategoryInvalid, errors);
        var technicianId = Id(text.TechnicianId, "technicianId", ReportMessages.TechnicianInvalid, errors);
        var status = Choice(text.Status, definition.StatusValues, "status", ReportMessages.StatusInvalid, errors);
        var bucket = Choice(text.Bucket, Buckets, "bucket", ReportMessages.BucketInvalid, errors);
        var evt = Choice(text.Event, Events, "event", ReportMessages.EventInvalid, errors);
        var onTime = Choice(text.OnTime, OnTimeResults, "onTime", ReportMessages.OnTimeInvalid, errors);

        var sort = definition.DefaultSort;

        if (!string.IsNullOrWhiteSpace(text.Sort))
        {
            if (definition.SortKeys.Contains(text.Sort.Trim(), StringComparer.Ordinal))
            {
                sort = text.Sort.Trim();
            }
            else
            {
                errors["sort"] = [ReportMessages.SortInvalid];
            }
        }

        var descending = definition.DefaultDescending;

        if (!string.IsNullOrWhiteSpace(text.Direction))
        {
            switch (text.Direction.Trim().ToLowerInvariant())
            {
                case "asc":
                    descending = false;
                    break;
                case "desc":
                    descending = true;
                    break;
                default:
                    errors["direction"] = [ReportMessages.DirectionInvalid];
                    break;
            }
        }

        var page = 1;
        var pageSize = PageSizes[0];

        if (!export)
        {
            if (!string.IsNullOrWhiteSpace(text.Page)
                && !(int.TryParse(text.Page.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out page) && page >= 1))
            {
                errors["page"] = [ReportMessages.PageInvalid];
            }

            if (!string.IsNullOrWhiteSpace(text.PageSize)
                && !(int.TryParse(text.PageSize.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out pageSize) && PageSizes.Contains(pageSize)))
            {
                errors["pageSize"] = [ReportMessages.PageSizeInvalid];
            }
        }

        string? format = null;
        var compare = true;

        if (export)
        {
            switch (text.Format?.Trim().ToLowerInvariant())
            {
                case "csv":
                    format = "csv";
                    break;
                case "pdf":
                    format = "pdf";
                    break;
                default:
                    errors["format"] = [ReportMessages.FormatInvalid];
                    break;
            }

            if (!string.IsNullOrWhiteSpace(text.Compare))
            {
                switch (text.Compare.Trim().ToLowerInvariant())
                {
                    case "true":
                        break;
                    case "false":
                        compare = false;
                        break;
                    default:
                        errors["compare"] = [BusinessOverviewMessages.CompareInvalid];
                        break;
                }
            }
        }

        if (errors.Count > 0 || from is not { } start || to is not { } end)
        {
            return (null, errors);
        }

        return (new ReportQuery(
            start, end, branchId, status, categoryId, technicianId, bucket, evt, onTime, sort, descending, Math.Max(page, 1), pageSize, format, compare), errors);
    }

    /// <summary>BR-07: stable order by the sort key; missing values last in either direction; ties by number or name, then id.</summary>
    public static List<ReportRow> Sort(IReadOnlyList<ReportRow> rows, string key, bool descending)
    {
        var comparer = new SortValueComparer();
        var present = rows.Where(row => row.SortValue(key) is not null);
        var absent = rows.Where(row => row.SortValue(key) is null);

        var ordered = descending
            ? present.OrderByDescending(row => row.SortValue(key), comparer)
            : present.OrderBy(row => row.SortValue(key), comparer);

        return
        [
            .. ordered
                .ThenBy(row => row.TieName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.TieNumber)
                .ThenBy(row => row.Id),
            .. absent
                .OrderBy(row => row.TieName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.TieNumber)
                .ThenBy(row => row.Id),
        ];
    }

    public static decimal Money(decimal value) => QuoteCalculator.Money(value);

    public static decimal OneDecimal(decimal value) => Math.Round(value, 1, MidpointRounding.AwayFromZero);

    /// <summary>BR-10 of business-overview: whole hours.</summary>
    public static decimal Hours(double minutes) => Math.Round((decimal)minutes / 60m, 0, MidpointRounding.AwayFromZero);

    public static int WholeMinutes(double minutes) => (int)Math.Round(minutes, MidpointRounding.AwayFromZero);

    public static string BucketLabel(BusinessBucket bucket) =>
        bucket.Start == bucket.End
            ? bucket.Start.ToString("MMM d", CultureInfo.InvariantCulture)
            : string.Create(
                CultureInfo.InvariantCulture, $"{bucket.Start:MMM d} – {bucket.End:MMM d}");

    public static string Day(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>BR-13: the ids of the destinations the caller's role may open; a missing entity stays null.</summary>
    public static ReportLinks Links(
        string role,
        Guid? job = null,
        Guid? visit = null,
        Guid? customer = null,
        Guid? invoice = null,
        Guid? quote = null,
        Guid? request = null,
        Guid? technician = null) =>
        new(
            ReportLinkAccess.CanOpenJob(role) ? job : null,
            ReportLinkAccess.CanOpenJob(role) ? visit : null,
            ReportLinkAccess.CanOpenCustomer(role) ? customer : null,
            ReportLinkAccess.CanOpenInvoice(role) ? invoice : null,
            ReportLinkAccess.CanOpenQuote(role) ? quote : null,
            ReportLinkAccess.CanOpenRequest(role) ? request : null,
            ReportLinkAccess.CanOpenTechnician(role) ? technician : null);

    public static ReportMetric Count(string key, string label, int? value, int? previous) =>
        new(key, label, "count", value, previous);

    public static ReportMetric Currency(string key, string label, decimal? value, decimal? previous) =>
        new(key, label, "currency", value is { } v ? Money(v) : null, previous is { } p ? Money(p) : null, 2);

    public static ReportMetric Percent(string key, string label, decimal? value, decimal? previous, int decimals = 0) =>
        new(key, label, "percent", value, previous, decimals);

    public static ReportMetric HoursMetric(string key, string label, double? minutes, double? previous) =>
        new(key, label, "hours", minutes is { } m ? Hours(m) : null, previous is { } p ? Hours(p) : null);

    public static ReportMetric MinutesMetric(string key, string label, int? value, int? previous) =>
        new(key, label, "minutes", value, previous);

    private static DateOnly? Date(string? text, string key, Dictionary<string, string[]> errors)
    {
        if (DateOnly.TryParseExact(text?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        errors[key] = [BusinessOverviewMessages.DateInvalid];

        return null;
    }

    private static Guid? Id(string? text, string key, string message, Dictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (Guid.TryParse(text.Trim(), out var parsed) && parsed != Guid.Empty)
        {
            return parsed;
        }

        errors[key] = [message];

        return null;
    }

    private static string Choice(string? text, IReadOnlyList<string> allowed, string key, string message, Dictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "all";
        }

        var value = text.Trim().ToLowerInvariant();

        if (allowed.Contains(value, StringComparer.Ordinal))
        {
            return value;
        }

        errors[key] = [message];

        return "all";
    }

    private sealed class SortValueComparer : IComparer<object?>
    {
        public int Compare(object? x, object? y) =>
            x is string left && y is string right
                ? StringComparer.OrdinalIgnoreCase.Compare(left, right)
                : x is IComparable comparable ? comparable.CompareTo(y) : 0;
    }
}
