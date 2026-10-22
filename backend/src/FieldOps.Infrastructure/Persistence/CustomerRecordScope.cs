using FieldOps.Application.Features.Access;
using FieldOps.Domain.Invoices;
using FieldOps.Domain.Quotes;
using FieldOps.Domain.Requests;
using FieldOps.Domain.WorkOrders;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Persistence;

/// <summary>
/// The single definition of record visibility for one customer (customer-related-records BR-02): the organization first,
/// then the customer, then the branch scope of the caller. Every list, count, summary, event and Overview value of the
/// customer page derives from these queries, so a record that is hidden in one place is hidden everywhere.
/// </summary>
internal static class CustomerRecordScope
{
    /// <summary>Requests of the customer whose branch is null or in scope.</summary>
    public static IQueryable<ServiceRequest> VisibleRequests(
        this FieldOpsDbContext dbContext, Guid organizationId, BranchScope scope, Guid customerId)
    {
        var requests = dbContext.ServiceRequests.AsNoTracking()
            .Where(request => request.OrganizationId == organizationId && request.CustomerId == customerId);

        if (scope.All)
        {
            return requests;
        }

        var ids = scope.BranchIds.ToArray();

        return requests.Where(request => request.BranchId == null || ids.Contains(request.BranchId.Value));
    }

    /// <summary>Quotes whose request is visible.</summary>
    public static IQueryable<Quote> VisibleQuotes(
        this FieldOpsDbContext dbContext, Guid organizationId, BranchScope scope, Guid customerId)
    {
        var requests = dbContext.VisibleRequests(organizationId, scope, customerId);

        return dbContext.Quotes.AsNoTracking()
            .Where(quote => quote.OrganizationId == organizationId && requests.Any(request => request.Id == quote.RequestId));
    }

    /// <summary>Non-draft jobs of the customer inside the branch scope.</summary>
    public static IQueryable<WorkOrder> VisibleJobs(
        this FieldOpsDbContext dbContext, Guid organizationId, BranchScope scope, Guid customerId)
    {
        var jobs = dbContext.WorkOrders.AsNoTracking()
            .Where(order => order.OrganizationId == organizationId
                && order.CustomerId == customerId
                && order.Status != WorkOrderStatus.Draft);

        if (scope.All)
        {
            return jobs;
        }

        var ids = scope.BranchIds.ToArray();

        return jobs.Where(order => ids.Contains(order.BranchId));
    }

    /// <summary>Non-void invoices of the customer inside the branch scope.</summary>
    public static IQueryable<Invoice> VisibleInvoices(
        this FieldOpsDbContext dbContext, Guid organizationId, BranchScope scope, Guid customerId)
    {
        var invoices = dbContext.Invoices.AsNoTracking()
            .Where(invoice => invoice.OrganizationId == organizationId
                && invoice.CustomerId == customerId
                && invoice.Status != InvoiceStatus.Void);

        if (scope.All)
        {
            return invoices;
        }

        var ids = scope.BranchIds.ToArray();

        return invoices.Where(invoice => ids.Contains(invoice.BranchId));
    }

    /// <summary>Visits whose job is visible.</summary>
    public static IQueryable<Visit> VisibleVisits(
        this FieldOpsDbContext dbContext, Guid organizationId, BranchScope scope, Guid customerId)
    {
        var jobs = dbContext.VisibleJobs(organizationId, scope, customerId);

        return dbContext.Visits.AsNoTracking()
            .Where(visit => visit.OrganizationId == organizationId && jobs.Any(order => order.Id == visit.WorkOrderId));
    }

    /// <summary>Payments with at least one allocation to a visible invoice.</summary>
    public static IQueryable<Payment> VisiblePayments(
        this FieldOpsDbContext dbContext, Guid organizationId, BranchScope scope, Guid customerId)
    {
        var invoices = dbContext.VisibleInvoices(organizationId, scope, customerId);

        return dbContext.Payments.AsNoTracking()
            .Where(payment => payment.OrganizationId == organizationId
                && dbContext.PaymentAllocations.Any(allocation => allocation.PaymentId == payment.Id
                    && invoices.Any(invoice => invoice.Id == allocation.InvoiceId)));
    }
}
