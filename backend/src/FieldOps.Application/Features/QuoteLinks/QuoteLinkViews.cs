using System.Net;
using FieldOps.Application.Features.PortalAccess;

namespace FieldOps.Application.Features.QuoteLinks;

/// <summary>Customer-quote-approval API contracts: the totals of a version, with or without optional lines selected.</summary>
public sealed record PublicTotals(
    decimal Subtotal,
    decimal DiscountTotal,
    string TaxLabel,
    decimal TaxTotal,
    decimal Total,
    string Currency);

public sealed record PublicOrganization(string Name, string? Phone, bool HasLogo);

public sealed record PublicQuoteInfo(
    string DisplayNumber,
    int VersionNo,
    string Status,
    DateOnly SentOn,
    DateOnly ValidUntil,
    string Scope,
    string? CustomerMessage,
    string? Terms);

public sealed record PublicCustomer(string Name, string? Address);

/// <summary>A customer-facing line; <see cref="Id"/> is set only for optional lines (the only ones the customer can select).</summary>
public sealed record PublicLine(
    Guid? Id,
    string Name,
    string Description,
    decimal Quantity,
    string Unit,
    decimal UnitPrice,
    decimal LineSubtotal,
    bool IsOptional);

public sealed record PublicPhoto(Guid Id);

public sealed record PublicProgress(DateOnly RequestSubmittedOn, DateOnly? AssessmentCompletedOn);

public sealed record PublicClarification(DateOnly AskedOn);

public sealed record PublicResponse(
    string Type,
    DateOnly RespondedOn,
    IReadOnlyList<Guid> SelectedOptionalLineIds,
    PublicTotals? Totals);

/// <summary>The public read of one sent version (BR-05). It never carries ids of the organization, quote or version.</summary>
public sealed record PublicQuote(
    PublicOrganization Organization,
    PublicQuoteInfo Quote,
    PublicCustomer Customer,
    IReadOnlyList<string> ScopeItems,
    IReadOnlyList<PublicLine> Lines,
    PublicTotals VersionTotals,
    IReadOnlyList<PublicPhoto> Photos,
    PublicProgress Progress,
    PublicClarification? Clarification,
    PublicResponse? Response);

/// <summary>An image served through a token-protected endpoint (BR-07, BR-08).</summary>
public sealed record PublicBinary(string ContentType, byte[] Content);

/// <summary>The connection of a public caller: the remote address and the truncated user agent (BR-13).</summary>
public sealed record QuoteLinkCaller(IPAddress? IpAddress, string? UserAgent);

public abstract record QuoteLinkOutcome<T>
{
    private QuoteLinkOutcome()
    {
    }

    public sealed record Succeeded(T Value) : QuoteLinkOutcome<T>;

    /// <summary>Missing, malformed, unknown, revoked, cancelled or expired token, or a resource that is not the token's (BR-03): one identical outcome.</summary>
    public sealed record Unavailable : QuoteLinkOutcome<T>;

    /// <summary>The token belongs to an older version than the quote's current sent one (BR-04).</summary>
    public sealed record Superseded : QuoteLinkOutcome<T>;

    /// <summary>A different action after approval or decline (BR-16).</summary>
    public sealed record AlreadyAnswered : QuoteLinkOutcome<T>;

    public sealed record Invalid(IReadOnlyDictionary<string, string[]> Errors) : QuoteLinkOutcome<T>;

    /// <summary>Portal only: the quote is shown read-only as expired and its actions are refused (customer portal BR-30).</summary>
    public sealed record Expired : QuoteLinkOutcome<T>;
}

/// <summary>
/// Persistence port of the public quote link. The raw token is the only credential: the organization, quote and
/// version always come from the token row. Responses are idempotent and serialized on the quote row.
/// </summary>
public interface IQuoteLinkStore
{
    Task<QuoteLinkOutcome<PublicQuote>> ViewAsync(ResourceAccess access, CancellationToken cancellationToken);

    Task<QuoteLinkOutcome<PublicTotals>> CalculateAsync(
        ResourceAccess access, IReadOnlyList<Guid> selectedOptionalLineIds, CancellationToken cancellationToken);

    Task<QuoteLinkOutcome<PublicQuote>> ApproveAsync(
        ResourceAccess access, IReadOnlyList<Guid> selectedOptionalLineIds, QuoteLinkCaller caller, CancellationToken cancellationToken);

    Task<QuoteLinkOutcome<PublicQuote>> DeclineAsync(
        ResourceAccess access, string reason, QuoteLinkCaller caller, CancellationToken cancellationToken);

    Task<QuoteLinkOutcome<PublicQuote>> AskAsync(
        ResourceAccess access, string message, QuoteLinkCaller caller, CancellationToken cancellationToken);

    Task<QuoteLinkOutcome<PublicBinary>> GetPhotoAsync(ResourceAccess access, Guid photoId, CancellationToken cancellationToken);

    Task<QuoteLinkOutcome<PublicBinary>> GetLogoAsync(ResourceAccess access, CancellationToken cancellationToken);
}

/// <summary>Renders the PDF of a composed document (BR-19); the implementation lives in Infrastructure.</summary>
public interface IQuotePdfRenderer
{
    byte[] Render(QuotePdfDocument document);
}
