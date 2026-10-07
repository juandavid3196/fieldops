using System.Globalization;
using System.Net;
using System.Text;
using FieldOps.Application.Features.Email;
using FieldOps.Application.Features.ServiceRequests;

namespace FieldOps.Application.Features.Dispatch;

/// <summary>
/// Composes the visit email (dispatch-calendar BR-17), following the assessment composer: dates and times use the
/// invariant culture in the branch time zone and every interpolated value is HTML-encoded in the HTML body. The
/// dispatch note, the override reason and technician contact data are never part of the message.
/// </summary>
public static class VisitEmailComposer
{
    public const string FallbackFirstName = "there";

    public static EmailMessage Compose(VisitEmail email)
    {
        var firstName = string.IsNullOrWhiteSpace(email.ContactFirstName) ? FallbackFirstName : email.ContactFirstName.Trim();
        var phoneLine = string.IsNullOrWhiteSpace(email.OrganizationPhone)
            ? null
            : $"Questions? Call us at {email.OrganizationPhone.Trim()}.";
        var message = MessageText(email);
        var technicianLine = email.TechnicianNames is { Count: > 0 } names
            ? $"Your technician: {string.Join(", ", names)}."
            : null;

        var text = new StringBuilder()
            .AppendLine($"Hi {firstName},")
            .AppendLine()
            .AppendLine(message);

        if (technicianLine is not null)
        {
            text.AppendLine().AppendLine(technicianLine);
        }

        text.AppendLine().AppendLine(email.OrganizationName);

        if (phoneLine is not null)
        {
            text.AppendLine().AppendLine(phoneLine);
        }

        var html = new StringBuilder("<!DOCTYPE html><html><body style=\"font-family:Arial,sans-serif;color:#1f2937\">")
            .Append($"<p>Hi {Encode(firstName)},</p>")
            .Append($"<p>{Encode(message)}</p>");

        if (technicianLine is not null)
        {
            html.Append($"<p>{Encode(technicianLine)}</p>");
        }

        html.Append($"<p>{Encode(email.OrganizationName)}</p>");

        if (phoneLine is not null)
        {
            html.Append($"<p>{Encode(phoneLine)}</p>");
        }

        html.Append("</body></html>");

        return new EmailMessage(email.RecipientEmail, Subject(email), text.ToString(), html.ToString());
    }

    /// <summary>"Your service visit for &lt;title&gt; is scheduled for &lt;EEE, MMM d&gt; with arrival ...".</summary>
    public static string MessageText(VisitEmail email)
    {
        var zone = OrganizationTime.FindZone(email.TimezoneId);
        var start = TimeZoneInfo.ConvertTime(email.Start, zone);
        var from = TimeZoneInfo.ConvertTime(email.ArrivalStart, zone);
        var to = TimeZoneInfo.ConvertTime(email.ArrivalEnd, zone);
        var day = start.ToString("ddd, MMM d", CultureInfo.InvariantCulture);
        var fromText = from.ToString("h:mm tt", CultureInfo.InvariantCulture);
        var toText = to.ToString("h:mm tt", CultureInfo.InvariantCulture);
        var arrival = email.ArrivalStart == email.ArrivalEnd
            ? $"with arrival at {fromText}."
            : $"with arrival between {fromText} and {toText}.";

        return $"Your service visit for {email.Title} is scheduled for {day} {arrival}";
    }

    public static string Subject(VisitEmail email) =>
        SingleLine(email.Kind switch
        {
            VisitEmailKind.Scheduled => $"{email.OrganizationName}: visit scheduled for {email.DisplayNumber}",
            VisitEmailKind.Rescheduled => $"{email.OrganizationName}: visit rescheduled for {email.DisplayNumber}",
            _ => $"{email.OrganizationName}: technician update for {email.DisplayNumber}",
        });

    private static string SingleLine(string value) =>
        string.Concat(value.Select(character => char.IsControl(character) ? ' ' : character));

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
