namespace FieldOps.Domain.Invoices;

public sealed class Payment
{
    private Payment()
    {
    }

    private Payment(
        Guid id,
        Guid organizationId,
        Guid customerId,
        long paymentNumber,
        PaymentMethod method,
        decimal amount,
        string currency,
        DateTimeOffset paidAt,
        Guid? receivedByUserId,
        Guid idempotencyKey)
    {
        Id = id;
        OrganizationId = organizationId;
        CustomerId = customerId;
        PaymentNumber = paymentNumber;
        Method = method;
        Amount = amount;
        Currency = currency;
        PaidAt = paidAt;
        ReceivedByUserId = receivedByUserId;
        IdempotencyKey = idempotencyKey;
        Status = PaymentStatus.Succeeded;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public Guid CustomerId { get; private set; }

    public long PaymentNumber { get; private set; }

    public PaymentMethod Method { get; private set; }

    public decimal Amount { get; private set; }

    public string Currency { get; private set; } = string.Empty;

    public DateTimeOffset PaidAt { get; private set; }

    public string? ExternalReference { get; private set; }

    public string? Notes { get; private set; }

    public Guid? RecordedByUserId { get; private set; }

    /// <summary>SA-12: the member who received the money; null only for <see cref="PaymentMethod.CardOnline"/> (SA-15).</summary>
    public Guid? ReceivedByUserId { get; private set; }

    /// <summary>SA-12: the client key of the record request, unique per organization.</summary>
    public Guid IdempotencyKey { get; private set; }

    // Set when the payment was captured externally (e.g. PaymentMethod.CardExternal):
    // FieldOps records the result, it never processes the payment itself.
    public string? ReceiptStorageKey { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>SA-15: succeeded, partially_refunded or refunded.</summary>
    public PaymentStatus Status { get; private set; }

    /// <summary>SA-15: the cumulative amount refunded by the provider; the net amount is <see cref="NetAmount"/>.</summary>
    public decimal RefundedAmount { get; private set; }

    /// <summary>SA-15: <c>RCT-&lt;invoice number&gt;-&lt;nn&gt;</c>, unique per organization; null for payments recorded before the feature.</summary>
    public string? ReceiptNumber { get; private set; }

    public DateTimeOffset? ReceiptSentAt { get; private set; }

    /// <summary>SA-15: the card brand read from the provider, lower case; the only card data FieldOps stores besides the last four digits.</summary>
    public string? CardBrand { get; private set; }

    public string? CardLast4 { get; private set; }

    /// <summary>The amount that remains paid: <c>Amount - RefundedAmount</c> (customer-invoice-payments BR-31).</summary>
    public decimal NetAmount => Amount - RefundedAmount;

    /// <summary>
    /// Records the cumulative amount refunded by the provider (BR-15). Returns the new delta; a cumulative amount that does
    /// not exceed the stored one changes nothing and returns zero. A cumulative amount above the payment amount is invalid.
    /// </summary>
    public decimal ApplyRefund(decimal cumulativeRefunded)
    {
        if (cumulativeRefunded < 0 || cumulativeRefunded > Amount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(cumulativeRefunded),
                cumulativeRefunded,
                "The refunded amount must be between zero and the payment amount.");
        }

        if (cumulativeRefunded <= RefundedAmount)
        {
            return 0m;
        }

        var delta = cumulativeRefunded - RefundedAmount;
        RefundedAmount = cumulativeRefunded;
        Status = cumulativeRefunded == Amount ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;

        return delta;
    }

    /// <summary>The receipt email went out (BR-12); replays never resend.</summary>
    public void MarkReceiptSent(DateTimeOffset now) => ReceiptSentAt = now;

    /// <summary>
    /// Creates a card payment confirmed by the provider (BR-11): no recorder, no receiver, the attempt idempotency key and
    /// the brand and last four digits read from the provider.
    /// </summary>
    public static Payment CreateOnline(
        Guid organizationId,
        Guid customerId,
        long paymentNumber,
        decimal amount,
        string currency,
        DateTimeOffset paidAt,
        Guid idempotencyKey,
        string receiptNumber,
        string? cardBrand,
        string cardLast4)
    {
        if (string.IsNullOrWhiteSpace(receiptNumber))
        {
            throw new ArgumentException("Receipt number is required.", nameof(receiptNumber));
        }

        if (cardLast4 is not { Length: 4 } || !cardLast4.All(char.IsAsciiDigit))
        {
            throw new ArgumentException("Card last four digits are required.", nameof(cardLast4));
        }

        var brand = string.IsNullOrWhiteSpace(cardBrand) ? null : cardBrand.Trim().ToLowerInvariant();

        if (brand is { Length: > 20 })
        {
            throw new ArgumentException("Card brand must be 20 characters or fewer.", nameof(cardBrand));
        }

        var payment = Create(
            organizationId,
            customerId,
            paymentNumber,
            PaymentMethod.CardOnline,
            amount,
            currency,
            paidAt,
            null,
            idempotencyKey,
            receiptNumber: receiptNumber);

        payment.CardBrand = brand;
        payment.CardLast4 = cardLast4;

        return payment;
    }

    public static Payment Create(
        Guid organizationId,
        Guid customerId,
        long paymentNumber,
        PaymentMethod method,
        decimal amount,
        string currency,
        DateTimeOffset paidAt,
        Guid? receivedByUserId,
        Guid idempotencyKey,
        Guid? recordedByUserId = null,
        string? externalReference = null,
        string? notes = null,
        string? receiptNumber = null)
    {
        if (organizationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Organization id is required.",
                nameof(organizationId));
        }

        if (customerId == Guid.Empty)
        {
            throw new ArgumentException(
                "Customer id is required.",
                nameof(customerId));
        }

        if (paymentNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(paymentNumber),
                paymentNumber,
                "Payment number must be greater than zero.");
        }

        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount),
                amount,
                "Amount must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(currency))
        {
            throw new ArgumentException(
                "Currency is required.",
                nameof(currency));
        }

        if (receivedByUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "Received by user id is required.",
                nameof(receivedByUserId));
        }

        // SA-15: the receiver is null exactly for card_online.
        if ((method == PaymentMethod.CardOnline) != (receivedByUserId is null))
        {
            throw new ArgumentException(
                "Only an online card payment has no receiver.",
                nameof(receivedByUserId));
        }

        if (idempotencyKey == Guid.Empty)
        {
            throw new ArgumentException(
                "Idempotency key is required.",
                nameof(idempotencyKey));
        }

        return new Payment(
            Guid.NewGuid(),
            organizationId,
            customerId,
            paymentNumber,
            method,
            amount,
            currency.Trim(),
            paidAt,
            receivedByUserId,
            idempotencyKey)
        {
            RecordedByUserId = recordedByUserId,
            ExternalReference = externalReference,
            Notes = notes,
            ReceiptNumber = string.IsNullOrWhiteSpace(receiptNumber) ? null : receiptNumber.Trim(),
        };
    }
}
