namespace FieldOps.Domain.Users;

/// <summary>
/// The user's single profile photo (one row per user). Content type and bytes
/// are validated by the caller before reaching the entity. The photo belongs
/// to the user, not to an organization.
/// </summary>
public sealed class UserPhoto
{
    public const int MaxSizeBytes = 5 * 1024 * 1024;

    private UserPhoto()
    {
    }

    private UserPhoto(Guid userId, string contentType, byte[] content, DateTimeOffset now)
    {
        UserId = userId;
        ContentType = contentType;
        Content = content;
        SizeBytes = content.Length;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid UserId { get; private set; }

    public string ContentType { get; private set; } = string.Empty;

    public byte[] Content { get; private set; } = [];

    public int SizeBytes { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static UserPhoto Create(Guid userId, string contentType, byte[] content, DateTimeOffset now)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("User id is required.", nameof(userId));
        }

        Validate(contentType, content);

        return new UserPhoto(userId, contentType.Trim(), content, now);
    }

    public void Replace(string contentType, byte[] content, DateTimeOffset now)
    {
        Validate(contentType, content);

        ContentType = contentType.Trim();
        Content = content;
        SizeBytes = content.Length;
        UpdatedAt = now;
    }

    private static void Validate(string contentType, byte[] content)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            throw new ArgumentException("Photo content type is required.", nameof(contentType));
        }

        ArgumentNullException.ThrowIfNull(content);

        if (content.Length is < 1 or > MaxSizeBytes)
        {
            throw new ArgumentException("Photo size must be between 1 byte and 5 MB.", nameof(content));
        }
    }
}
