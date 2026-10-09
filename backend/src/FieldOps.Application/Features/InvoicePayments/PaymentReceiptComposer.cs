using System.Globalization;
using System.Net;
using System.Text;
using FieldOps.Application.Features.Email;

namespace FieldOps.Application.Features.InvoicePayments;

/// <summary>
/// Composes the payment receipt (invoices-payments-management BR-17) after the invoice email precedent: every value is
/// HTML-encoded in the HTML body and the subject is one line. It never carries the reference, note, receiver or recorder.
/// </summary>
public static class PaymentReceiptComposer
{
    public static EmailMessage Compose(PaymentReceiptData data)
    {
        var date = data.PaidDate.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
        var hasReceipt = !string.IsNullOrWhiteSpace(data.ReceiptNumber);

        // customer-invoice-payments BR-12: a payment with a receipt number leads with it and splits the payment line.
        var receipt = hasReceipt ? $"Receipt {data.ReceiptNumber} · Payment {data.PaymentNumber}" : null;
        var payment = hasReceipt
            ? string.Create(CultureInfo.InvariantCulture, $"{data.Amount:N2} {data.Currency} · {date} · {data.MethodLabel}")
            : string.Create(CultureInfo.InvariantCulture, $"Payment {data.PaymentNumber} · {data.Amount:N2} {data.Currency} · {date} · {data.MethodLabel}");
        var invoice = string.Create(
            CultureInfo.InvariantCulture,
            $"Invoice {data.InvoiceNumber} · Remaining balance {data.RemainingBalance:N2} {data.Currency}");
        var phoneLine = string.IsNullOrWhiteSpace(data.OrganizationPhone)
            ? null
            : $"Questions? Call us at {data.OrganizationPhone.Trim()}.";
        const string thanks = "We received your payment. Thank you!";

        var text = new StringBuilder()
            .AppendLine(thanks)
            .AppendLine();

        if (receipt is not null)
        {
            text.AppendLine(receipt);
        }

        text
            .AppendLine(payment)
            .AppendLine(invoice)
            .AppendLine()
            .AppendLine(data.OrganizationName);

        if (phoneLine is not null)
        {
            text.AppendLine().AppendLine(phoneLine);
        }

        var html = new StringBuilder("<!DOCTYPE html><html><body style=\"font-family:Arial,sans-serif;color:#1f2937\">")
            .Append($"<p>{Encode(thanks)}</p>")
            .Append(receipt is null ? string.Empty : $"<p><strong>{Encode(receipt)}</strong></p>")
            .Append($"<p><strong>{Encode(payment)}</strong></p>")
            .Append($"<p>{Encode(invoice)}</p>")
            .Append($"<p>{Encode(data.OrganizationName)}</p>");

        if (phoneLine is not null)
        {
            html.Append($"<p>{Encode(phoneLine)}</p>");
        }

        html.Append("</body></html>");

        return new EmailMessage(
            data.RecipientEmail,
            SingleLine($"{data.OrganizationName}: payment receipt {(hasReceipt ? data.ReceiptNumber : data.PaymentNumber)}"),
            text.ToString(),
            html.ToString());
    }

    private static string SingleLine(string value) =>
        string.Concat(value.Select(character => char.IsControl(character) ? ' ' : character));

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
