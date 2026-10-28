using FieldOps.Application.Features.Email;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FieldOps.Infrastructure.Email;

/// <summary>
/// Demo guard in front of the provider: a recipient outside
/// <c>Email:AllowedRecipients</c> is never sent and the call fails exactly
/// like a provider failure, so invitations roll back (502), notifiers record
/// the failed delivery and password reset stays silent (no address
/// enumeration). Only the failure category is logged, never the recipient.
/// </summary>
internal sealed class RecipientRestrictedEmailSender(
    IEmailSender inner,
    IOptions<EmailSettings> options,
    ILogger<RecipientRestrictedEmailSender> logger) : IEmailSender
{
    public const string Provider = "RecipientRestriction";

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        if (!RecipientAllowList.IsAllowed(message.To, options.Value.AllowedRecipients))
        {
            logger.LogWarning("Email not sent: recipient is outside the demo allow-list");

            throw new EmailDeliveryException(Provider, null);
        }

        return inner.SendAsync(message, cancellationToken);
    }
}
