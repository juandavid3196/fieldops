namespace FieldOps.Domain.Invoices;

// amount_paid and balance_due are persisted values, not computed columns:
// keeping them consistent with the sum of PaymentAllocation.Amount for this
// invoice requires transactional domain logic (e.g. a use case that updates
// both an invoice's amount_paid/balance_due and creates the allocation in
// the same transaction). A CHECK constraint cannot reference other rows, so
// only amount_paid<=total is enforced at the database level.
public sealed class Invoice
{
    private Invoice()
    {
    }

    private Invoice(
        Guid id,
        Guid organizationId,
        Guid branchId,
        long invoiceNumber,
        Guid workOrderId,
        Guid customerId,
        string currency,
        decimal subtotal,
        decimal taxTotal,
        decimal total,
        decimal balanceDue,
        Guid createdByUserId)
    {
        Id = id;
        OrganizationId = organizationId;
        BranchId = branchId;
        InvoiceNumber = invoiceNumber;
        WorkOrderId = workOrderId;
        CustomerId = customerId;
        Currency = currency;
        Subtotal = subtotal;
        TaxTotal = taxTotal;
        Total = total;
        AmountPaid = 0m;
        BalanceDue = balanceDue;
        CreatedByUserId = createdByUserId;
        Status = InvoiceStatus.Draft;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public Guid BranchId { get; private set; }

    public long InvoiceNumber { get; private set; }

    public Guid WorkOrderId { get; private set; }

    public Guid CustomerId { get; private set; }

    public InvoiceStatus Status { get; private set; }

    public DateOnly? IssueDate { get; private set; }

    public DateOnly? DueDate { get; private set; }

    public string? PaymentTerms { get; private set; }

    public string Currency { get; private set; } = string.Empty;

    public decimal Subtotal { get; private set; }

    public decimal DiscountTotal { get; private set; }

    public decimal TaxTotal { get; private set; }

    public decimal Total { get; private set; }

    public decimal AmountPaid { get; private set; }

    public decimal BalanceDue { get; private set; }

    public string? Notes { get; private set; }

    public DateTimeOffset? SentAt { get; private set; }

    public DateTimeOffset? VoidedAt { get; private set; }

    public string? VoidReason { get; private set; }

    /// <summary>SA-08: the customer email the invoice is sent to; null until the first save with a recipient.</summary>
    public string? RecipientEmail { get; private set; }

    /// <summary>SA-08: the message of the delivery email; null until the first save or send.</summary>
    public string? DeliveryMessage { get; private set; }

    /// <summary>SA-09: the customer-facing content frozen at send (JSON); null while the invoice is a draft.</summary>
    public string? CustomerSnapshot { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Stores the delivery data of a draft (invoice-draft-delivery BR-09). The concurrency token moves only when
    /// something changed; the result tells whether it did.
    /// </summary>
    public bool UpdateDelivery(
        string paymentTerms,
        DateOnly? dueDate,
        string? recipientEmail,
        string message,
        DateTimeOffset now)
    {
        EnsureDraft();
        ValidateDelivery(paymentTerms, dueDate, message);

        if (PaymentTerms == paymentTerms
            && DueDate == dueDate
            && RecipientEmail == recipientEmail
            && DeliveryMessage == message)
        {
            return false;
        }

        PaymentTerms = paymentTerms;
        DueDate = dueDate;
        RecipientEmail = recipientEmail;
        DeliveryMessage = message;
        Touch(now);

        return true;
    }

    /// <summary>The draft to sent transition (BR-14): delivery data, send instant and the frozen customer content.</summary>
    public void MarkSent(
        string paymentTerms,
        DateOnly? dueDate,
        string recipientEmail,
        string message,
        string customerSnapshot,
        DateTimeOffset now)
    {
        EnsureDraft();
        ValidateDelivery(paymentTerms, dueDate, message);

        if (string.IsNullOrWhiteSpace(recipientEmail))
        {
            throw new ArgumentException("Recipient email is required.", nameof(recipientEmail));
        }

        if (string.IsNullOrWhiteSpace(customerSnapshot))
        {
            throw new ArgumentException("Customer snapshot is required.", nameof(customerSnapshot));
        }

        PaymentTerms = paymentTerms;
        DueDate = dueDate;
        RecipientEmail = recipientEmail;
        DeliveryMessage = message;
        CustomerSnapshot = customerSnapshot;
        Status = InvoiceStatus.Sent;
        SentAt = Truncate(now);
        Touch(now);
    }

    /// <summary>
    /// Applies an external payment (invoices-payments-management BR-13, BR-15): a sent or partially paid invoice moves
    /// to partially_paid, or to paid when the balance reaches zero. The amount never exceeds the balance.
    /// </summary>
    public void ApplyPayment(decimal amount, DateTimeOffset now)
    {
        if (Status is not (InvoiceStatus.Sent or InvoiceStatus.PartiallyPaid))
        {
            throw new InvalidOperationException("Only a sent or partially paid invoice can receive payments.");
        }

        if (amount <= 0 || amount > BalanceDue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount),
                amount,
                "Amount must be greater than zero and not exceed the balance due.");
        }

        AmountPaid += amount;
        BalanceDue -= amount;
        Status = BalanceDue == 0m ? InvoiceStatus.Paid : InvoiceStatus.PartiallyPaid;
        Touch(now);
    }

    /// <summary>
    /// Applies a provider refund delta (customer-invoice-payments BR-15): the amount paid shrinks and the balance grows by
    /// the delta, and a non-void invoice recomputes its status from the amounts (a paid invoice reopens).
    /// </summary>
    public void ApplyRefund(decimal delta, DateTimeOffset now)
    {
        if (delta <= 0 || delta > AmountPaid)
        {
            throw new ArgumentOutOfRangeException(
                nameof(delta),
                delta,
                "The refund must be greater than zero and not exceed the amount paid.");
        }

        AmountPaid -= delta;
        BalanceDue += delta;

        if (Status != InvoiceStatus.Void)
        {
            Status = AmountPaid == 0m
                ? InvoiceStatus.Sent
                : AmountPaid >= Total ? InvoiceStatus.Paid : InvoiceStatus.PartiallyPaid;
        }

        Touch(now);
    }

    /// <summary>Sets a new concurrency value, truncated to microseconds (the PostgreSQL precision) and always greater than the previous one.</summary>
    public void Touch(DateTimeOffset now)
    {
        var next = Truncate(now);

        if (next <= UpdatedAt)
        {
            next = UpdatedAt.AddTicks(10);
        }

        UpdatedAt = next;
    }

    private void EnsureDraft()
    {
        if (Status != InvoiceStatus.Draft)
        {
            throw new InvalidOperationException("Only a draft invoice can change its delivery data.");
        }
    }

    private void ValidateDelivery(string paymentTerms, DateOnly? dueDate, string message)
    {
        if (!PaymentTermsCodes.All.Contains(paymentTerms))
        {
            throw new ArgumentException(
                "Payment terms must be due_upon_receipt, net_15 or net_30.",
                nameof(paymentTerms));
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("Message is required.", nameof(message));
        }

        if (IssueDate is not null && dueDate is not null && dueDate < IssueDate)
        {
            throw new ArgumentException("Due date cannot be before issue date.", nameof(dueDate));
        }
    }

    private static DateTimeOffset Truncate(DateTimeOffset value)
    {
        var utc = value.UtcDateTime;

        return new DateTimeOffset(utc.Ticks - (utc.Ticks % 10), TimeSpan.Zero);
    }

    public static Invoice Create(
        Guid organizationId,
        Guid branchId,
        long invoiceNumber,
        Guid workOrderId,
        Guid customerId,
        string currency,
        decimal subtotal,
        decimal taxTotal,
        decimal total,
        decimal balanceDue,
        Guid createdByUserId,
        DateOnly? issueDate = null,
        DateOnly? dueDate = null,
        decimal discountTotal = 0m,
        string? paymentTerms = null)
    {
        if (organizationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Organization id is required.",
                nameof(organizationId));
        }

        if (branchId == Guid.Empty)
        {
            throw new ArgumentException(
                "Branch id is required.",
                nameof(branchId));
        }

        if (invoiceNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(invoiceNumber),
                invoiceNumber,
                "Invoice number must be greater than zero.");
        }

        if (workOrderId == Guid.Empty)
        {
            throw new ArgumentException(
                "Work order id is required.",
                nameof(workOrderId));
        }

        if (customerId == Guid.Empty)
        {
            throw new ArgumentException(
                "Customer id is required.",
                nameof(customerId));
        }

        if (string.IsNullOrWhiteSpace(currency))
        {
            throw new ArgumentException(
                "Currency is required.",
                nameof(currency));
        }

        if (subtotal < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(subtotal),
                subtotal,
                "Subtotal cannot be negative.");
        }

        if (taxTotal < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(taxTotal),
                taxTotal,
                "Tax total cannot be negative.");
        }

        if (total < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(total),
                total,
                "Total cannot be negative.");
        }

        if (balanceDue < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(balanceDue),
                balanceDue,
                "Balance due cannot be negative.");
        }

        if (discountTotal < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(discountTotal),
                discountTotal,
                "Discount total cannot be negative.");
        }

        if (paymentTerms is not null && !PaymentTermsCodes.All.Contains(paymentTerms))
        {
            throw new ArgumentException(
                "Payment terms must be due_upon_receipt, net_15 or net_30.",
                nameof(paymentTerms));
        }

        if (issueDate is not null && dueDate is not null && dueDate < issueDate)
        {
            throw new ArgumentException(
                "Due date cannot be before issue date.",
                nameof(dueDate));
        }

        if (createdByUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "Created by user id is required.",
                nameof(createdByUserId));
        }

        return new Invoice(
            Guid.NewGuid(),
            organizationId,
            branchId,
            invoiceNumber,
            workOrderId,
            customerId,
            currency.Trim(),
            subtotal,
            taxTotal,
            total,
            balanceDue,
            createdByUserId)
        {
            IssueDate = issueDate,
            DueDate = dueDate,
            DiscountTotal = discountTotal,
            PaymentTerms = paymentTerms,
        };
    }
}

/// <summary>Payment terms of an invoice (completed-jobs-review SA-05, BR-14).</summary>
public static class PaymentTermsCodes
{
    public const string DueUponReceipt = "due_upon_receipt";

    public const string Net15 = "net_15";

    public const string Net30 = "net_30";

    public static readonly string[] All = [DueUponReceipt, Net15, Net30];
}
