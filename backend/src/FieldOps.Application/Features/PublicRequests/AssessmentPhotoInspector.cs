namespace FieldOps.Application.Features.PublicRequests;

/// <summary>
/// Content-signature detection and extension matching for assessment photos (quote-builder BR-02): JPG and
/// PNG only, 1 byte to 10 MB each, never trusting the client-declared content type.
/// </summary>
public static class AssessmentPhotoInspector
{
    public const int MaxPhotos = 6;

    public const int MaxFileBytes = AttachmentContentInspector.MaxFileBytes;

    public const int MaxTotalBytes = AttachmentContentInspector.MaxTotalBytes;

    public const string PhotosMessage = "Photos must be JPG or PNG files of 10 MB or less.";

    public const string TooManyMessage = "Add up to 6 photos.";

    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF];

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>The detected MIME type, or null when the content is empty, too large, not JPG/PNG or does not match the extension.</summary>
    public static string? Inspect(string? fileName, ReadOnlySpan<byte> content)
    {
        if (content.Length is 0 or > MaxFileBytes)
        {
            return null;
        }

        string mimeType;
        string[] extensions;

        if (content.StartsWith(Jpeg))
        {
            mimeType = "image/jpeg";
            extensions = [".jpg", ".jpeg"];
        }
        else if (content.StartsWith(Png))
        {
            mimeType = "image/png";
            extensions = [".png"];
        }
        else
        {
            return null;
        }

        var name = fileName ?? string.Empty;
        var separator = name.LastIndexOfAny(['/', '\\']);
        var extension = Path.GetExtension(separator < 0 ? name : name[(separator + 1)..]);

        return extensions.Contains(extension, StringComparer.OrdinalIgnoreCase) ? mimeType : null;
    }
}
