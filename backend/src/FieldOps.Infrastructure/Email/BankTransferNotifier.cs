using FieldOps.Application.Features.Email;
using FieldOps.Application.Features.OnlinePayments;
using Microsoft.Extensions.Logging;

namespace FieldOps.Infrastructure.Email;

/// <summary>
/// Emails the organization that a customer reported a bank transfer (customer-invoice-payments BR-17), after the commit.
/// A delivery failure never reaches the caller; only the invoice number-free category is logged, never the recipient or text.
/// </summary>
internal sealed class BankTransferNotifier(IEmailSender emailSender, ILogger<BankTransferNotifier> logger) : IBankTransferNotifier
{
    public async Task SendAsync(BankTransferNoticeData notice, CancellationToken cancellationToken)
    {
        try
        {
            await emailSender.SendAsync(BankTransferNoticeComposer.Compose(notice), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning("Bank transfer notice email failed. Category={Category}", exception.GetType().Name);
        }
    }
}
