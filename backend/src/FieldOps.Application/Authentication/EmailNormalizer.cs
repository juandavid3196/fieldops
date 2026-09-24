namespace FieldOps.Application.Authentication;

/// <summary>
/// Normalizes an email address before any check or lookup: trimmed and
/// lowercased with the invariant culture.
/// </summary>
public static class EmailNormalizer
{
    public static string Normalize(string? email) =>
        (email ?? string.Empty).Trim().ToLowerInvariant();
}
