namespace FieldOps.Application.Features.Search;

/// <summary>The five searchable entity types and their API codes (global-search BR-08).</summary>
public static class SearchEntityTypes
{
    public const string Customer = "customer";

    public const string ServiceRequest = "service_request";

    public const string Quote = "quote";

    public const string WorkOrder = "work_order";

    public const string Invoice = "invoice";
}

/// <summary>Per-type eligibility of the caller (global-search BR-03), computed by the API from the existing read policies.</summary>
public sealed record SearchEligibility(bool Customer, bool ServiceRequest, bool Quote, bool WorkOrder, bool Invoice);

/// <summary>A validated request: the trimmed query and the item limit (global-search BR-05).</summary>
public sealed record SearchRequest(string Query, int Limit);

/// <summary>The organization document prefixes and the active branch count, read once per search.</summary>
public sealed record SearchOrganization(
    string RequestPrefix, string QuotePrefix, string WorkOrderPrefix, string InvoicePrefix, int ActiveBranchCount);

/// <summary>
/// The query terms handed to the store: the trimmed text, its LIKE-escaped prefix and contains patterns, and the digits
/// of a document number per type (null when <c>q</c> is not a number for that type).
/// </summary>
public sealed record SearchTerms(
    string Text,
    string PrefixPattern,
    string ContainsPattern,
    string? RequestDigits,
    string? QuoteDigits,
    string? WorkOrderDigits,
    string? InvoiceDigits);

/// <summary>
/// One matched entity as read by the store: raw projected columns, so the Application layer owns the titles, numbers and
/// status codes. <c>Rank</c> is 1 exact, 2 prefix, 3 partial.
/// </summary>
public sealed record SearchHit(
    string EntityType,
    Guid Id,
    int Rank,
    DateTimeOffset UpdatedAt,
    long? Number,
    string Status,
    Guid? BranchId,
    string? BranchName,
    string? Name,
    string? Alternate,
    string? CustomerName,
    string? GuestName,
    long? RequestNumber,
    decimal? Total,
    string? Currency,
    DateOnly? DueDate);

/// <summary>The kept hits (at most 5 per type) and whether any type had more matches than it kept.</summary>
public sealed record SearchMatches(IReadOnlyList<SearchHit> Hits, bool Overflow);

public sealed record SearchItemView(
    string EntityType,
    Guid EntityId,
    string? DisplayNumber,
    string Title,
    string? Subtitle,
    string? Status,
    Guid? BranchId,
    string? BranchName,
    DateTimeOffset UpdatedAt);

public sealed record GlobalSearchView(string Query, IReadOnlyList<SearchItemView> Items, bool HasMore);
