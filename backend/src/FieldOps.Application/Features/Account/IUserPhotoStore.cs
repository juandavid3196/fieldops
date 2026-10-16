using System.Net;

namespace FieldOps.Application.Features.Account;

/// <summary>
/// Persistence for the session user's photo (BR-03, BR-04). Photo operations
/// never touch <c>users.updated_at</c>; audit rows use the session organization.
/// </summary>
public interface IUserPhotoStore
{
    Task<UserPhotoContent?> GetAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Creates or replaces the photo and writes one <c>user.photo_updated</c>
    /// audit row in the same save. A primary-key race is retried once.
    /// </summary>
    Task<UserPhotoMetadata> UpsertAsync(
        Guid userId,
        Guid organizationId,
        string contentType,
        byte[] content,
        IPAddress? clientIp,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the photo and writes one <c>user.photo_removed</c> audit row in
    /// the same save; returns false, without a row, when there was none.
    /// </summary>
    Task<bool> DeleteAsync(
        Guid userId,
        Guid organizationId,
        IPAddress? clientIp,
        CancellationToken cancellationToken);
}
