using System.Globalization;
using System.Net;
using System.Text;
using FieldOps.Application.Features.Email;

namespace FieldOps.Application.Features.InvoiceDelivery;

/// <summary>
/// Composes the invoice email (invoice-draft-delivery BR-16); every interpolated value is HTML-encoded in the HTML body,
/// line breaks of the message are kept and the subject is one line. No attachments.
/// </summary>
public static class InvoiceEmailComposer
{
    public static EmailMessage Compose(InvoiceEmail email)
    {
        var data = email.Data;
        var due = data.DueDate is { } dueDate
            ? string.Create(CultureInfo.InvariantCulture, $" · Due {dueDate.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)}")
            : string.Empty;
        var summary = string.Create(CultureInfo.InvariantCulture, $"Invoice {data.DisplayNumber} · Total due {data.Total:N2} {data.Currency}{due}");
        var phoneLine = string.IsNullOrWhiteSpace(data.OrganizationPhone)
            ? null
            : $"Questions? Call us at {data.OrganizationPhone.Trim()}.";

        var text = new StringBuilder()
            .AppendLine(data.Message)
            .AppendLine()
            .AppendLine(summary)
            .AppendLine()
            .AppendLine($"View your invoice: {email.Link}")
            .AppendLine()
            .AppendLine(data.OrganizationName);

        if (phoneLine is not null)
        {
            text.AppendLine().AppendLine(phoneLine);
        }

        var html = new StringBuilder("<!DOCTYPE html><html><body style=\"font-family:Arial,sans-serif;color:#1f2937\">")
            .Append($"<p style=\"white-space:pre-wrap\">{Encode(data.Message)}</p>")
            .Append($"<p><strong>{Encode(summary)}</strong></p>")
            .Append($"<p>View your invoice: <a href=\"{Encode(email.Link)}\">{Encode(email.Link)}</a></p>")
            .Append($"<p>{Encode(data.OrganizationName)}</p>");

        if (phoneLine is not null)
        {
            html.Append($"<p>{Encode(phoneLine)}</p>");
        }

        html.Append("</body></html>");

        return new EmailMessage(
            data.RecipientEmail,
            SingleLine($"{data.OrganizationName}: invoice {data.DisplayNumber}"),
            text.ToString(),
            html.ToString());
    }

    private static string SingleLine(string value) =>
        string.Concat(value.Select(character => char.IsControl(character) ? ' ' : character));

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
