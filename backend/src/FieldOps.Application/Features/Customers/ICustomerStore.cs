using System.Net;
using FieldOps.Application.Features.Access;
using FieldOps.Domain.Customers;

namespace FieldOps.Application.Features.Customers;

/// <summary>
/// Persistence for customers and their tags. Every query filters by the session organization first and
/// then by the caller's branch scope; a customer outside either is reported as not found.
/// </summary>
public interface ICustomerStore
{
    Task<CustomerOrganizationContext?> GetOrganizationContextAsync(Guid organizationId, CancellationToken cancellationToken);

    /// <summary>Active branches of the organization inside the scope, ordered by name.</summary>
    Task<IReadOnlyList<CustomerBranchOption>> ListBranchOptionsAsync(
        Guid organizationId, BranchScope scope, CancellationToken cancellationToken);

    /// <summary>True when the branch exists in the organization and the scope, and (for writes) is active.</summary>
    Task<bool> IsBranchAllowedAsync(
        Guid organizationId, BranchScope scope, Guid branchId, bool requireActive, CancellationToken cancellationToken);

    /// <summary>Active in-scope branches by lower-cased code.</summary>
    Task<IReadOnlyDictionary<string, Guid>> FindActiveBranchesByCodeAsync(
        Guid organizationId, BranchScope scope, IReadOnlyCollection<string> lowerCodes, CancellationToken cancellationToken);

    /// <summary>How many of the ids are tags of the organization.</summary>
    Task<int> CountTagsAsync(Guid organizationId, IReadOnlyCollection<Guid> tagIds, CancellationToken cancellationToken);

    Task<CustomerListData> ListAsync(
        Guid organizationId,
        BranchScope scope,
        CustomerListFilter filter,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<CustomerMetricsData> GetMetricsAsync(
        Guid organizationId,
        BranchScope scope,
        Guid? branchId,
        DateTimeOffset monthStartUtc,
        DateTimeOffset nextMonthStartUtc,
        CancellationToken cancellationToken);

    Task<CustomerDetail?> GetDetailAsync(
        Guid organizationId, BranchScope scope, Guid customerId, CancellationToken cancellationToken);

    /// <summary>The type of an in-scope customer, or null when it is not visible.</summary>
    Task<CustomerType?> GetTypeAsync(
        Guid organizationId, BranchScope scope, Guid customerId, CancellationToken cancellationToken);

    /// <summary>Adds the customer, primary contact, first property, tag assignments and audit row in one save.</summary>
    Task<Guid> CreateAsync(
        Guid organizationId,
        CustomerWrite write,
        Guid actorUserId,
        IPAddress? clientIp,
        CancellationToken cancellationToken);

    /// <summary>
    /// Updates the customer, primary contact, first property and tags in one save; a no-op writes
    /// nothing. Null when the customer is not visible.
    /// </summary>
    Task<CustomerDetail?> UpdateAsync(
        Guid organizationId,
        BranchScope scope,
        Guid customerId,
        CustomerValues values,
        Guid branchId,
        IReadOnlyList<Guid> tagIds,
        Guid actorUserId,
        IPAddress? clientIp,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<CustomerStateOutcome> SetActiveAsync(
        Guid organizationId,
        BranchScope scope,
        Guid customerId,
        bool isActive,
        Guid actorUserId,
        IPAddress? clientIp,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    /// <summary>Up to three distinct customers of the organization (any scope, archived included) sharing the email or phone.</summary>
    Task<IReadOnlyList<CustomerDuplicateRow>> FindDuplicatesAsync(
        Guid organizationId,
        string? email,
        string? phone,
        Guid? excludeCustomerId,
        CancellationToken cancellationToken);

    /// <summary>Existing contacts of the organization with one of the emails or phones, ordered by customer name then id.</summary>
    Task<IReadOnlyList<CustomerContactMatch>> FindContactMatchesAsync(
        Guid organizationId,
        IReadOnlyCollection<string> emails,
        IReadOnlyCollection<string> phones,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<CustomerTagView>> ListTagsAsync(Guid organizationId, CancellationToken cancellationToken);

    /// <summary>The existing case-insensitive match, or a new tag with its audit row. Created is true only on a real insert.</summary>
    Task<(CustomerTagView Tag, bool Created)> GetOrCreateTagAsync(
        Guid organizationId, string name, Guid actorUserId, IPAddress? clientIp, CancellationToken cancellationToken);

    /// <summary>
    /// Adds every row (resolving or creating its tags) and one <c>customer.imported</c> audit row in a
    /// single save.
    /// </summary>
    Task<int> ImportAsync(
        Guid organizationId,
        IReadOnlyList<CustomerImportRow> rows,
        string countryCode,
        Guid actorUserId,
        IPAddress? clientIp,
        CancellationToken cancellationToken);
}
