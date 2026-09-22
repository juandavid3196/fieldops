namespace FieldOps.Domain.Invoices;

// Models the many-to-many relationship between Payment and Invoice: a
// payment may be split across several invoices, and an invoice may be paid
// through several payments, at most one allocation per (payment, invoice)
// pair. Keeping sum(PaymentAllocation.Amount) consistent with
// Payment.Amount and Invoice.AmountPaid/BalanceDue requires transactional
// domain logic; it cannot be guaranteed by a simple CHECK constraint.
public sealed class PaymentAllocation
{
    private PaymentAllocation()
    {
    }

    private PaymentAllocation(Guid id, Guid paymentId, Guid invoiceId, decimal amount)
    {
        Id = id;
        PaymentId = paymentId;
        InvoiceId = invoiceId;
        Amount = amount;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid PaymentId { get; private set; }

    public Guid InvoiceId { get; private set; }

    public decimal Amount { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static PaymentAllocation Create(Guid paymentId, Guid invoiceId, decimal amount)
    {
        if (paymentId == Guid.Empty)
        {
            throw new ArgumentException(
                "Payment id is required.",
                nameof(paymentId));
        }

        if (invoiceId == Guid.Empty)
        {
            throw new ArgumentException(
                "Invoice id is required.",
                nameof(invoiceId));
        }

        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount),
                amount,
                "Amount must be greater than zero.");
        }

        return new PaymentAllocation(Guid.NewGuid(), paymentId, invoiceId, amount);
    }
}
