namespace FieldOps.Domain.Organizations;

/// <summary>
/// The organization's single logo image (one row per organization). Content
/// type and bytes are validated by the caller before reaching the entity.
/// </summary>
public sealed class OrganizationLogo
{
    public const int MaxSizeBytes = 2 * 1024 * 1024;

    private OrganizationLogo()
    {
    }

    private OrganizationLogo(Guid organizationId, string contentType, byte[] content, DateTimeOffset now)
    {
        OrganizationId = organizationId;
        ContentType = contentType;
        Content = content;
        SizeBytes = content.Length;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid OrganizationId { get; private set; }

    public string ContentType { get; private set; } = string.Empty;

    public byte[] Content { get; private set; } = [];

    public int SizeBytes { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static OrganizationLogo Create(
        Guid organizationId,
        string contentType,
        byte[] content,
        DateTimeOffset now)
    {
        if (organizationId == Guid.Empty)
        {
            throw new ArgumentException("Organization id is required.", nameof(organizationId));
        }

        Validate(contentType, content);

        return new OrganizationLogo(organizationId, contentType.Trim(), content, now);
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
            throw new ArgumentException("Logo content type is required.", nameof(contentType));
        }

        ArgumentNullException.ThrowIfNull(content);

        if (content.Length is < 1 or > MaxSizeBytes)
        {
            throw new ArgumentException("Logo size must be between 1 byte and 2 MB.", nameof(content));
        }
    }
}
