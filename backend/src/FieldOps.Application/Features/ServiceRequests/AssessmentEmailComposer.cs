using System.Globalization;
using System.Net;
using System.Text;
using FieldOps.Application.Features.Email;

namespace FieldOps.Application.Features.ServiceRequests;

/// <summary>
/// Composes the assessment email (schedule-assessment BR-13, BR-14). Dates and times use the invariant culture in
/// the organization time zone; every interpolated value is HTML-encoded in the HTML body. Purpose and internal
/// instructions are never part of the message.
/// </summary>
public static class AssessmentEmailComposer
{
    public const string FallbackFirstName = "there";

    public static EmailMessage Compose(AssessmentEmail email)
    {
        var firstName = string.IsNullOrWhiteSpace(email.ContactFirstName) ? FallbackFirstName : email.ContactFirstName.Trim();
        var phoneLine = string.IsNullOrWhiteSpace(email.OrganizationPhone)
            ? null
            : $"Questions? Call us at {email.OrganizationPhone.Trim()}.";
        var message = MessageText(email);

        var text = new StringBuilder()
            .AppendLine($"Hi {firstName},")
            .AppendLine()
            .AppendLine(message)
            .AppendLine()
            .AppendLine(email.OrganizationName);

        if (phoneLine is not null)
        {
            text.AppendLine().AppendLine(phoneLine);
        }

        var html = new StringBuilder("<!DOCTYPE html><html><body style=\"font-family:Arial,sans-serif;color:#1f2937\">")
            .Append($"<p>Hi {Encode(firstName)},</p>")
            .Append($"<p>{Encode(message)}</p>")
            .Append($"<p>{Encode(email.OrganizationName)}</p>");

        if (phoneLine is not null)
        {
            html.Append($"<p>{Encode(phoneLine)}</p>");
        }

        html.Append("</body></html>");

        return new EmailMessage(email.RecipientEmail, Subject(email), text.ToString(), html.ToString());
    }

    /// <summary>The BR-13 customer message for the three operations.</summary>
    public static string MessageText(AssessmentEmail email)
    {
        var zone = OrganizationTime.FindZone(email.TimezoneId);
        var start = TimeZoneInfo.ConvertTime(email.Start, zone);
        var end = TimeZoneInfo.ConvertTime(email.Start.AddHours(1), zone);
        var day = start.ToString("ddd, MMM d", CultureInfo.InvariantCulture);
        var from = start.ToString("h:mm tt", CultureInfo.InvariantCulture);
        var to = end.ToString("h:mm tt", CultureInfo.InvariantCulture);
        var technician = string.IsNullOrWhiteSpace(email.TechnicianName) ? "Our technician" : email.TechnicianName.Trim();

        return email.Kind switch
        {
            AssessmentEmailKind.Scheduled =>
                $"Your assessment visit is scheduled for {day} between {from} and {to}. {technician} will inspect the issue before we prepare your quote.",
            AssessmentEmailKind.Rescheduled =>
                $"Your assessment visit has been rescheduled to {day} between {from} and {to}. {technician} will inspect the issue before we prepare your quote.",
            _ =>
                $"Your assessment visit on {day} between {from} and {to} has been cancelled. We'll contact you if we need to arrange another visit.",
        };
    }

    public static string Subject(AssessmentEmail email) =>
        SingleLine(email.Kind switch
        {
            AssessmentEmailKind.Scheduled => $"{email.OrganizationName}: assessment visit for {email.RequestNumber}",
            AssessmentEmailKind.Rescheduled => $"{email.OrganizationName}: assessment visit rescheduled for {email.RequestNumber}",
            _ => $"{email.OrganizationName}: assessment visit cancelled for {email.RequestNumber}",
        });

    private static string SingleLine(string value) =>
        string.Concat(value.Select(character => char.IsControl(character) ? ' ' : character));

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
