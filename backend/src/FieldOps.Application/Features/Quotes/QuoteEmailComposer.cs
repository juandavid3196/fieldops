using System.Globalization;
using System.Net;
using System.Text;
using FieldOps.Application.Features.Email;

namespace FieldOps.Application.Features.Quotes;

/// <summary>Composes the quote email (BR-26); every interpolated value is HTML-encoded in the HTML body and the subject is one line.</summary>
public static class QuoteEmailComposer
{
    public static EmailMessage Compose(QuoteEmail email)
    {
        var data = email.Data;
        var summary = string.Create(
            CultureInfo.InvariantCulture,
            $"Quote {data.DisplayNumber} · Total {data.Total:N2} {data.Currency} · Valid until {data.ValidUntil.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)}");
        var phoneLine = string.IsNullOrWhiteSpace(data.OrganizationPhone)
            ? null
            : $"Questions? Call us at {data.OrganizationPhone.Trim()}.";

        var text = new StringBuilder()
            .AppendLine(email.Message)
            .AppendLine()
            .AppendLine(summary)
            .AppendLine()
            .AppendLine($"Review your quote: {email.Link}")
            .AppendLine()
            .AppendLine(data.OrganizationName);

        if (phoneLine is not null)
        {
            text.AppendLine().AppendLine(phoneLine);
        }

        var html = new StringBuilder("<!DOCTYPE html><html><body style=\"font-family:Arial,sans-serif;color:#1f2937\">")
            .Append($"<p style=\"white-space:pre-wrap\">{Encode(email.Message)}</p>")
            .Append($"<p><strong>{Encode(summary)}</strong></p>")
            .Append($"<p>Review your quote: <a href=\"{Encode(email.Link)}\">{Encode(email.Link)}</a></p>")
            .Append($"<p>{Encode(data.OrganizationName)}</p>");

        if (phoneLine is not null)
        {
            html.Append($"<p>{Encode(phoneLine)}</p>");
        }

        html.Append("</body></html>");

        return new EmailMessage(
            data.RecipientEmail,
            SingleLine($"{data.OrganizationName}: quote {data.DisplayNumber} for {data.RequestTitle}"),
            text.ToString(),
            html.ToString());
    }

    private static string SingleLine(string value) =>
        string.Concat(value.Select(character => char.IsControl(character) ? ' ' : character));

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
