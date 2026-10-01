using System.Net;
using FieldOps.Application.Features.Access;

namespace FieldOps.Application.Features.Customers;

/// <summary>
/// Persistence of the customer detail page: properties, derived overview data, notes and activity. Every
/// method resolves the customer inside the organization and the caller's branch scope first; a hidden
/// customer, or a property of another customer, is reported as null or <see cref="CustomerPropertyOutcome.NotFound"/>.
/// Derived data covers all branches of a visible customer.
/// </summary>
public interface ICustomerDetailStore
{
    Task<bool> IsCustomerVisibleAsync(
        Guid organizationId, BranchScope scope, Guid customerId, CancellationToken cancellationToken);

    Task<CustomerOverviewData?> GetOverviewAsync(
        Guid organizationId, BranchScope scope, Guid customerId, CancellationToken cancellationToken);

    /// <summary>Active properties first (primary, then name, then id), then archived ones.</summary>
    Task<IReadOnlyList<CustomerPropertyView>?> ListPropertiesAsync(
        Guid organizationId, BranchScope scope, Guid customerId, DateTimeOffset now, CancellationToken cancellationToken);

    Task<CustomerPropertyView?> GetPropertyAsync(
        Guid organizationId,
        BranchScope scope,
        Guid customerId,
        Guid propertyId,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<CustomerPropertyState?> GetPropertyStateAsync(
        Guid organizationId, BranchScope scope, Guid customerId, Guid propertyId, CancellationToken cancellationToken);

    /// <summary>
    /// Adds the property (primary when the customer has no active primary) and its audit row in one save.
    /// Null when the customer is not visible.
    /// </summary>
    Task<CustomerPropertyView?> CreatePropertyAsync(
        Guid organizationId,
        BranchScope scope,
        Guid customerId,
        CustomerPropertyValues values,
        string countryCode,
        Guid actorUserId,
        IPAddress? clientIp,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    /// <summary>Edits an active property; a no-op writes no audit row.</summary>
    Task<CustomerPropertyChange> UpdatePropertyAsync(
        Guid organizationId,
        BranchScope scope,
        Guid customerId,
        Guid propertyId,
        CustomerPropertyValues values,
        Guid actorUserId,
        IPAddress? clientIp,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    /// <summary>Set primary, archive or reactivate, each with its audit row in one transaction.</summary>
    Task<CustomerPropertyOutcome> ChangePropertyStateAsync(
        Guid organizationId,
        BranchScope scope,
        Guid customerId,
        Guid propertyId,
        CustomerPropertyAction action,
        Guid actorUserId,
        IPAddress? clientIp,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<CustomerRecentWorkItem>?> ListRecentWorkAsync(
        Guid organizationId, BranchScope scope, Guid customerId, CancellationToken cancellationToken);

    Task<IReadOnlyList<CustomerUpcomingAppointment>?> ListUpcomingAppointmentsAsync(
        Guid organizationId, BranchScope scope, Guid customerId, DateTimeOffset now, CancellationToken cancellationToken);

    Task<CustomerNotesPage?> ListNotesAsync(
        Guid organizationId, BranchScope scope, Guid customerId, int page, string timezone, CancellationToken cancellationToken);

    /// <summary>Appends the note and its audit row (without the text) in one save.</summary>
    Task<CustomerNoteView?> AddNoteAsync(
        Guid organizationId,
        BranchScope scope,
        Guid customerId,
        string note,
        Guid actorUserId,
        IPAddress? clientIp,
        CancellationToken cancellationToken);

    Task<CustomerActivityPage?> ListActivityAsync(
        Guid organizationId, BranchScope scope, Guid customerId, int page, string timezone, CancellationToken cancellationToken);
}
