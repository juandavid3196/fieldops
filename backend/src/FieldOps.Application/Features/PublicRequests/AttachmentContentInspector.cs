using System.Text;

namespace FieldOps.Application.Features.PublicRequests;

/// <summary>Outcome of inspecting one uploaded file; <see cref="Error"/> is null when valid.</summary>
public sealed record AttachmentInspection(string? MimeType, string? Error)
{
    public bool IsValid => Error is null;
}

/// <summary>
/// Content-signature detection, extension matching and file name
/// sanitization for public request attachments (BR-13, BR-14). Never trusts
/// the client-declared content type.
/// </summary>
public static class AttachmentContentInspector
{
    public const int MaxFileBytes = 10 * 1024 * 1024;

    public const int MaxTotalBytes = 25 * 1024 * 1024;

    public const int MaxFiles = 5;

    public const int MaxFileNameLength = 255;

    public const string FallbackFileName = "attachment";

    public const string EmptyMessage = "The file is empty.";

    public const string TooLargeMessage = "The file is larger than 10 MB.";

    public const string TypeMessage = "Only JPG, PNG and PDF files are accepted.";

    public const string ExtensionMessage = "The file extension does not match its content.";

    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF];

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static readonly byte[] Pdf = [0x25, 0x50, 0x44, 0x46, 0x2D];

    public static AttachmentInspection Inspect(string? fileName, ReadOnlySpan<byte> content)
    {
        if (content.Length == 0)
        {
            return new AttachmentInspection(null, EmptyMessage);
        }

        if (content.Length > MaxFileBytes)
        {
            return new AttachmentInspection(null, TooLargeMessage);
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
        else if (content.StartsWith(Pdf))
        {
            mimeType = "application/pdf";
            extensions = [".pdf"];
        }
        else
        {
            return new AttachmentInspection(null, TypeMessage);
        }

        // The extension comes from the last path segment, case-insensitively.
        var extension = Path.GetExtension(LastSegment(fileName ?? string.Empty));

        return extensions.Contains(extension, StringComparer.OrdinalIgnoreCase)
            ? new AttachmentInspection(mimeType, null)
            : new AttachmentInspection(null, ExtensionMessage);
    }

    /// <summary>Directory parts and control characters removed, trimmed, at most 255 characters; empty becomes <c>attachment</c>.</summary>
    public static string SanitizeFileName(string? fileName)
    {
        var name = LastSegment(fileName ?? string.Empty);
        var builder = new StringBuilder(name.Length);

        foreach (var character in name)
        {
            if (!char.IsControl(character))
            {
                builder.Append(character);
            }
        }

        var result = builder.ToString().Trim();

        if (result.Length > MaxFileNameLength)
        {
            result = result[..MaxFileNameLength].Trim();
        }

        return result.Length == 0 ? FallbackFileName : result;
    }

    private static string LastSegment(string fileName)
    {
        var index = fileName.LastIndexOfAny(['/', '\\']);

        return index < 0 ? fileName : fileName[(index + 1)..];
    }
}
