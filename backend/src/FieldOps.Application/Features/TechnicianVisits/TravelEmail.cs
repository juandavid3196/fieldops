using System.Globalization;
using System.Net;
using System.Text;
using FieldOps.Application.Features.Email;
using FieldOps.Application.Features.ServiceRequests;

namespace FieldOps.Application.Features.TechnicianVisits;

/// <summary>
/// The customer "on the way" email (mobile-job-details BR-11). Never carries an ETA, instructions, the dispatch note,
/// access details or technician contact data; <c>TechnicianName</c> is set only when the work order shares it.
/// </summary>
public sealed record TravelEmail(
    Guid VisitId,
    string RecipientEmail,
    string? ContactFirstName,
    string OrganizationName,
    string? OrganizationPhone,
    string DisplayNumber,
    string Title,
    string AddressLine1,
    string TimezoneId,
    DateTimeOffset? ArrivalStart,
    DateTimeOffset? ArrivalEnd,
    string? TechnicianName);

/// <summary>
/// Composes the travel email like the visit composer: invariant culture, times in the branch zone, HTML-encoded values
/// and a single-line subject.
/// </summary>
public static class TravelEmailComposer
{
    public const string FallbackFirstName = "there";

    public static EmailMessage Compose(TravelEmail email)
    {
        var firstName = string.IsNullOrWhiteSpace(email.ContactFirstName) ? FallbackFirstName : email.ContactFirstName.Trim();
        var lines = new List<string> { $"Your technician is on the way for {email.Title} at {email.AddressLine1}." };

        if (ArrivalLine(email) is { } arrival)
        {
            lines.Add(arrival);
        }

        if (!string.IsNullOrWhiteSpace(email.TechnicianName))
        {
            lines.Add($"Your technician: {email.TechnicianName.Trim()}.");
        }

        lines.Add(email.OrganizationName);

        if (!string.IsNullOrWhiteSpace(email.OrganizationPhone))
        {
            lines.Add($"Questions? Call us at {email.OrganizationPhone.Trim()}.");
        }

        var text = new StringBuilder().AppendLine($"Hi {firstName},");
        var html = new StringBuilder("<!DOCTYPE html><html><body style=\"font-family:Arial,sans-serif;color:#1f2937\">")
            .Append($"<p>Hi {WebUtility.HtmlEncode(firstName)},</p>");

        foreach (var line in lines)
        {
            text.AppendLine().AppendLine(line);
            html.Append($"<p>{WebUtility.HtmlEncode(line)}</p>");
        }

        html.Append("</body></html>");

        return new EmailMessage(email.RecipientEmail, Subject(email), text.ToString(), html.ToString());
    }

    public static string Subject(TravelEmail email) =>
        string.Concat($"{email.OrganizationName}: your technician is on the way for {email.DisplayNumber}"
            .Select(character => char.IsControl(character) ? ' ' : character));

    /// <summary>"Scheduled arrival window: h:mm a – h:mm a." or "Scheduled arrival: h:mm a." when both ends are equal; null without a window.</summary>
    private static string? ArrivalLine(TravelEmail email)
    {
        if (email.ArrivalStart is not { } start || email.ArrivalEnd is not { } end)
        {
            return null;
        }

        var zone = OrganizationTime.FindZone(email.TimezoneId);
        var from = TimeZoneInfo.ConvertTime(start, zone).ToString("h:mm tt", CultureInfo.InvariantCulture);
        var to = TimeZoneInfo.ConvertTime(end, zone).ToString("h:mm tt", CultureInfo.InvariantCulture);

        return start == end ? $"Scheduled arrival: {from}." : $"Scheduled arrival window: {from} – {to}.";
    }
}
