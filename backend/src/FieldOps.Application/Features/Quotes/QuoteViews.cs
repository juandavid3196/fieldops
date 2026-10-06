using System.Net;
using FieldOps.Application.Features.Access;
using FieldOps.Application.Features.ServiceRequests;

namespace FieldOps.Application.Features.Quotes;

/// <summary>The authenticated caller of a quote mutation: session organization, user, branch scope and address.</summary>
public sealed record QuoteActor(Guid OrganizationId, Guid UserId, BranchScope Scope, IPAddress? IpAddress);

public sealed record QuoteRequestRef(Guid Id, string DisplayNumber, string Title, string? Category, string Status);

public sealed record QuoteCustomerRef(Guid? Id, string Name, string? Phone, string? Email, string? Address);

public sealed record QuoteRecipient(string? Email);

public sealed record QuoteOrganizationRef(string Name, string Currency, decimal DefaultTaxRate);

public sealed record QuoteTermsView(string Preset, string? CustomText);

public sealed record QuoteDraftLineView(
    Guid? CatalogItemId,
    string Type,
    string Name,
    string Description,
    decimal Quantity,
    string Unit,
    decimal UnitPrice,
    decimal UnitCost,
    bool Taxable,
    bool IsOptional);

/// <summary>The mutable version (API contracts QuoteDetail.draft): the DraftBody fields plus the calculation.</summary>
public sealed record QuoteDraftView(
    int VersionNo,
    IReadOnlyList<QuoteDraftLineView> Lines,
    decimal DiscountTotal,
    string? CustomerMessage,
    string? InternalNote,
    QuoteTermsView Terms,
    DateOnly ValidUntil,
    QuoteCalculation Calculation);

public sealed record QuoteSentVersion(int VersionNo, DateTimeOffset SentAt, decimal Total, bool IsCurrent);

public sealed record QuoteResponseLine(string Name, decimal LineSubtotal);

public sealed record QuoteResponseTotals(decimal Subtotal, decimal DiscountTotal, decimal TaxTotal, decimal Total);

/// <summary>A customer response of a sent version (customer-quote-approval BR-23); <c>Type</c> is approved, rejected or clarification_requested.</summary>
public sealed record QuoteResponseView(
    int VersionNo,
    string Type,
    DateTimeOffset RespondedAt,
    string ResponderName,
    string? Comment,
    IReadOnlyList<QuoteResponseLine> SelectedOptionalLines,
    QuoteResponseTotals? Totals);

/// <summary>Internal quote read (API contracts QuoteDetail); margin, unit cost and internal note appear only here.</summary>
public sealed record QuoteDetail(
    Guid Id,
    long Number,
    string DisplayNumber,
    string Status,
    string DisplayStatus,
    DateTimeOffset UpdatedAt,
    bool CanManage,
    QuoteRequestRef Request,
    QuoteCustomerRef Customer,
    QuoteRecipient Recipient,
    DetailCompletedAssessment? CompletedAssessment,
    QuoteOrganizationRef Organization,
    QuoteDraftView? Draft,
    IReadOnlyList<QuoteSentVersion> SentVersions,
    IReadOnlyList<QuoteResponseView> Responses);

public sealed record QuoteVersionLine(
    string Type,
    string Name,
    string Description,
    decimal Quantity,
    string Unit,
    decimal UnitPrice,
    decimal UnitCost,
    decimal TaxRate,
    decimal LineSubtotal,
    decimal LineTax,
    decimal LineTotal,
    bool IsOptional);

/// <summary>A sent version read-only (API contracts Version), derived from the frozen rows.</summary>
public sealed record QuoteVersionView(
    int VersionNo,
    DateTimeOffset SentAt,
    string Scope,
    string? CustomerMessage,
    string? InternalNote,
    string? Terms,
    DateOnly? ValidUntil,
    string Currency,
    IReadOnlyList<QuoteVersionLine> Lines,
    decimal Subtotal,
    decimal DiscountTotal,
    string TaxLabel,
    decimal TaxTotal,
    decimal Total,
    QuoteMargin Margin);

public sealed record QuoteCreated(QuoteDetail Detail, bool Created);

public sealed record QuoteDiscarded(Guid QuoteId, string Status, Guid RequestId);

/// <summary>What the store returns after committing a send or a resend: the data of the email, without message or link.</summary>
public sealed record QuoteEmailData(
    Guid QuoteId,
    int VersionNo,
    string RecipientEmail,
    string OrganizationName,
    string? OrganizationPhone,
    string DisplayNumber,
    string RequestTitle,
    decimal Total,
    string Currency,
    DateOnly ValidUntil);

public sealed record QuoteSent(QuoteDetail Detail, QuoteEmailData Email);

/// <summary>The email after the commit: the data, the editable message and the link holding the raw token.</summary>
public sealed record QuoteEmail(QuoteEmailData Data, string Message, string Link);

public enum QuoteEmailStatus
{
    Sent,
    Failed,
}

public sealed record QuoteSendResponse(QuoteDetail Quote, string EmailStatus);

/// <summary>A quote visible to the caller: the organization time zone the validation needs.</summary>
public sealed record QuoteContext(Guid QuoteId, string Timezone);

public abstract record QuoteOutcome<T>
{
    private QuoteOutcome()
    {
    }

    public sealed record Succeeded(T Value) : QuoteOutcome<T>;

    /// <summary>A missing, foreign or out-of-scope quote or request: one identical outcome.</summary>
    public sealed record NotFound : QuoteOutcome<T>;

    public sealed record Conflict(string Code) : QuoteOutcome<T>;

    public sealed record Invalid(IReadOnlyDictionary<string, string[]> Errors) : QuoteOutcome<T>;
}

/// <summary>Persistence port of the quote builder. Every method is scoped to one organization and the caller's branch scope.</summary>
public interface IQuoteStore
{
    /// <summary>The quote when visible (non-locking), otherwise null: lets handlers answer 404 before field validation.</summary>
    Task<QuoteContext?> GetContextAsync(Guid organizationId, BranchScope scope, Guid quoteId, CancellationToken cancellationToken);

    Task<QuoteDetail?> GetAsync(
        Guid organizationId, BranchScope scope, Guid quoteId, bool canManage, CancellationToken cancellationToken);

    /// <summary>A sent version, otherwise null.</summary>
    Task<QuoteVersionView?> GetVersionAsync(
        Guid organizationId, BranchScope scope, Guid quoteId, int versionNo, CancellationToken cancellationToken);

    Task<QuoteOutcome<QuoteCreated>> CreateAsync(QuoteActor actor, Guid requestId, CancellationToken cancellationToken);

    Task<QuoteOutcome<QuoteCalculation>> CalculateAsync(
        QuoteActor actor, Guid quoteId, QuoteDraft draft, CancellationToken cancellationToken);

    Task<QuoteOutcome<QuoteDetail>> SaveDraftAsync(
        QuoteActor actor, Guid quoteId, DateTimeOffset updatedAt, QuoteDraft draft, CancellationToken cancellationToken);

    /// <summary>Saves, freezes and sends; only the hash of the raw token reaches the store.</summary>
    Task<QuoteOutcome<QuoteSent>> SendAsync(
        QuoteActor actor,
        Guid quoteId,
        DateTimeOffset updatedAt,
        QuoteDraft draft,
        string tokenHash,
        CancellationToken cancellationToken);

    Task<QuoteOutcome<QuoteDetail>> ReviseAsync(
        QuoteActor actor, Guid quoteId, DateTimeOffset updatedAt, CancellationToken cancellationToken);

    Task<QuoteOutcome<QuoteDiscarded>> DiscardDraftAsync(
        QuoteActor actor, Guid quoteId, DateTimeOffset updatedAt, CancellationToken cancellationToken);

    Task<QuoteOutcome<QuoteSent>> ResendEmailAsync(
        QuoteActor actor, Guid quoteId, DateTimeOffset updatedAt, string tokenHash, CancellationToken cancellationToken);
}

/// <summary>Builds the public quote link from the raw token (BR-27).</summary>
public interface IQuoteLinkBuilder
{
    string BuildLink(string rawToken);
}

/// <summary>Sends the quote email after the commit (BR-26); implementations never throw for a delivery failure.</summary>
public interface IQuoteNotifier
{
    Task<QuoteEmailStatus> SendAsync(QuoteEmail email, CancellationToken cancellationToken);
}

/// <summary>The API codes of the quote status enum (<c>clarification_requested</c> and the rest).</summary>
public static class QuoteStatusCodes
{
    public static string Code(FieldOps.Domain.Quotes.QuoteStatus status) => status switch
    {
        FieldOps.Domain.Quotes.QuoteStatus.Draft => "draft",
        FieldOps.Domain.Quotes.QuoteStatus.Sent => "sent",
        FieldOps.Domain.Quotes.QuoteStatus.Approved => "approved",
        FieldOps.Domain.Quotes.QuoteStatus.Rejected => "rejected",
        FieldOps.Domain.Quotes.QuoteStatus.ClarificationRequested => "clarification_requested",
        FieldOps.Domain.Quotes.QuoteStatus.Expired => "expired",
        FieldOps.Domain.Quotes.QuoteStatus.Cancelled => "cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };
}
