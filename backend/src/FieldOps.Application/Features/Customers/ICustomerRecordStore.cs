using FieldOps.Application.Features.Access;

namespace FieldOps.Application.Features.Customers;

/// <summary>
/// Read model of the related-record tabs of the customer page (customer-related-records BR-02, BR-04 to BR-08). Every
/// method filters the organization first, then the customer and the record visibility of the caller's branch scope; the
/// handler has already resolved the customer as visible. Counts and sums come only from visible records.
/// </summary>
public interface ICustomerRecordStore
{
    Task<CustomerRecordSlice<CustomerRequestRow>> ListRequestsAsync(
        Guid organizationId, BranchScope scope, Guid customerId, CustomerRecordQuery query, CancellationToken cancellationToken);

    Task<CustomerRecordSlice<CustomerQuoteRow>> ListQuotesAsync(
        Guid organizationId,
        BranchScope scope,
        Guid customerId,
        CustomerRecordQuery query,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<CustomerRecordSlice<CustomerJobRow>> ListJobsAsync(
        Guid organizationId,
        BranchScope scope,
        Guid customerId,
        CustomerRecordQuery query,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<CustomerInvoiceSlice> ListInvoicesAsync(
        Guid organizationId,
        BranchScope scope,
        Guid customerId,
        CustomerRecordQuery query,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

/// <summary>Read model of the Activity tab: whitelisted audit events of the customer's visible records (BR-10 to BR-13).</summary>
public interface ICustomerActivityStore
{
    /// <summary>Up to <see cref="CustomerActivityRules.BlockSize"/> events newest first, plus the cursor of the next block.</summary>
    Task<CustomerActivityBlock> ListAsync(
        Guid organizationId,
        BranchScope scope,
        Guid customerId,
        CustomerRecordAccess access,
        CustomerActivityQuery query,
        CancellationToken cancellationToken);
}
