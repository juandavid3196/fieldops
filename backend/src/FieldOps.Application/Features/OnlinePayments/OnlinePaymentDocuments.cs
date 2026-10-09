using System.Globalization;
using FieldOps.Application.Features.BillingReview;
using FieldOps.Application.Features.InvoiceDelivery;

namespace FieldOps.Application.Features.OnlinePayments;

/// <summary>Every string of the receipt PDF (BR-19), composed in Application so the renderer only lays it out.</summary>
public sealed record ReceiptPdfDocument(
    string Title,
    string OrganizationName,
    IReadOnlyList<string> OrganizationLines,
    byte[]? Logo,
    string Heading,
    IReadOnlyList<(string Label, string Value)> Meta,
    string BillToHeading,
    IReadOnlyList<string> BillToLines,
    IReadOnlyList<(string Label, string Value, bool Emphasis)> Amounts,
    string ThankYou,
    string PoweredBy);

public sealed record CompletionReportChecklistLine(string Label, string State);

/// <summary>Every string of the completion report PDF (BR-20); there is no signature image or internal data to compose.</summary>
public sealed record CompletionReportPdfDocument(
    string Title,
    string OrganizationName,
    IReadOnlyList<string> OrganizationLines,
    byte[]? Logo,
    string Heading,
    IReadOnlyList<(string Label, string Value)> Meta,
    string? ServiceAddress,
    string? Summary,
    IReadOnlyList<CompletionReportChecklistLine> Checklist,
    IReadOnlyList<(string Label, string Value)> SignOff,
    string PoweredBy);

public interface IReceiptPdfRenderer
{
    byte[] Render(ReceiptPdfDocument document);
}

public interface ICompletionReportPdfRenderer
{
    byte[] Render(CompletionReportPdfDocument document);
}

/// <summary>Builds the receipt document from stored data only; the BR-19 exclusions are not part of the source.</summary>
public static class ReceiptPdfDocumentComposer
{
    public const string ThankYouText = "Thank you for your payment!";

    public static string FileName(ReceiptSource source) => InvoiceDeliveryRules.SafeFileName(source.ReceiptNumber);

    public static ReceiptPdfDocument Compose(ReceiptSource source)
    {
        var amounts = new List<(string Label, string Value, bool Emphasis)> { ("Amount", Money(source.Amount, source.Currency), false) };

        if (source.RefundedAmount > 0m)
        {
            amounts.Add(("Refunded", "−" + Money(source.RefundedAmount, source.Currency), false));
            amounts.Add(("Net amount", Money(source.Amount - source.RefundedAmount, source.Currency), true));
        }

        amounts.Add(("Invoice total", Money(source.InvoiceTotal, source.Currency), false));
        amounts.Add(("Total paid", Money(source.TotalPaid, source.Currency), false));
        amounts.Add(("Balance", Money(source.BalanceDue, source.Currency), true));

        return new ReceiptPdfDocument(
            $"Receipt {source.ReceiptNumber}",
            source.OrganizationName,
            source.OrganizationLines,
            Logo(source.Logo, source.LogoContentType),
            "RECEIPT",
            [
                ("Receipt number", source.ReceiptNumber),
                ("Payment number", source.PaymentNumber),
                ("Paid on", Date(source.PaidOn)),
                ("Invoice number", source.InvoiceNumber),
                ("Payment method", source.MethodLabel),
            ],
            "Bill to",
            source.BillToLines,
            amounts,
            ThankYouText,
            InvoicePdfDocumentComposer.PoweredByText);
    }

    internal static byte[]? Logo(byte[]? logo, string? contentType) =>
        logo is { Length: > 0 } && contentType is "image/png" or "image/jpeg" ? logo : null;

    private static string Money(decimal amount, string currency) =>
        string.Create(CultureInfo.InvariantCulture, $"{amount:N2} {currency}");

    private static string Date(DateOnly date) => date.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
}

/// <summary>Builds the completion report document (BR-20).</summary>
public static class CompletionReportPdfDocumentComposer
{
    public static string FileName(CompletionReportSource source) =>
        InvoiceDeliveryRules.SafeFileName(source.WorkOrderNumber + "-completion-report");

    public static CompletionReportPdfDocument Compose(CompletionReportSource source)
    {
        var meta = new List<(string Label, string Value)>
        {
            ("Work order", source.WorkOrderNumber),
            ("Service", source.WorkOrderTitle),
            ("Completed on", source.CompletedOn.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)),
        };

        if (!string.IsNullOrWhiteSpace(source.TechnicianName))
        {
            meta.Add(("Technician", source.TechnicianName));
        }

        var signOff = new List<(string Label, string Value)>();

        if (!string.IsNullOrWhiteSpace(source.AcknowledgementLabel))
        {
            signOff.Add(("Acknowledgement", source.AcknowledgementLabel));
        }

        if (!string.IsNullOrWhiteSpace(source.SignerName))
        {
            signOff.Add(("Signer", source.SignerName));
        }

        if (source.SignedOn is { } signed)
        {
            signOff.Add(("Signed on", signed.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)));
        }

        return new CompletionReportPdfDocument(
            $"Completion report {source.WorkOrderNumber}",
            source.OrganizationName,
            source.OrganizationLines,
            ReceiptPdfDocumentComposer.Logo(source.Logo, source.LogoContentType),
            "COMPLETION REPORT",
            meta,
            string.IsNullOrWhiteSpace(source.ServiceAddress) ? null : source.ServiceAddress,
            string.IsNullOrWhiteSpace(source.CompletionSummary) ? null : source.CompletionSummary,
            source.Checklist
                .Select(item => new CompletionReportChecklistLine(item.Label, item.Completed ? "Completed" : "Not completed"))
                .ToList(),
            signOff,
            InvoicePdfDocumentComposer.PoweredByText);
    }
}
