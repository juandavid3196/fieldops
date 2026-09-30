using System.Globalization;
using System.Net;
using System.Text;
using FieldOps.Application.Features.Email;
using FieldOps.Application.Features.Users;

namespace FieldOps.Infrastructure.Email;

/// <summary>
/// Composes the invitation message (invitation BR-15, unchanged content) and
/// sends it through <see cref="IEmailSender"/>. A send failure throws so the
/// design-18 rollback and 502 apply.
/// </summary>
internal sealed class InvitationDelivery(IEmailSender emailSender) : IInvitationDelivery
{
    public Task SendAsync(InvitationDeliveryMessage message, CancellationToken cancellationToken) =>
        emailSender.SendAsync(BuildMessage(message), cancellationToken);

    internal static EmailMessage BuildMessage(InvitationDeliveryMessage message)
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

        return new EmailMessage(
            message.RecipientEmail,
            $"{message.InviterName} invited you to join {message.OrganizationName} on FieldOps",
            text,
            html);
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
