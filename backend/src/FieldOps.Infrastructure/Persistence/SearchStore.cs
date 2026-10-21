using FieldOps.Application.Features.Access;
using FieldOps.Application.Features.Search;
using FieldOps.Domain.Customers;
using FieldOps.Domain.Invoices;
using FieldOps.Domain.Quotes;
using FieldOps.Domain.Requests;
using FieldOps.Domain.WorkOrders;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Persistence;

/// <summary>
/// Global search reads (global-search). No-tracking projected queries, always organization first (BR-02): one query per
/// eligible type plus the organization prefixes and branch count (BR-09). Joined rows are joined inside the organization. The
/// effective branch of a row is the branch the scope is checked against (BR-03); a null branch is visible.
/// </summary>
internal sealed class SearchStore(FieldOpsDbContext dbContext) : ISearchStore
{
    private const int Fetch = SearchRules.PerTypeCap + 1;

    public async Task<SearchOrganization> GetOrganizationAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var row = await dbContext.Organizations.AsNoTracking()
            .Where(organization => organization.Id == organizationId)
            .Select(organization => new
            {
                organization.RequestPrefix,
                organization.QuotePrefix,
                organization.WorkOrderPrefix,
                organization.InvoicePrefix,
                ActiveBranches = dbContext.Branches.Count(branch => branch.OrganizationId == organizationId && branch.IsActive),
            })
            .SingleAsync(cancellationToken);

        return new SearchOrganization(row.RequestPrefix, row.QuotePrefix, row.WorkOrderPrefix, row.InvoicePrefix, row.ActiveBranches);
    }

    public async Task<SearchMatches> SearchAsync(
        Guid organizationId,
        BranchScope scope,
        SearchEligibility eligibility,
        SearchTerms terms,
        CancellationToken cancellationToken)
    {
        var hits = new List<SearchHit>();
        var overflow = false;

        // Sequential: one DbContext. Each ineligible type is never queried (BR-03).
        if (eligibility.Customer)
        {
            overflow |= Add(hits, await ReadAsync(organizationId, scope, Customers(organizationId), terms, null, cancellationToken, CustomerHit, c => c.T1, c => c.T2, c => c.T3, c => c.T4));
        }

        if (eligibility.ServiceRequest)
        {
            overflow |= Add(hits, await ReadAsync(organizationId, scope, Requests(organizationId), terms, terms.RequestDigits, cancellationToken, RequestHit, c => c.T1, c => c.T2, c => c.T3, c => c.T4));
        }

        if (eligibility.Quote)
        {
            overflow |= Add(hits, await ReadAsync(organizationId, scope, Quotes(organizationId), terms, terms.QuoteDigits, cancellationToken, QuoteHit, c => c.T1 ?? c.T2));
        }

        if (eligibility.WorkOrder)
        {
            overflow |= Add(hits, await ReadAsync(organizationId, scope, WorkOrders(organizationId), terms, terms.WorkOrderDigits, cancellationToken, WorkOrderHit, c => c.T1, c => c.T2));
        }

        if (eligibility.Invoice)
        {
            overflow |= Add(hits, await ReadAsync(organizationId, scope, Invoices(organizationId), terms, terms.InvoiceDigits, cancellationToken, InvoiceHit, c => c.T1));
        }

        return new SearchMatches(hits, overflow);
    }

    // Keeps the top 5 and reports whether the sixth row existed.
    private static bool Add(List<SearchHit> hits, List<SearchHit> rows)
    {
        hits.AddRange(rows.Take(SearchRules.PerTypeCap));

        return rows.Count > SearchRules.PerTypeCap;
    }

    private async Task<List<SearchHit>> ReadAsync<TStatus>(
        Guid organizationId,
        BranchScope scope,
        IQueryable<SearchCandidate<TStatus>> source,
        SearchTerms terms,
        string? digits,
        CancellationToken cancellationToken,
        Func<SearchCandidate<TStatus>, int, string?, SearchHit> map,
        params System.Linq.Expressions.Expression<Func<SearchCandidate<TStatus>, string?>>[] fields)
    {
        if (!scope.All)
        {
            var branchIds = scope.BranchIds.ToList();
            source = source.Where(row => row.BranchId == null || branchIds.Contains(row.BranchId.Value));
        }

        var rows = await source
            .Select(SearchRanking.Rank(terms, digits, fields))
            .Where(ranked => ranked.Rank > 0)
            .OrderBy(ranked => ranked.Rank)
            .ThenByDescending(ranked => ranked.Row.UpdatedAt)
            .ThenBy(ranked => ranked.Row.Id)
            .Take(Fetch)
            .Select(ranked => new
            {
                ranked.Row,
                ranked.Rank,
                BranchName = dbContext.Branches
                    .Where(branch => branch.OrganizationId == organizationId && branch.Id == ranked.Row.BranchId)
                    .Select(branch => branch.Name)
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        return [.. rows.Select(row => map(row.Row, row.Rank, row.BranchName))];
    }

    // customer: T1 display name, T2 legal name, T3 email, T4 phone; Kind person or company.
    private IQueryable<SearchCandidate<bool>> Customers(Guid organizationId) =>
        dbContext.Customers.AsNoTracking()
            .Where(customer => customer.OrganizationId == organizationId)
            .Select(customer => new SearchCandidate<bool>
            {
                Id = customer.Id,
                UpdatedAt = customer.UpdatedAt,
                Status = customer.IsActive,
                BranchId = customer.BranchId,
                T1 = customer.DisplayName,
                T2 = customer.LegalName,
                T3 = customer.PrimaryEmail,
                T4 = customer.PrimaryPhone,
                Kind = customer.Type == CustomerType.Company ? "company" : "person",
            });

    // service request: T1 catalog item name, T2 category name, T3 customer name, T4 guest name.
    private IQueryable<SearchCandidate<RequestStatus>> Requests(Guid organizationId) =>
        from request in dbContext.ServiceRequests.AsNoTracking()
        where request.OrganizationId == organizationId
        join customer in dbContext.Customers.AsNoTracking().Where(row => row.OrganizationId == organizationId)
            on request.CustomerId equals (Guid?)customer.Id into customers
        from customer in customers.DefaultIfEmpty()
        join item in dbContext.CatalogItems.AsNoTracking().Where(row => row.OrganizationId == organizationId)
            on request.CatalogItemId equals (Guid?)item.Id into items
        from item in items.DefaultIfEmpty()
        join category in dbContext.ServiceCategories.AsNoTracking().Where(row => row.OrganizationId == organizationId)
            on request.CategoryId equals (Guid?)category.Id into categories
        from category in categories.DefaultIfEmpty()
        select new SearchCandidate<RequestStatus>
        {
            Id = request.Id,
            UpdatedAt = request.UpdatedAt,
            Number = request.RequestNumber,
            Status = request.Status,
            BranchId = request.BranchId,
            T1 = item!.Name,
            T2 = category!.Name,
            T3 = customer!.DisplayName,
            T4 = request.GuestName,
        };

    // quote: T1 customer name, T2 the request's guest name; the effective branch falls back to the request's.
    private IQueryable<SearchCandidate<QuoteStatus>> Quotes(Guid organizationId) =>
        from quote in dbContext.Quotes.AsNoTracking()
        where quote.OrganizationId == organizationId
        join request in dbContext.ServiceRequests.AsNoTracking().Where(row => row.OrganizationId == organizationId)
            on quote.RequestId equals request.Id
        join customer in dbContext.Customers.AsNoTracking().Where(row => row.OrganizationId == organizationId)
            on quote.CustomerId equals (Guid?)customer.Id into customers
        from customer in customers.DefaultIfEmpty()
        select new SearchCandidate<QuoteStatus>
        {
            Id = quote.Id,
            UpdatedAt = quote.UpdatedAt,
            Number = quote.QuoteNumber,
            Status = quote.Status,
            BranchId = quote.BranchId ?? request.BranchId,
            T1 = customer!.DisplayName,
            T2 = request.GuestName,
            RequestNumber = request.RequestNumber,
        };

    // work order: T1 title, T2 customer name.
    private IQueryable<SearchCandidate<WorkOrderStatus>> WorkOrders(Guid organizationId) =>
        from order in dbContext.WorkOrders.AsNoTracking()
        where order.OrganizationId == organizationId
        join customer in dbContext.Customers.AsNoTracking().Where(row => row.OrganizationId == organizationId)
            on order.CustomerId equals customer.Id
        select new SearchCandidate<WorkOrderStatus>
        {
            Id = order.Id,
            UpdatedAt = order.UpdatedAt,
            Number = order.WorkOrderNumber,
            Status = order.Status,
            BranchId = order.BranchId,
            T1 = order.Title,
            T2 = customer.DisplayName,
        };

    // invoice: T1 customer name.
    private IQueryable<SearchCandidate<InvoiceStatus>> Invoices(Guid organizationId) =>
        from invoice in dbContext.Invoices.AsNoTracking()
        where invoice.OrganizationId == organizationId
        join customer in dbContext.Customers.AsNoTracking().Where(row => row.OrganizationId == organizationId)
            on invoice.CustomerId equals customer.Id
        select new SearchCandidate<InvoiceStatus>
        {
            Id = invoice.Id,
            UpdatedAt = invoice.UpdatedAt,
            Number = invoice.InvoiceNumber,
            Status = invoice.Status,
            BranchId = invoice.BranchId,
            T1 = customer.DisplayName,
            Total = invoice.Total,
            Currency = invoice.Currency,
            DueDate = invoice.DueDate,
        };

    private static SearchHit CustomerHit(SearchCandidate<bool> row, int rank, string? branchName) =>
        new(SearchEntityTypes.Customer, row.Id, rank, row.UpdatedAt, null, row.Status ? "active" : "inactive", row.BranchId, branchName,
            row.T1, row.Kind, null, null, null, null, null, null);

    private static SearchHit RequestHit(SearchCandidate<RequestStatus> row, int rank, string? branchName) =>
        new(SearchEntityTypes.ServiceRequest, row.Id, rank, row.UpdatedAt, row.Number, SearchRules.StatusCode(row.Status), row.BranchId, branchName,
            row.T1, row.T2, row.T3, row.T4, null, null, null, null);

    private static SearchHit QuoteHit(SearchCandidate<QuoteStatus> row, int rank, string? branchName) =>
        new(SearchEntityTypes.Quote, row.Id, rank, row.UpdatedAt, row.Number, SearchRules.StatusCode(row.Status), row.BranchId, branchName,
            null, null, row.T1, row.T2, row.RequestNumber, null, null, null);

    private static SearchHit WorkOrderHit(SearchCandidate<WorkOrderStatus> row, int rank, string? branchName) =>
        new(SearchEntityTypes.WorkOrder, row.Id, rank, row.UpdatedAt, row.Number, SearchRules.StatusCode(row.Status), row.BranchId, branchName,
            row.T1, null, row.T2, null, null, null, null, null);

    private static SearchHit InvoiceHit(SearchCandidate<InvoiceStatus> row, int rank, string? branchName) =>
        new(SearchEntityTypes.Invoice, row.Id, rank, row.UpdatedAt, row.Number, SearchRules.StatusCode(row.Status), row.BranchId, branchName,
            null, null, row.T1, null, null, row.Total, row.Currency, row.DueDate);
}
