using System.Net;

namespace FieldOps.Application.Features.Account;

/// <summary>
/// Persistence for the session user's own account. Every operation acts on
/// the session user id only; none takes a client-supplied identifier.
/// </summary>
public interface IAccountStore
{
    /// <summary>The profile joined with the session membership's organization and role, or null.</summary>
    Task<AccountProfileView?> GetProfileAsync(
        Guid userId, Guid organizationId, Guid membershipId, CancellationToken cancellationToken);

    /// <summary>
    /// Updates first name, last name and phone and, only when at least one value
    /// changed, writes one <c>user.profile_updated</c> audit row in the same
    /// save (metadata = changed field names). Returns the profile, or null.
    /// </summary>
    Task<AccountProfileView?> UpdateProfileAsync(
        Guid userId,
        Guid organizationId,
        Guid membershipId,
        AccountProfileValues values,
        IPAddress? clientIp,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    /// <summary>
    /// Stores the language and, only when it differs, writes one
    /// <c>user.language_updated</c> audit row ({from,to}) in the same save.
    /// Returns the stored language, or null when the user does not exist.
    /// </summary>
    Task<string?> UpdateLanguageAsync(
        Guid userId,
        Guid organizationId,
        string language,
        IPAddress? clientIp,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    /// <summary>
    /// Stores the theme and, only when it differs, writes one <c>user.theme_updated</c>
    /// audit row ({previousTheme,newTheme,source}) in the same save.
    /// Returns the stored theme, or null when the user does not exist.
    /// </summary>
    Task<string?> UpdateThemeAsync(
        Guid userId,
        Guid organizationId,
        string theme,
        string source,
        IPAddress? clientIp,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<AccountCredential?> FindCredentialAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Writes the new hash, <c>password_changed_at</c>, <c>updated_at</c> and
    /// the <c>auth.password_changed</c> audit row in one save.
    /// </summary>
    Task ChangePasswordAsync(
        Guid userId,
        Guid organizationId,
        string passwordHash,
        IPAddress? clientIp,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}
