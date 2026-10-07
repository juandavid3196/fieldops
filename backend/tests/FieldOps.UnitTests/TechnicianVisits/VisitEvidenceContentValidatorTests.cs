using FieldOps.Application.Features.TechnicianVisits;

namespace FieldOps.UnitTests.TechnicianVisits;

/// <summary>Photo signature, declared type and size checks and the stored file name (mobile-job-progress BR-11).</summary>
public class VisitEvidenceContentValidatorTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1];

    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 1];

    public static TheoryData<byte[], string?, string?> Cases => new()
    {
        { Png, "image/png", "image/png" },
        { Png, "IMAGE/PNG; charset=binary", "image/png" },
        { Jpeg, "image/jpeg", "image/jpeg" },
        { Png, "image/jpeg", null },
        { Jpeg, "image/png", null },
        { "GIF89a"u8.ToArray(), "image/gif", null },
        { "text"u8.ToArray(), "image/jpeg", null },
        { Jpeg, null, null },
        { [], "image/jpeg", null },
        { new byte[VisitEvidenceContentValidator.MaxBytes + 1], "image/jpeg", null },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Check_RequiresTheSignatureToMatchTheDeclaredTypeAndTheSizeLimit(byte[] content, string? declared, string? expected)
    {
        var check = VisitEvidenceContentValidator.Check(content, declared);

        Assert.Equal(expected, check.ContentType);
        Assert.Equal(expected is null ? VisitEvidenceContentValidator.Message : null, check.Error);
    }

    [Theory]
    [InlineData("..\\..\\evil/photo.jpg", "image/jpeg", "photo.jpg")]
    [InlineData("C:\\Users\\me\\scan.png", "image/png", "scan.png")]
    [InlineData("  pho\u0000to\u0007.jpg ", "image/jpeg", "photo.jpg")]
    [InlineData("dir/", "image/png", "photo.png")]
    [InlineData(null, "image/jpeg", "photo.jpg")]
    public void SanitizeFileName_KeepsTheBaseNameWithoutControlCharactersWithin255Characters(string? name, string type, string expected)
    {
        Assert.Equal(expected, VisitEvidenceContentValidator.SanitizeFileName(name, type));
        Assert.Equal(255, VisitEvidenceContentValidator.SanitizeFileName("dir/" + new string('a', 400), type).Length);
    }
}
