using FieldOps.Application.Features.Email;
using FieldOps.Application.Features.TechnicianVisits;
using Microsoft.Extensions.Logging;

namespace FieldOps.Infrastructure.Email;

/// <summary>
/// Sends the "on the way" email after the commit (mobile-job-details BR-11). A delivery failure never reaches the
/// caller; only the visit id and the failure category are logged, never the recipient, names, address or message.
/// </summary>
internal sealed class TravelNotifier(
    IEmailSender emailSender,
    ILogger<TravelNotifier> logger) : ITravelNotifier
{
    public async Task<bool> SendAsync(TravelEmail email, CancellationToken cancellationToken)
    {
        try
        {
            await emailSender.SendAsync(TravelEmailComposer.Compose(email), cancellationToken);

            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                "Travel email failed. VisitId={VisitId} Category={Category}",
                email.VisitId,
                exception.GetType().Name);

            return false;
        }
    }
}
