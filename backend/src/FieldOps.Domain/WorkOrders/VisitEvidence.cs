namespace FieldOps.Domain.WorkOrders;

public sealed class VisitEvidence
{
    private VisitEvidence()
    {
    }

    private VisitEvidence(
        Guid id,
        Guid visitId,
        string fileName,
        string storageKey,
        string mimeType,
        long sizeBytes,
        VisitEvidenceType evidenceType,
        Guid uploadedByUserId)
    {
        Id = id;
        VisitId = visitId;
        FileName = fileName;
        StorageKey = storageKey;
        MimeType = mimeType;
        SizeBytes = sizeBytes;
        EvidenceType = evidenceType;
        UploadedByUserId = uploadedByUserId;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid VisitId { get; private set; }

    // Metadata and storage key only: binary file contents are never stored
    // in the relational model.
    public string FileName { get; private set; } = string.Empty;

    public string StorageKey { get; private set; } = string.Empty;

    public string MimeType { get; private set; } = string.Empty;

    public long SizeBytes { get; private set; }

    public VisitEvidenceType EvidenceType { get; private set; }

    public string? Caption { get; private set; }

    public Guid UploadedByUserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static VisitEvidence Create(
        Guid visitId,
        string fileName,
        string storageKey,
        string mimeType,
        long sizeBytes,
        VisitEvidenceType evidenceType,
        Guid uploadedByUserId)
    {
        if (visitId == Guid.Empty)
        {
            throw new ArgumentException(
                "Visit id is required.",
                nameof(visitId));
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

        if (uploadedByUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "Uploaded by user id is required.",
                nameof(uploadedByUserId));
        }

        return new VisitEvidence(
            Guid.NewGuid(),
            visitId,
            fileName.Trim(),
            storageKey.Trim(),
            mimeType.Trim(),
            sizeBytes,
            evidenceType,
            uploadedByUserId);
    }
}
