namespace FieldOps.Domain.Catalog;

/// <summary>
/// The optional single image of a catalog item (one row per item). Content
/// type and bytes are validated by the caller before reaching the entity.
/// </summary>
public sealed class CatalogItemImage
{
    public const int MaxSizeBytes = 5 * 1024 * 1024;

    private CatalogItemImage()
    {
    }

    private CatalogItemImage(
        Guid catalogItemId,
        Guid organizationId,
        string contentType,
        byte[] content,
        DateTimeOffset now)
    {
        CatalogItemId = catalogItemId;
        OrganizationId = organizationId;
        ContentType = contentType;
        Content = content;
        SizeBytes = content.Length;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid CatalogItemId { get; private set; }

    public Guid OrganizationId { get; private set; }

    public string ContentType { get; private set; } = string.Empty;

    public byte[] Content { get; private set; } = [];

    public int SizeBytes { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static CatalogItemImage Create(
        Guid catalogItemId,
        Guid organizationId,
        string contentType,
        byte[] content,
        DateTimeOffset now)
    {
        if (catalogItemId == Guid.Empty)
        {
            throw new ArgumentException("Catalog item id is required.", nameof(catalogItemId));
        }

        if (organizationId == Guid.Empty)
        {
            throw new ArgumentException("Organization id is required.", nameof(organizationId));
        }

        Validate(contentType, content);

        return new CatalogItemImage(catalogItemId, organizationId, contentType.Trim(), content, now);
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
            throw new ArgumentException("Image content type is required.", nameof(contentType));
        }

        ArgumentNullException.ThrowIfNull(content);

        if (content.Length is < 1 or > MaxSizeBytes)
        {
            throw new ArgumentException("Image size must be between 1 byte and 5 MB.", nameof(content));
        }
    }
}
