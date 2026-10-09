using FieldOps.Application.Features.Email;
using FieldOps.Application.Features.InvoicePayments;
using Microsoft.Extensions.Logging;

namespace FieldOps.Infrastructure.Email;

/// <summary>
/// Sends the payment receipt after the commit (invoices-payments-management BR-17). A delivery failure never reaches the
/// caller: the result is <see cref="PaymentReceiptStatus.Failed"/> and only the payment id and the exception type are
/// logged, never the recipient, amounts or bodies.
/// </summary>
internal sealed class PaymentReceiptNotifier(
    IEmailSender emailSender,
    ILogger<PaymentReceiptNotifier> logger) : IPaymentReceiptNotifier
{
    public async Task<PaymentReceiptStatus> SendAsync(PaymentReceiptData receipt, CancellationToken cancellationToken)
    {
        try
        {
            await emailSender.SendAsync(PaymentReceiptComposer.Compose(receipt), cancellationToken);

            return PaymentReceiptStatus.Sent;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                "Payment receipt email failed. PaymentId={PaymentId} Category={Category}",
                receipt.PaymentId,
                exception.GetType().Name);

            return PaymentReceiptStatus.Failed;
        }
    }
}
