using FieldOps.Application.Features.Access;
using FieldOps.Application.Features.Customers;
using FieldOps.Application.Features.InvoicePayments;
using FieldOps.Application.Features.ServiceRequests;
using FieldOps.Application.Features.WorkOrders;
using FieldOps.Domain.Invoices;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Persistence;

internal sealed partial class CustomerRecordStore
{
    // The visible jobs of one customer are read with a few narrow commands and derived in memory (current visit,
    // operational status, next visit, technician): the status and technician filters depend on that derivation.
    public async Task<CustomerRecordSlice<CustomerJobRow>> ListJobsAsync(
        Guid organizationId,
        BranchScope scope,
        Guid customerId,
        CustomerRecordQuery query,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var organization = await ReadOrganizationAsync(organizationId, cancellationToken);
        var jobs = dbContext.VisibleJobs(organizationId, scope, customerId);

        var orders = await (
            from order in jobs
            join property in dbContext.Properties.AsNoTracking()
                on new { order.OrganizationId, Id = order.PropertyId } equals new { property.OrganizationId, property.Id }
            select new
            {
                order.Id,
                order.WorkOrderNumber,
                order.Title,
                order.Status,
                order.CreatedAt,
                PropertyName = property.Name,
            })
            .ToListAsync(cancellationToken);

        var customerTotal = orders.Count;

        if (customerTotal == 0)
        {
            return new CustomerRecordSlice<CustomerJobRow>([], 0, 0);
        }

        var visits = (await dbContext.VisibleVisits(organizationId, scope, customerId)
            .Select(visit => new
            {
                visit.Id,
                visit.WorkOrderId,
                visit.VisitNumber,
                visit.Status,
                visit.ScheduledStart,
                visit.ScheduledEnd,
                visit.ActualCompletedAt,
            })
            .ToListAsync(cancellationToken))
            .ToLookup(
                visit => visit.WorkOrderId,
                visit => new CustomerJobVisit(
                    visit.Id, visit.VisitNumber, visit.Status, visit.ScheduledStart, visit.ScheduledEnd, visit.ActualCompletedAt));

        var visibleVisits = dbContext.VisibleVisits(organizationId, scope, customerId);

        var technicians = (await (
            from assignment in dbContext.VisitAssignments.AsNoTracking()
            join technician in dbContext.TechnicianProfiles.AsNoTracking() on assignment.TechnicianId equals technician.Id
            where assignment.IsPrimary
                && assignment.UnassignedAt == null
                && technician.OrganizationId == organizationId
                && visibleVisits.Any(visit => visit.Id == assignment.VisitId)
            orderby assignment.AssignedAt, assignment.Id
            select new { assignment.VisitId, technician.FirstName, technician.LastName })
            .ToListAsync(cancellationToken))
            .GroupBy(row => row.VisitId)
            .ToDictionary(group => group.Key, group => $"{group.First().FirstName} {group.First().LastName}".Trim());

        var derived = orders.Select(order =>
        {
            var orderVisits = visits[order.Id].ToList();
            var current = CustomerJobRules.CurrentVisit(orderVisits);
            var status = CustomerJobRules.Status(
                order.Status, current, orderVisits.Any(visit => visit.Status == FieldOps.Domain.WorkOrders.VisitStatus.NeedsCorrection));
            var (nextVisitAt, completedAt) = CustomerJobRules.VisitColumn(status, orderVisits, now);
            var technician = current is not null && technicians.TryGetValue(current.Id, out var name) ? name : null;

            return new { Order = order, Status = status, NextVisitAt = nextVisitAt, CompletedAt = completedAt, Technician = technician };
        });

        if (query.Status is { } code && JobsRules.TryParseStatus(code, JobsTab.All, out var wanted) && wanted is { } status)
        {
            derived = derived.Where(row => row.Status == status);
        }

        if (query.Search is { } term)
        {
            var number = RequestSearch.TryParseNumber(term, organization.WorkOrderPrefix, out var parsed) ? parsed : -1L;

            derived = derived.Where(row =>
                row.Order.WorkOrderNumber == number
                || row.Order.Title.Contains(term, StringComparison.OrdinalIgnoreCase)
                || row.Order.PropertyName.Contains(term, StringComparison.OrdinalIgnoreCase)
                || (row.Technician is not null && row.Technician.Contains(term, StringComparison.OrdinalIgnoreCase)));
        }

        var filtered = derived.ToList();

        // Ids compare as lower-case hex text, the same order as PostgreSQL's uuid comparison.
        var page = filtered
            .OrderByDescending(row => row.Order.CreatedAt)
            .ThenBy(row => row.Order.Id.ToString("D"), StringComparer.Ordinal)
            .Skip(query.Skip)
            .Take(query.PageSize)
            .Select(row => new CustomerJobRow(
                row.Order.Id,
                RequestCardRules.DisplayNumber(organization.WorkOrderPrefix, row.Order.WorkOrderNumber),
                row.Order.Title,
                row.Order.PropertyName,
                row.NextVisitAt,
                row.CompletedAt,
                row.Technician is null ? null : new CustomerJobTechnician(row.Technician, RequestCardRules.Initials(row.Technician)),
                JobsRules.StatusCode(row.Status)))
            .ToList();

        return new CustomerRecordSlice<CustomerJobRow>(page, filtered.Count, customerTotal);
    }

    public async Task<CustomerInvoiceSlice> ListInvoicesAsync(
        Guid organizationId,
        BranchScope scope,
        Guid customerId,
        CustomerRecordQuery query,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var organization = await ReadOrganizationAsync(organizationId, cancellationToken);
        var today = OrganizationTime.LocalDate(now, OrganizationTime.FindZone(organization.Timezone));
        var visible = dbContext.VisibleInvoices(organizationId, scope, customerId);
        var customerTotal = await visible.CountAsync(cancellationToken);

        // BR-08: Outstanding, Overdue and Paid are summed over every visible invoice, whatever search, status or page.
        var outstanding = await visible
            .Where(invoice => invoice.Status == InvoiceStatus.Sent
                || invoice.Status == InvoiceStatus.PartiallyPaid
                || invoice.Status == InvoiceStatus.Overdue)
            .SumAsync(invoice => invoice.BalanceDue, cancellationToken);
        var overdue = await visible
            .Where(invoice => invoice.Status == InvoiceStatus.Overdue
                || ((invoice.Status == InvoiceStatus.Sent || invoice.Status == InvoiceStatus.PartiallyPaid) && invoice.DueDate < today))
            .SumAsync(invoice => invoice.BalanceDue, cancellationToken);
        var paid = await visible.SumAsync(invoice => invoice.AmountPaid, cancellationToken);
        var summary = new CustomerInvoicesSummary(outstanding, overdue, paid);

        var rows =
            from invoice in visible
            join order in dbContext.WorkOrders.AsNoTracking()
                on new { invoice.OrganizationId, Id = invoice.WorkOrderId } equals new { order.OrganizationId, order.Id }
            select new InvoiceData
            {
                Id = invoice.Id,
                InvoiceNumber = invoice.InvoiceNumber,
                WorkOrderId = order.Id,
                WorkOrderNumber = order.WorkOrderNumber,
                IssueDate = invoice.IssueDate,
                DueDate = invoice.DueDate,
                BalanceDue = invoice.BalanceDue,
                Status = invoice.Status,
                CreatedAt = invoice.CreatedAt,
            };

        // The BR-03 display status of the invoices hub at the organization-local date.
        rows = query.Status switch
        {
            InvoiceStatusFilters.Draft => rows.Where(row => row.Status == InvoiceStatus.Draft),
            InvoiceStatusFilters.Paid => rows.Where(row => row.Status == InvoiceStatus.Paid),
            InvoiceStatusFilters.Sent => rows.Where(row =>
                row.Status == InvoiceStatus.Sent && (row.DueDate == null || row.DueDate >= today)),
            InvoiceStatusFilters.PartiallyPaid => rows.Where(row =>
                row.Status == InvoiceStatus.PartiallyPaid && (row.DueDate == null || row.DueDate >= today)),
            InvoiceStatusFilters.Overdue => rows.Where(row =>
                row.Status == InvoiceStatus.Overdue
                || ((row.Status == InvoiceStatus.Sent || row.Status == InvoiceStatus.PartiallyPaid) && row.DueDate < today)),
            _ => rows,
        };

        if (query.Search is { } term)
        {
            var invoiceNumber = RequestSearch.TryParseNumber(term, organization.InvoicePrefix, out var parsedInvoice) ? parsedInvoice : -1L;
            var jobNumber = RequestSearch.TryParseNumber(term, organization.WorkOrderPrefix, out var parsedJob) ? parsedJob : -1L;

            rows = rows.Where(row => row.InvoiceNumber == invoiceNumber || row.WorkOrderNumber == jobNumber);
        }

        var total = await rows.CountAsync(cancellationToken);
        var page = await rows
            .OrderByDescending(row => row.IssueDate)
            .ThenByDescending(row => row.CreatedAt)
            .ThenBy(row => row.Id)
            .Skip(query.Skip)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        return new CustomerInvoiceSlice(
            [.. page.Select(row => new CustomerInvoiceRow(
                row.Id,
                InvoiceHubRules.DisplayNumber(organization.InvoicePrefix, row.InvoiceNumber),
                new CustomerInvoiceJobRef(row.WorkOrderId, RequestCardRules.DisplayNumber(organization.WorkOrderPrefix, row.WorkOrderNumber)),
                row.IssueDate,
                row.DueDate,
                row.BalanceDue,
                InvoiceHubRules.Display(row.Status, row.DueDate, today).Status))],
            total,
            customerTotal,
            summary);
    }

    private sealed class InvoiceData
    {
        public Guid Id { get; set; }

        public long InvoiceNumber { get; set; }

        public Guid WorkOrderId { get; set; }

        public long WorkOrderNumber { get; set; }

        public DateOnly? IssueDate { get; set; }

        public DateOnly? DueDate { get; set; }

        public decimal BalanceDue { get; set; }

        public InvoiceStatus Status { get; set; }

        public DateTimeOffset CreatedAt { get; set; }
    }
}
