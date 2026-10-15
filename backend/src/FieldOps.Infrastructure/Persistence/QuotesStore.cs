using FieldOps.Application.Features.Access;
using FieldOps.Application.Features.Quotes;
using FieldOps.Application.Features.ServiceRequests;
using FieldOps.Domain.Quotes;
using FieldOps.Domain.WorkOrders;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Persistence;

/// <summary>
/// Read model of the quotes workspace (quotes-management). Every query filters by organization first, then by the
/// branch scope of the quote request (BR-02), and runs a bounded number of commands regardless of the page size
/// (BR-23). The shown version, the display status, the response and the work order are derived once, in SQL.
/// </summary>
internal sealed partial class QuotesStore(FieldOpsDbContext dbContext, TimeProvider timeProvider) : IQuotesStore
{
    public async Task<bool> BranchAvailableAsync(
        Guid organizationId, BranchScope scope, Guid branchId, CancellationToken cancellationToken)
    {
        if (!scope.Contains(branchId))
        {
            return false;
        }

        return await dbContext.Branches.AsNoTracking()
            .AnyAsync(branch => branch.OrganizationId == organizationId && branch.Id == branchId && branch.IsActive, cancellationToken);
    }

    public async Task<QuotesOptions> OptionsAsync(
        Guid organizationId, BranchScope scope, QuotesPermissions permissions, CancellationToken cancellationToken)
    {
        var organization = await ReadOrganizationAsync(organizationId, cancellationToken);
        var branches = dbContext.Branches.AsNoTracking()
            .Where(branch => branch.OrganizationId == organizationId && branch.IsActive);

        if (!scope.All)
        {
            var ids = scope.BranchIds.ToArray();
            branches = branches.Where(branch => ids.Contains(branch.Id));
        }

        var rows = await branches
            .OrderBy(branch => branch.Name)
            .ThenBy(branch => branch.Id)
            .Select(branch => new { branch.Id, branch.Name })
            .ToListAsync(cancellationToken);

        return new QuotesOptions(
            rows.Select(row => new QuotesOptionBranch(row.Id, row.Name)).ToList(),
            organization.Timezone,
            organization.Currency,
            permissions.CanManage,
            permissions.CanManageWorkOrders,
            permissions.CanReadWorkOrders);
    }

    public async Task<QuotesPage> ListAsync(
        Guid organizationId, BranchScope scope, QuotesFilter filter, QuotesPermissions permissions, CancellationToken cancellationToken)
    {
        var organization = await ReadOrganizationAsync(organizationId, cancellationToken);
        var zone = OrganizationTime.FindZone(organization.Timezone);
        var today = OrganizationTime.LocalDate(timeProvider.GetUtcNow(), zone);
        var rows = Filtered(Rows(organizationId, scope, filter.BranchId, today), filter, organization, zone, today);

        var grouped = await rows
            .GroupBy(row => row.DisplayStatus)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);
        var byStatus = grouped.ToDictionary(group => group.Status, group => group.Count);

        int CountOf(QuoteDisplayStatus[] statuses) => statuses.Sum(status => byStatus.GetValueOrDefault(status));

        var counts = new QuoteTabCounts(
            CountOf(QuotesWorkspaceRules.TabStatuses(QuotesTab.All)),
            CountOf(QuotesWorkspaceRules.TabStatuses(QuotesTab.Drafts)),
            CountOf(QuotesWorkspaceRules.TabStatuses(QuotesTab.Sent)),
            CountOf(QuotesWorkspaceRules.TabStatuses(QuotesTab.Approved)),
            CountOf(QuotesWorkspaceRules.TabStatuses(QuotesTab.Closed)));

        var statuses = filter.Status is { } selected ? [selected] : QuotesWorkspaceRules.TabStatuses(filter.Tab);
        var total = CountOf(statuses);

        if (total == 0)
        {
            return new QuotesPage([], 0, counts);
        }

        var skip = (filter.Page - 1) * filter.PageSize;
        var data = await Sort(rows.Where(row => statuses.Contains(row.DisplayStatus)), filter.Sort)
            .Skip(skip)
            .Take(filter.PageSize)
            .ToListAsync(cancellationToken);

        return new QuotesPage(data.Select(row => ToRow(row, organization, permissions)).ToList(), total, counts);
    }

    private static QuoteRow ToRow(QuoteData row, OrganizationInfo organization, QuotesPermissions permissions)
    {
        var inProgress = QuotesWorkspaceRules.RevisionInProgress(row.HasMutableVersion, row.CurrentVersionNo);
        var customerName = !string.IsNullOrWhiteSpace(row.CustomerName) ? row.CustomerName : row.GuestName ?? string.Empty;

        return new QuoteRow(
            row.Id,
            RequestCardRules.DisplayNumber(organization.QuotePrefix, row.QuoteNumber),
            QuotesWorkspaceRules.RevisionNo(row.CurrentVersionNo),
            inProgress,
            customerName,
            row.PropertyName is null ? null : new QuoteRowProperty(row.PropertyName, row.PostalCode, row.City),
            new QuoteRowRequest(
                row.RequestId,
                RequestCardRules.DisplayNumber(organization.RequestPrefix, row.RequestNumber),
                RequestCardRules.Title(row.ServiceName, row.CategoryName, row.RequestDescription)),
            row.BranchId is { } branchId && row.BranchName is not null ? new QuoteRowBranch(branchId, row.BranchName) : null,
            row.ShownTotal ?? 0m,
            row.ShownCurrency ?? organization.Currency,
            QuotesWorkspaceRules.StatusCode(row.DisplayStatus),
            row.ShownSentAt,
            row.ShownValidUntil,
            row.RespondedAt,
            row.UpdatedAt,
            row.CreatedAt,
            row.WorkOrderId,
            QuotesWorkspaceRules.Actions(
                row.DisplayStatus,
                row.Status,
                row.HasMutableVersion,
                row.CurrentVersionNo,
                row.WorkOrderId is not null,
                row.WorkOrderStatus,
                permissions.CanManage,
                permissions.CanManageWorkOrders,
                permissions.CanReadWorkOrders));
    }

    /// <summary>BR-09: the requested order; every order falls back to the quote number, newest first.</summary>
    private static IOrderedQueryable<QuoteData> Sort(IQueryable<QuoteData> rows, QuotesSort sort) => sort switch
    {
        QuotesSort.Newest => rows.OrderByDescending(row => row.CreatedAt).ThenByDescending(row => row.QuoteNumber),
        QuotesSort.Oldest => rows.OrderBy(row => row.CreatedAt).ThenByDescending(row => row.QuoteNumber),
        QuotesSort.Total => rows.OrderBy(row => row.ShownTotal == null)
            .ThenByDescending(row => row.ShownTotal)
            .ThenByDescending(row => row.QuoteNumber),
        QuotesSort.Expiring => rows.OrderBy(row => row.ShownValidUntil == null)
            .ThenBy(row => row.ShownValidUntil)
            .ThenByDescending(row => row.QuoteNumber),
        _ => rows.OrderByDescending(row => row.UpdatedAt).ThenByDescending(row => row.QuoteNumber),
    };

    /// <summary>Search (BR-08) and date (BR-07) over the scoped rows. Neither depends on the tab or the status filter.</summary>
    private static IQueryable<QuoteData> Filtered(
        IQueryable<QuoteData> rows, QuotesFilter filter, OrganizationInfo organization, TimeZoneInfo zone, DateOnly today)
    {
        if (QuotesWorkspaceRules.WindowStart(filter.Date, today) is { } windowStart)
        {
            var from = OrganizationTime.StartOfDayUtc(windowStart, zone);
            rows = rows.Where(row => row.CreatedAt >= from);
        }

        var term = filter.Search?.Trim().TrimStart('#').Trim();

        if (string.IsNullOrEmpty(term))
        {
            return rows;
        }

        var pattern = RequestSearch.ContainsPattern(term);
        var quoteNumber = RequestSearch.TryParseNumber(term, organization.QuotePrefix, out var parsedQuote) ? parsedQuote : -1L;
        var requestNumber = term.StartsWith(organization.RequestPrefix + "-", StringComparison.OrdinalIgnoreCase)
            && RequestSearch.TryParseNumber(term, organization.RequestPrefix, out var parsedRequest)
                ? parsedRequest
                : -1L;

        return rows.Where(row =>
            row.QuoteNumber == quoteNumber
            || row.RequestNumber == requestNumber
            || (row.CustomerName != null && EF.Functions.ILike(row.CustomerName, pattern))
            || (row.GuestName != null && EF.Functions.ILike(row.GuestName, pattern))
            || (row.PropertyName != null && EF.Functions.ILike(row.PropertyName, pattern))
            || (row.AddressLine1 != null && EF.Functions.ILike(row.AddressLine1, pattern))
            || (row.City != null && EF.Functions.ILike(row.City, pattern))
            || (row.ServiceName != null && EF.Functions.ILike(row.ServiceName, pattern))
            || (row.CategoryName != null && EF.Functions.ILike(row.CategoryName, pattern)));
    }

    /// <summary>
    /// The visible quotes of the organization (BR-02): the request branch is null or in scope, and with a selected
    /// branch the quote branch (the quote's own, else the request's) equals it. One row per quote: the shown version
    /// (BR-04), the final response of that version and the work order of the approved version are single-row joins.
    /// </summary>
    private IQueryable<QuoteData> Rows(Guid organizationId, BranchScope scope, Guid? branchId, DateOnly today)
    {
        var rows =
            from quote in dbContext.Quotes.AsNoTracking().Where(candidate => candidate.OrganizationId == organizationId)
            join request in dbContext.ServiceRequests.AsNoTracking()
                on new { quote.OrganizationId, Id = quote.RequestId } equals new { request.OrganizationId, request.Id }
            from shown in dbContext.QuoteVersions.AsNoTracking()
                .Where(version => version.OrganizationId == quote.OrganizationId
                    && version.QuoteId == quote.Id
                    && (quote.CurrentVersionNo == 0 ? !version.IsImmutable : version.VersionNo == quote.CurrentVersionNo))
                .DefaultIfEmpty()
            from response in dbContext.QuoteResponses.AsNoTracking()
                .Where(candidate => candidate.OrganizationId == quote.OrganizationId
                    && candidate.QuoteVersionId == shown.Id
                    && (candidate.Response == QuoteStatus.Approved || candidate.Response == QuoteStatus.Rejected))
                .DefaultIfEmpty()
            from order in dbContext.WorkOrders.AsNoTracking()
                .Where(candidate => candidate.OrganizationId == quote.OrganizationId
                    && candidate.QuoteVersionId == quote.ApprovedVersionId)
                .DefaultIfEmpty()
            from branch in dbContext.Branches.AsNoTracking()
                .Where(candidate => candidate.OrganizationId == quote.OrganizationId
                    && candidate.Id == (quote.BranchId ?? request.BranchId))
                .DefaultIfEmpty()
            from customer in dbContext.Customers.AsNoTracking()
                .Where(candidate => candidate.OrganizationId == quote.OrganizationId
                    && candidate.Id == (quote.CustomerId ?? request.CustomerId))
                .DefaultIfEmpty()
            from property in dbContext.Properties.AsNoTracking()
                .Where(candidate => candidate.OrganizationId == quote.OrganizationId
                    && candidate.Id == (quote.PropertyId ?? request.PropertyId))
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
                Status = quote.Status,
                CurrentVersionNo = quote.CurrentVersionNo,
                CreatedAt = quote.CreatedAt,
                UpdatedAt = quote.UpdatedAt,
                RequestId = request.Id,
                RequestNumber = request.RequestNumber,
                RequestDescription = request.Description,
                RequestBranchId = request.BranchId,
                EffectiveBranchId = quote.BranchId ?? request.BranchId,
                BranchId = branch == null ? null : (Guid?)branch.Id,
                BranchName = branch == null ? null : branch.Name,
                CustomerName = customer == null ? null : customer.DisplayName,
                GuestName = request.GuestName,
                PropertyName = property == null ? null : property.Name,
                AddressLine1 = property == null ? null : property.AddressLine1,
                PostalCode = property == null ? null : property.PostalCode,
                City = property == null ? null : property.City,
                ServiceName = item == null ? null : item.Name,
                CategoryName = category == null ? null : category.Name,
                ShownTotal = shown == null ? null : (decimal?)shown.Total,
                ShownCurrency = shown == null ? null : shown.Currency,
                ShownValidUntil = shown == null ? null : shown.ValidUntil,
                ShownSentAt = shown == null ? null : shown.SentAt,
                RespondedAt = response == null ? null : (DateTimeOffset?)response.RespondedAt,
                ResponseTotal = response == null ? null : response.Total,
                HasMutableVersion = dbContext.QuoteVersions.Any(candidate => candidate.OrganizationId == quote.OrganizationId
                    && candidate.QuoteId == quote.Id
                    && !candidate.IsImmutable),
                WorkOrderId = order == null ? null : (Guid?)order.Id,
                WorkOrderStatus = order == null ? null : (WorkOrderStatus?)order.Status,
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

        if (!scope.All)
        {
            var ids = scope.BranchIds.ToArray();
            rows = rows.Where(row => row.RequestBranchId == null || ids.Contains(row.RequestBranchId.Value));
        }

        return branchId is { } selected ? rows.Where(row => row.EffectiveBranchId == selected) : rows;
    }

    private async Task<OrganizationInfo> ReadOrganizationAsync(Guid organizationId, CancellationToken cancellationToken) =>
        await dbContext.Organizations.AsNoTracking()
            .Where(organization => organization.Id == organizationId)
            .Select(organization => new OrganizationInfo(
                organization.Timezone, organization.Currency, organization.QuotePrefix, organization.RequestPrefix))
            .SingleAsync(cancellationToken);

    private sealed record OrganizationInfo(string Timezone, string Currency, string QuotePrefix, string RequestPrefix);

    private sealed class QuoteData
    {
        public Guid Id { get; set; }

        public long QuoteNumber { get; set; }

        public QuoteStatus Status { get; set; }

        public QuoteDisplayStatus DisplayStatus { get; set; }

        public int CurrentVersionNo { get; set; }

        public DateTimeOffset CreatedAt { get; set; }

        public DateTimeOffset UpdatedAt { get; set; }

        public Guid RequestId { get; set; }

        public long RequestNumber { get; set; }

        public string RequestDescription { get; set; } = string.Empty;

        public Guid? RequestBranchId { get; set; }

        public Guid? EffectiveBranchId { get; set; }

        public Guid? BranchId { get; set; }

        public string? BranchName { get; set; }

        public string? CustomerName { get; set; }

        public string? GuestName { get; set; }

        public string? PropertyName { get; set; }

        public string? AddressLine1 { get; set; }

        public string? PostalCode { get; set; }

        public string? City { get; set; }

        public string? ServiceName { get; set; }

        public string? CategoryName { get; set; }

        public decimal? ShownTotal { get; set; }

        public string? ShownCurrency { get; set; }

        public DateOnly? ShownValidUntil { get; set; }

        public DateTimeOffset? ShownSentAt { get; set; }

        public DateTimeOffset? RespondedAt { get; set; }

        public decimal? ResponseTotal { get; set; }

        public bool HasMutableVersion { get; set; }

        public Guid? WorkOrderId { get; set; }

        public WorkOrderStatus? WorkOrderStatus { get; set; }
    }
}
