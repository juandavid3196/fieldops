namespace FieldOps.Application.Features.Customers;

/// <summary>
/// The module read permissions of the caller, resolved by the Api layer from the existing role policies
/// (requests/quotes: ServiceRequestView, jobs: WorkOrderRead, invoices: BillingReviewRead). Handlers never re-authorize.
/// </summary>
public sealed record CustomerRecordAccess(bool Requests, bool Jobs, bool Invoices)
{
    public static CustomerRecordAccess Everything { get; } = new(true, true, true);
}

/// <summary>A validated list query (BR-04): trimmed search, a status code of the tab's set, page and page size.</summary>
public sealed record CustomerRecordQuery(string? Search, string? Status, int Page, int PageSize)
{
    public int Skip => (Page - 1) * PageSize;
}

/// <summary>Raw list query text as received.</summary>
public sealed record CustomerRecordQueryText(string? Search, string? Status, string? Page, string? PageSize);

/// <summary>One page of a tab plus the visible total of the customer (BR-04).</summary>
public sealed record CustomerRecordSlice<T>(IReadOnlyList<T> Items, int TotalCount, int CustomerTotal);

public sealed record CustomerRequestRow(
    Guid Id, string Number, string Service, string? PropertyName, DateTimeOffset CreatedAt, string Status);

public sealed record CustomerRequestsPage(
    IReadOnlyList<CustomerRequestRow> Items, int TotalCount, int CustomerTotal, int Page, int PageSize, string Timezone);

public sealed record CustomerQuoteRequestRef(Guid Id, string Number, string Service);

public sealed record CustomerQuoteRow(
    Guid Id, string Number, CustomerQuoteRequestRef Request, decimal? Total, DateOnly? ValidUntil, string Status);

public sealed record CustomerQuotesPage(
    IReadOnlyList<CustomerQuoteRow> Items,
    int TotalCount,
    int CustomerTotal,
    int Page,
    int PageSize,
    string Currency,
    string Timezone);

public sealed record CustomerJobTechnician(string Name, string Initials);

public sealed record CustomerJobRow(
    Guid Id,
    string Number,
    string Title,
    string PropertyName,
    DateTimeOffset? NextVisitAt,
    DateTimeOffset? CompletedAt,
    CustomerJobTechnician? Technician,
    string Status);

public sealed record CustomerJobsPage(
    IReadOnlyList<CustomerJobRow> Items, int TotalCount, int CustomerTotal, int Page, int PageSize, string Timezone);

public sealed record CustomerInvoiceJobRef(Guid Id, string Number);

public sealed record CustomerInvoiceRow(
    Guid Id, string Number, CustomerInvoiceJobRef Job, DateOnly? IssueDate, DateOnly? DueDate, decimal BalanceDue, string Status);

public sealed record CustomerInvoicesSummary(decimal Outstanding, decimal Overdue, decimal Paid);

public sealed record CustomerInvoiceSlice(
    IReadOnlyList<CustomerInvoiceRow> Items, int TotalCount, int CustomerTotal, CustomerInvoicesSummary Summary);

public sealed record CustomerInvoicesPage(
    IReadOnlyList<CustomerInvoiceRow> Items,
    CustomerInvoicesSummary Summary,
    int TotalCount,
    int CustomerTotal,
    int Page,
    int PageSize,
    string Currency,
    string Timezone);

public sealed record CustomerActivityOrigin(string Kind, string? ActorName);

public sealed record CustomerActivitySubject(
    string? Number,
    string? Title,
    string? Service,
    decimal? Amount,
    IReadOnlyList<string>? InvoiceNumbers,
    string? ContactName,
    string? PropertyName,
    DateTimeOffset? ScheduledAt,
    int? VisitNumber,
    string? NewStatus)
{
    public static CustomerActivitySubject Empty { get; } = new(null, null, null, null, null, null, null, null, null, null);
}

public sealed record CustomerActivityLink(string Kind, Guid Id);

/// <summary>One whitelisted audit event (BR-10, BR-11): never before/after data, metadata values, IP, message or note text.</summary>
public sealed record CustomerActivityEvent(
    string Id,
    string Type,
    string Action,
    DateTimeOffset OccurredAt,
    CustomerActivityOrigin Origin,
    CustomerActivitySubject Subject,
    CustomerActivityLink? Link);

public sealed record CustomerActivityResponse(
    IReadOnlyList<CustomerActivityEvent> Items,
    IReadOnlyList<string> AvailableTypes,
    string? NextCursor,
    string Currency,
    string Timezone);

/// <summary>A validated activity query (BR-12).</summary>
public sealed record CustomerActivityQuery(
    IReadOnlyList<string> Types, DateTimeOffset? Since, CustomerActivityCursor? Before);

public sealed record CustomerActivityCursor(DateTimeOffset OccurredAt, long Id);

/// <summary>One block of events plus the cursor of the next older block, null when none exists in the window.</summary>
public sealed record CustomerActivityBlock(IReadOnlyList<CustomerActivityEvent> Items, CustomerActivityCursor? Next);
