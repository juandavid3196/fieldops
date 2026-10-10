namespace FieldOps.Domain.Invoices;

/// <summary>
/// SA-16: one card or bank transfer attempt of an invoice (customer-invoice-payments). A card attempt holds the invoice
/// against external payments while it is pending (BR-14); a bank transfer attempt is only a customer notice (BR-17).
/// Status changes happen through the methods below, always under the locks of the store.
/// </summary>
public sealed class InvoicePaymentAttempt
{
    public const int CardLifetimeMinutes = 30;

    private InvoicePaymentAttempt()
    {
    }

    private InvoicePaymentAttempt(
        Guid id,
        Guid organizationId,
        Guid invoiceId,
        PaymentMethod method,
        decimal amount,
        string currency,
        Guid idempotencyKey,
        DateTimeOffset? expiresAt,
        DateTimeOffset now)
    {
        Id = id;
        OrganizationId = organizationId;
        InvoiceId = invoiceId;
        Method = method;
        Status = PaymentAttemptStatus.Pending;
        Amount = amount;
        Currency = currency;
        IdempotencyKey = idempotencyKey;
        ExpiresAt = expiresAt;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public Guid InvoiceId { get; private set; }

    public PaymentMethod Method { get; private set; }

    public PaymentAttemptStatus Status { get; private set; }

    public decimal Amount { get; private set; }

    public string Currency { get; private set; } = string.Empty;

    public Guid IdempotencyKey { get; private set; }

    /// <summary>The provider intent id; stored outside the transaction that created the attempt (BR-06) and never exposed.</summary>
    public string? ProviderPaymentIntentId { get; private set; }

    public Guid? PaymentId { get; private set; }

    public string? FailureCategory { get; private set; }

    public decimal RefundedAmount { get; private set; }

    public DateTimeOffset? ExpiresAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static InvoicePaymentAttempt CreateCard(
        Guid organizationId, Guid invoiceId, decimal amount, string currency, Guid idempotencyKey, DateTimeOffset now) =>
        Create(organizationId, invoiceId, PaymentMethod.CardOnline, amount, currency, idempotencyKey, now.AddMinutes(CardLifetimeMinutes), now);

    public static InvoicePaymentAttempt CreateBankTransfer(
        Guid organizationId, Guid invoiceId, decimal amount, string currency, Guid idempotencyKey, DateTimeOffset now) =>
        Create(organizationId, invoiceId, PaymentMethod.BankTransfer, amount, currency, idempotencyKey, null, now);

    /// <summary>Stores the provider intent id found through the event metadata when phase C of the card intent never ran (BR-09).</summary>
    public void AttachProviderIntent(string intentId, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(intentId))
        {
            throw new ArgumentException("Intent id is required.", nameof(intentId));
        }

        if (ProviderPaymentIntentId is null)
        {
            ProviderPaymentIntentId = intentId.Trim();
            UpdatedAt = now;
        }
    }

    /// <summary>The card attempt succeeded: the payment of the webhook (BR-11) is linked.</summary>
    public void MarkSucceeded(Guid paymentId, DateTimeOffset now)
    {
        if (Status is not (PaymentAttemptStatus.Pending or PaymentAttemptStatus.Failed))
        {
            throw new InvalidOperationException("Only a pending or failed attempt can succeed.");
        }

        if (paymentId == Guid.Empty)
        {
            throw new ArgumentException("Payment id is required.", nameof(paymentId));
        }

        Status = PaymentAttemptStatus.Succeeded;
        PaymentId = paymentId;
        UpdatedAt = now;
    }

    /// <summary>Pending to failed (BR-13, BR-14) with the category the caller decided; false when the attempt is not pending.</summary>
    public bool MarkFailed(string category, DateTimeOffset now)
    {
        if (Status != PaymentAttemptStatus.Pending)
        {
            return false;
        }

        Status = PaymentAttemptStatus.Failed;
        FailureCategory = category;
        UpdatedAt = now;

        return true;
    }

    /// <summary>Stores the category of a failure that did not (yet) release the attempt (BR-13).</summary>
    public void RecordFailureCategory(string category, DateTimeOffset now)
    {
        FailureCategory = category;
        UpdatedAt = now;
    }

    /// <summary>The cumulative refund of a succeeded attempt (BR-15); returns false when nothing is new.</summary>
    public bool ApplyRefund(decimal cumulativeRefunded, DateTimeOffset now)
    {
        if (Status is not (PaymentAttemptStatus.Succeeded or PaymentAttemptStatus.PartiallyRefunded)
            || cumulativeRefunded <= RefundedAmount)
        {
            return false;
        }

        if (cumulativeRefunded > Amount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(cumulativeRefunded),
                cumulativeRefunded,
                "The refunded amount cannot exceed the attempt amount.");
        }

        RefundedAmount = cumulativeRefunded;
        Status = cumulativeRefunded == Amount ? PaymentAttemptStatus.Refunded : PaymentAttemptStatus.PartiallyRefunded;
        UpdatedAt = now;

        return true;
    }

    private static InvoicePaymentAttempt Create(
        Guid organizationId,
        Guid invoiceId,
        PaymentMethod method,
        decimal amount,
        string currency,
        Guid idempotencyKey,
        DateTimeOffset? expiresAt,
        DateTimeOffset now)
    {
        if (organizationId == Guid.Empty)
        {
            throw new ArgumentException("Organization id is required.", nameof(organizationId));
        }

        if (invoiceId == Guid.Empty)
        {
            throw new ArgumentException("Invoice id is required.", nameof(invoiceId));
        }

        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Amount must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(currency))
        {
            throw new ArgumentException("Currency is required.", nameof(currency));
        }

        if (idempotencyKey == Guid.Empty)
        {
            throw new ArgumentException("Idempotency key is required.", nameof(idempotencyKey));
        }

        return new InvoicePaymentAttempt(
            Guid.NewGuid(), organizationId, invoiceId, method, amount, currency.Trim(), idempotencyKey, expiresAt, now);
    }
}

/// <summary>Failure categories of a card attempt (BR-13, BR-14); the codes are stored in <c>failure_category</c>.</summary>
public static class PaymentFailureCategories
{
    public const string CardDeclined = "card_declined";

    public const string InsufficientFunds = "insufficient_funds";

    public const string ExpiredCard = "expired_card";

    public const string IncorrectCvc = "incorrect_cvc";

    public const string AuthenticationFailed = "authentication_failed";

    public const string ProcessingError = "processing_error";

    public const string Other = "other";

    public const string Expired = "expired";

    public const string Canceled = "canceled";

    public const string ProviderUnavailable = "provider_unavailable";

    /// <summary>Maps the provider error (decline) code of a failed payment to a stored category (BR-13).</summary>
    public static string FromProviderCode(string? code) => code?.Trim().ToLowerInvariant() switch
    {
        "card_declined" or "generic_decline" or "do_not_honor" or "lost_card" or "stolen_card" or "fraudulent"
            or "pickup_card" or "restricted_card" or "card_not_supported" or "currency_not_supported" => CardDeclined,
        "insufficient_funds" => InsufficientFunds,
        "expired_card" => ExpiredCard,
        "incorrect_cvc" or "invalid_cvc" => IncorrectCvc,
        "authentication_required" or "authentication_failure" or "payment_intent_authentication_failure" => AuthenticationFailed,
        "processing_error" => ProcessingError,
        _ => Other,
    };
}
