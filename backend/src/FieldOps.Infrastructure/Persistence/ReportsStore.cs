using FieldOps.Application.Features.Access;
using FieldOps.Application.Features.AuditTrail;
using FieldOps.Application.Features.Reports;
using FieldOps.Domain.Invoices;
using FieldOps.Domain.Quotes;
using FieldOps.Domain.Technicians;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Persistence;

/// <summary>
/// Reports workspace reads (reports-workspace). No-tracking, read-only, always organization first, then the selected branch
/// ids (already authorized against the caller scope). Each report loads its source in a bounded number of queries that does not
/// depend on the page size; sorting and paging of the filtered set run in the builders.
/// </summary>
internal sealed partial class ReportsStore(FieldOpsDbContext dbContext) : IReportsStore
{
    public async Task<ReportOrganization> GetOrganizationAsync(Guid organizationId, CancellationToken cancellationToken) =>
        await dbContext.Organizations.AsNoTracking()
            .Where(organization => organization.Id == organizationId)
            .Select(organization => new ReportOrganization(
                organization.Name,
                organization.Timezone,
                organization.Currency,
                organization.WorkOrderPrefix,
                organization.InvoicePrefix,
                organization.QuotePrefix,
                organization.RequestPrefix))
            .SingleAsync(cancellationToken);

    public async Task<ReportDirectory> GetDirectoryAsync(Guid organizationId, BranchScope scope, CancellationToken cancellationToken)
    {
        var branches = dbContext.Branches.AsNoTracking().Where(branch => branch.OrganizationId == organizationId);

        if (!scope.All)
        {
            var scoped = scope.BranchIds.ToArray();
            branches = branches.Where(branch => scoped.Contains(branch.Id));
        }

        var branchList = await branches
            .OrderBy(branch => branch.Name)
            .ThenBy(branch => branch.Id)
            .Select(branch => new ReportBranch(branch.Id, branch.Name, branch.IsActive))
            .ToListAsync(cancellationToken);
        var categories = await dbContext.ServiceCategories.AsNoTracking()
            .Where(category => category.OrganizationId == organizationId)
            .OrderBy(category => category.Name)
            .ThenBy(category => category.Id)
            .Select(category => new ReportCategory(category.Id, category.Name, category.IsActive))
            .ToListAsync(cancellationToken);

        var branchIds = branchList.Select(branch => branch.Id).ToArray();
        var profiles = await dbContext.TechnicianProfiles.AsNoTracking()
            .Where(profile => profile.OrganizationId == organizationId && branchIds.Contains(profile.BranchId))
            .Select(profile => new { profile.Id, profile.FirstName, profile.LastName, profile.Status, profile.BranchId })
            .ToListAsync(cancellationToken);

        return new ReportDirectory(
            branchList,
            categories,
            [.. profiles
                .Select(profile => new ReportTechnicianOption(
                    profile.Id, AuditLogStore.FullName(profile.FirstName, profile.LastName), profile.Status == TechnicianStatus.Active, profile.BranchId))
                .OrderBy(option => option.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(option => option.Id)]);
    }

    public async Task<IReadOnlyList<ReportExportRecord>> ListExportsAsync(
        Guid organizationId,
        Guid userId,
        DateTimeOffset since,
        IReadOnlyList<string> reportTypes,
        int take,
        CancellationToken cancellationToken)
    {
        var types = reportTypes.ToArray();
        var rows = await dbContext.AuditLogs.AsNoTracking()
            .Where(audit => audit.OrganizationId == organizationId
                && audit.ActorUserId == userId
                && audit.Action == AuditCatalog.ExportGenerated
                && audit.OccurredAt >= since
                && FieldOpsDbContext.PayloadText(audit.Metadata, "reportType") != null
                && types.Contains(FieldOpsDbContext.PayloadText(audit.Metadata, "reportType")!))
            .OrderByDescending(audit => audit.OccurredAt)
            .ThenByDescending(audit => audit.Id)
            .Take(take)
            .Select(audit => new { audit.Id, audit.OccurredAt, audit.Metadata })
            .ToListAsync(cancellationToken);

        return [.. rows.Select(row => new ReportExportRecord(row.Id, row.OccurredAt, row.Metadata))];
    }

    public async Task<AgingSource> LoadAgingAsync(ReportContext context, CancellationToken cancellationToken)
    {
        var organizationId = context.Call.OrganizationId;
        var branchIds = context.BranchIds.ToArray();

        var rows = await (
            from invoice in dbContext.Invoices.AsNoTracking()
            join customer in dbContext.Customers.AsNoTracking()
                on new { invoice.OrganizationId, Id = invoice.CustomerId } equals new { customer.OrganizationId, customer.Id }
            join order in dbContext.WorkOrders.AsNoTracking()
                on new { invoice.OrganizationId, Id = invoice.WorkOrderId } equals new { order.OrganizationId, order.Id }
            where invoice.OrganizationId == organizationId
                && branchIds.Contains(invoice.BranchId)
                && (invoice.Status == InvoiceStatus.Sent || invoice.Status == InvoiceStatus.PartiallyPaid)
            select new
            {
                invoice.Id,
                invoice.InvoiceNumber,
                invoice.CustomerId,
                CustomerName = customer.DisplayName,
                OrderId = order.Id,
                order.WorkOrderNumber,
                invoice.IssueDate,
                invoice.DueDate,
                invoice.Status,
                invoice.Total,
                invoice.BalanceDue,
                invoice.BranchId,
            })
            .ToListAsync(cancellationToken);

        return new AgingSource([.. rows.Select(row => new AgingInvoice(
            row.Id,
            row.InvoiceNumber,
            row.CustomerId,
            row.CustomerName,
            row.OrderId,
            row.WorkOrderNumber,
            row.IssueDate,
            row.DueDate,
            row.Status,
            row.Total,
            row.BalanceDue,
            row.BranchId))]);
    }

    // BR-10: quotes visible through their request, with a first sent or an approval inside the previous or current period.
    public async Task<QuotesSource> LoadQuotesAsync(ReportContext context, CancellationToken cancellationToken)
    {
        var organizationId = context.Call.OrganizationId;
        var start = context.PreviousStart;
        var end = context.CurrentEnd;
        var scope = context.Scope;
        var scoped = scope.BranchIds.ToArray();

        var visible =
            from quote in dbContext.Quotes.AsNoTracking()
            from request in dbContext.ServiceRequests.AsNoTracking()
                .Where(candidate => candidate.OrganizationId == quote.OrganizationId && candidate.Id == quote.RequestId)
                .DefaultIfEmpty()
            where quote.OrganizationId == organizationId
            select new
            {
                quote.Id,
                RequestBranch = request == null ? null : request.BranchId,
                Effective = quote.BranchId ?? (request == null ? null : request.BranchId),
            };

        if (!scope.All)
        {
            visible = visible.Where(row => row.RequestBranch == null || scoped.Contains(row.RequestBranch.Value));
            visible = visible.Where(row => row.Effective == null || scoped.Contains(row.Effective.Value));
        }

        if (context.Query.BranchId is { } selected)
        {
            visible = visible.Where(row => row.Effective == selected);
        }

        var firstSent = dbContext.QuoteVersions.AsNoTracking()
            .Where(version => version.OrganizationId == organizationId && version.SentAt != null)
            .GroupBy(version => version.QuoteId)
            .Select(group => new { QuoteId = group.Key, First = group.Min(version => version.SentAt) });

        var sentIds = await (
            from sent in firstSent
            join row in visible on sent.QuoteId equals row.Id
            where sent.First >= start && sent.First < end
            select sent.QuoteId)
            .ToListAsync(cancellationToken);

        var accepted = await (
            from response in dbContext.QuoteResponses.AsNoTracking()
            join version in dbContext.QuoteVersions.AsNoTracking() on response.QuoteVersionId equals version.Id
            join row in visible on version.QuoteId equals row.Id
            where response.OrganizationId == organizationId
                && response.Response == QuoteStatus.Approved
                && response.RespondedAt >= start
                && response.RespondedAt < end
            select new { version.QuoteId, response.RespondedAt, ResponseTotal = response.Total, VersionTotal = version.Total, version.Currency })
            .ToListAsync(cancellationToken);

        var ids = sentIds.Concat(accepted.Select(row => row.QuoteId)).Distinct().ToArray();

        if (ids.Length == 0)
        {
            return new QuotesSource([]);
        }

        var details = await (
            from quote in dbContext.Quotes.AsNoTracking()
            from request in dbContext.ServiceRequests.AsNoTracking()
                .Where(candidate => candidate.OrganizationId == quote.OrganizationId && candidate.Id == quote.RequestId)
                .DefaultIfEmpty()
            from shown in dbContext.QuoteVersions.AsNoTracking()
                .Where(version => version.OrganizationId == quote.OrganizationId
                    && version.QuoteId == quote.Id
                    && (quote.CurrentVersionNo == 0 ? !version.IsImmutable : version.VersionNo == quote.CurrentVersionNo))
                .DefaultIfEmpty()
            from customer in dbContext.Customers.AsNoTracking()
                .Where(candidate => candidate.OrganizationId == quote.OrganizationId
                    && candidate.Id == (quote.CustomerId ?? (request == null ? null : request.CustomerId)))
                .DefaultIfEmpty()
            from item in dbContext.CatalogItems.AsNoTracking()
                .Where(candidate => candidate.OrganizationId == quote.OrganizationId && candidate.Id == (request == null ? null : request.CatalogItemId))
                .DefaultIfEmpty()
            from category in dbContext.ServiceCategories.AsNoTracking()
                .Where(candidate => candidate.OrganizationId == quote.OrganizationId && candidate.Id == (request == null ? null : request.CategoryId))
                .DefaultIfEmpty()
            where quote.OrganizationId == organizationId && ids.Contains(quote.Id)
            select new
            {
                quote.Id,
                quote.QuoteNumber,
                quote.Status,
                quote.CurrentVersionNo,
                RequestId = quote.RequestId,
                RequestNumber = request == null ? 0L : request.RequestNumber,
                GuestName = request == null ? null : request.GuestName,
                Description = request == null ? string.Empty : request.Description,
                CategoryId = request == null ? null : request.CategoryId,
                Effective = quote.BranchId ?? (request == null ? null : request.BranchId),
                CustomerId = customer == null ? (Guid?)null : customer.Id,
                CustomerName = customer == null ? null : customer.DisplayName,
                ServiceName = item == null ? null : item.Name,
                CategoryName = category == null ? null : category.Name,
                ShownTotal = shown == null ? null : (decimal?)shown.Total,
                ShownCurrency = shown == null ? null : shown.Currency,
                ShownValidUntil = shown == null ? null : shown.ValidUntil,
            })
            .ToListAsync(cancellationToken);

        var sentVersions = await dbContext.QuoteVersions.AsNoTracking()
            .Where(version => version.OrganizationId == organizationId && ids.Contains(version.QuoteId) && version.SentAt != null)
            .Select(version => new { version.QuoteId, version.SentAt, version.Total, version.Currency })
            .ToListAsync(cancellationToken);
        var earliest = sentVersions
            .GroupBy(version => version.QuoteId)
            .ToDictionary(group => group.Key, group => group.OrderBy(version => version.SentAt).First());

        return new QuotesSource([.. details.Select(row =>
        {
            var first = earliest.GetValueOrDefault(row.Id);

            return new QuoteFact(
                row.Id,
                row.QuoteNumber,
                row.RequestId,
                row.RequestNumber,
                row.CustomerId,
                row.CustomerName,
                row.GuestName,
                row.Effective,
                row.CategoryId,
                row.ServiceName,
                row.CategoryName,
                row.Description,
                row.Status,
                row.CurrentVersionNo,
                row.ShownValidUntil,
                row.ShownTotal,
                row.ShownCurrency,
                first?.SentAt,
                first?.Total,
                first?.Currency,
                [.. accepted
                    .Where(item => item.QuoteId == row.Id)
                    .Select(item => new QuoteAcceptance(item.RespondedAt, item.ResponseTotal ?? item.VersionTotal, item.Currency))]);
        })]);
    }
}
