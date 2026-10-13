using FieldOps.Application.Features.InvoiceDelivery;
using FieldOps.Application.Features.InvoicePayments;
using FieldOps.Application.Features.PortalAccess;
using FieldOps.Application.Features.QuoteLinks;
using FieldOps.Domain.Invoices;

namespace FieldOps.Application.Features.OnlinePayments;

internal static class PublicPaymentSupport
{
    /// <summary>BR-01: a malformed token never reaches the store (the handler wrappers check the shape); an unknown, revoked, expired, draft or void resource is the same null.</summary>
    public static Task<InvoiceRef?> ResolveAsync(IOnlinePaymentStore store, ResourceAccess access, CancellationToken cancellationToken) =>
        store.ResolveAsync(access, cancellationToken);

    public static PublicOutcome<T> Invalid<T>(string field, string message) =>
        new PublicOutcome<T>.Invalid(new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [message] });
}

/// <summary>
/// POST /public/invoice-links/payments/card-intent (BR-06). Token and ownership (404) come before field validation (400).
/// Phase A (attempt row, audit) commits before the provider is called; the provider call and its failure handling run
/// outside any transaction, so a provider outage never holds the invoice lock.
/// </summary>
public sealed class CreateCardIntentHandler(
    IOnlinePaymentStore store,
    IPaymentGateway gateway,
    PaymentAttemptExpirer expirer,
    IInvoiceActionThrottle throttle,
    TimeProvider timeProvider)
{
    public Task<PublicOutcome<CardIntentView>> HandleAsync(string? token, string? idempotencyKey, CancellationToken cancellationToken) =>
        QuoteLinkTokens.IsWellFormed(token)
            ? HandleAsync(ResourceAccess.FromToken(token!), idempotencyKey, cancellationToken)
            : Task.FromResult<PublicOutcome<CardIntentView>>(new PublicOutcome<CardIntentView>.NotFound());

    public async Task<PublicOutcome<CardIntentView>> HandleAsync(ResourceAccess access, string? idempotencyKey, CancellationToken cancellationToken)
    {
        if (await PublicPaymentSupport.ResolveAsync(store, access, cancellationToken) is not { } invoice)
        {
            return new PublicOutcome<CardIntentView>.NotFound();
        }

        if (!OnlinePaymentRules.TryParseKey(idempotencyKey, out var key))
        {
            return PublicPaymentSupport.Invalid<CardIntentView>("idempotencyKey", OnlinePaymentMessages.KeyInvalid);
        }

        if (throttle.TryAcquire(InvoiceAction.CardIntent, access.ThrottleKey(invoice.InvoiceId)) is { } retryAfter)
        {
            return new PublicOutcome<CardIntentView>.RateLimited(retryAfter);
        }

        // BR-14: evaluated before the invoice lock of phase A, never inside it.
        await expirer.EvaluateAsync(invoice.OrganizationId, invoice.InvoiceId, cancellationToken);

        var prepared = await store.PrepareCardIntentAsync(access, key, timeProvider.GetUtcNow(), cancellationToken);

        switch (prepared)
        {
            case CardIntentPrepared.Rejected rejected:
                return new PublicOutcome<CardIntentView>.Conflict(rejected.Code, rejected.Title);
            case CardIntentPrepared.Replay replay:
                return await ReplayAsync(replay.Attempt, cancellationToken);
            case CardIntentPrepared.Created created:
                return await CreateIntentAsync(created.Attempt, cancellationToken);
            default:
                return new PublicOutcome<CardIntentView>.NotFound();
        }
    }

    // The secret is read again from the provider only while the attempt is pending and has an intent (BR-06 step 1).
    private async Task<PublicOutcome<CardIntentView>> ReplayAsync(CardAttemptFacts attempt, CancellationToken cancellationToken)
    {
        string? secret = null;

        if (attempt.Status == OnlinePaymentRules.AttemptStatusCode(PaymentAttemptStatus.Pending)
            && !string.IsNullOrWhiteSpace(attempt.ProviderPaymentIntentId))
        {
            try
            {
                secret = await gateway.RetrieveClientSecretAsync(attempt.ProviderPaymentIntentId, cancellationToken);
            }
            catch (PaymentGatewayUnavailableException)
            {
                return new PublicOutcome<CardIntentView>.ProviderUnavailable();
            }
        }

        return new PublicOutcome<CardIntentView>.Ok(
            new CardIntentView(attempt.AttemptId, secret, attempt.Amount, attempt.Currency, attempt.Status));
    }

    private async Task<PublicOutcome<CardIntentView>> CreateIntentAsync(CardAttemptFacts attempt, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        try
        {
            var intent = await gateway.CreateIntentAsync(
                new GatewayIntentRequest(attempt.AttemptId, attempt.OrganizationId, attempt.InvoiceId, attempt.Amount, attempt.Currency),
                cancellationToken);

            await store.StoreIntentAsync(attempt.AttemptId, intent.IntentId, now, cancellationToken);

            return new PublicOutcome<CardIntentView>.Ok(
                new CardIntentView(attempt.AttemptId, intent.ClientSecret, attempt.Amount, attempt.Currency, attempt.Status), Created: true);
        }
        catch (PaymentGatewayUnavailableException)
        {
            await store.MarkFailedIfPendingAsync(attempt.AttemptId, PaymentFailureCategories.ProviderUnavailable, now, cancellationToken);

            return new PublicOutcome<CardIntentView>.ProviderUnavailable();
        }
    }
}

/// <summary>POST /public/invoice-links/payments/status (BR-16): writes only through the BR-14 expiry evaluation.</summary>
public sealed class GetPaymentStatusHandler(IOnlinePaymentStore store, PaymentAttemptExpirer expirer)
{
    public Task<PublicOutcome<PaymentStatusView>> HandleAsync(string? token, string? attemptId, CancellationToken cancellationToken) =>
        QuoteLinkTokens.IsWellFormed(token)
            ? HandleAsync(ResourceAccess.FromToken(token!), attemptId, cancellationToken)
            : Task.FromResult<PublicOutcome<PaymentStatusView>>(new PublicOutcome<PaymentStatusView>.NotFound());

    public async Task<PublicOutcome<PaymentStatusView>> HandleAsync(ResourceAccess access, string? attemptId, CancellationToken cancellationToken)
    {
        if (await PublicPaymentSupport.ResolveAsync(store, access, cancellationToken) is not { } invoice)
        {
            return new PublicOutcome<PaymentStatusView>.NotFound();
        }

        if (!OnlinePaymentRules.TryParseKey(attemptId, out var id))
        {
            return PublicPaymentSupport.Invalid<PaymentStatusView>("attemptId", OnlinePaymentMessages.IdInvalid);
        }

        await expirer.EvaluateAsync(invoice.OrganizationId, invoice.InvoiceId, cancellationToken);

        return await store.GetStatusAsync(access, id, cancellationToken) is { } status
            ? new PublicOutcome<PaymentStatusView>.Ok(status)
            : new PublicOutcome<PaymentStatusView>.NotFound();
    }
}

/// <summary>POST /public/invoice-links/payments/bank-transfer-notice (BR-17): the organization email goes out after the commit.</summary>
public sealed class ReportBankTransferHandler(
    IOnlinePaymentStore store,
    IInvoiceActionThrottle throttle,
    IBankTransferNotifier notifier,
    TimeProvider timeProvider)
{
    public Task<PublicOutcome<BankNoticeView>> HandleAsync(string? token, string? idempotencyKey, CancellationToken cancellationToken) =>
        QuoteLinkTokens.IsWellFormed(token)
            ? HandleAsync(ResourceAccess.FromToken(token!), idempotencyKey, cancellationToken)
            : Task.FromResult<PublicOutcome<BankNoticeView>>(new PublicOutcome<BankNoticeView>.NotFound());

    public async Task<PublicOutcome<BankNoticeView>> HandleAsync(ResourceAccess access, string? idempotencyKey, CancellationToken cancellationToken)
    {
        if (await PublicPaymentSupport.ResolveAsync(store, access, cancellationToken) is not { } invoice)
        {
            return new PublicOutcome<BankNoticeView>.NotFound();
        }

        if (!OnlinePaymentRules.TryParseKey(idempotencyKey, out var key))
        {
            return PublicPaymentSupport.Invalid<BankNoticeView>("idempotencyKey", OnlinePaymentMessages.KeyInvalid);
        }

        if (throttle.TryAcquire(InvoiceAction.BankTransferNotice, access.ThrottleKey(invoice.InvoiceId)) is { } retryAfter)
        {
            return new PublicOutcome<BankNoticeView>.RateLimited(retryAfter);
        }

        switch (await store.ReportBankTransferAsync(access, key, timeProvider.GetUtcNow(), cancellationToken))
        {
            case BankNoticeResult.Rejected rejected:
                return new PublicOutcome<BankNoticeView>.Conflict(rejected.Code, rejected.Title);
            case BankNoticeResult.Reported reported:
                if (reported.Email is not null)
                {
                    await notifier.SendAsync(reported.Email, cancellationToken);
                }

                return new PublicOutcome<BankNoticeView>.Ok(new BankNoticeView(reported.Changed, reported.ReportedOn), Created: reported.Changed);
            default:
                return new PublicOutcome<BankNoticeView>.NotFound();
        }
    }
}

/// <summary>POST /public/invoice-links/review (BR-22).</summary>
public sealed class SubmitInvoiceReviewHandler(IOnlinePaymentStore store, IInvoiceActionThrottle throttle, TimeProvider timeProvider)
{
    public Task<PublicOutcome<ReviewSubmittedView>> HandleAsync(
        string? token, decimal? rating, string? comment, CancellationToken cancellationToken) =>
        QuoteLinkTokens.IsWellFormed(token)
            ? HandleAsync(ResourceAccess.FromToken(token!), rating, comment, cancellationToken)
            : Task.FromResult<PublicOutcome<ReviewSubmittedView>>(new PublicOutcome<ReviewSubmittedView>.NotFound());

    public async Task<PublicOutcome<ReviewSubmittedView>> HandleAsync(
        ResourceAccess access, decimal? rating, string? comment, CancellationToken cancellationToken)
    {
        if (await PublicPaymentSupport.ResolveAsync(store, access, cancellationToken) is not { } invoice)
        {
            return new PublicOutcome<ReviewSubmittedView>.NotFound();
        }

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (OnlinePaymentRules.ValidateReview(rating, comment, errors) is not { } review)
        {
            return new PublicOutcome<ReviewSubmittedView>.Invalid(errors);
        }

        if (throttle.TryAcquire(InvoiceAction.Review, access.ThrottleKey(invoice.InvoiceId)) is { } retryAfter)
        {
            return new PublicOutcome<ReviewSubmittedView>.RateLimited(retryAfter);
        }

        return await store.SubmitReviewAsync(access, review.Rating, review.Comment, timeProvider.GetUtcNow(), cancellationToken) switch
        {
            ReviewResult.Created => new PublicOutcome<ReviewSubmittedView>.Ok(new ReviewSubmittedView(true), Created: true),
            ReviewResult.NotAvailable => new PublicOutcome<ReviewSubmittedView>.Conflict(
                OnlinePaymentMessages.ReviewNotAvailableCode, OnlinePaymentMessages.ReviewNotAvailableTitle),
            ReviewResult.Exists => new PublicOutcome<ReviewSubmittedView>.Conflict(
                OnlinePaymentMessages.ReviewExistsCode, OnlinePaymentMessages.ReviewExistsTitle),
            _ => new PublicOutcome<ReviewSubmittedView>.NotFound(),
        };
    }
}

/// <summary>POST /public/invoice-links/receipt (BR-19): generated on demand from stored data.</summary>
public sealed class DownloadReceiptPdfHandler(IOnlinePaymentStore store, IInvoiceLinkStore documents, IReceiptPdfRenderer renderer)
{
    public Task<PublicOutcome<DocumentFile>> HandleAsync(string? token, string? paymentId, CancellationToken cancellationToken) =>
        QuoteLinkTokens.IsWellFormed(token)
            ? HandleAsync(ResourceAccess.FromToken(token!), paymentId, cancellationToken)
            : Task.FromResult<PublicOutcome<DocumentFile>>(new PublicOutcome<DocumentFile>.NotFound());

    public async Task<PublicOutcome<DocumentFile>> HandleAsync(ResourceAccess access, string? paymentId, CancellationToken cancellationToken)
    {
        if (await store.ResolveAsync(access, cancellationToken) is null)
        {
            return new PublicOutcome<DocumentFile>.NotFound();
        }

        if (!OnlinePaymentRules.TryParseKey(paymentId, out var id))
        {
            return PublicPaymentSupport.Invalid<DocumentFile>("paymentId", OnlinePaymentMessages.IdInvalid);
        }

        return await documents.GetReceiptSourceAsync(access, id, cancellationToken) is { } source
            ? new PublicOutcome<DocumentFile>.Ok(new DocumentFile(
                ReceiptPdfDocumentComposer.FileName(source),
                OnlinePaymentMessages.ReceiptContentType,
                renderer.Render(ReceiptPdfDocumentComposer.Compose(source))))
            : new PublicOutcome<DocumentFile>.NotFound();
    }
}

/// <summary>POST /public/invoice-links/completion-report (BR-20).</summary>
public sealed class DownloadCompletionReportHandler(IInvoiceLinkStore documents, ICompletionReportPdfRenderer renderer)
{
    public async Task<DocumentFile?> HandleAsync(string? token, CancellationToken cancellationToken) =>
        QuoteLinkTokens.IsWellFormed(token)
            ? await HandleAsync(ResourceAccess.FromToken(token!), cancellationToken)
            : null;

    public async Task<DocumentFile?> HandleAsync(ResourceAccess access, CancellationToken cancellationToken)
    {
        return await documents.GetCompletionReportSourceAsync(access, cancellationToken) is { } source
            ? new DocumentFile(
                CompletionReportPdfDocumentComposer.FileName(source),
                OnlinePaymentMessages.ReceiptContentType,
                renderer.Render(CompletionReportPdfDocumentComposer.Compose(source)))
            : null;
    }
}

/// <summary>POST /public/invoice-links/photos (BR-21).</summary>
public sealed class ListInvoicePhotosHandler(IInvoiceLinkStore documents)
{
    public Task<IReadOnlyList<InvoicePhoto>?> HandleAsync(string? token, CancellationToken cancellationToken) =>
        QuoteLinkTokens.IsWellFormed(token)
            ? HandleAsync(ResourceAccess.FromToken(token!), cancellationToken)
            : Task.FromResult<IReadOnlyList<InvoicePhoto>?>(null);

    public Task<IReadOnlyList<InvoicePhoto>?> HandleAsync(ResourceAccess access, CancellationToken cancellationToken) =>
        documents.ListPhotosAsync(access, cancellationToken);
}

/// <summary>POST /public/invoice-links/photos/content (BR-21): the photo must belong to a visit of the token work order.</summary>
public sealed class GetInvoicePhotoHandler(IOnlinePaymentStore store, IInvoiceLinkStore documents)
{
    public Task<PublicOutcome<PublicBinary>> HandleAsync(string? token, string? photoId, CancellationToken cancellationToken) =>
        QuoteLinkTokens.IsWellFormed(token)
            ? HandleAsync(ResourceAccess.FromToken(token!), photoId, cancellationToken)
            : Task.FromResult<PublicOutcome<PublicBinary>>(new PublicOutcome<PublicBinary>.NotFound());

    public async Task<PublicOutcome<PublicBinary>> HandleAsync(ResourceAccess access, string? photoId, CancellationToken cancellationToken)
    {
        if (await store.ResolveAsync(access, cancellationToken) is null)
        {
            return new PublicOutcome<PublicBinary>.NotFound();
        }

        if (!OnlinePaymentRules.TryParseKey(photoId, out var id))
        {
            return PublicPaymentSupport.Invalid<PublicBinary>("photoId", OnlinePaymentMessages.IdInvalid);
        }

        return await documents.GetPhotoAsync(access, id, cancellationToken) is { } photo
            ? new PublicOutcome<PublicBinary>.Ok(photo)
            : new PublicOutcome<PublicBinary>.NotFound();
    }
}

/// <summary>
/// POST /webhooks/stripe (BR-09, BR-10). The signature is verified first; the provider answers a failed intent needs and the
/// card details of a success are fetched before the transaction, so no provider call runs under a lock. Nothing is caught
/// around the transaction: any failure is a 500 and the provider retries.
/// </summary>
public sealed class ProcessPaymentWebhookHandler(
    IOnlinePaymentStore store,
    IPaymentGateway gateway,
    IPaymentReceiptNotifier receiptNotifier,
    TimeProvider timeProvider)
{
    /// <summary>False when the signature is missing or invalid (400); true for every verified event.</summary>
    public async Task<bool> HandleAsync(string payload, string? signature, CancellationToken cancellationToken)
    {
        switch (gateway.ParseWebhook(payload, signature))
        {
            case GatewayWebhookParse.Handled handled:
                await ApplyAsync(handled.Event, cancellationToken);

                return true;
            case GatewayWebhookParse.Unhandled:
                return true;
            default:
                return false;
        }
    }

    private async Task ApplyAsync(GatewayEvent gatewayEvent, CancellationToken cancellationToken)
    {
        var lookup = await store.FindAttemptAsync(gatewayEvent, cancellationToken);
        var pending = lookup?.Status == OnlinePaymentRules.AttemptStatusCode(PaymentAttemptStatus.Pending);
        GatewayCancelResult? cancel = null;
        GatewayCardDetails? card = null;

        if (gatewayEvent.Type == GatewayEventTypes.PaymentFailed && pending && gatewayEvent.IntentId is { } failedIntent)
        {
            cancel = await CancelAsync(failedIntent, cancellationToken);
        }
        else if (gatewayEvent.Type == GatewayEventTypes.PaymentSucceeded
            && lookup?.Status is "pending" or "failed"
            && gatewayEvent.IntentId is { } succeededIntent)
        {
            // A provider failure here is a 500: the event is retried and nothing was written.
            card = await gateway.GetCardDetailsAsync(succeededIntent, cancellationToken);
        }

        var now = timeProvider.GetUtcNow();
        var result = await store.ApplyWebhookAsync(gatewayEvent, new WebhookExtras(cancel, card), now, cancellationToken);

        if (result.Receipt is { } receipt && await receiptNotifier.SendAsync(receipt, cancellationToken) == PaymentReceiptStatus.Sent)
        {
            await store.MarkReceiptSentAsync(receipt.PaymentId, timeProvider.GetUtcNow(), cancellationToken);
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
