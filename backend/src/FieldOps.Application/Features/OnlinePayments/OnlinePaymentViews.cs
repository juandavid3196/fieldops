using System.Text.Json.Serialization;
using FieldOps.Application.Features.InvoicePayments;
using FieldOps.Application.Features.QuoteLinks;

namespace FieldOps.Application.Features.OnlinePayments;

/// <summary>Machine codes, messages and limits of the customer invoice payments (customer-invoice-payments BR-06 to BR-25).</summary>
public static class OnlinePaymentMessages
{
    public const string NotPayableCode = "invoice_not_payable";

    public const string NotPayableTitle = "This invoice can't be paid online.";

    public const string CardUnavailableCode = "card_unavailable";

    public const string CardUnavailableTitle = "Card payments aren't available right now.";

    public const string PaymentInProgressCode = "payment_in_progress";

    public const string PaymentInProgressTitle = "A payment is already in progress for this invoice.";

    /// <summary>The 409 of an external payment while a card attempt is pending (BR-31 f).</summary>
    public const string ExternalPaymentInProgressTitle = "An online card payment is in progress for this invoice. Try again in a few minutes.";

    public const string IdempotencyConflictCode = "idempotency_conflict";

    public const string IdempotencyConflictTitle = "This request was already used for different details.";

    public const string BankUnavailableCode = "bank_transfer_unavailable";

    public const string BankUnavailableTitle = "Bank transfer isn't available for this invoice.";

    public const string ReviewNotAvailableCode = "review_not_available";

    public const string ReviewNotAvailableTitle = "This invoice can't be reviewed yet.";

    public const string ReviewExistsCode = "review_exists";

    public const string ReviewExistsTitle = "A review was already submitted.";

    public const string ProviderUnavailableCode = "payment_provider_unavailable";

    public const string ProviderUnavailableTitle = "The payment provider is unavailable.";

    public const string KeyInvalid = "Try again.";

    public const string IdInvalid = "Enter a valid id.";

    public const string RatingInvalid = "Select a rating.";

    public const string CommentTooLong = "Use 500 characters or fewer.";

    public const int MaxCommentLength = 500;

    public const string ReceiptContentType = "application/pdf";
}

/// <summary>The outcome of a public payment operation; every unusable token or foreign id is the one identical <see cref="NotFound"/>.</summary>
public abstract record PublicOutcome<T>
{
    private PublicOutcome()
    {
    }

    /// <summary>200, or 201 when <paramref name="Created"/>.</summary>
    public sealed record Ok(T Value, bool Created = false) : PublicOutcome<T>;

    public sealed record NotFound : PublicOutcome<T>;

    public sealed record Invalid(IReadOnlyDictionary<string, string[]> Errors) : PublicOutcome<T>;

    public sealed record Conflict(string Code, string Title) : PublicOutcome<T>;

    public sealed record RateLimited(TimeSpan RetryAfter) : PublicOutcome<T>;

    /// <summary>502 payment_provider_unavailable.</summary>
    public sealed record ProviderUnavailable : PublicOutcome<T>;
}

public sealed record PublicTimeline(DateOnly? ServiceCompletedOn, DateOnly? SentOn, DateOnly? PaidOn, DateOnly? ReceiptOn);

public sealed record PublicService(
    string Title,
    string WorkOrderNumber,
    string? CompletionNote,
    string? TechnicianName,
    DateOnly? ServiceDate,
    bool HasCompletionReport,
    int PhotoCount);

public sealed record PublicCardOption(bool Available, string? PublishableKey);

/// <summary>The bank transfer instructions (BR-04); only <c>available</c> is written while the details are not configured.</summary>
public sealed record PublicBankOption(
    bool Available,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? BankName = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AccountNumber = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? RoutingNumber = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Reference = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? Amount = null);

public sealed record PublicCashOption(string? Phone, string? Email);

public sealed record PublicPaymentOptions(PublicCardOption Card, PublicBankOption BankTransfer, PublicCashOption Cash);

public sealed record PublicActiveAttempt(Guid AttemptId, string Status);

/// <summary>A payment of the public invoice (BR-05): the gross amount and the refunded amount; never reference, note or recorder.</summary>
public sealed record PublicPaymentItem(
    Guid PaymentId,
    string Number,
    string? ReceiptNumber,
    DateOnly PaidOn,
    string Method,
    string MethodLabel,
    decimal Amount,
    decimal RefundedAmount,
    string Status);

public sealed record PublicReviewState(bool Available, bool Submitted);

public sealed record CardIntentView(Guid AttemptId, string? ClientSecret, decimal Amount, string Currency, string Status);

public sealed record PaymentStatusInvoice(string Status, decimal AmountPaid, decimal BalanceDue);

public sealed record PaymentStatusView(
    Guid AttemptId, string Status, string? FailureCategory, PublicPaymentItem? Payment, PaymentStatusInvoice Invoice);

public sealed record BankNoticeView(bool Changed, DateOnly ReportedOn);

public sealed record ReviewSubmittedView(bool Submitted);

public sealed record InvoicePhoto(Guid PhotoId, string Type, string? Caption, DateOnly TakenOn);

/// <summary>The invoice and organization of a valid token; used only inside the server.</summary>
public sealed record InvoiceRef(Guid OrganizationId, Guid InvoiceId);

/// <summary>A pending card attempt past <c>expires_at</c> (BR-14).</summary>
public sealed record ExpiredAttempt(Guid AttemptId, string? ProviderPaymentIntentId);

/// <summary>An existing or new card attempt as phase A of the card intent returns it (BR-06).</summary>
public sealed record CardAttemptFacts(Guid AttemptId, string Status, decimal Amount, string Currency, string? ProviderPaymentIntentId, Guid OrganizationId, Guid InvoiceId);

public abstract record CardIntentPrepared
{
    private CardIntentPrepared()
    {
    }

    public sealed record Unavailable : CardIntentPrepared;

    public sealed record Rejected(string Code, string Title) : CardIntentPrepared;

    public sealed record Replay(CardAttemptFacts Attempt) : CardIntentPrepared;

    public sealed record Created(CardAttemptFacts Attempt) : CardIntentPrepared;
}

/// <summary>The data of the organization notice of a reported bank transfer (BR-17).</summary>
public sealed record BankTransferNoticeData(
    string RecipientEmail, string OrganizationName, string InvoiceNumber, decimal Amount, string Currency, DateOnly ReportedOn);

public abstract record BankNoticeResult
{
    private BankNoticeResult()
    {
    }

    public sealed record Unavailable : BankNoticeResult;

    public sealed record Rejected(string Code, string Title) : BankNoticeResult;

    /// <summary>The notice exists; <c>Email</c> is set only for a new one and an organization with an email.</summary>
    public sealed record Reported(bool Changed, DateOnly ReportedOn, BankTransferNoticeData? Email) : BankNoticeResult;
}

public abstract record ReviewResult
{
    private ReviewResult()
    {
    }

    public sealed record Unavailable : ReviewResult;

    public sealed record NotAvailable : ReviewResult;

    public sealed record Exists : ReviewResult;

    public sealed record Created : ReviewResult;
}

/// <summary>The source of the receipt PDF (BR-19): stored data of one payment of the token invoice.</summary>
public sealed record ReceiptSource(
    string OrganizationName,
    IReadOnlyList<string> OrganizationLines,
    byte[]? Logo,
    string? LogoContentType,
    string ReceiptNumber,
    string PaymentNumber,
    DateOnly PaidOn,
    IReadOnlyList<string> BillToLines,
    string InvoiceNumber,
    string MethodLabel,
    string Currency,
    decimal Amount,
    decimal RefundedAmount,
    decimal InvoiceTotal,
    decimal TotalPaid,
    decimal BalanceDue);

public sealed record CompletionChecklistItem(string Label, bool Completed);

/// <summary>The source of the completion report PDF (BR-20); it holds no signature image, notes, incidents, materials or costs.</summary>
public sealed record CompletionReportSource(
    string OrganizationName,
    IReadOnlyList<string> OrganizationLines,
    byte[]? Logo,
    string? LogoContentType,
    string WorkOrderNumber,
    string WorkOrderTitle,
    string? ServiceAddress,
    DateOnly CompletedOn,
    string? TechnicianName,
    string? CompletionSummary,
    IReadOnlyList<CompletionChecklistItem> Checklist,
    string? AcknowledgementLabel,
    string? SignerName,
    DateOnly? SignedOn);

public sealed record DocumentFile(string FileName, string ContentType, byte[] Content);

public enum WebhookOutcome
{
    Applied,
    NoEffect,
    Ignored,
    NeedsAttention,
    Duplicate,
}

/// <summary>The attempt of an event as the non-locking lookup sees it (webhook pre-step).</summary>
public sealed record WebhookAttemptLookup(Guid AttemptId, string Status);

/// <summary>Provider answers fetched before the webhook transaction: the cancellation of a failed intent and the card details of a success.</summary>
public sealed record WebhookExtras(GatewayCancelResult? Cancel, GatewayCardDetails? Card);

public sealed record WebhookResult(WebhookOutcome Outcome, PaymentReceiptData? Receipt);

/// <summary>
/// Persistence port of the customer payment flow. Every public call starts from the token row: the organization and invoice
/// never come from the client. Lock order of every transaction: attempt row, invoice row, organization row; an attempt row
/// is never locked while the invoice lock is held by the same transaction (expiry runs before the invoice lock).
/// </summary>
public interface IOnlinePaymentStore
{
    /// <summary>The organization and invoice of a valid token (BR-01), or null.</summary>
    Task<InvoiceRef?> ResolveAsync(string token, CancellationToken cancellationToken);

    /// <summary>Pending card attempts of the invoice that passed <c>expires_at</c>; read without locks.</summary>
    Task<IReadOnlyList<ExpiredAttempt>> FindExpiredAsync(Guid organizationId, Guid invoiceId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary><c>UPDATE ... WHERE status = 'pending'</c> plus the audit row in one short transaction; false when it was no longer pending.</summary>
    Task<bool> MarkFailedIfPendingAsync(Guid attemptId, string category, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Phase A of the card intent (BR-06 steps 1 to 5): one transaction under the invoice lock.</summary>
    Task<CardIntentPrepared> PrepareCardIntentAsync(string token, Guid idempotencyKey, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Phase C: stores the provider intent id only while the attempt has none.</summary>
    Task StoreIntentAsync(Guid attemptId, string intentId, DateTimeOffset now, CancellationToken cancellationToken);

    Task<PaymentStatusView?> GetStatusAsync(string token, Guid attemptId, CancellationToken cancellationToken);

    Task<BankNoticeResult> ReportBankTransferAsync(string token, Guid idempotencyKey, DateTimeOffset now, CancellationToken cancellationToken);

    Task<ReviewResult> SubmitReviewAsync(string token, int rating, string? comment, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>The attempt an event refers to (intent id, then matching metadata), read without locks.</summary>
    Task<WebhookAttemptLookup?> FindAttemptAsync(GatewayEvent gatewayEvent, CancellationToken cancellationToken);

    /// <summary>The whole webhook transaction (BR-10): the event row first, then the effects; any failure rolls everything back and throws.</summary>
    Task<WebhookResult> ApplyWebhookAsync(GatewayEvent gatewayEvent, WebhookExtras extras, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Sets <c>receipt_sent_at</c> after the email went out; a failure is logged by payment id and never thrown.</summary>
    Task MarkReceiptSentAsync(Guid paymentId, DateTimeOffset now, CancellationToken cancellationToken);
}

public enum InvoiceAction
{
    CardIntent,
    BankTransferNotice,
    Review,
}

/// <summary>Per invoice limits of the public actions (BR-23): card intent 10, bank notice 3 and review 3 per hour.</summary>
public interface IInvoiceActionThrottle
{
    /// <summary>Counts the request and returns the time until the invoice may try again, or null when it is allowed.</summary>
    TimeSpan? TryAcquire(InvoiceAction action, Guid invoiceId);
}

public static class InvoiceActionLimits
{
    public static readonly TimeSpan Window = TimeSpan.FromHours(1);

    public static int Limit(InvoiceAction action) => action == InvoiceAction.CardIntent ? 10 : 3;
}

public interface IBankTransferNotifier
{
    /// <summary>Emails the organization after the commit; a failure is logged and never reaches the caller.</summary>
    Task SendAsync(BankTransferNoticeData notice, CancellationToken cancellationToken);
}
