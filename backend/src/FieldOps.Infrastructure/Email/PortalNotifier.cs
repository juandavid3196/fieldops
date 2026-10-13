using System.Globalization;
using System.Net;
using FieldOps.Application.Features.Email;
using FieldOps.Application.Features.PortalDashboard;
using FieldOps.Application.Features.PortalInvitations;
using Microsoft.Extensions.Logging;

namespace FieldOps.Infrastructure.Email;

/// <summary>
/// The emails of the customer portal sent after a commit (invitation BR-11, reschedule BR-32, message BR-35). A send failure
/// never reaches the caller and never undoes the committed data; only the category is logged, never the recipient, names,
/// links, reasons or message text.
/// </summary>
internal sealed class PortalNotifier(IEmailSender emailSender, ILogger<PortalNotifier> logger)
    : IPortalInvitationNotifier, IPortalNotifier
{
    public async Task SendAsync(PortalInvitationEmailData email, string link, CancellationToken cancellationToken)
    {
        var expires = email.ExpiresOn.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture);
        var text =
            $"Hi {email.FirstName},\n\n"
            + $"{email.OrganizationName} invited you to your customer portal.\n\n"
            + $"Activate your access: {link}\n\n"
            + $"This invitation expires on {expires}.\n";
        var html =
            "<!DOCTYPE html><html><body style=\"font-family:Arial,sans-serif;color:#1f2937\">"
            + $"<p>Hi {WebUtility.HtmlEncode(email.FirstName)},</p>"
            + $"<p>{WebUtility.HtmlEncode(email.OrganizationName)} invited you to your customer portal.</p>"
            + $"<p><a href=\"{WebUtility.HtmlEncode(link)}\">Activate your access</a></p>"
            + $"<p>This invitation expires on {WebUtility.HtmlEncode(expires)}.</p></body></html>";

        await TrySendAsync(
            new EmailMessage(email.RecipientEmail, $"{email.OrganizationName} invited you to your customer portal", text, html),
            "invitation",
            cancellationToken);
    }

    public async Task SendMessageAsync(
        PortalMessageTarget target, string message, string organizationName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(target.RecipientEmail))
        {
            logger.LogWarning("Portal message email skipped. Category={Category}", "NoRecipient");

            return;
        }

        await TrySendAsync(PortalEmailComposer.Message(target, message), "message", cancellationToken);
    }

    public async Task SendRescheduleAsync(PortalRescheduleEmailData email, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email.RecipientEmail))
        {
            logger.LogWarning("Portal reschedule email skipped. Category={Category}", "NoRecipient");

            return;
        }

        await TrySendAsync(PortalEmailComposer.Reschedule(email), "reschedule", cancellationToken);
    }

    private async Task TrySendAsync(EmailMessage message, string kind, CancellationToken cancellationToken)
    {
        try
        {
            await emailSender.SendAsync(message, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning("Portal {Kind} email failed. Category={Category}", kind, exception.GetType().Name);
        }
    }
}
