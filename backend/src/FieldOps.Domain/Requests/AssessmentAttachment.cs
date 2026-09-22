namespace FieldOps.Domain.Requests;

public sealed class AssessmentAttachment
{
    private AssessmentAttachment()
    {
    }

    private AssessmentAttachment(
        Guid id,
        Guid assessmentId,
        string fileName,
        string storageKey,
        string mimeType,
        long sizeBytes)
    {
        Id = id;
        AssessmentId = assessmentId;
        FileName = fileName;
        StorageKey = storageKey;
        MimeType = mimeType;
        SizeBytes = sizeBytes;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid AssessmentId { get; private set; }

    public string FileName { get; private set; } = string.Empty;

    public string StorageKey { get; private set; } = string.Empty;

    public string MimeType { get; private set; } = string.Empty;

    public long SizeBytes { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static AssessmentAttachment Create(
        Guid assessmentId,
        string fileName,
        string storageKey,
        string mimeType,
        long sizeBytes)
    {
        if (assessmentId == Guid.Empty)
        {
            throw new ArgumentException(
                "Assessment id is required.",
                nameof(assessmentId));
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

        return new AssessmentAttachment(
            Guid.NewGuid(),
            assessmentId,
            fileName.Trim(),
            storageKey.Trim(),
            mimeType.Trim(),
            sizeBytes);
    }
}
