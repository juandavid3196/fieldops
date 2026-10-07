using FieldOps.Application.Features.Dispatch;
using FieldOps.Application.Features.Email;
using Microsoft.Extensions.Logging;

namespace FieldOps.Infrastructure.Email;

/// <summary>
/// Sends the visit email after the commit (dispatch-calendar BR-17). A delivery failure never reaches the caller;
/// only the visit id and the failure category are logged, never the recipient, names or message.
/// </summary>
internal sealed class VisitNotifier(
    IEmailSender emailSender,
    ILogger<VisitNotifier> logger) : IVisitNotifier
{
    public async Task<bool> SendAsync(VisitEmail email, CancellationToken cancellationToken)
    {
        try
        {
            await emailSender.SendAsync(VisitEmailComposer.Compose(email), cancellationToken);

            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                "Visit email failed. VisitId={VisitId} Category={Category}",
                email.VisitId,
                exception.GetType().Name);

            return false;
        }
    }
}
