namespace FieldOps.Domain.Quotes;

/// <summary>
/// The hashed secret of the public quote link (quote-builder BR-27), scoped to one quote version. The raw
/// token is never part of the entity.
/// </summary>
public sealed class QuoteAccessToken
{
    private QuoteAccessToken()
    {
    }

    private QuoteAccessToken(
        Guid id,
        Guid organizationId,
        Guid quoteVersionId,
        string tokenHash,
        DateTimeOffset expiresAt,
        Guid createdByUserId,
        DateTimeOffset createdAt)
    {
        Id = id;
        OrganizationId = organizationId;
        QuoteVersionId = quoteVersionId;
        TokenHash = tokenHash;
        ExpiresAt = expiresAt;
        CreatedByUserId = createdByUserId;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public Guid QuoteVersionId { get; private set; }

    public string TokenHash { get; private set; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static QuoteAccessToken Create(
        Guid organizationId,
        Guid quoteVersionId,
        string tokenHash,
        DateTimeOffset expiresAt,
        Guid createdByUserId,
        DateTimeOffset now)
    {
        if (organizationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Organization id is required.",
                nameof(organizationId));
        }

        if (quoteVersionId == Guid.Empty)
        {
            throw new ArgumentException(
                "Quote version id is required.",
                nameof(quoteVersionId));
        }

        if (string.IsNullOrWhiteSpace(tokenHash))
        {
            throw new ArgumentException(
                "Token hash is required.",
                nameof(tokenHash));
        }

        if (createdByUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "Created by user id is required.",
                nameof(createdByUserId));
        }

        var createdAt = new DateTimeOffset(now.UtcDateTime.Ticks - (now.UtcDateTime.Ticks % 10), TimeSpan.Zero);

        if (expiresAt <= createdAt)
        {
            throw new ArgumentException(
                "The token must expire after it is created.",
                nameof(expiresAt));
        }

        return new QuoteAccessToken(
            Guid.NewGuid(),
            organizationId,
            quoteVersionId,
            tokenHash.Trim(),
            expiresAt,
            createdByUserId,
            createdAt);
    }
}
