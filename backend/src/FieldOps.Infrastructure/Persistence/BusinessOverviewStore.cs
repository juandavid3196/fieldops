using FieldOps.Application.Features.BusinessOverview;
using FieldOps.Application.Features.OperationsDashboard;
using FieldOps.Application.Features.ServiceRequests;
using FieldOps.Domain.Invoices;
using FieldOps.Domain.Quotes;
using FieldOps.Domain.Requests;
using FieldOps.Domain.Technicians;
using FieldOps.Domain.WorkOrders;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Persistence;

/// <summary>
/// Business overview reads (business-overview). No-tracking, read-only, always organization first and then the optional
/// branch (BR-02). Money and counts are aggregated in the database; only the facts that need local-date or rounding rules
/// per row (payment allocations, completions, visits, technician schedules) are loaded as small projections.
/// </summary>
internal sealed class BusinessOverviewStore(FieldOpsDbContext dbContext) : IBusinessOverviewStore
{
    public async Task<BusinessOrganization> GetOrganizationAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var row = await dbContext.Organizations.AsNoTracking()
            .Where(organization => organization.Id == organizationId)
            .Select(organization => new { organization.Timezone, organization.Currency })
            .SingleAsync(cancellationToken);

        return new BusinessOrganization(row.Timezone, row.Currency);
    }

    public async Task<IReadOnlyList<BusinessBranchOption>> GetActiveBranchesAsync(Guid organizationId, CancellationToken cancellationToken) =>
        await dbContext.Branches.AsNoTracking()
            .Where(branch => branch.OrganizationId == organizationId && branch.IsActive)
            .OrderBy(branch => branch.Name)
            .ThenBy(branch => branch.Id)
            .Select(branch => new BusinessBranchOption(branch.Id, branch.Name))
            .ToListAsync(cancellationToken);

    public Task<bool> BranchIsActiveAsync(Guid organizationId, Guid branchId, CancellationToken cancellationToken) =>
        dbContext.Branches.AsNoTracking().AnyAsync(
            branch => branch.OrganizationId == organizationId && branch.Id == branchId && branch.IsActive, cancellationToken);

    public async Task<BusinessOverviewSource> LoadAsync(
        Guid organizationId, BusinessOverviewQuery query, CancellationToken cancellationToken)
    {
        var period = query.Period;
        var zone = OrganizationTime.FindZone(query.TimezoneId);
        var previousStart = OrganizationTime.StartOfDayUtc(period.PreviousFrom, zone);
        var currentStart = OrganizationTime.StartOfDayUtc(period.From, zone);
        var currentEnd = OrganizationTime.StartOfDayUtc(period.To.AddDays(1), zone);
        var branchId = query.BranchId;

        var branches = await dbContext.Branches.AsNoTracking()
            .Where(branch => branch.OrganizationId == organizationId && (branchId == null || branch.Id == branchId))
            .Select(branch => new BusinessBranchInfo(branch.Id, branch.Name, branch.IsActive))
            .ToListAsync(cancellationToken);
        var categories = await dbContext.ServiceCategories.AsNoTracking()
            .Where(category => category.OrganizationId == organizationId)
            .Select(category => new BusinessCategoryInfo(category.Id, category.Name))
            .ToListAsync(cancellationToken);

        var invoices = dbContext.Invoices.AsNoTracking()
            .Where(invoice => invoice.OrganizationId == organizationId && (branchId == null || invoice.BranchId == branchId));
        var orders = dbContext.WorkOrders.AsNoTracking()
            .Where(order => order.OrganizationId == organizationId && (branchId == null || order.BranchId == branchId));

        var (invoiceFacts, serviceJobs) = await LoadInvoicesAsync(
            organizationId, invoices, period, cancellationToken);
        var collections = await LoadCollectionsAsync(
            organizationId, invoices, previousStart, currentEnd, cancellationToken);
        var outstanding = await LoadOutstandingAsync(invoices, cancellationToken);

        var quotes = dbContext.Quotes.AsNoTracking()
            .Where(quote => quote.OrganizationId == organizationId && (branchId == null || quote.BranchId == branchId));
        var sent = await CountSentAsync(organizationId, quotes, currentStart, currentEnd, cancellationToken);
        var accepted = await CountAcceptedAsync(organizationId, quotes, currentStart, currentEnd, cancellationToken);
        var requests = await dbContext.ServiceRequests.AsNoTracking()
            .CountAsync(
                request => request.OrganizationId == organizationId
                    && (branchId == null || request.BranchId == branchId)
                    && request.CreatedAt >= currentStart
                    && request.CreatedAt < currentEnd,
                cancellationToken);
        var created = await orders
            .CountAsync(
                order => order.CreatedAt >= currentStart
                    && order.CreatedAt < currentEnd
                    && order.Status != WorkOrderStatus.Draft
                    && order.Status != WorkOrderStatus.Cancelled,
                cancellationToken);

        var completions = await LoadCompletionsAsync(organizationId, orders, previousStart, currentEnd, cancellationToken);
        var arrivals = await LoadArrivalsAsync(organizationId, orders, currentStart, currentEnd, cancellationToken);
        var reviews = await LoadReviewsAsync(organizationId, orders, currentStart, currentEnd, cancellationToken);
        var technicians = await LoadTechniciansAsync(organizationId, query, zone, cancellationToken);

        return new BusinessOverviewSource(
            branches,
            categories,
            invoiceFacts,
            serviceJobs,
            collections,
            outstanding,
            requests,
            sent,
            accepted,
            created,
            completions,
            arrivals,
            reviews,
            technicians);
    }

    // Recognized invoices (BR-05) of the current and previous period: revenue and totals by issue date, branch and category,
    // the cost of the line source (quote line, else visit material, else 0) and the distinct jobs per category.
    private async Task<(List<BusinessInvoiceFact> Facts, List<BusinessServiceJobsFact> Jobs)> LoadInvoicesAsync(
        Guid organizationId, IQueryable<Invoice> invoices, BusinessPeriod period, CancellationToken cancellationToken)
    {
        var from = period.PreviousFrom;
        var to = period.To;
        var recognized = invoices.Where(invoice => invoice.Status != InvoiceStatus.Draft
            && invoice.Status != InvoiceStatus.Void
            && invoice.IssueDate >= from
            && invoice.IssueDate <= to);

        var revenue = await (
            from invoice in recognized
            join order in dbContext.WorkOrders.AsNoTracking() on invoice.WorkOrderId equals order.Id
            where order.OrganizationId == organizationId
            group invoice by new { invoice.IssueDate, invoice.BranchId, order.ServiceCategoryId } into bucket
            select new
            {
                bucket.Key.IssueDate,
                bucket.Key.BranchId,
                bucket.Key.ServiceCategoryId,
                Revenue = bucket.Sum(invoice => invoice.Subtotal - invoice.DiscountTotal),
                Total = bucket.Sum(invoice => invoice.Total),
            })
            .ToListAsync(cancellationToken);

        var cost = await (
            from line in dbContext.InvoiceLines.AsNoTracking()
            join invoice in recognized on line.InvoiceId equals invoice.Id
            join order in dbContext.WorkOrders.AsNoTracking() on invoice.WorkOrderId equals order.Id
            join quoteLine in dbContext.QuoteLines.AsNoTracking() on line.SourceQuoteLineId equals (Guid?)quoteLine.Id into quoteLines
            from quoteLine in quoteLines.DefaultIfEmpty()
            join material in dbContext.VisitMaterials.AsNoTracking() on line.SourceVisitMaterialId equals (Guid?)material.Id into materials
            from material in materials.DefaultIfEmpty()
            where order.OrganizationId == organizationId
            group new { line.Quantity, QuoteCost = (decimal?)quoteLine.UnitCost, MaterialCost = (decimal?)material.UnitCost }
                by new { invoice.IssueDate, invoice.BranchId, order.ServiceCategoryId } into bucket
            select new
            {
                bucket.Key.IssueDate,
                bucket.Key.BranchId,
                bucket.Key.ServiceCategoryId,
                Cost = bucket.Sum(item => item.Quantity * (item.QuoteCost ?? item.MaterialCost ?? 0m)),
            })
            .ToListAsync(cancellationToken);

        var costs = cost.ToDictionary(item => (item.IssueDate, item.BranchId, item.ServiceCategoryId), item => item.Cost);
        var facts = revenue
            .Select(item => new BusinessInvoiceFact(
                item.IssueDate!.Value,
                item.BranchId,
                item.ServiceCategoryId,
                item.Revenue,
                item.Total,
                costs.GetValueOrDefault((item.IssueDate, item.BranchId, item.ServiceCategoryId))))
            .ToList();

        var currentFrom = period.From;
        var jobs = await (
            from invoice in recognized.Where(invoice => invoice.IssueDate >= currentFrom)
            join order in dbContext.WorkOrders.AsNoTracking() on invoice.WorkOrderId equals order.Id
            where order.OrganizationId == organizationId
            group order.Id by order.ServiceCategoryId into category
            select new { CategoryId = category.Key, Jobs = category.Distinct().Count() })
            .ToListAsync(cancellationToken);

        return (facts, [.. jobs.Select(item => new BusinessServiceJobsFact(item.CategoryId, item.Jobs))]);
    }

    // One row per allocation of a payment paid in the window, to invoices of the scope (BR-13); the refund share and the
    // local date are applied by the builder.
    private async Task<List<BusinessCollectionFact>> LoadCollectionsAsync(
        Guid organizationId, IQueryable<Invoice> invoices, DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken)
    {
        var rows = await (
            from allocation in dbContext.PaymentAllocations.AsNoTracking()
            join payment in dbContext.Payments.AsNoTracking() on allocation.PaymentId equals payment.Id
            join invoice in invoices on allocation.InvoiceId equals invoice.Id
            where payment.OrganizationId == organizationId && payment.PaidAt >= start && payment.PaidAt < end
            select new { payment.PaidAt, Allocation = allocation.Amount, PaymentAmount = payment.Amount, payment.RefundedAmount })
            .ToListAsync(cancellationToken);

        return [.. rows.Select(row => new BusinessCollectionFact(row.PaidAt, row.Allocation, row.PaymentAmount, row.RefundedAmount))];
    }

    // Outstanding invoices (sent or partially paid) by due date; overdue and aging are derived per invoices-payments BR-03/BR-05.
    private static async Task<List<BusinessDueFact>> LoadOutstandingAsync(IQueryable<Invoice> invoices, CancellationToken cancellationToken)
    {
        var rows = await invoices
            .Where(invoice => invoice.Status == InvoiceStatus.Sent || invoice.Status == InvoiceStatus.PartiallyPaid)
            .GroupBy(invoice => invoice.DueDate)
            .Select(group => new { Due = group.Key, Count = group.Count(), Balance = group.Sum(invoice => invoice.BalanceDue) })
            .ToListAsync(cancellationToken);

        return [.. rows.Select(row => new BusinessDueFact(row.Due, row.Count, row.Balance))];
    }

    // BR-06: quotes whose earliest sent version was sent in the period.
    private async Task<int> CountSentAsync(
        Guid organizationId, IQueryable<Quote> quotes, DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken)
    {
        var firstSent = dbContext.QuoteVersions.AsNoTracking()
            .Where(version => version.OrganizationId == organizationId && version.SentAt != null)
            .GroupBy(version => version.QuoteId)
            .Select(group => new { QuoteId = group.Key, First = group.Min(version => version.SentAt) });

        return await (
            from sent in firstSent
            join quote in quotes on sent.QuoteId equals quote.Id
            where sent.First >= start && sent.First < end
            select sent.QuoteId)
            .CountAsync(cancellationToken);
    }

    // BR-06: distinct quotes approved in the period.
    private async Task<int> CountAcceptedAsync(
        Guid organizationId, IQueryable<Quote> quotes, DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken) =>
        await (
            from response in dbContext.QuoteResponses.AsNoTracking()
            join version in dbContext.QuoteVersions.AsNoTracking() on response.QuoteVersionId equals version.Id
            join quote in quotes on version.QuoteId equals quote.Id
            where response.OrganizationId == organizationId
                && response.Response == QuoteStatus.Approved
                && response.RespondedAt >= start
                && response.RespondedAt < end
            select quote.Id)
            .Distinct()
            .CountAsync(cancellationToken);

    // BR-08: completed or approved work orders whose latest completion among their non-cancelled visits falls in the window.
    private async Task<List<BusinessCompletionFact>> LoadCompletionsAsync(
        Guid organizationId, IQueryable<WorkOrder> orders, DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken)
    {
        var rows = await orders
            .Where(order => (order.Status == WorkOrderStatus.Completed || order.Status == WorkOrderStatus.ApprovedForBilling)
                && dbContext.Visits.Any(visit => visit.OrganizationId == organizationId
                    && visit.WorkOrderId == order.Id
                    && visit.Status != VisitStatus.Cancelled
                    && visit.ActualCompletedAt >= start))
            .Select(order => new
            {
                order.BranchId,
                Last = dbContext.Visits
                    .Where(visit => visit.OrganizationId == organizationId
                        && visit.WorkOrderId == order.Id
                        && visit.Status != VisitStatus.Cancelled)
                    .Max(visit => visit.ActualCompletedAt),
            })
            .Where(row => row.Last >= start && row.Last < end)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(row => new BusinessCompletionFact(row.BranchId, row.Last!.Value))];
    }

    // BR-15 on-time: visits scheduled in the period of work orders that are not draft or cancelled.
    private async Task<List<BusinessArrivalFact>> LoadArrivalsAsync(
        Guid organizationId, IQueryable<WorkOrder> orders, DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken)
    {
        var rows = await (
            from visit in dbContext.Visits.AsNoTracking()
            join order in orders on visit.WorkOrderId equals order.Id
            where visit.OrganizationId == organizationId
                && order.Status != WorkOrderStatus.Draft
                && order.Status != WorkOrderStatus.Cancelled
                && visit.Status != VisitStatus.Unscheduled
                && visit.Status != VisitStatus.Cancelled
                && visit.ScheduledStart >= start
                && visit.ScheduledStart < end
            select new { order.BranchId, visit.Status, visit.ScheduledStart, visit.ArrivalWindowEnd, visit.ActualStartedAt })
            .ToListAsync(cancellationToken);

        return [.. rows.Select(row => new BusinessArrivalFact(
            row.BranchId, new DashboardArrival(row.Status, row.ScheduledStart!.Value, row.ArrivalWindowEnd, row.ActualStartedAt)))];
    }

    // BR-15 rating: review count and rating sum per branch of the work order, for reviews created in the period.
    private async Task<List<BusinessReviewFact>> LoadReviewsAsync(
        Guid organizationId, IQueryable<WorkOrder> orders, DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken)
    {
        var rows = await (
            from review in dbContext.InvoiceReviews.AsNoTracking()
            join order in orders on review.WorkOrderId equals order.Id
            where review.OrganizationId == organizationId && review.CreatedAt >= start && review.CreatedAt < end
            group review by order.BranchId into branch
            select new { BranchId = branch.Key, Count = branch.Count(), Sum = branch.Sum(review => (int)review.Rating) })
            .ToListAsync(cancellationToken);

        return [.. rows.Select(row => new BusinessReviewFact(row.BranchId, row.Count, row.Sum))];
    }

    // BR-17: the active technicians of the scope with their schedule and scheduled assessments across the previous period,
    // the current period and the next 7 days.
    private async Task<List<BusinessTechnician>> LoadTechniciansAsync(
        Guid organizationId, BusinessOverviewQuery query, TimeZoneInfo zone, CancellationToken cancellationToken)
    {
        var branchId = query.BranchId;
        var profiles = await (
            from profile in dbContext.TechnicianProfiles.AsNoTracking()
            join branch in dbContext.Branches.AsNoTracking() on profile.BranchId equals branch.Id
            where profile.OrganizationId == organizationId
                && branch.OrganizationId == organizationId
                && profile.Status == TechnicianStatus.Active
                && (branchId == null || profile.BranchId == branchId)
            select new { profile.Id, profile.Status, BranchTimezone = branch.Timezone })
            .ToListAsync(cancellationToken);

        if (profiles.Count == 0)
        {
            return [];
        }

        var last = query.Period.To > query.Today.AddDays(6) ? query.Period.To : query.Today.AddDays(6);
        var low = OrganizationTime.StartOfDayUtc(query.Period.PreviousFrom, zone);
        var high = OrganizationTime.StartOfDayUtc(last.AddDays(1), zone);
        var organizationTimezone = query.TimezoneId;

        var schedules = await TechnicianScheduleLoader.LoadAsync(
            dbContext,
            organizationId,
            [.. profiles.Select(profile => new ScheduleSubject(profile.Id, profile.Status, profile.BranchTimezone))],
            organizationTimezone,
            string.Empty,
            low,
            high,
            onlyActiveExceptions: true,
            cancellationToken);

        var ids = profiles.Select(profile => profile.Id).ToArray();
        var assessments = await dbContext.Assessments.AsNoTracking()
            .Where(assessment => assessment.OrganizationId == organizationId
                && assessment.TechnicianId != null
                && ids.Contains(assessment.TechnicianId.Value)
                && assessment.Status == AssessmentStatus.Scheduled
                && assessment.ScheduledStart >= low
                && assessment.ScheduledStart < high)
            .Select(assessment => new { TechnicianId = assessment.TechnicianId!.Value, assessment.ScheduledStart, assessment.ScheduledEnd })
            .ToListAsync(cancellationToken);

        return [.. profiles.Select(profile => new BusinessTechnician(
            profile.Id,
            schedules[profile.Id],
            [.. assessments
                .Where(row => row.TechnicianId == profile.Id)
                .Select(row => new DashboardInterval(row.ScheduledStart, row.ScheduledEnd))]))];
    }
}
