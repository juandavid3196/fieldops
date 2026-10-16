using FieldOps.Application.Features.OperationsDashboard;
using FieldOps.Application.Features.Reports;
using FieldOps.Domain.Invoices;
using FieldOps.Domain.Requests;
using FieldOps.Domain.Technicians;
using FieldOps.Domain.WorkOrders;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Persistence;

internal sealed partial class ReportsStore
{
    // Work orders completed (business-overview BR-08: latest completion among non-cancelled visits) in the previous or current period.
    private IQueryable<WorkOrder> CompletedOrders(Guid organizationId, Guid[] branchIds, Guid? categoryId, DateTimeOffset start)
    {
        var orders = dbContext.WorkOrders.AsNoTracking()
            .Where(order => order.OrganizationId == organizationId
                && branchIds.Contains(order.BranchId)
                && (order.Status == WorkOrderStatus.Completed || order.Status == WorkOrderStatus.ApprovedForBilling)
                && dbContext.Visits.Any(visit => visit.OrganizationId == organizationId
                    && visit.WorkOrderId == order.Id
                    && visit.Status != VisitStatus.Cancelled
                    && visit.ActualCompletedAt >= start));

        return categoryId is { } category ? orders.Where(order => order.ServiceCategoryId == category) : orders;
    }

    public async Task<ProfitabilitySource> LoadProfitabilityAsync(ReportContext context, CancellationToken cancellationToken)
    {
        var organizationId = context.Call.OrganizationId;
        var start = context.PreviousStart;
        var end = context.CurrentEnd;
        var orders = CompletedOrders(organizationId, context.BranchIds.ToArray(), context.Query.CategoryId, start);

        var rows = await (
            from order in orders
            join customer in dbContext.Customers.AsNoTracking()
                on new { order.OrganizationId, Id = order.CustomerId } equals new { customer.OrganizationId, customer.Id }
            join category in dbContext.ServiceCategories.AsNoTracking()
                on new { order.OrganizationId, Id = order.ServiceCategoryId } equals new { category.OrganizationId, category.Id }
            select new
            {
                order.Id,
                order.WorkOrderNumber,
                order.BranchId,
                order.CustomerId,
                CustomerName = customer.DisplayName,
                order.ServiceCategoryId,
                CategoryName = category.Name,
                order.Status,
                Last = dbContext.Visits
                    .Where(visit => visit.OrganizationId == organizationId
                        && visit.WorkOrderId == order.Id
                        && visit.Status != VisitStatus.Cancelled)
                    .Max(visit => visit.ActualCompletedAt),
            })
            .Where(row => row.Last >= start && row.Last < end)
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return new ProfitabilitySource([], [], [], []);
        }

        var orderIds = rows.Select(row => row.Id).ToArray();

        // Completed by: the active primary assignee of the visit with the latest completion (completed-jobs-review BR-03).
        var completions = await (
            from visit in dbContext.Visits.AsNoTracking()
            join assignment in dbContext.VisitAssignments.AsNoTracking() on visit.Id equals assignment.VisitId
            where visit.OrganizationId == organizationId
                && orderIds.Contains(visit.WorkOrderId)
                && visit.Status != VisitStatus.Cancelled
                && visit.ActualCompletedAt != null
                && assignment.IsPrimary
                && assignment.UnassignedAt == null
            select new { visit.WorkOrderId, visit.ActualCompletedAt, assignment.TechnicianId })
            .ToListAsync(cancellationToken);
        var completedBy = completions
            .GroupBy(item => item.WorkOrderId)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(item => item.ActualCompletedAt).First());

        var orderFacts = rows
            .Select(row => new ProfitabilityOrder(
                row.Id,
                row.WorkOrderNumber,
                row.BranchId,
                row.CustomerId,
                row.CustomerName,
                row.ServiceCategoryId,
                row.CategoryName,
                row.Status,
                row.Last!.Value,
                completedBy.TryGetValue(row.Id, out var found) && found.ActualCompletedAt == row.Last ? found.TechnicianId : null))
            .Where(order => context.Query.TechnicianId is not { } technician || order.TechnicianId == technician)
            .ToList();

        var keptIds = orderFacts.Select(order => order.OrderId).ToArray();
        var recognized = dbContext.Invoices.AsNoTracking()
            .Where(invoice => invoice.OrganizationId == organizationId
                && keptIds.Contains(invoice.WorkOrderId)
                && invoice.Status != InvoiceStatus.Draft
                && invoice.Status != InvoiceStatus.Void);

        var invoices = await recognized
            .Select(invoice => new { invoice.WorkOrderId, invoice.Id, invoice.InvoiceNumber, Revenue = invoice.Subtotal - invoice.DiscountTotal })
            .ToListAsync(cancellationToken);

        // business-overview BR-05 line cost: quantity x quote line cost, else visit material cost, else 0.
        var costs = await (
            from line in dbContext.InvoiceLines.AsNoTracking()
            join invoice in recognized on line.InvoiceId equals invoice.Id
            join quoteLine in dbContext.QuoteLines.AsNoTracking() on line.SourceQuoteLineId equals (Guid?)quoteLine.Id into quoteLines
            from quoteLine in quoteLines.DefaultIfEmpty()
            join material in dbContext.VisitMaterials.AsNoTracking() on line.SourceVisitMaterialId equals (Guid?)material.Id into materials
            from material in materials.DefaultIfEmpty()
            group new { line.Quantity, QuoteCost = (decimal?)quoteLine.UnitCost, MaterialCost = (decimal?)material.UnitCost }
                by invoice.WorkOrderId into bucket
            select new { OrderId = bucket.Key, Cost = bucket.Sum(item => item.Quantity * (item.QuoteCost ?? item.MaterialCost ?? 0m)) })
            .ToListAsync(cancellationToken);

        var technicianIds = orderFacts.Where(order => order.TechnicianId != null).Select(order => order.TechnicianId!.Value).Distinct().ToArray();
        var technicians = await dbContext.TechnicianProfiles.AsNoTracking()
            .Where(profile => profile.OrganizationId == organizationId && technicianIds.Contains(profile.Id))
            .Select(profile => new { profile.Id, profile.FirstName, profile.LastName, profile.BranchId })
            .ToListAsync(cancellationToken);

        return new ProfitabilitySource(
            orderFacts,
            [.. invoices.Select(item => new ProfitabilityInvoice(item.WorkOrderId, item.Id, item.InvoiceNumber, item.Revenue))],
            [.. costs.Select(item => new ProfitabilityCost(item.OrderId, item.Cost))],
            [.. technicians.Select(item => new ReportTechnicianName(item.Id, AuditLogStore.FullName(item.FirstName, item.LastName), item.BranchId))]);
    }

    public async Task<JobsSource> LoadJobsAsync(ReportContext context, CancellationToken cancellationToken)
    {
        var organizationId = context.Call.OrganizationId;
        var start = context.PreviousStart;
        var end = context.CurrentEnd;
        var branchIds = context.BranchIds.ToArray();
        var query = context.Query;

        var orders = dbContext.WorkOrders.AsNoTracking()
            .Where(order => order.OrganizationId == organizationId
                && branchIds.Contains(order.BranchId)
                && order.Status != WorkOrderStatus.Draft
                && order.Status != WorkOrderStatus.Cancelled);

        if (query.CategoryId is { } categoryId)
        {
            orders = orders.Where(order => order.ServiceCategoryId == categoryId);
        }

        var visits = from visit in dbContext.Visits.AsNoTracking()
                     join order in orders on visit.WorkOrderId equals order.Id
                     join customer in dbContext.Customers.AsNoTracking()
                         on new { order.OrganizationId, Id = order.CustomerId } equals new { customer.OrganizationId, customer.Id }
                     where visit.OrganizationId == organizationId
                         && visit.Status != VisitStatus.Unscheduled
                         && visit.Status != VisitStatus.Cancelled
                         && visit.ScheduledStart >= start
                         && visit.ScheduledStart < end
                     select new { Visit = visit, Order = order, CustomerName = customer.DisplayName };

        if (query.TechnicianId is { } technicianId)
        {
            visits = visits.Where(row => dbContext.VisitAssignments.Any(assignment =>
                assignment.VisitId == row.Visit.Id && assignment.TechnicianId == technicianId && assignment.UnassignedAt == null));
        }

        var rows = await visits
            .Select(row => new
            {
                row.Visit.Id,
                row.Visit.VisitNumber,
                OrderId = row.Order.Id,
                row.Order.WorkOrderNumber,
                row.Order.Title,
                row.Order.CustomerId,
                row.CustomerName,
                row.Order.ServiceCategoryId,
                row.Order.BranchId,
                row.Order.EstimatedDurationMinutes,
                row.Visit.Status,
                row.Visit.ScheduledStart,
                row.Visit.ArrivalWindowEnd,
                row.Visit.ActualStartedAt,
                row.Visit.ActualCompletedAt,
                row.Visit.PauseSeconds,
            })
            .ToListAsync(cancellationToken);

        var visitIds = rows.Select(row => row.Id).ToArray();
        var primaries = visitIds.Length == 0
            ? []
            : await (
                from assignment in dbContext.VisitAssignments.AsNoTracking()
                join profile in dbContext.TechnicianProfiles.AsNoTracking() on assignment.TechnicianId equals profile.Id
                where visitIds.Contains(assignment.VisitId)
                    && assignment.IsPrimary
                    && assignment.UnassignedAt == null
                    && profile.OrganizationId == organizationId
                select new { assignment.VisitId, profile.Id, profile.FirstName, profile.LastName, profile.BranchId })
                .ToListAsync(cancellationToken);
        var byVisit = primaries.GroupBy(item => item.VisitId).ToDictionary(group => group.Key, group => group.First());

        // business-overview BR-08 for the same window and branch, with the category filter only.
        var completions = await CompletedOrders(organizationId, branchIds, query.CategoryId, start)
            .Select(order => dbContext.Visits
                .Where(visit => visit.OrganizationId == organizationId
                    && visit.WorkOrderId == order.Id
                    && visit.Status != VisitStatus.Cancelled)
                .Max(visit => visit.ActualCompletedAt))
            .ToListAsync(cancellationToken);

        return new JobsSource(
            [.. rows.Select(row =>
            {
                var primary = byVisit.GetValueOrDefault(row.Id);

                return new JobVisit(
                    row.Id,
                    row.VisitNumber,
                    row.OrderId,
                    row.WorkOrderNumber,
                    row.Title,
                    row.CustomerId,
                    row.CustomerName,
                    row.ServiceCategoryId,
                    row.BranchId,
                    row.EstimatedDurationMinutes,
                    row.Status,
                    row.ScheduledStart!.Value,
                    row.ArrivalWindowEnd,
                    row.ActualStartedAt,
                    row.ActualCompletedAt,
                    row.PauseSeconds,
                    primary?.Id,
                    primary is null ? null : AuditLogStore.FullName(primary.FirstName, primary.LastName),
                    primary?.BranchId);
            })],
            [.. completions.Where(last => last >= start && last < end).Select(last => last!.Value)]);
    }

    public async Task<UtilizationSource> LoadUtilizationAsync(ReportContext context, CancellationToken cancellationToken)
    {
        var organizationId = context.Call.OrganizationId;
        var branchIds = context.BranchIds.ToArray();
        var low = context.PreviousStart;
        var high = context.CurrentEnd;

        var profilesQuery =
            from profile in dbContext.TechnicianProfiles.AsNoTracking()
            join branch in dbContext.Branches.AsNoTracking() on profile.BranchId equals branch.Id
            where profile.OrganizationId == organizationId
                && branch.OrganizationId == organizationId
                && profile.Status == TechnicianStatus.Active
                && branchIds.Contains(profile.BranchId)
            select new { profile.Id, profile.FirstName, profile.LastName, profile.BranchId, profile.Status, BranchTimezone = branch.Timezone };

        if (context.Query.TechnicianId is { } technicianId)
        {
            profilesQuery = profilesQuery.Where(profile => profile.Id == technicianId);
        }

        var profiles = await profilesQuery.ToListAsync(cancellationToken);

        if (profiles.Count == 0)
        {
            return new UtilizationSource([], []);
        }

        var ids = profiles.Select(profile => profile.Id).ToArray();
        var schedules = await TechnicianScheduleLoader.LoadAsync(
            dbContext,
            organizationId,
            [.. profiles.Select(profile => new ScheduleSubject(profile.Id, profile.Status, profile.BranchTimezone))],
            context.Organization.Timezone,
            string.Empty,
            low,
            high,
            onlyActiveExceptions: true,
            cancellationToken);

        var assessments = await dbContext.Assessments.AsNoTracking()
            .Where(assessment => assessment.OrganizationId == organizationId
                && assessment.TechnicianId != null
                && ids.Contains(assessment.TechnicianId.Value)
                && assessment.Status == AssessmentStatus.Scheduled
                && assessment.ScheduledStart >= low
                && assessment.ScheduledStart < high)
            .Select(assessment => new { TechnicianId = assessment.TechnicianId!.Value, assessment.ScheduledStart, assessment.ScheduledEnd })
            .ToListAsync(cancellationToken);

        var currentStart = context.CurrentStart;
        var visits = await (
            from assignment in dbContext.VisitAssignments.AsNoTracking()
            join visit in dbContext.Visits.AsNoTracking() on assignment.VisitId equals visit.Id
            join order in dbContext.WorkOrders.AsNoTracking() on visit.WorkOrderId equals order.Id
            where assignment.UnassignedAt == null
                && ids.Contains(assignment.TechnicianId)
                && visit.OrganizationId == organizationId
                && order.OrganizationId == organizationId
                && order.Status != WorkOrderStatus.Draft
                && order.Status != WorkOrderStatus.Cancelled
                && visit.Status != VisitStatus.Unscheduled
                && visit.Status != VisitStatus.Cancelled
                && ((visit.ScheduledStart >= currentStart && visit.ScheduledStart < high)
                    || (visit.ActualCompletedAt >= currentStart && visit.ActualCompletedAt < high))
            select new { assignment.TechnicianId, visit.Status, visit.ScheduledStart, visit.ArrivalWindowEnd, visit.ActualStartedAt, visit.ActualCompletedAt })
            .ToListAsync(cancellationToken);

        return new UtilizationSource(
            [.. profiles.Select(profile => new UtilizationTechnician(
                profile.Id,
                AuditLogStore.FullName(profile.FirstName, profile.LastName),
                profile.BranchId,
                schedules[profile.Id],
                [.. assessments
                    .Where(row => row.TechnicianId == profile.Id)
                    .Select(row => new DashboardInterval(row.ScheduledStart, row.ScheduledEnd))]))],
            [.. visits.Select(row => new UtilizationVisit(
                row.TechnicianId, row.Status, row.ScheduledStart!.Value, row.ArrivalWindowEnd, row.ActualStartedAt, row.ActualCompletedAt))]);
    }
}
