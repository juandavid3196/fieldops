using FieldOps.Application.Features.Email;
using FieldOps.Application.Features.PublicRequests;
using Microsoft.Extensions.Logging;

namespace FieldOps.Infrastructure.Email;

/// <summary>
/// Sends the public request confirmation after the commit (BR-19). A delivery
/// failure never reaches the caller; only the request id and the failure
/// category are logged, never the recipient, names or bodies.
/// </summary>
internal sealed class PublicRequestConfirmationSender(
    IEmailSender emailSender,
    ILogger<PublicRequestConfirmationSender> logger) : IPublicRequestConfirmationSender
{
    public async Task SendAsync(PublicRequestConfirmation confirmation, CancellationToken cancellationToken)
    {
        try
        {
            await emailSender.SendAsync(
                PublicRequestConfirmationEmailComposer.Compose(confirmation),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                "Public request confirmation email failed. RequestId={RequestId} Category={Category}",
                confirmation.RequestId,
                exception.GetType().Name);
        }
    }
}
