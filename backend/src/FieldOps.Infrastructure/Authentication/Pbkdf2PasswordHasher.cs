using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FieldOps.Application.Authentication;

namespace FieldOps.Infrastructure.Authentication;

/// <summary>
/// PBKDF2-HMAC-SHA256 hasher and verifier for the
/// "pbkdf2-sha256${iterations}${base64-salt}${base64-hash}" format with at
/// least 600,000 iterations, a 16-byte salt and a 32-byte hash.
/// </summary>
public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    public const string Algorithm = "pbkdf2-sha256";

    public const int Iterations = 600_000;

    public const int SaltSize = 16;

    public const int HashSize = 32;

    // Random bytes that no password derives to in practice. Only its cost
    // matters: it is verified when the email does not exist.
    private const string FixedDummyHash =
        "pbkdf2-sha256$600000$wEi8EN2QvIpm+R7q2jo5rg==$iHBVqwLtV5dWZUK0vv3X0t6PcwtaUTR3gtGIEtddnM4=";

    public string DummyHash => FixedDummyHash;

    public string Hash(string password)
    {
        ArgumentNullException.ThrowIfNull(password);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Derive(password, salt, Iterations);

        return string.Join(
            '$',
            Algorithm,
            Iterations.ToString(CultureInfo.InvariantCulture),
            Convert.ToBase64String(salt),
            Convert.ToBase64String(hash));
    }

    public bool Verify(string password, string storedHash)
    {
        if (password is null)
        {
            return false;
        }

        if (!TryParse(storedHash, out var iterations, out var salt, out var expected))
        {
            // BR-04: any unreadable format is a verification failure. The
            // dummy hash is still derived so the response time matches BR-05.
            if (!TryParse(FixedDummyHash, out iterations, out salt, out expected))
            {
                throw new InvalidOperationException("The dummy password hash is not in the supported format.");
            }

            _ = Derive(password, salt, iterations);

            return false;
        }

        var actual = Derive(password, salt, iterations);

        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static bool TryParse(string? storedHash, out int iterations, out byte[] salt, out byte[] expected)
    {
        iterations = 0;
        salt = new byte[SaltSize];
        expected = new byte[HashSize];

        if (string.IsNullOrEmpty(storedHash))
        {
            return false;
        }

        var parts = storedHash.Split('$');

        if (parts.Length != 4 || !string.Equals(parts[0], Algorithm, StringComparison.Ordinal))
        {
            return false;
        }

        if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out iterations)
            || iterations < Iterations)
        {
            return false;
        }

        return Convert.TryFromBase64String(parts[2], salt, out var saltLength) && saltLength == SaltSize
            && Convert.TryFromBase64String(parts[3], expected, out var hashLength) && hashLength == HashSize;
    }

    private static byte[] Derive(string password, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            HashSize);
}
