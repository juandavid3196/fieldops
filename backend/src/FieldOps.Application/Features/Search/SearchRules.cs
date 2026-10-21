using System.Globalization;
using System.Text;
using FieldOps.Application.Features.BillingReview;
using FieldOps.Application.Features.ServiceRequests;

namespace FieldOps.Application.Features.Search;

public static class SearchMessages
{
    public const string QueryInvalid = "Enter between 2 and 100 characters.";

    public const string LimitInvalid = "Enter a whole number from 1 to 20.";
}

/// <summary>
/// Pure rules of the global search (global-search BR-05 to BR-08): input validation, number parsing, LIKE escaping, the
/// BR-07 merge and the item content. No EF and no clock. Messages never echo the query.
/// </summary>
public static class SearchRules
{
    public const int MinQueryLength = 2;

    public const int MaxQueryLength = 100;

    public const int DefaultLimit = 20;

    public const int MaxLimit = 20;

    /// <summary>The per-type cap of BR-07; the store reads one more to know whether the type has more matches.</summary>
    public const int PerTypeCap = 5;

    private const int MaxNumberDigits = 18;

    public static BillingOutcome<SearchRequest> Parse(string? query, string? limit)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var text = query?.Trim() ?? string.Empty;

        if (text.Length is < MinQueryLength or > MaxQueryLength)
        {
            errors["q"] = [SearchMessages.QueryInvalid];
        }

        var count = DefaultLimit;

        if (limit is not null
            && (!int.TryParse(limit.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out count) || count is < 1 or > MaxLimit))
        {
            errors["limit"] = [SearchMessages.LimitInvalid];
        }

        return errors.Count > 0
            ? new BillingOutcome<SearchRequest>.Invalid(errors)
            : new BillingOutcome<SearchRequest>.Succeeded(new SearchRequest(text, count));
    }

    /// <summary>
    /// BR-06: the digits of <c>q</c> when it is digits, <c>#digits</c>, <c>prefixdigits</c> or <c>prefix-digits</c> (prefix in
    /// any case); otherwise null. The digits are compared with the document number as text.
    /// </summary>
    public static string? ParseNumber(string query, string prefix)
    {
        var rest = query.Trim();

        if (rest.StartsWith('#'))
        {
            rest = rest[1..];
        }
        else if (prefix.Length > 0 && rest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            rest = rest[prefix.Length..];

            if (rest.StartsWith('-'))
            {
                rest = rest[1..];
            }
        }

        return rest.Length is > 0 and <= MaxNumberDigits && rest.All(char.IsAsciiDigit) ? rest : null;
    }

    /// <summary>Escapes the backslash, percent and underscore so they match literally under a backslash ESCAPE.</summary>
    public static string EscapeLike(string text) =>
        text.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    public static SearchTerms BuildTerms(string query, SearchOrganization organization)
    {
        var escaped = EscapeLike(query);

        return new SearchTerms(
            query,
            escaped + "%",
            "%" + escaped + "%",
            ParseNumber(query, organization.RequestPrefix),
            ParseNumber(query, organization.QuotePrefix),
            ParseNumber(query, organization.WorkOrderPrefix),
            ParseNumber(query, organization.InvoicePrefix));
    }

    /// <summary>The snake-case API code of a status enum value (<c>ready_to_schedule</c>, <c>partially_paid</c>).</summary>
    public static string StatusCode<TStatus>(TStatus status)
        where TStatus : struct, Enum
    {
        var name = status.ToString();
        var code = new StringBuilder(name.Length + 4);

        for (var index = 0; index < name.Length; index++)
        {
            if (char.IsUpper(name[index]) && index > 0)
            {
                code.Append('_');
            }

            code.Append(char.ToLowerInvariant(name[index]));
        }

        return code.ToString();
    }

    /// <summary>BR-07: global order by rank, <c>updated_at</c> descending, id; the first <paramref name="limit"/>.</summary>
    public static (IReadOnlyList<SearchHit> Kept, bool HasMore) Merge(SearchMatches matches, int limit)
    {
        var ordered = matches.Hits
            .OrderBy(hit => hit.Rank)
            .ThenByDescending(hit => hit.UpdatedAt)
            .ThenBy(hit => hit.Id)
            .ToList();

        return (ordered.Take(limit).ToList(), matches.Overflow || ordered.Count > limit);
    }

    /// <summary>BR-08 and the title/subtitle table. <paramref name="showBranchName"/> is false below two active branches.</summary>
    public static SearchItemView ToItem(SearchHit hit, SearchOrganization organization, bool showBranchName)
    {
        var (number, title, subtitle) = hit.EntityType switch
        {
            SearchEntityTypes.Customer => (
                null,
                hit.Name ?? string.Empty,
                hit.Alternate == "company" ? "Commercial customer" : "Residential customer"),
            SearchEntityTypes.ServiceRequest => (
                Number(organization.RequestPrefix, hit.Number),
                FirstText(hit.Name, hit.Alternate) ?? "Service request",
                FirstText(hit.CustomerName, hit.GuestName)),
            SearchEntityTypes.Quote => (
                Number(organization.QuotePrefix, hit.Number),
                FirstText(hit.CustomerName, hit.GuestName) ?? "Quote",
                Number(organization.RequestPrefix, hit.RequestNumber)),
            SearchEntityTypes.WorkOrder => (
                Number(organization.WorkOrderPrefix, hit.Number),
                hit.Name ?? string.Empty,
                FirstText(hit.CustomerName)),
            _ => (
                Number(organization.InvoicePrefix, hit.Number),
                FormatMoney(hit.Total ?? 0m, hit.Currency),
                InvoiceSubtitle(hit)),
        };

        return new SearchItemView(
            hit.EntityType,
            hit.Id,
            number,
            title,
            subtitle,
            hit.Status,
            hit.BranchId,
            showBranchName && hit.BranchId is not null ? hit.BranchName : null,
            hit.UpdatedAt);
    }

    /// <summary>The en-US currency format the invoices list uses (<c>$485.00</c>); unknown symbols fall back to the code.</summary>
    public static string FormatMoney(decimal value, string? currency)
    {
        var code = (currency ?? string.Empty).Trim().ToUpperInvariant();
        var symbol = code switch
        {
            "USD" => "$",
            "EUR" => "€",
            "GBP" => "£",
            "CAD" => "CA$",
            "AUD" => "A$",
            "MXN" => "MX$",
            "" => string.Empty,
            _ => code + " ",
        };

        return symbol + value.ToString("N2", CultureInfo.InvariantCulture);
    }

    private static string? Number(string prefix, long? number) =>
        number is { } value ? RequestCardRules.DisplayNumber(prefix, value) : null;

    private static string? FirstText(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static string? InvoiceSubtitle(SearchHit hit)
    {
        var customer = FirstText(hit.CustomerName);
        var due = hit.DueDate is { } date
            ? "Due " + date.ToString("MMM d", CultureInfo.InvariantCulture)
            : null;

        return customer is not null && due is not null ? $"{customer} · {due}" : customer ?? due;
    }
}
