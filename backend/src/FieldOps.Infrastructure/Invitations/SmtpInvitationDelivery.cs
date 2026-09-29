using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using FieldOps.Application.Features.Users;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FieldOps.Infrastructure.Invitations;

/// <summary>
/// Sends the invitation email with the built-in SMTP client (BR-15). A send
/// failure throws so the design-18 rollback and 502 apply. Only the failure
/// category is logged: never the recipient, the link or the exception text.
/// </summary>
internal sealed class SmtpInvitationDelivery(
    IOptions<SmtpSettings> options,
    ILogger<SmtpInvitationDelivery> logger) : IInvitationDelivery
{
    public async Task SendAsync(InvitationDeliveryMessage message, CancellationToken cancellationToken)
    {
        var settings = options.Value;

        try
        {
            using var mail = BuildMail(settings, message);
            using var client = new SmtpClient(settings.Host, settings.Port)
            {
                EnableSsl = settings.EnableSsl,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                Timeout = 15_000,
            };

            if (!string.IsNullOrEmpty(settings.UserName))
            {
                client.Credentials = new NetworkCredential(settings.UserName, settings.Password);
            }

            await client.SendMailAsync(mail, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError("Invitation email delivery failed: {FailureCategory}", ex.GetType().Name);

            throw;
        }
    }

    internal static MailMessage BuildMail(SmtpSettings settings, InvitationDeliveryMessage message)
    {
        var expires = message.ExpiresAt.UtcDateTime.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture) + " (UTC)";

        var text = new StringBuilder()
            .AppendLine(CultureInfo.InvariantCulture, $"Hi {message.FirstName},")
            .AppendLine()
            .AppendLine(CultureInfo.InvariantCulture,
                $"{message.InviterName} invited you to join {message.OrganizationName} on FieldOps as {message.RoleName}.")
            .AppendLine()
            .AppendLine(CultureInfo.InvariantCulture, $"Accept invitation: {message.AcceptLink}")
            .AppendLine()
            .AppendLine(CultureInfo.InvariantCulture, $"This invitation expires on {expires}.")
            .AppendLine("This invitation can only be used once.")
            .ToString();

        var html =
            "<!DOCTYPE html><html><body style=\"font-family:Arial,sans-serif;color:#1f2937\">"
            + $"<p>Hi {Encode(message.FirstName)},</p>"
            + $"<p>{Encode(message.InviterName)} invited you to join <strong>{Encode(message.OrganizationName)}</strong> "
            + $"on FieldOps as <strong>{Encode(message.RoleName)}</strong>.</p>"
            + $"<p><a href=\"{Encode(message.AcceptLink)}\" style=\"display:inline-block;padding:12px 20px;"
            + "background:#0f766e;color:#ffffff;text-decoration:none;border-radius:6px\">Accept invitation</a></p>"
            + $"<p>This invitation expires on {Encode(expires)}.<br/>This invitation can only be used once.</p>"
            + "</body></html>";

        var mail = new MailMessage
        {
            From = new MailAddress(settings.SenderAddress, settings.SenderName),
            Subject = $"{message.InviterName} invited you to join {message.OrganizationName} on FieldOps",
            SubjectEncoding = Encoding.UTF8,
            BodyEncoding = Encoding.UTF8,
            Body = text,
            IsBodyHtml = false,
        };

        mail.To.Add(new MailAddress(message.RecipientEmail));
        mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(html, Encoding.UTF8, MediaTypeNames.Text.Html));

        return mail;
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
