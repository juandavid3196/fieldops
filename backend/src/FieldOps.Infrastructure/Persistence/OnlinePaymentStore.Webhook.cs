using FieldOps.Application.Features.InvoicePayments;
using FieldOps.Application.Features.OnlinePayments;
using FieldOps.Application.Features.Quotes;
using FieldOps.Application.Features.ServiceRequests;
using FieldOps.Domain.Invoices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FieldOps.Infrastructure.Persistence;

/// <summary>
/// The provider webhook (customer-invoice-payments BR-09 to BR-15). One transaction per event: the event row is inserted
/// first (a duplicate id ends it), then attempt, invoice and organization are locked in that order and the effects are
/// applied. Nothing is caught inside the transaction: any failure rolls back everything, event row and counters included,
/// and surfaces as a 500 so the provider retries. No provider call runs inside it.
/// </summary>
internal sealed partial class OnlinePaymentStore
{
    private const string NeedsAttentionAmountMismatch = "amount_mismatch";

    private const string NeedsAttentionNotPayable = "invoice_not_payable";

    private const string NeedsAttentionBalanceBelowAmount = "balance_below_amount";

    private const string NeedsAttentionCardDetails = "card_details_missing";

    private const string NeedsAttentionRefundMismatch = "refund_mismatch";

    public async Task<WebhookAttemptLookup?> FindAttemptAsync(GatewayEvent gatewayEvent, CancellationToken cancellationToken)
    {
        var attempts = dbContext.InvoicePaymentAttempts.AsNoTracking().Where(attempt => attempt.Method == PaymentMethod.CardOnline);

        if (gatewayEvent.IntentId is { } intentId
            && await attempts.Where(attempt => attempt.ProviderPaymentIntentId == intentId)
                .Select(attempt => new { attempt.Id, attempt.Status })
                .FirstOrDefaultAsync(cancellationToken) is { } byIntent)
        {
            return new WebhookAttemptLookup(byIntent.Id, OnlinePaymentRules.AttemptStatusCode(byIntent.Status));
        }

        if (gatewayEvent is { AttemptId: { } attemptId, OrganizationId: { } organizationId, InvoiceId: { } invoiceId }
            && await attempts.Where(attempt => attempt.Id == attemptId
                    && attempt.OrganizationId == organizationId
                    && attempt.InvoiceId == invoiceId)
                .Select(attempt => new { attempt.Id, attempt.Status })
                .FirstOrDefaultAsync(cancellationToken) is { } byMetadata)
        {
            return new WebhookAttemptLookup(byMetadata.Id, OnlinePaymentRules.AttemptStatusCode(byMetadata.Status));
        }

        return null;
    }

    public async Task<WebhookResult> ApplyWebhookAsync(
        GatewayEvent gatewayEvent, WebhookExtras extras, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // BR-10: the event row first; a duplicate id (also from a concurrent request) inserts nothing.
        var inserted = await dbContext.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO payment_webhook_events (provider, provider_event_id, event_type, outcome, received_at)
            VALUES ('stripe', {gatewayEvent.EventId}, {gatewayEvent.Type}, 'no_effect', {now})
            ON CONFLICT (provider, provider_event_id) DO NOTHING
            """,
            cancellationToken);

        if (inserted == 0)
        {
            return new WebhookResult(WebhookOutcome.Duplicate, null);
        }

        var attempt = await LockAttemptAsync(gatewayEvent, cancellationToken);

        if (attempt is null)
        {
            await SetEventOutcomeAsync(gatewayEvent, PaymentWebhookOutcomes.Ignored, null, null, cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new WebhookResult(WebhookOutcome.Ignored, null);
        }

        // A matched attempt without a stored intent id (phase C never ran) learns it from the event.
        if (attempt.ProviderPaymentIntentId is null && gatewayEvent.IntentId is { } learned)
        {
            attempt.AttachProviderIntent(learned, now);
        }

        var result = gatewayEvent.Type switch
        {
            GatewayEventTypes.PaymentSucceeded => await ApplySucceededAsync(gatewayEvent, extras, attempt, now, cancellationToken),
            GatewayEventTypes.PaymentFailed => await ApplyFailedAsync(gatewayEvent, extras, attempt, now, cancellationToken),
            GatewayEventTypes.PaymentCanceled => await ApplyCanceledAsync(attempt, now, cancellationToken),
            GatewayEventTypes.ChargeRefunded => await ApplyRefundAsync(gatewayEvent, attempt, now, cancellationToken),
            _ => new WebhookResult(WebhookOutcome.NoEffect, null),
        };

        await dbContext.SaveChangesAsync(cancellationToken);
        await SetEventOutcomeAsync(
            gatewayEvent, OutcomeCode(result.Outcome), attempt.OrganizationId, attempt.Id, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        if (result.Outcome == WebhookOutcome.Applied)
        {
            logger.LogInformation(
                "Payment webhook applied. AttemptId={AttemptId} EventType={EventType}", attempt.Id, gatewayEvent.Type);
        }

        return result;
    }

    // Locks the matched attempt (intent id first, then the metadata ids with matching organization and invoice) and loads it.
    private async Task<InvoicePaymentAttempt?> LockAttemptAsync(GatewayEvent gatewayEvent, CancellationToken cancellationToken)
    {
        List<Guid> locked = [];

        if (gatewayEvent.IntentId is { } intentId)
        {
            locked = await dbContext.Database
                .SqlQuery<Guid>(
                    $"""
                    SELECT id AS "Value" FROM invoice_payment_attempts
                    WHERE provider_payment_intent_id = {intentId} AND method::text = 'card_online'
                    FOR UPDATE
                    """)
                .ToListAsync(cancellationToken);
        }

        if (locked.Count == 0 && gatewayEvent is { AttemptId: { } attemptId, OrganizationId: { } organizationId, InvoiceId: { } invoiceId })
        {
            locked = await dbContext.Database
                .SqlQuery<Guid>(
                    $"""
                    SELECT id AS "Value" FROM invoice_payment_attempts
                    WHERE id = {attemptId} AND organization_id = {organizationId} AND invoice_id = {invoiceId} AND method::text = 'card_online'
                    FOR UPDATE
                    """)
                .ToListAsync(cancellationToken);
        }

        if (locked.Count != 1)
        {
            return null;
        }

        var attempt = await dbContext.InvoicePaymentAttempts.SingleAsync(candidate => candidate.Id == locked[0], cancellationToken);

        // Metadata never overrides a different stored intent: the event is not for this attempt.
        return attempt.ProviderPaymentIntentId is { } stored && gatewayEvent.IntentId is { } incoming && stored != incoming
            ? null
            : attempt;
    }

    private async Task<Invoice> LockInvoiceAsync(InvoicePaymentAttempt attempt, CancellationToken cancellationToken)
    {
        var locked = await dbContext.Database
            .SqlQuery<Guid>(
                $"""
                SELECT id AS "Value" FROM invoices
                WHERE id = {attempt.InvoiceId} AND organization_id = {attempt.OrganizationId}
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);

        if (locked.Count != 1)
        {
            throw new InvalidOperationException("The invoice of the attempt is no longer available.");
        }

        return await dbContext.Invoices.SingleAsync(
            invoice => invoice.OrganizationId == attempt.OrganizationId && invoice.Id == attempt.InvoiceId, cancellationToken);
    }

    // BR-11: payment, receipt, allocation, balance, status, attempt and audit in the one transaction of the event.
    private async Task<WebhookResult> ApplySucceededAsync(
        GatewayEvent gatewayEvent, WebhookExtras extras, InvoicePaymentAttempt attempt, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (attempt.Status is not (PaymentAttemptStatus.Pending or PaymentAttemptStatus.Failed))
        {
            return new WebhookResult(WebhookOutcome.NoEffect, null);
        }

        var invoice = await LockInvoiceAsync(attempt, cancellationToken);
        var category = invoice.Status is not (InvoiceStatus.Sent or InvoiceStatus.PartiallyPaid)
            ? NeedsAttentionNotPayable
            : gatewayEvent.Amount != attempt.Amount
                || !string.Equals(gatewayEvent.Currency, attempt.Currency, StringComparison.OrdinalIgnoreCase)
                ? NeedsAttentionAmountMismatch
                : invoice.BalanceDue < attempt.Amount
                    ? NeedsAttentionBalanceBelowAmount
                    : extras.Card?.Last4 is not { Length: 4 } last4 || !last4.All(char.IsAsciiDigit)
                        ? NeedsAttentionCardDetails
                        : null;

        if (category is not null)
        {
            return NeedsAttention(invoice, attempt, category);
        }

        // The organization row owns the payment counter; it is locked last (attempt, invoice, organization).
        var numbers = await dbContext.Database
            .SqlQuery<long>(
                $"""
                SELECT next_payment_number AS "Value" FROM organizations
                WHERE id = {attempt.OrganizationId}
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);

        if (numbers.Count != 1)
        {
            throw new InvalidOperationException("The organization is no longer available.");
        }

        var number = numbers[0];
        var org = await dbContext.Organizations.AsNoTracking()
            .Where(organization => organization.Id == attempt.OrganizationId)
            .Select(organization => new
            {
                organization.Name,
                organization.Phone,
                organization.Timezone,
                organization.PaymentPrefix,
                organization.InvoicePrefix,
            })
            .SingleAsync(cancellationToken);
        var receiptCount = await PublicPaymentMapper.ReceiptCountAsync(dbContext, attempt.OrganizationId, invoice.Id, cancellationToken);

        await dbContext.Organizations
            .Where(organization => organization.Id == attempt.OrganizationId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(organization => organization.NextPaymentNumber, number + 1), cancellationToken);

        var before = AuditState(invoice);
        var receiptNumber = OnlinePaymentRules.ReceiptNumber(invoice.InvoiceNumber, receiptCount + 1);
        var payment = Payment.CreateOnline(
            attempt.OrganizationId,
            invoice.CustomerId,
            number,
            attempt.Amount,
            attempt.Currency,
            now,
            attempt.IdempotencyKey,
            receiptNumber,
            extras.Card!.Brand,
            extras.Card.Last4!);

        dbContext.Payments.Add(payment);
        dbContext.PaymentAllocations.Add(PaymentAllocation.Create(payment.Id, invoice.Id, attempt.Amount));
        invoice.ApplyPayment(attempt.Amount, now);
        attempt.MarkSucceeded(payment.Id, now);

        AddAudit(
            attempt.OrganizationId,
            invoice.BranchId,
            "payment.recorded",
            "payment",
            payment.Id,
            null,
            new { amount = QuoteCalculator.Money(payment.Amount), currency = payment.Currency, method = PaymentMethodCodes.Code(payment.Method) },
            new { paymentNumber = number, receiptNumber, invoiceId = invoice.Id, source = "online" });
        AddAudit(
            attempt.OrganizationId,
            invoice.BranchId,
            "invoice.payment_applied",
            InvoiceEntityType,
            invoice.Id,
            before,
            AuditState(invoice),
            new { paymentId = payment.Id });

        var receipt = string.IsNullOrWhiteSpace(invoice.RecipientEmail)
            ? null
            : new PaymentReceiptData(
                payment.Id,
                invoice.RecipientEmail,
                org.Name,
                org.Phone,
                InvoiceHubRules.DisplayNumber(org.PaymentPrefix, number),
                payment.Amount,
                payment.Currency,
                OrganizationTime.LocalDate(now, OrganizationTime.FindZone(org.Timezone)),
                OnlinePaymentRules.CardLabel(payment.CardBrand, payment.CardLast4),
                InvoiceHubRules.DisplayNumber(org.InvoicePrefix, invoice.InvoiceNumber),
                invoice.BalanceDue,
                receiptNumber);

        return new WebhookResult(WebhookOutcome.Applied, receipt);
    }

    // BR-13: the failure category is stored; the attempt fails only when the provider confirmed the cancellation.
    private async Task<WebhookResult> ApplyFailedAsync(
        GatewayEvent gatewayEvent, WebhookExtras extras, InvoicePaymentAttempt attempt, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (attempt.Status != PaymentAttemptStatus.Pending)
        {
            return new WebhookResult(WebhookOutcome.NoEffect, null);
        }

        var category = PaymentFailureCategories.FromProviderCode(gatewayEvent.FailureCode);

        if (extras.Cancel is GatewayCancelResult.Canceled or GatewayCancelResult.AlreadyCanceled)
        {
            return await FailAsync(attempt, category, now, cancellationToken);
        }

        attempt.RecordFailureCategory(category, now);

        return new WebhookResult(WebhookOutcome.NoEffect, null);
    }

    private async Task<WebhookResult> ApplyCanceledAsync(InvoicePaymentAttempt attempt, DateTimeOffset now, CancellationToken cancellationToken) =>
        attempt.Status == PaymentAttemptStatus.Pending
            ? await FailAsync(attempt, attempt.FailureCategory ?? PaymentFailureCategories.Canceled, now, cancellationToken)
            : new WebhookResult(WebhookOutcome.NoEffect, null);

    private async Task<WebhookResult> FailAsync(
        InvoicePaymentAttempt attempt, string category, DateTimeOffset now, CancellationToken cancellationToken)
    {
        attempt.MarkFailed(category, now);
        AuditFailed(attempt.OrganizationId, await BranchOfAsync(attempt.OrganizationId, attempt.InvoiceId, cancellationToken), attempt.Id, category);

        return new WebhookResult(WebhookOutcome.Applied, null);
    }

    // BR-15: only the new refunded delta moves payment, attempt and invoice.
    private async Task<WebhookResult> ApplyRefundAsync(
        GatewayEvent gatewayEvent, InvoicePaymentAttempt attempt, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (attempt.Status is not (PaymentAttemptStatus.Succeeded or PaymentAttemptStatus.PartiallyRefunded) || attempt.PaymentId is not { } paymentId)
        {
            return new WebhookResult(WebhookOutcome.NoEffect, null);
        }

        var payment = await dbContext.Payments.SingleAsync(
            candidate => candidate.OrganizationId == attempt.OrganizationId && candidate.Id == paymentId, cancellationToken);

        if (gatewayEvent.CumulativeRefunded <= payment.RefundedAmount)
        {
            return new WebhookResult(WebhookOutcome.NoEffect, null);
        }

        var invoice = await LockInvoiceAsync(attempt, cancellationToken);

        if (gatewayEvent.CumulativeRefunded > payment.Amount
            || !string.Equals(gatewayEvent.Currency, payment.Currency, StringComparison.OrdinalIgnoreCase))
        {
            return NeedsAttention(invoice, attempt, NeedsAttentionRefundMismatch);
        }

        var paymentBefore = new { status = OnlinePaymentRules.PaymentStatusCode(payment.Status), refundedAmount = QuoteCalculator.Money(payment.RefundedAmount) };
        var invoiceBefore = AuditState(invoice);
        var delta = payment.ApplyRefund(gatewayEvent.CumulativeRefunded);

        attempt.ApplyRefund(gatewayEvent.CumulativeRefunded, now);
        invoice.ApplyRefund(delta, now);

        AddAudit(
            attempt.OrganizationId,
            invoice.BranchId,
            "payment.refunded",
            "payment",
            payment.Id,
            paymentBefore,
            new { status = OnlinePaymentRules.PaymentStatusCode(payment.Status), refundedAmount = QuoteCalculator.Money(payment.RefundedAmount) },
            null);
        AddAudit(
            attempt.OrganizationId,
            invoice.BranchId,
            "invoice.refund_applied",
            InvoiceEntityType,
            invoice.Id,
            invoiceBefore,
            AuditState(invoice),
            new { paymentId = payment.Id });

        return new WebhookResult(WebhookOutcome.Applied, null);
    }

    // BR-11: nothing is applied; the event, an audit row and the log record the condition by attempt id and category.
    private WebhookResult NeedsAttention(Invoice invoice, InvoicePaymentAttempt attempt, string category)
    {
        AuditAttempt(invoice, "invoice_payment_attempt.needs_attention", attempt.Id, new { attemptId = attempt.Id, category });
        logger.LogWarning("Online payment needs attention. AttemptId={AttemptId} Category={Category}", attempt.Id, category);

        return new WebhookResult(WebhookOutcome.NeedsAttention, null);
    }

    private Task<int> SetEventOutcomeAsync(
        GatewayEvent gatewayEvent, string outcome, Guid? organizationId, Guid? attemptId, CancellationToken cancellationToken) =>
        dbContext.Database.ExecuteSqlAsync(
            $"""
            UPDATE payment_webhook_events
            SET outcome = {outcome}, organization_id = {organizationId}::uuid, attempt_id = {attemptId}::uuid
            WHERE provider = 'stripe' AND provider_event_id = {gatewayEvent.EventId}
            """,
            cancellationToken);

    private static string OutcomeCode(WebhookOutcome outcome) => outcome switch
    {
        WebhookOutcome.Applied => PaymentWebhookOutcomes.Applied,
        WebhookOutcome.NeedsAttention => PaymentWebhookOutcomes.NeedsAttention,
        WebhookOutcome.Ignored => PaymentWebhookOutcomes.Ignored,
        _ => PaymentWebhookOutcomes.NoEffect,
    };

    private static object AuditState(Invoice invoice) =>
        new
        {
            status = InvoiceHubRules.StoredStatus(invoice.Status),
            amountPaid = QuoteCalculator.Money(invoice.AmountPaid),
            balanceDue = QuoteCalculator.Money(invoice.BalanceDue),
        };
}
