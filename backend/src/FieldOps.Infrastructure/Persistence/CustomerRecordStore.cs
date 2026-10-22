using FieldOps.Application.Features.Access;
using FieldOps.Application.Features.Customers;
using FieldOps.Application.Features.Quotes;
using FieldOps.Application.Features.ServiceRequests;
using FieldOps.Domain.Quotes;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Persistence;

/// <summary>
/// Read model of the related-record tabs (customer-related-records). Every list is built on <see cref="CustomerRecordScope"/>,
/// so organization, customer and branch scope are applied before any search, status filter or paging. The visible
/// total of the customer ignores search and status.
/// </summary>
internal sealed partial class CustomerRecordStore(FieldOpsDbContext dbContext) : ICustomerRecordStore
{
    public async Task<CustomerRecordSlice<CustomerRequestRow>> ListRequestsAsync(
        Guid organizationId, BranchScope scope, Guid customerId, CustomerRecordQuery query, CancellationToken cancellationToken)
    {
        var organization = await ReadOrganizationAsync(organizationId, cancellationToken);
        var visible = dbContext.VisibleRequests(organizationId, scope, customerId);
        var customerTotal = await visible.CountAsync(cancellationToken);

        var rows =
            from request in visible
            from item in dbContext.CatalogItems.AsNoTracking()
                .Where(candidate => candidate.OrganizationId == organizationId && candidate.Id == request.CatalogItemId)
                .DefaultIfEmpty()
            from category in dbContext.ServiceCategories.AsNoTracking()
                .Where(candidate => candidate.OrganizationId == organizationId && candidate.Id == request.CategoryId)
                .DefaultIfEmpty()
            from property in dbContext.Properties.AsNoTracking()
                .Where(candidate => candidate.OrganizationId == organizationId && candidate.Id == request.PropertyId)
                .DefaultIfEmpty()
            select new RequestData
            {
                Id = request.Id,
                RequestNumber = request.RequestNumber,
                Description = request.Description,
                Status = request.Status,
                CreatedAt = request.CreatedAt,
                ServiceName = item == null ? null : item.Name,
                CategoryName = category == null ? null : category.Name,
                PropertyName = property == null ? null : property.Name,
            };

        if (query.Status is { } code && RequestTransitions.TryParseCode(code, out var status))
        {
            rows = rows.Where(row => row.Status == status);
        }

        if (query.Search is { } term)
        {
            var pattern = RequestSearch.ContainsPattern(term);
            var number = RequestSearch.TryParseNumber(term, organization.RequestPrefix, out var parsed) ? parsed : -1L;

            rows = rows.Where(row =>
                row.RequestNumber == number
                || (row.ServiceName != null && EF.Functions.ILike(row.ServiceName, pattern))
                || (row.CategoryName != null && EF.Functions.ILike(row.CategoryName, pattern))
                || (row.PropertyName != null && EF.Functions.ILike(row.PropertyName, pattern))
                || EF.Functions.ILike(row.Description, pattern));
        }

        var total = await rows.CountAsync(cancellationToken);
        var page = await rows
            .OrderByDescending(row => row.CreatedAt)
            .ThenBy(row => row.Id)
            .Skip(query.Skip)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        return new CustomerRecordSlice<CustomerRequestRow>(
            [.. page.Select(row => new CustomerRequestRow(
                row.Id,
                RequestCardRules.DisplayNumber(organization.RequestPrefix, row.RequestNumber),
                RequestCardRules.Title(row.ServiceName, row.CategoryName, row.Description),
                row.PropertyName,
                row.CreatedAt,
                RequestTransitions.Code(row.Status)))],
            total,
            customerTotal);
    }

    public async Task<CustomerRecordSlice<CustomerQuoteRow>> ListQuotesAsync(
        Guid organizationId,
        BranchScope scope,
        Guid customerId,
        CustomerRecordQuery query,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var organization = await ReadOrganizationAsync(organizationId, cancellationToken);
        var today = OrganizationTime.LocalDate(now, OrganizationTime.FindZone(organization.Timezone));
        var visible = dbContext.VisibleQuotes(organizationId, scope, customerId);
        var customerTotal = await visible.CountAsync(cancellationToken);

        var rows =
            from quote in visible
            join request in dbContext.ServiceRequests.AsNoTracking()
                on new { quote.OrganizationId, Id = quote.RequestId } equals new { request.OrganizationId, request.Id }
            from shown in dbContext.QuoteVersions.AsNoTracking()
                .Where(version => version.OrganizationId == quote.OrganizationId
                    && version.QuoteId == quote.Id
                    && (quote.CurrentVersionNo == 0 ? !version.IsImmutable : version.VersionNo == quote.CurrentVersionNo))
                .DefaultIfEmpty()
            from item in dbContext.CatalogItems.AsNoTracking()
                .Where(candidate => candidate.OrganizationId == quote.OrganizationId && candidate.Id == request.CatalogItemId)
                .DefaultIfEmpty()
            from category in dbContext.ServiceCategories.AsNoTracking()
                .Where(candidate => candidate.OrganizationId == quote.OrganizationId && candidate.Id == request.CategoryId)
                .DefaultIfEmpty()
            select new QuoteData
            {
                Id = quote.Id,
                QuoteNumber = quote.QuoteNumber,
                CreatedAt = quote.CreatedAt,
                RequestId = request.Id,
                RequestNumber = request.RequestNumber,
                RequestDescription = request.Description,
                ServiceName = item == null ? null : item.Name,
                CategoryName = category == null ? null : category.Name,
                ShownTotal = shown == null ? null : (decimal?)shown.Total,
                ShownValidUntil = shown == null ? null : shown.ValidUntil,

                // The display status of quotes-management BR-03, derived once in SQL as the workspace does.
                DisplayStatus = quote.Status == QuoteStatus.Cancelled
                    ? QuoteDisplayStatus.Cancelled
                    : quote.CurrentVersionNo == 0
                        ? QuoteDisplayStatus.Draft
                        : quote.Status == QuoteStatus.Approved
                            ? QuoteDisplayStatus.Approved
                            : quote.Status == QuoteStatus.Rejected
                                ? QuoteDisplayStatus.Rejected
                                : quote.Status == QuoteStatus.Expired
                                    ? QuoteDisplayStatus.Expired
                                    : quote.Status == QuoteStatus.Sent || quote.Status == QuoteStatus.ClarificationRequested
                                        ? (shown != null && shown.ValidUntil != null && shown.ValidUntil < today
                                            ? QuoteDisplayStatus.Expired
                                            : quote.Status == QuoteStatus.Sent
                                                ? QuoteDisplayStatus.AwaitingResponse
                                                : QuoteDisplayStatus.ClarificationRequested)
                                        : QuoteDisplayStatus.Draft,
            };

        if (query.Status is { } code
            && QuotesWorkspaceRules.TryParseStatus(code, QuotesTab.All, out var status)
            && status is { } display)
        {
            rows = rows.Where(row => row.DisplayStatus == display);
        }

        if (query.Search is { } term)
        {
            var pattern = RequestSearch.ContainsPattern(term);
            var quoteNumber = RequestSearch.TryParseNumber(term, organization.QuotePrefix, out var parsedQuote) ? parsedQuote : -1L;
            var requestNumber = RequestSearch.TryParseNumber(term, organization.RequestPrefix, out var parsedRequest) ? parsedRequest : -1L;

            rows = rows.Where(row =>
                row.QuoteNumber == quoteNumber
                || row.RequestNumber == requestNumber
                || (row.ServiceName != null && EF.Functions.ILike(row.ServiceName, pattern))
                || (row.CategoryName != null && EF.Functions.ILike(row.CategoryName, pattern)));
        }

        var total = await rows.CountAsync(cancellationToken);
        var page = await rows
            .OrderByDescending(row => row.CreatedAt)
            .ThenBy(row => row.Id)
            .Skip(query.Skip)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        return new CustomerRecordSlice<CustomerQuoteRow>(
            [.. page.Select(row => new CustomerQuoteRow(
                row.Id,
                RequestCardRules.DisplayNumber(organization.QuotePrefix, row.QuoteNumber),
                new CustomerQuoteRequestRef(
                    row.RequestId,
                    RequestCardRules.DisplayNumber(organization.RequestPrefix, row.RequestNumber),
                    RequestCardRules.Title(row.ServiceName, row.CategoryName, row.RequestDescription)),
                row.ShownTotal,
                row.ShownValidUntil,
                QuotesWorkspaceRules.StatusCode(row.DisplayStatus)))],
            total,
            customerTotal);
    }

    private async Task<OrganizationInfo> ReadOrganizationAsync(Guid organizationId, CancellationToken cancellationToken) =>
        await dbContext.Organizations.AsNoTracking()
            .Where(organization => organization.Id == organizationId)
            .Select(organization => new OrganizationInfo(
                organization.Timezone,
                organization.RequestPrefix,
                organization.QuotePrefix,
                organization.WorkOrderPrefix,
                organization.InvoicePrefix))
            .SingleAsync(cancellationToken);

    private sealed record OrganizationInfo(
        string Timezone, string RequestPrefix, string QuotePrefix, string WorkOrderPrefix, string InvoicePrefix);

    private sealed class RequestData
    {
        public Guid Id { get; set; }

        public long RequestNumber { get; set; }

        public string Description { get; set; } = string.Empty;

        public FieldOps.Domain.Requests.RequestStatus Status { get; set; }

        public DateTimeOffset CreatedAt { get; set; }

        public string? ServiceName { get; set; }

        public string? CategoryName { get; set; }

        public string? PropertyName { get; set; }
    }

    private sealed class QuoteData
    {
        public Guid Id { get; set; }

        public long QuoteNumber { get; set; }

        public DateTimeOffset CreatedAt { get; set; }

        public Guid RequestId { get; set; }

        public long RequestNumber { get; set; }

        public string RequestDescription { get; set; } = string.Empty;

        public string? ServiceName { get; set; }

        public string? CategoryName { get; set; }

        public decimal? ShownTotal { get; set; }

        public DateOnly? ShownValidUntil { get; set; }

        public QuoteDisplayStatus DisplayStatus { get; set; }
    }
}
