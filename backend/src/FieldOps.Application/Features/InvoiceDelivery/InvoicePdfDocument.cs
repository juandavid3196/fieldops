using System.Globalization;

namespace FieldOps.Application.Features.InvoiceDelivery;

public sealed record InvoicePdfLine(string Description, string? Detail, string Quantity, string Rate, string Tax, string Amount);

public sealed record InvoicePdfTotal(string Label, string Amount, bool IsGrandTotal);

/// <summary>Every string of the invoice PDF (BR-12), composed in Application so the renderer only lays it out.</summary>
public sealed record InvoicePdfDocument(
    string Title,
    string OrganizationName,
    IReadOnlyList<string> OrganizationLines,
    byte[]? Logo,
    bool IsDraft,
    string BillToHeading,
    IReadOnlyList<string> BillToLines,
    string? ServiceAddress,
    IReadOnlyList<(string Label, string Value)> Meta,
    IReadOnlyList<InvoicePdfLine> Lines,
    IReadOnlyList<InvoicePdfTotal> Totals,
    string? CompletionNote,
    string ThankYou,
    string PoweredBy);

/// <summary>
/// Builds the PDF document from the preview (BR-04, BR-05) of a draft or sent invoice. The delivery data (recipient,
/// message) and the BR-04 exclusions are not part of the preview, so they cannot reach the document.
/// </summary>
public static class InvoicePdfDocumentComposer
{
    public const string DraftMark = "DRAFT";

    public const string ThankYouText = "Thank you for your business!";

    public const string PoweredByText = "Powered by FieldOps";

    public static InvoicePdfDocument Compose(InvoicePdfSource source)
    {
        var preview = source.Preview;
        var currency = preview.Currency;
        var billTo = new List<string> { preview.BillTo.Name };

        if (!string.IsNullOrWhiteSpace(preview.BillTo.Email))
        {
            billTo.Add(preview.BillTo.Email);
        }

        if (!string.IsNullOrWhiteSpace(preview.BillTo.Phone))
        {
            billTo.Add(preview.BillTo.Phone);
        }

        billTo.AddRange(preview.BillTo.AddressLines);

        var organization = new List<string>(preview.Organization.AddressLines);

        if (!string.IsNullOrWhiteSpace(preview.Organization.Phone))
        {
            organization.Add(preview.Organization.Phone);
        }

        if (!string.IsNullOrWhiteSpace(preview.Organization.Email))
        {
            organization.Add(preview.Organization.Email);
        }

        var meta = new List<(string Label, string Value)> { ("Invoice number", preview.Number) };

        if (preview.IssueDate is { } issued)
        {
            meta.Add(("Issue date", Date(issued)));
        }

        if (preview.DueDate is { } due)
        {
            meta.Add(("Due date", Date(due)));
        }

        meta.Add(("Work order", preview.WorkOrderNumber));

        var totals = new List<InvoicePdfTotal> { new("Subtotal", Money(preview.Totals.Subtotal, currency), false) };

        if (preview.Totals.DiscountTotal > 0m)
        {
            totals.Add(new InvoicePdfTotal("Discount", "−" + Money(preview.Totals.DiscountTotal, currency), false));
        }

        totals.Add(new InvoicePdfTotal(preview.Totals.TaxLabel, Money(preview.Totals.TaxTotal, currency), false));
        totals.Add(new InvoicePdfTotal("Total due", Money(preview.Totals.Total, currency), true));

        return new InvoicePdfDocument(
            $"Invoice {preview.Number}",
            preview.Organization.Name,
            organization,
            source.Logo is not null && source.LogoContentType is "image/png" or "image/jpeg" ? source.Logo : null,
            source.IsDraft,
            "Bill to",
            billTo,
            preview.ServiceAddress,
            meta,
            preview.Lines.Select(line => new InvoicePdfLine(
                line.Description,
                line.Detail,
                string.Equals(line.Unit, "unit", StringComparison.OrdinalIgnoreCase) ? line.Quantity : $"{line.Quantity} {line.Unit}",
                Money(line.UnitPrice, currency),
                line.TaxRate == 0m ? "—" : string.Create(CultureInfo.InvariantCulture, $"{line.TaxRate.ToString("0.####", CultureInfo.InvariantCulture)}%"),
                Money(line.Amount, currency))).ToList(),
            totals,
            string.IsNullOrWhiteSpace(preview.CompletionNote) ? null : preview.CompletionNote,
            ThankYouText,
            PoweredByText);
    }

    private static string Money(decimal amount, string currency) =>
        string.Create(CultureInfo.InvariantCulture, $"{amount:N2} {currency}");

    private static string Date(DateOnly date) => date.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
}
