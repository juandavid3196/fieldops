using FieldOps.Application.Features.Access;

namespace FieldOps.Application.Features.Quotes;

/// <summary>quotes-management: the property block of a quote row.</summary>
public sealed record QuoteRowProperty(string Name, string? PostalCode, string? City);

public sealed record QuoteRowRequest(Guid Id, string DisplayNumber, string Title);

public sealed record QuoteRowBranch(Guid Id, string Name);

/// <summary>API contract QuoteRow. <c>Status</c> is the BR-03 code; total, currency and dates come from the shown version (BR-04).</summary>
public sealed record QuoteRow(
    Guid Id,
    string DisplayNumber,
    int RevisionNo,
    bool RevisionInProgress,
    string CustomerName,
    QuoteRowProperty? Property,
    QuoteRowRequest Request,
    QuoteRowBranch? Branch,
    decimal Total,
    string Currency,
    string Status,
    DateTimeOffset? SentAt,
    DateOnly? ValidUntil,
    DateTimeOffset? RespondedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset CreatedAt,
    Guid? WorkOrderId,
    IReadOnlyList<string> Actions);

public sealed record QuoteTabCounts(int All, int Drafts, int Sent, int Approved, int Closed);

public sealed record QuotesPage(IReadOnlyList<QuoteRow> Items, int Total, QuoteTabCounts Counts);

public sealed record QuotesOptionBranch(Guid Id, string Name);

public sealed record QuotesOptions(
    IReadOnlyList<QuotesOptionBranch> Branches,
    string OrganizationTimezone,
    string Currency,
    bool CanManage,
    bool CanManageWorkOrders,
    bool CanReadWorkOrders);

public sealed record QuotesMetric(int Count, decimal Amount);

public sealed record QuotesAttentionMetric(int Count);

public sealed record QuotesSummary(
    string Currency,
    QuotesMetric Drafts,
    QuotesMetric AwaitingResponse,
    QuotesMetric ApprovedThisMonth,
    QuotesAttentionMetric NeedsAttention,
    int ReadyRequests);

/// <summary>The raw query values of <c>GET /quotes</c>, validated by the handler.</summary>
public sealed record QuotesListRequest(
    string? Tab,
    string? Q,
    string? BranchId,
    string? Status,
    string? Date,
    string? Sort,
    int? Page,
    int? PageSize);

/// <summary>The caller permissions that only shape flags and row actions (BR-01).</summary>
public sealed record QuotesPermissions(bool CanManage, bool CanManageWorkOrders, bool CanReadWorkOrders);

/// <summary>A validated list request. <c>BranchId</c> is already authorized against the caller scope.</summary>
public sealed record QuotesFilter(
    QuotesTab Tab,
    string? Search,
    Guid? BranchId,
    QuoteDisplayStatus? Status,
    QuotesDateRange Date,
    QuotesSort Sort,
    int Page,
    int PageSize);

/// <summary>Persistence port of the quotes workspace. Every method is scoped to one organization and the caller's branch scope.</summary>
public interface IQuotesStore
{
    /// <summary>True when the branch belongs to the organization, is active and is inside the scope.</summary>
    Task<bool> BranchAvailableAsync(Guid organizationId, BranchScope scope, Guid branchId, CancellationToken cancellationToken);

    Task<QuotesPage> ListAsync(
        Guid organizationId, BranchScope scope, QuotesFilter filter, QuotesPermissions permissions, CancellationToken cancellationToken);

    Task<QuotesSummary> SummaryAsync(
        Guid organizationId, BranchScope scope, Guid? branchId, CancellationToken cancellationToken);

    Task<QuotesOptions> OptionsAsync(
        Guid organizationId, BranchScope scope, QuotesPermissions permissions, CancellationToken cancellationToken);
}
