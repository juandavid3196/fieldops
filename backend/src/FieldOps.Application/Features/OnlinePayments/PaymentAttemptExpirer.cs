using FieldOps.Domain.Invoices;

namespace FieldOps.Application.Features.OnlinePayments;

/// <summary>
/// Lazy expiry of pending card attempts (customer-invoice-payments BR-14). It runs before any invoice lock is taken, by the
/// card intent, the status endpoint, the public view and the recording of an external payment: the provider is asked to
/// cancel the intent, and only a confirmed cancellation releases the invoice. An attempt without an intent id fails with
/// <c>provider_unavailable</c>. A provider failure leaves the attempt pending.
/// </summary>
public sealed class PaymentAttemptExpirer(IOnlinePaymentStore store, IPaymentGateway gateway, TimeProvider timeProvider)
{
    public async Task EvaluateAsync(Guid organizationId, Guid invoiceId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        foreach (var attempt in await store.FindExpiredAsync(organizationId, invoiceId, now, cancellationToken))
        {
            if (string.IsNullOrWhiteSpace(attempt.ProviderPaymentIntentId))
            {
                await store.MarkFailedIfPendingAsync(attempt.AttemptId, PaymentFailureCategories.ProviderUnavailable, now, cancellationToken);

                continue;
            }

            var result = await CancelAsync(attempt.ProviderPaymentIntentId, cancellationToken);

            if (result is GatewayCancelResult.Canceled or GatewayCancelResult.AlreadyCanceled)
            {
                await store.MarkFailedIfPendingAsync(attempt.AttemptId, PaymentFailureCategories.Expired, now, cancellationToken);
            }
        }
    }

    private async Task<GatewayCancelResult> CancelAsync(string intentId, CancellationToken cancellationToken)
    {
        try
        {
            return await gateway.CancelIntentAsync(intentId, cancellationToken);
        }
        catch (PaymentGatewayUnavailableException)
        {
            return GatewayCancelResult.Unavailable;
        }
    }
}
