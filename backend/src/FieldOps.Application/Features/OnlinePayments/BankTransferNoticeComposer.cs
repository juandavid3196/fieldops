using System.Globalization;
using System.Net;
using System.Text;
using FieldOps.Application.Features.Email;

namespace FieldOps.Application.Features.OnlinePayments;

/// <summary>Composes the organization notice of a reported bank transfer (BR-17); values are HTML-encoded and the subject is one line.</summary>
public static class BankTransferNoticeComposer
{
    public static EmailMessage Compose(BankTransferNoticeData data)
    {
        var line = string.Create(
            CultureInfo.InvariantCulture,
            $"A customer reported a bank transfer of {data.Amount:N2} {data.Currency} for invoice {data.InvoiceNumber} on {data.ReportedOn.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)}. Confirm it in your bank before recording the payment.");
        var subject = string.Concat($"Bank transfer reported for {data.InvoiceNumber}".Select(character => char.IsControl(character) ? ' ' : character));
        var html = new StringBuilder("<!DOCTYPE html><html><body style=\"font-family:Arial,sans-serif;color:#1f2937\">")
            .Append($"<p>{WebUtility.HtmlEncode(line)}</p>")
            .Append("</body></html>");

        return new EmailMessage(data.RecipientEmail, subject, line + Environment.NewLine, html.ToString());
    }
}
