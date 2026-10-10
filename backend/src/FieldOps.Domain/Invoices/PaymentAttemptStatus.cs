namespace FieldOps.Domain.Invoices;

/// <summary>SA-14: PostgreSQL enum <c>payment_attempt_status</c>.</summary>
public enum PaymentAttemptStatus
{
    Pending,
    Succeeded,
    Failed,
    PartiallyRefunded,
    Refunded,
}
