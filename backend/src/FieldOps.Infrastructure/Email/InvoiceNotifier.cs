using FieldOps.Application.Features.Email;
using FieldOps.Application.Features.InvoiceDelivery;
using Microsoft.Extensions.Logging;

namespace FieldOps.Infrastructure.Email;

/// <summary>
/// Sends the invoice email after the commit (invoice-draft-delivery BR-16). A delivery failure never reaches the caller:
/// the result is <see cref="InvoiceEmailStatus.Failed"/>, and only the invoice id and the exception type are logged,
/// never the recipient, link, token or message.
/// </summary>
internal sealed class InvoiceNotifier(
    IEmailSender emailSender,
    ILogger<InvoiceNotifier> logger) : IInvoiceNotifier
{
    public async Task<InvoiceEmailStatus> SendAsync(InvoiceEmail email, CancellationToken cancellationToken)
    {
        try
        {
            await emailSender.SendAsync(InvoiceEmailComposer.Compose(email), cancellationToken);

            return InvoiceEmailStatus.Sent;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                "Invoice email failed. InvoiceId={InvoiceId} Category={Category}",
                email.Data.InvoiceId,
                exception.GetType().Name);

            return InvoiceEmailStatus.Failed;
        }
    }
}
