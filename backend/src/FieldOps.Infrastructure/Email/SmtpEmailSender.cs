using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using FieldOps.Application.Features.Email;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FieldOps.Infrastructure.Email;

/// <summary>
/// Sends through the built-in SMTP client (Mailpit in development). A failure
/// throws; only the provider and failure category are logged, never the
/// recipient, bodies or credentials.
/// </summary>
internal sealed class SmtpEmailSender(
    IOptions<EmailSettings> options,
    ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var settings = options.Value;

        try
        {
            using var mail = BuildMail(settings, message);
            using var client = new SmtpClient(settings.Smtp.Host, settings.Smtp.Port)
            {
                EnableSsl = settings.Smtp.EnableSsl,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                Timeout = 15_000,
            };

            if (!string.IsNullOrEmpty(settings.Smtp.UserName))
            {
                client.Credentials = new NetworkCredential(settings.Smtp.UserName, settings.Smtp.Password);
            }

            await client.SendMailAsync(mail, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                "Email delivery failed: {Provider} {FailureCategory}",
                EmailSettings.SmtpProvider,
                ex.GetType().Name);

            throw;
        }
    }

    private static MailMessage BuildMail(EmailSettings settings, EmailMessage message)
    {
        var mail = new MailMessage
        {
            From = new MailAddress(settings.SenderAddress, settings.SenderName),
            Subject = message.Subject,
            SubjectEncoding = Encoding.UTF8,
            BodyEncoding = Encoding.UTF8,
            Body = message.TextBody,
            IsBodyHtml = false,
        };

        mail.To.Add(new MailAddress(message.To));
        mail.AlternateViews.Add(
            AlternateView.CreateAlternateViewFromString(message.HtmlBody, Encoding.UTF8, MediaTypeNames.Text.Html));

        return mail;
    }
}
