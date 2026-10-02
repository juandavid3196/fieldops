using System.Net;
using System.Text;
using FieldOps.Application.Features.Email;

namespace FieldOps.Application.Features.ServiceRequests;

/// <summary>Composes the information-request email (BR-13); every interpolated value is HTML-encoded in the HTML body.</summary>
public static class RequestInformationEmailComposer
{
    public const string FallbackFirstName = "there";

    public static EmailMessage Compose(InformationRequestEmail email)
    {
        var firstName = string.IsNullOrWhiteSpace(email.ContactFirstName) ? FallbackFirstName : email.ContactFirstName.Trim();
        var phoneLine = string.IsNullOrWhiteSpace(email.OrganizationPhone)
            ? null
            : $"Questions? Call us at {email.OrganizationPhone.Trim()}.";

        var text = new StringBuilder()
            .AppendLine($"Hi {firstName},")
            .AppendLine()
            .AppendLine($"{email.OrganizationName} needs more information about {email.RequestNumber}.")
            .AppendLine()
            .AppendLine(email.MessageBody);

        if (phoneLine is not null)
        {
            text.AppendLine().AppendLine(phoneLine);
        }

        var html = new StringBuilder("<!DOCTYPE html><html><body style=\"font-family:Arial,sans-serif;color:#1f2937\">")
            .Append($"<p>Hi {Encode(firstName)},</p>")
            .Append($"<p>{Encode(email.OrganizationName)} needs more information about ")
            .Append($"<strong>{Encode(email.RequestNumber)}</strong>.</p>")
            .Append($"<blockquote style=\"white-space:pre-wrap\">{Encode(email.MessageBody)}</blockquote>");

        if (phoneLine is not null)
        {
            html.Append($"<p>{Encode(phoneLine)}</p>");
        }

        html.Append("</body></html>");

        return new EmailMessage(
            email.RecipientEmail,
            SingleLine($"{email.OrganizationName} needs more information about {email.RequestNumber}"),
            text.ToString(),
            html.ToString());
    }

    private static string SingleLine(string value) =>
        string.Concat(value.Select(character => char.IsControl(character) ? ' ' : character));

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
