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
        DateTimeOffset paidAt)
    {
        Id = id;
        OrganizationId = organizationId;
        CustomerId = customerId;
        PaymentNumber = paymentNumber;
        Method = method;
        Amount = amount;
        Currency = currency;
        PaidAt = paidAt;
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

    // Set when the payment was captured externally (e.g. PaymentMethod.CardExternal):
    // FieldOps records the result, it never processes the payment itself.
    public string? ReceiptStorageKey { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static Payment Create(
        Guid organizationId,
        Guid customerId,
        long paymentNumber,
        PaymentMethod method,
        decimal amount,
        string currency,
        DateTimeOffset paidAt)
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

        return new Payment(
            Guid.NewGuid(),
            organizationId,
            customerId,
            paymentNumber,
            method,
            amount,
            currency.Trim(),
            paidAt);
    }
}
