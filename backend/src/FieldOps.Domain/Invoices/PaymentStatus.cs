namespace FieldOps.Domain.Invoices;

/// <summary>SA-14: PostgreSQL enum <c>payment_status</c>.</summary>
public enum PaymentStatus
{
    Succeeded,
    PartiallyRefunded,
    Refunded,
}
