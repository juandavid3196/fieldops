namespace FieldOps.Application.Features.Account;

/// <summary>Outcome of <see cref="UserPhotoContentValidator.Check"/>.</summary>
public readonly record struct UserPhotoCheck(string? ContentType, string? Error)
{
    public bool IsValid => Error is null;
}

/// <summary>
/// Photo content rules (BR-03): the type is derived from the bytes only (PNG or
/// JPEG signature) and must equal the declared part type. Never trusts file
/// names or extensions.
/// </summary>
public static class UserPhotoContentValidator
{
    public const int MaxBytes = 5 * 1024 * 1024;

    public const string SizeMessage = "Choose a file of 5 MB or smaller.";

    public const string TypeMessage = "Choose a JPG or PNG file.";

    public const string PngType = "image/png";

    public const string JpegType = "image/jpeg";

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static UserPhotoCheck Check(byte[] content, string? declaredContentType)
    {
        if (content.Length > MaxBytes)
        {
            return new UserPhotoCheck(null, SizeMessage);
        }

        var declared = NormalizeDeclaredType(declaredContentType);
        var detected = Detect(content);

        if (detected is null || declared is null || !string.Equals(detected, declared, StringComparison.Ordinal))
        {
            return new UserPhotoCheck(null, TypeMessage);
        }

        return new UserPhotoCheck(detected, null);
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
        if (content.Length == 0)
        {
            return null;
        }

        if (content.AsSpan().StartsWith(PngSignature))
        {
            return PngType;
        }

        return content.Length >= 3 && content[0] == 0xFF && content[1] == 0xD8 && content[2] == 0xFF
            ? JpegType
            : null;
    }
}
