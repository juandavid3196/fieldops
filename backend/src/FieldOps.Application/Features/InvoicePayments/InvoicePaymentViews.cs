using FieldOps.Application.Features.BillingReview;
using FieldOps.Domain.Invoices;

namespace FieldOps.Application.Features.InvoicePayments;

/// <summary>Machine codes, messages and limits of the invoices and payments hub (invoices-payments-management BR-01 to BR-19).</summary>
public static class InvoicePaymentMessages
{
    public const int PageSize = BillingMessages.PageSize;

    public const int MaxSearchLength = BillingMessages.MaxSearchLength;

    public const int MaxReferenceLength = 160;

    public const int MaxNoteLength = BillingMessages.MaxNoteLength;

    public const int MaxExportRows = BillingMessages.MaxExportRows;

    public const int MaxCustomers = 20;

    public const int RecentPayments = 5;

    public const int AverageWindowDays = 90;

    public const string NotPayableCode = "invoice_not_payable";

    public const string NotPayableTitle = "This invoice can't receive payments.";

    /// <summary>customer-invoice-payments BR-31 f: a pending online card attempt holds the invoice.</summary>
    public const string PaymentInProgressCode = "payment_in_progress";

    public const string PaymentInProgressTitle = "An online card payment is in progress for this invoice. Try again in a few minutes.";

    public const string IdempotencyConflictCode = "idempotency_conflict";

    public const string IdempotencyConflictTitle = "This payment request was already used for different details.";

    public const string ExportTooLargeTitle = "Narrow the filters to export 5,000 rows or fewer.";

    public const string SearchTooLong = "Use 100 characters or fewer.";

    public const string BranchInvalid = "Select a valid branch.";

    public const string CustomerInvalid = "Select a valid customer.";

    public const string DateInvalid = "Enter a valid date.";

    public const string RangeInvalid = "Use an end date on or after the start date.";

    public const string StatusInvalid = "Select a valid status.";

    public const string MethodFilterInvalid = "Select a valid payment method.";

    public const string PageInvalid = "Use a page number of 1 or more.";

    public const string KeyInvalid = "Try again.";

    public const string AmountInvalid = "Enter an amount greater than 0.";

    public const string AmountPrecision = "Use up to 2 decimal places.";

    public const string PaidDateInvalid = "Enter a valid payment date.";

    public const string PaidDateFuture = "Payment date can't be in the future.";

    public const string PaidDateBeforeIssue = "Payment date can't be before the invoice date.";

    public const string MethodRequired = "Select a payment method.";

    public const string ReferenceRequired = "Enter the reference number.";

    public const string ReferenceTooLong = "Use 160 characters or fewer.";

    public const string ReceivedByInvalid = "Select who received the payment.";

    public const string NoteTooLong = "Use 500 characters or fewer.";

    public const string EmailSent = "sent";

    public const string EmailFailed = "failed";

    public const string EmailNotSent = "not_sent";

    public static string AmountOverBalance(decimal balance, string currency) =>
        string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"Amount can't exceed the outstanding balance of {balance:N2} {currency}.");
}

public static class InvoiceStatusFilters
{
    public const string Draft = "draft";

    public const string Sent = "sent";

    public const string PartiallyPaid = "partially_paid";

    public const string Paid = "paid";

    public const string Overdue = "overdue";

    public static readonly string[] All = [Draft, Sent, PartiallyPaid, Paid, Overdue];
}

/// <summary>Payment method codes and labels (schema enum <c>payment_method</c>).</summary>
public static class PaymentMethodCodes
{
    public static readonly PaymentMethod[] RequiringReference = [PaymentMethod.BankTransfer, PaymentMethod.CardExternal, PaymentMethod.Check];

    public static string Code(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => "cash",
        PaymentMethod.BankTransfer => "bank_transfer",
        PaymentMethod.CardExternal => "card_external",
        PaymentMethod.Check => "check",
        PaymentMethod.CardOnline => "card_online",
        _ => "other",
    };

    public static string Label(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => "Cash",
        PaymentMethod.BankTransfer => "Bank transfer",
        PaymentMethod.CardExternal => "External card payment",
        PaymentMethod.Check => "Check",
        PaymentMethod.CardOnline => "Online card",
        _ => "Other",
    };

    public static bool TryParse(string? code, out PaymentMethod method)
    {
        foreach (var candidate in Enum.GetValues<PaymentMethod>())
        {
            if (Code(candidate) == code?.Trim())
            {
                method = candidate;

                return true;
            }
        }

        method = default;

        return false;
    }
}

public sealed record HubOptions(
    string Timezone,
    string Currency,
    string PaymentPrefix,
    IReadOnlyList<NamedOption> Branches,
    IReadOnlyList<HubMember> Members,
    bool CanAct);

public sealed record HubMember(Guid UserId, string Name);

public sealed record HubMetrics(
    decimal Outstanding, decimal Overdue, decimal Draft, decimal PaidThisMonth, decimal? AverageDaysToPay, string Currency);

public sealed record HubAging(decimal Current, decimal Days1To30, decimal Days31To60, decimal Days60Plus);

public sealed record RecentPayment(
    Guid PaymentId, DateOnly PaidDate, string CustomerName, Guid InvoiceId, string InvoiceNumber, decimal Amount, string Method);

public sealed record HubOverview(HubMetrics Metrics, HubAging Aging, IReadOnlyList<RecentPayment> RecentPayments);

public sealed record LastActivity(string Kind, DateOnly Date);

public sealed record InvoiceRow(
    Guid Id,
    string Number,
    Guid CustomerId,
    string CustomerName,
    Guid WorkOrderId,
    string WorkOrderNumber,
    string BranchName,
    DateOnly? IssueDate,
    DateOnly? DueDate,
    decimal Total,
    decimal BalanceDue,
    string Currency,
    string Status,
    string StoredStatus,
    int? DaysOverdue,
    LastActivity? LastActivity,
    bool HasRecipient,
    DateTimeOffset UpdatedAt,
    bool CanRecordPayment);

public sealed record InvoicePage(IReadOnlyList<InvoiceRow> Items, int Page, int PageSize, int Total);

/// <summary>
/// A payment of the hub. <c>Amount</c> is the gross amount and <c>RefundedAmount</c> what the provider refunded
/// (customer-invoice-payments BR-31); <c>ReceivedByName</c> is null for online card payments.
/// </summary>
public sealed record PaymentRow(
    Guid Id,
    string Number,
    DateOnly PaidDate,
    string CustomerName,
    Guid InvoiceId,
    string InvoiceNumber,
    string Method,
    string? Reference,
    decimal Amount,
    string Currency,
    string? ReceivedByName,
    string Status,
    decimal RefundedAmount);

public sealed record PaymentPage(IReadOnlyList<PaymentRow> Items, int Page, int PageSize, int Total);

/// <summary>Raw invoice list query (BR-07); <c>Paged</c> is false for the export.</summary>
public sealed record InvoiceQueryText(
    string? Search, string? From, string? To, string? Status, string? CustomerId, string? BranchId, string? Page);

public sealed record InvoiceQuery(
    string? Search, DateOnly? From, DateOnly? To, string? Status, Guid? CustomerId, Guid? BranchId, int Page);

/// <summary>Raw payment list query (BR-09).</summary>
public sealed record PaymentQueryText(string? Search, string? From, string? To, string? Method, string? BranchId, string? Page);

public sealed record PaymentQuery(string? Search, DateOnly? From, DateOnly? To, PaymentMethod? Method, Guid? BranchId, int Page);

public sealed record InvoiceExportRow(
    string Number,
    string CustomerName,
    string WorkOrderNumber,
    string BranchName,
    DateOnly? IssueDate,
    DateOnly? DueDate,
    string Status,
    int? DaysOverdue,
    decimal Total,
    decimal AmountPaid,
    decimal BalanceDue,
    string Currency);

public sealed record InvoiceExport(DateOnly OrganizationDate, IReadOnlyList<InvoiceExportRow> Rows);

public sealed record PaymentExport(DateOnly OrganizationDate, IReadOnlyList<PaymentRow> Rows);

/// <summary>Raw record body (BR-11): everything optional text so a missing or malformed value becomes a field error.</summary>
public sealed record PaymentBodyText(
    string? IdempotencyKey,
    decimal? Amount,
    string? PaidDate,
    string? Method,
    string? Reference,
    string? ReceivedByUserId,
    bool? SendReceipt,
    string? Note,
    string? UpdatedAt);

/// <summary>Validated record values (payment field rules); the instant of <c>UpdatedAt</c> stays text for the stale check.</summary>
public sealed record PaymentInput(
    Guid IdempotencyKey,
    decimal Amount,
    DateOnly PaidDate,
    PaymentMethod Method,
    string? Reference,
    Guid ReceivedByUserId,
    bool SendReceipt,
    string? Note,
    string? UpdatedAt);

public sealed record PaymentInvoiceSummary(Guid Id, string Status, decimal AmountPaid, decimal BalanceDue, DateTimeOffset UpdatedAt);

/// <summary>
/// The data of the receipt email (BR-17); it holds no reference, note, receiver or recorder. A payment with a receipt number
/// (customer-invoice-payments BR-12) uses the receipt layout and subject.
/// </summary>
public sealed record PaymentReceiptData(
    Guid PaymentId,
    string RecipientEmail,
    string OrganizationName,
    string? OrganizationPhone,
    string PaymentNumber,
    decimal Amount,
    string Currency,
    DateOnly PaidDate,
    string MethodLabel,
    string InvoiceNumber,
    decimal RemainingBalance,
    string? ReceiptNumber = null);

public enum PaymentReceiptStatus
{
    Sent,
    Failed,
}

/// <summary>The store result of a record: no receipt data when nothing changed or no receipt was requested and possible.</summary>
public sealed record PaymentRecorded(bool Changed, PaymentRow Payment, PaymentInvoiceSummary Invoice, PaymentReceiptData? Receipt);

public sealed record PaymentRecordResponse(bool Changed, string EmailStatus, PaymentRow Payment, PaymentInvoiceSummary Invoice);

/// <summary>Read port of the hub. Every call starts from the session organization and branch scope (BR-02); null-like outcomes are one identical "not found".</summary>
public interface IInvoiceHubStore
{
    Task<HubOptions> GetOptionsAsync(BillingActor actor, bool canAct, CancellationToken cancellationToken);

    Task<IReadOnlyList<NamedOption>> GetCustomersAsync(BillingActor actor, string? search, CancellationToken cancellationToken);

    Task<BillingOutcome<HubOverview>> GetOverviewAsync(BillingActor actor, Guid? branchId, DateTimeOffset now, CancellationToken cancellationToken);

    Task<BillingOutcome<InvoicePage>> GetInvoicesAsync(
        BillingActor actor, InvoiceQuery query, bool canAct, DateTimeOffset now, CancellationToken cancellationToken);

    Task<BillingOutcome<InvoiceExport>> ExportInvoicesAsync(
        BillingActor actor, InvoiceQuery query, DateTimeOffset now, CancellationToken cancellationToken);

    Task<BillingOutcome<PaymentPage>> GetPaymentsAsync(
        BillingActor actor, PaymentQuery query, DateTimeOffset now, CancellationToken cancellationToken);

    Task<BillingOutcome<PaymentExport>> ExportPaymentsAsync(
        BillingActor actor, PaymentQuery query, DateTimeOffset now, CancellationToken cancellationToken);
}

/// <summary>Write port: records one external payment inside one transaction (BR-12 to BR-14).</summary>
public interface IInvoicePaymentStore
{
    Task<BillingOutcome<PaymentRecorded>> RecordAsync(
        BillingActor actor, Guid invoiceId, PaymentInput input, DateTimeOffset now, CancellationToken cancellationToken);
}

public interface IPaymentReceiptNotifier
{
    Task<PaymentReceiptStatus> SendAsync(PaymentReceiptData receipt, CancellationToken cancellationToken);
}
