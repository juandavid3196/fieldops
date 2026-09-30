namespace FieldOps.Application.Features.Email;

/// <summary>One outgoing message: recipient, subject and both bodies.</summary>
public sealed record EmailMessage(string To, string Subject, string TextBody, string HtmlBody);

/// <summary>
/// Sends one email through the configured provider (SMTP or Resend). A send
/// failure throws; callers decide whether it rolls back or is only logged.
/// Implementations never log the recipient, bodies or credentials.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}
