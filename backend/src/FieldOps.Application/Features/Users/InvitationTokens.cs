using System.Security.Cryptography;
using System.Text;

namespace FieldOps.Application.Features.Users;

/// <summary>
/// Invitation token generation (BR-11): 32 random bytes as URL-safe base64
/// without padding; only the lowercase-hex SHA-256 of the raw text is
/// stored. The raw value must never be persisted, returned, logged or audited.
/// </summary>
public static class InvitationTokens
{
    public const int TokenByteLength = 32;

    public static (string Raw, string Hash) Generate()
    {
        var raw = ToBase64Url(RandomNumberGenerator.GetBytes(TokenByteLength));

        return (raw, Hash(raw));
    }

    public static string Hash(string rawToken) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

    private static string ToBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
