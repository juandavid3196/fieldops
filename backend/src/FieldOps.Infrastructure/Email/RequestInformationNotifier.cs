using FieldOps.Application.Features.Email;
using FieldOps.Application.Features.ServiceRequests;
using Microsoft.Extensions.Logging;

namespace FieldOps.Infrastructure.Email;

/// <summary>
/// Sends the information-request email after the commit (BR-13). A delivery failure never reaches the
/// caller; only the request id and the failure category are logged, never the recipient, names or bodies.
/// </summary>
internal sealed class RequestInformationNotifier(
    IEmailSender emailSender,
    ILogger<RequestInformationNotifier> logger) : IRequestInformationNotifier
{
    public async Task SendAsync(InformationRequestEmail email, CancellationToken cancellationToken)
    {
        try
        {
            await emailSender.SendAsync(RequestInformationEmailComposer.Compose(email), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                "Request information email failed. RequestId={RequestId} Category={Category}",
                email.RequestId,
                exception.GetType().Name);
        }
    }
}
