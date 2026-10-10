namespace FieldOps.Domain.Invoices;

public enum PaymentMethod
{
    Cash,
    BankTransfer,
    CardExternal,
    Check,
    Other,

    /// <summary>SA-14: a card payment confirmed by the payment provider (customer-invoice-payments).</summary>
    CardOnline,
}
