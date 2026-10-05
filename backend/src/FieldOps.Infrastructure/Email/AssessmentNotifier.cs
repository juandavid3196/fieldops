using FieldOps.Application.Features.Email;
using FieldOps.Application.Features.ServiceRequests;
using Microsoft.Extensions.Logging;

namespace FieldOps.Infrastructure.Email;

/// <summary>
/// Sends the assessment email after the commit (schedule-assessment BR-14). A delivery failure never reaches the
/// caller; only the request id and the failure category are logged, never the recipient, names or message.
/// </summary>
internal sealed class AssessmentNotifier(
    IEmailSender emailSender,
    ILogger<AssessmentNotifier> logger) : IAssessmentNotifier
{
    public async Task SendAsync(AssessmentEmail email, CancellationToken cancellationToken)
    {
        try
        {
            await emailSender.SendAsync(AssessmentEmailComposer.Compose(email), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                "Assessment email failed. RequestId={RequestId} Category={Category}",
                email.RequestId,
                exception.GetType().Name);
        }
    }
}
