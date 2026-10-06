namespace FieldOps.Domain.Requests;

/// <summary>An assessment photo stored inline (quote-builder BR-02); <see cref="StorageKey"/> stays null while the content lives in the database.</summary>
public sealed class AssessmentAttachment
{
    private AssessmentAttachment()
    {
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public Guid AssessmentId { get; private set; }

    public string FileName { get; private set; } = string.Empty;

    public string? StorageKey { get; private set; }

    public byte[]? Content { get; private set; }

    public string MimeType { get; private set; } = string.Empty;

    public long SizeBytes { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static AssessmentAttachment Create(
        Guid organizationId,
        Guid assessmentId,
        string fileName,
        string mimeType,
        long sizeBytes,
        byte[] content)
    {
        if (organizationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Organization id is required.",
                nameof(organizationId));
        }

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

        if (string.IsNullOrWhiteSpace(mimeType))
        {
            throw new ArgumentException(
                "Mime type is required.",
                nameof(mimeType));
        }

        ArgumentNullException.ThrowIfNull(content);

        if (content.Length == 0)
        {
            throw new ArgumentException(
                "Content is required.",
                nameof(content));
        }

        return new AssessmentAttachment
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            AssessmentId = assessmentId,
            FileName = fileName.Trim(),
            MimeType = mimeType.Trim(),
            SizeBytes = sizeBytes,
            Content = content,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }
}
