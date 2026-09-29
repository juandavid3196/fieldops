using System.Net;

namespace FieldOps.Application.Features.Organizations;

/// <summary>The stored logo bytes served by the logo endpoint (BR-08).</summary>
public sealed record OrganizationLogoContent(string ContentType, byte[] Content, DateTimeOffset UpdatedAt);

/// <summary>
/// Persistence operations for the organization logo (FR-09). Every
/// operation is scoped by the organization id from the session; logo
/// operations never touch <c>organizations.updated_at</c>.
/// </summary>
public interface IOrganizationLogoStore
{
    /// <summary>The logo bytes, or null when the organization has none.</summary>
    Task<OrganizationLogoContent?> GetAsync(Guid organizationId, CancellationToken cancellationToken);

    /// <summary>
    /// Creates or replaces the logo and writes exactly one
    /// <c>organization.logo_updated</c> audit row in the same save. A
    /// primary-key race between two first uploads is retried once as a replace.
    /// </summary>
    Task<OrganizationLogoMetadata> UpsertAsync(
        Guid organizationId,
        string contentType,
        byte[] content,
        Guid actorUserId,
        IPAddress? clientIp,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the logo and writes one <c>organization.logo_removed</c> audit
    /// row in the same save. Returns <c>false</c>, with no audit row, when
    /// there was no logo.
    /// </summary>
    Task<bool> DeleteAsync(
        Guid organizationId,
        Guid actorUserId,
        IPAddress? clientIp,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}
