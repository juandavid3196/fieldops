namespace FieldOps.Domain.Requests;

public sealed class RequestAttachment
{
    private RequestAttachment()
    {
    }

    private RequestAttachment(
        Guid id,
        Guid organizationId,
        Guid requestId,
        string fileName,
        string storageKey,
        string mimeType,
        long sizeBytes)
    {
        Id = id;
        OrganizationId = organizationId;
        RequestId = requestId;
        FileName = fileName;
        StorageKey = storageKey;
        MimeType = mimeType;
        SizeBytes = sizeBytes;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public Guid RequestId { get; private set; }

    public string FileName { get; private set; } = string.Empty;

    public string StorageKey { get; private set; } = string.Empty;

    public string MimeType { get; private set; } = string.Empty;

    public long SizeBytes { get; private set; }

    public Guid? UploadedByUserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static RequestAttachment Create(
        Guid organizationId,
        Guid requestId,
        string fileName,
        string storageKey,
        string mimeType,
        long sizeBytes)
    {
        if (organizationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Organization id is required.",
                nameof(organizationId));
        }

        if (requestId == Guid.Empty)
        {
            throw new ArgumentException(
                "Request id is required.",
                nameof(requestId));
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException(
                "File name is required.",
                nameof(fileName));
        }

        if (string.IsNullOrWhiteSpace(storageKey))
        {
            throw new ArgumentException(
                "Storage key is required.",
                nameof(storageKey));
        }

        if (string.IsNullOrWhiteSpace(mimeType))
        {
            throw new ArgumentException(
                "Mime type is required.",
                nameof(mimeType));
        }

        if (sizeBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sizeBytes),
                sizeBytes,
                "Size in bytes must be greater than zero.");
        }

        return new RequestAttachment(
            Guid.NewGuid(),
            organizationId,
            requestId,
            fileName.Trim(),
            storageKey.Trim(),
            mimeType.Trim(),
            sizeBytes);
    }
}
