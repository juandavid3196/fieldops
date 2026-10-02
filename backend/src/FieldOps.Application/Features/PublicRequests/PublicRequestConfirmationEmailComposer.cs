using System.Net;
using System.Text;
using FieldOps.Application.Features.Email;

namespace FieldOps.Application.Features.PublicRequests;

/// <summary>Composes the confirmation email (public service request BR-19): no address, description or attachments.</summary>
public static class PublicRequestConfirmationEmailComposer
{
    public const string Notice = "Submitting this request does not confirm a price or appointment.";

    private static readonly string[] NextSteps =
    [
        "Our team reviews your request.",
        "We contact you to confirm the details.",
        "We schedule the visit with you.",
    ];

    public static EmailMessage Compose(PublicRequestConfirmation confirmation)
    {
        var text = new StringBuilder()
            .AppendLine($"Hi {confirmation.FirstName},")
            .AppendLine()
            .AppendLine($"{confirmation.OrganizationName} received your request {confirmation.RequestNumber}.")
            .AppendLine()
            .AppendLine("What happens next:");

        for (var index = 0; index < NextSteps.Length; index++)
        {
            text.AppendLine($"{index + 1}. {NextSteps[index]}");
        }

        text.AppendLine().AppendLine(Notice);

        var html = new StringBuilder("<!DOCTYPE html><html><body style=\"font-family:Arial,sans-serif;color:#1f2937\">")
            .Append($"<p>Hi {Encode(confirmation.FirstName)},</p>")
            .Append($"<p>{Encode(confirmation.OrganizationName)} received your request ")
            .Append($"<strong>{Encode(confirmation.RequestNumber)}</strong>.</p>")
            .Append("<p>What happens next:</p><ol>");

        foreach (var step in NextSteps)
        {
            html.Append($"<li>{Encode(step)}</li>");
        }

        html.Append($"</ol><p>{Encode(Notice)}</p></body></html>");

        return new EmailMessage(
            confirmation.RecipientEmail,
            $"We received your request {confirmation.RequestNumber}",
            text.ToString(),
            html.ToString());
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
