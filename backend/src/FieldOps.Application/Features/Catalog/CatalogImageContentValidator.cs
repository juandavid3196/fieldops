namespace FieldOps.Application.Features.Catalog;

public readonly record struct CatalogImageCheck(string? ContentType, string? Error)
{
    public bool IsValid => Error is null;
}

/// <summary>
/// Image rules (BR-11): the type comes from the bytes only (PNG or JPEG
/// signature) and must equal the declared part type; file names and
/// extensions are never trusted.
/// </summary>
public static class CatalogImageContentValidator
{
    public const int MaxBytes = 5_242_880;

    public const string SizeMessage = "Choose an image of 5 MB or smaller.";

    public const string TypeMessage = "Choose a PNG or JPG image.";

    public const string PngType = "image/png";

    public const string JpegType = "image/jpeg";

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static CatalogImageCheck Check(byte[] content, string? declaredContentType)
    {
        if (content.Length > MaxBytes)
        {
            return new CatalogImageCheck(null, SizeMessage);
        }

        var declared = NormalizeDeclaredType(declaredContentType);
        var detected = Detect(content);

        if (detected is null || declared is null || !string.Equals(detected, declared, StringComparison.Ordinal))
        {
            return new CatalogImageCheck(null, TypeMessage);
        }

        return new CatalogImageCheck(detected, null);
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
