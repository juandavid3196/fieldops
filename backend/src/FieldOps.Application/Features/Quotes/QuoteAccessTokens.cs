using System.Security.Cryptography;
using System.Text;
using FieldOps.Application.Features.Team;

namespace FieldOps.Application.Features.Quotes;

/// <summary>
/// Quote link token (BR-27): 32 random bytes as URL-safe base64 without padding; only the lowercase-hex
/// SHA-256 of the raw text is stored. The raw value must never be persisted, returned, logged or audited.
/// </summary>
public static class QuoteAccessTokens
{
    public const int TokenByteLength = 32;

    public static (string Raw, string Hash) Generate()
    {
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(TokenByteLength))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        return (raw, Hash(raw));
    }

    public static string Hash(string rawToken) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

    /// <summary>The end of the <c>valid_until</c> day in the organization time zone: the next local midnight, as a UTC instant.</summary>
    public static DateTimeOffset ExpiresAt(DateOnly validUntil, TimeZoneInfo zone) =>
        BranchTime.ToInstant(validUntil.AddDays(1), TimeOnly.MinValue, zone);
}
