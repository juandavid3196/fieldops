using FieldOps.Application.Features.PortalAccess;
using FieldOps.Domain.Customers;
using FieldOps.Domain.Invoices;
using FieldOps.Domain.Quotes;
using FieldOps.Domain.Requests;
using FieldOps.Domain.WorkOrders;

namespace FieldOps.Infrastructure.Persistence.Portal;

/// <summary>
/// The customer-scoped query layer of the portal (customer portal BR-37). Every portal read starts from one of these
/// extensions, so the organization and customer of the validated session are always part of the query; a path or body id
/// that is not the customer's simply matches nothing and becomes the one identical 404. The scope is built in the API from
/// the revalidated session only, never from a request value.
/// </summary>
internal static class PortalScopeExtensions
{
    public static IQueryable<Property> ForPortal(this IQueryable<Property> properties, PortalScope scope)
    {
        var organizationId = scope.OrganizationId;
        var customerId = scope.CustomerId;

        return properties.Where(property => property.OrganizationId == organizationId && property.CustomerId == customerId);
    }

    public static IQueryable<ServiceRequest> ForPortal(this IQueryable<ServiceRequest> requests, PortalScope scope)
    {
        var organizationId = scope.OrganizationId;
        var customerId = scope.CustomerId;

        return requests.Where(request => request.OrganizationId == organizationId && request.CustomerId == customerId);
    }

    public static IQueryable<WorkOrder> ForPortal(this IQueryable<WorkOrder> orders, PortalScope scope)
    {
        var organizationId = scope.OrganizationId;
        var customerId = scope.CustomerId;

        return orders.Where(order => order.OrganizationId == organizationId && order.CustomerId == customerId);
    }

    public static IQueryable<Invoice> ForPortal(this IQueryable<Invoice> invoices, PortalScope scope)
    {
        var organizationId = scope.OrganizationId;
        var customerId = scope.CustomerId;

        return invoices.Where(invoice => invoice.OrganizationId == organizationId && invoice.CustomerId == customerId);
    }

    public static IQueryable<Payment> ForPortal(this IQueryable<Payment> payments, PortalScope scope)
    {
        var organizationId = scope.OrganizationId;
        var customerId = scope.CustomerId;

        return payments.Where(payment => payment.OrganizationId == organizationId && payment.CustomerId == customerId);
    }

    public static IQueryable<CustomerContact> ForPortal(this IQueryable<CustomerContact> contacts, PortalScope scope)
    {
        var organizationId = scope.OrganizationId;
        var customerId = scope.CustomerId;

        return contacts.Where(contact => contact.OrganizationId == organizationId && contact.CustomerId == customerId);
    }

    /// <summary>
    /// The quotes of the customer, reached through the customer's requests (the indexed customer path) and restricted to
    /// the customer again on the quote itself.
    /// </summary>
    public static IQueryable<Quote> ForPortal(this IQueryable<Quote> quotes, FieldOpsDbContext dbContext, PortalScope scope)
    {
        var customerId = scope.CustomerId;

        return
            from request in dbContext.ServiceRequests.ForPortal(scope)
            join quote in quotes
                on new { request.OrganizationId, request.Id } equals new { quote.OrganizationId, Id = quote.RequestId }
            where quote.CustomerId == customerId
            select quote;
    }

    /// <summary>The visits of the work orders of the customer.</summary>
    public static IQueryable<Visit> ForPortal(this IQueryable<Visit> visits, FieldOpsDbContext dbContext, PortalScope scope) =>
        from order in dbContext.WorkOrders.ForPortal(scope)
        join visit in visits
            on new { order.OrganizationId, WorkOrderId = order.Id } equals new { visit.OrganizationId, visit.WorkOrderId }
        select visit;
}
