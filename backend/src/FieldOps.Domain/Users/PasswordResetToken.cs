namespace FieldOps.Domain.Users;

/// <summary>
/// One-time password reset token (BR-01 to BR-03). Only the hash of the
/// random token is stored; the raw value lives in the email and the client.
/// </summary>
public sealed class PasswordResetToken
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    private PasswordResetToken()
    {
    }

    private PasswordResetToken(Guid id, Guid userId, string tokenHash, DateTimeOffset createdAt)
    {
        Id = id;
        UserId = userId;
        TokenHash = tokenHash;
        CreatedAt = createdAt;
        ExpiresAt = createdAt + Lifetime;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string TokenHash { get; private set; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? UsedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static PasswordResetToken Create(Guid userId, string tokenHash, DateTimeOffset now)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("User id is required.", nameof(userId));
        }

        if (string.IsNullOrWhiteSpace(tokenHash))
        {
            throw new ArgumentException("Token hash is required.", nameof(tokenHash));
        }

        return new PasswordResetToken(Guid.NewGuid(), userId, tokenHash, now);
    }

    /// <summary>Unused and not expired; the user's status is checked by the caller.</summary>
    public bool IsUsable(DateTimeOffset now) => UsedAt is null && ExpiresAt > now;

    public void MarkUsed(DateTimeOffset now)
    {
        if (!IsUsable(now))
        {
            throw new InvalidOperationException("The password reset token can no longer be used.");
        }

        UsedAt = now;
    }
}
