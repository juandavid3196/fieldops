using FieldOps.Domain.Branches;
using FieldOps.Domain.Notifications;
using FieldOps.Domain.Organizations;
using FieldOps.Domain.Users;

namespace FieldOps.Application.Features.Organizations;

/// <summary>
/// Persistence operations needed by organization registration.
/// </summary>
public interface IOrganizationRegistrationStore
{
    /// <summary>Whether a user with this normalized email already exists.</summary>
    Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken cancellationToken);

    /// <summary>Existing public slugs that start with the prefix (public request BR-21 uniqueness lookup).</summary>
    Task<IReadOnlySet<string>> FindSlugsStartingWithAsync(string prefix, CancellationToken cancellationToken);

    /// <summary>The id of the seeded <c>owner</c> role, or null when missing.</summary>
    Task<short?> FindOwnerRoleIdAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Saves every registration row in one <c>SaveChangesAsync</c> call, so
    /// either all six rows are written or none is (FR-04, FR-09, AC-19).
    /// Throws <see cref="DuplicateEmailException"/> when a concurrent
    /// request wins the unique-constraint race on the owner's email, and
    /// <see cref="DuplicatePublicSlugException"/> when it took the same public slug.
    /// </summary>
    Task SaveRegistrationAsync(
        Organization organization,
        Branch branch,
        User user,
        OrganizationUser membership,
        OrganizationUserBranch membershipBranch,
        AuditLog auditLog,
        CancellationToken cancellationToken);
}
