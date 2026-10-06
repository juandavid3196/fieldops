using System.Globalization;

namespace FieldOps.Application.Features.QuoteLinks;

public sealed record QuotePdfMeta(string Label, string Value);

/// <summary>A price row; <see cref="Status"/> is set on optional items only.</summary>
public sealed record QuotePdfLine(string Name, string? Description, string Quantity, string UnitPrice, string Amount, string? Status);

public sealed record QuotePdfTotal(string Label, string Amount, bool IsGrandTotal);

/// <summary>Every string of the PDF (BR-19), composed in Application so the renderer only lays it out.</summary>
public sealed record QuotePdfDocument(
    string OrganizationName,
    string? Phone,
    string Title,
    string? ResponseStamp,
    IReadOnlyList<QuotePdfMeta> Meta,
    string Scope,
    string? CustomerMessage,
    IReadOnlyList<string> ScopeOfWork,
    IReadOnlyList<QuotePdfLine> Lines,
    IReadOnlyList<QuotePdfLine> OptionalLines,
    IReadOnlyList<QuotePdfTotal> Totals,
    string? Terms,
    string Copyright,
    string PoweredBy);

/// <summary>Builds the PDF document from the public read of the version and its response; photos are never included (AS-03).</summary>
public static class QuotePdfDocumentComposer
{
    public const string IncludedText = "Included";

    public const string NotIncludedText = "Not included";

    public const string OptionalNotIncludedText = "Optional — not included in the total";

    public const string PoweredByText = "Powered by FieldOps";

    /// <summary>The file name of the download: <c>&lt;Q-n&gt;-v&lt;versionNo&gt;.pdf</c> with only safe characters.</summary>
    public static string FileName(PublicQuote quote)
    {
        var number = new string(quote.Quote.DisplayNumber.Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.').ToArray());

        return string.Create(CultureInfo.InvariantCulture, $"{number}-v{quote.Quote.VersionNo}.pdf");
    }

    public static QuotePdfDocument Compose(PublicQuote quote, int year)
    {
        var response = quote.Response;
        var approved = response is { Type: "approved" };
        var selected = approved ? response!.SelectedOptionalLineIds.ToHashSet() : [];
        var totals = approved && response!.Totals is { } approvedTotals ? approvedTotals : quote.VersionTotals;
        var currency = quote.VersionTotals.Currency;

        var meta = new List<QuotePdfMeta>
        {
            new("Quote number", quote.Quote.DisplayNumber),
            new("Version", quote.Quote.VersionNo.ToString(CultureInfo.InvariantCulture)),
            new("Sent", Date(quote.Quote.SentOn)),
            new("Valid until", Date(quote.Quote.ValidUntil)),
        };

        if (!string.IsNullOrWhiteSpace(quote.Customer.Name))
        {
            meta.Add(new QuotePdfMeta("Prepared for", quote.Customer.Name));
        }

        if (!string.IsNullOrWhiteSpace(quote.Customer.Address))
        {
            meta.Add(new QuotePdfMeta("Property", quote.Customer.Address));
        }

        var stamp = response?.Type switch
        {
            "approved" => $"Approved on {Date(response.RespondedOn)}",
            "rejected" => $"Declined on {Date(response.RespondedOn)}",
            _ => null,
        };

        var rows = new List<QuotePdfTotal> { new("Subtotal", Money(totals.Subtotal, currency), false) };

        if (totals.DiscountTotal > 0m)
        {
            rows.Add(new QuotePdfTotal("Discount", "−" + Money(totals.DiscountTotal, currency), false));
        }

        rows.Add(new QuotePdfTotal(totals.TaxLabel, Money(totals.TaxTotal, currency), false));
        rows.Add(new QuotePdfTotal("Total", Money(totals.Total, currency), true));

        return new QuotePdfDocument(
            quote.Organization.Name,
            quote.Organization.Phone,
            $"Quote {quote.Quote.DisplayNumber}",
            stamp,
            meta,
            quote.Quote.Scope,
            quote.Quote.CustomerMessage,
            quote.ScopeItems,
            quote.Lines.Where(line => !line.IsOptional).Select(line => ToLine(line, currency, null)).ToList(),
            quote.Lines.Where(line => line.IsOptional)
                .Select(line => ToLine(
                    line,
                    currency,
                    !approved
                        ? OptionalNotIncludedText
                        : line.Id is { } id && selected.Contains(id) ? IncludedText : NotIncludedText))
                .ToList(),
            rows,
            quote.Quote.Terms,
            string.Create(CultureInfo.InvariantCulture, $"© {year} {quote.Organization.Name}. All rights reserved."),
            PoweredByText);
    }

    private static QuotePdfLine ToLine(PublicLine line, string currency, string? status) =>
        new(
            line.Name,
            string.IsNullOrWhiteSpace(line.Description) ? null : line.Description,
            Quantity(line),
            Money(line.UnitPrice, currency),
            Money(line.LineSubtotal, currency),
            status);

    // The unit is shown unless it is the plain "unit" (BR-05).
    private static string Quantity(PublicLine line)
    {
        var amount = line.Quantity.ToString("0.###", CultureInfo.InvariantCulture);

        return string.Equals(line.Unit, "unit", StringComparison.OrdinalIgnoreCase) ? amount : $"{amount} {line.Unit}";
    }

    private static string Money(decimal amount, string currency) =>
        string.Create(CultureInfo.InvariantCulture, $"{amount:N2} {currency}");

    private static string Date(DateOnly date) => date.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
}
