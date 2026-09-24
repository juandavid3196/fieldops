namespace FieldOps.Application.Authentication;

/// <summary>
/// PBKDF2-HMAC-SHA256 password hashing in the
/// "pbkdf2-sha256${iterations}${base64-salt}${base64-hash}" format.
/// </summary>
public interface IPasswordHasher
{
    /// <summary>
    /// A fixed hash in the supported format, verified when no user has the
    /// email so the response time does not reveal whether it exists.
    /// </summary>
    string DummyHash { get; }

    string Hash(string password);

    /// <summary>
    /// Returns false for a wrong password and for any unreadable hash.
    /// </summary>
    bool Verify(string password, string storedHash);
}
