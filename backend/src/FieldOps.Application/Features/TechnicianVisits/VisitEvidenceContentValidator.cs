using FieldOps.Domain.WorkOrders;

namespace FieldOps.Application.Features.TechnicianVisits;

public readonly record struct VisitEvidenceCheck(string? ContentType, string? Error)
{
    public bool IsValid => Error is null;
}

/// <summary>
/// Photo rules (mobile-job-progress BR-11): the type comes from the bytes only (PNG or JPEG signature) and must equal
/// the declared part type; file names and extensions are never trusted. Mirrors <c>CatalogImageContentValidator</c>.
/// </summary>
public static class VisitEvidenceContentValidator
{
    public const int MaxBytes = VisitEvidence.MaxInlineBytes;

    public const string Message = "Upload a JPEG or PNG image up to 10 MB.";

    public const string PngType = "image/png";

    public const string JpegType = "image/jpeg";

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static VisitEvidenceCheck Check(byte[] content, string? declaredContentType)
    {
        if (content.Length is 0 or > MaxBytes)
        {
            return new VisitEvidenceCheck(null, Message);
        }

        var declared = NormalizeDeclaredType(declaredContentType);
        var detected = Detect(content);

        return detected is null || declared is null || !string.Equals(detected, declared, StringComparison.Ordinal)
            ? new VisitEvidenceCheck(null, Message)
            : new VisitEvidenceCheck(detected, null);
    }

    /// <summary>The base name after any / or \, without control characters, trimmed, at most 255 characters; a default when empty.</summary>
    public static string SanitizeFileName(string? fileName, string contentType)
    {
        var name = fileName ?? string.Empty;
        var separator = name.LastIndexOfAny(['/', '\\']);

        if (separator >= 0)
        {
            name = name[(separator + 1)..];
        }

        name = new string([.. name.Where(character => !char.IsControl(character))]).Trim();

        if (name.Length > 255)
        {
            name = name[..255].Trim();
        }

        return name.Length > 0 ? name : contentType == PngType ? "photo.png" : "photo.jpg";
    }

    private static string? NormalizeDeclaredType(string? declared)
    {
        if (string.IsNullOrWhiteSpace(declared))
        {
            return null;
        }

        var semicolon = declared.IndexOf(';', StringComparison.Ordinal);
        var type = (semicolon >= 0 ? declared[..semicolon] : declared).Trim().ToLowerInvariant();

        return type is PngType or JpegType ? type : null;
    }

    private static string? Detect(byte[] content)
    {
        if (content.AsSpan().StartsWith(PngSignature))
        {
            return PngType;
        }

        return content.Length >= 3 && content[0] == 0xFF && content[1] == 0xD8 && content[2] == 0xFF
            ? JpegType
            : null;
    }
}
