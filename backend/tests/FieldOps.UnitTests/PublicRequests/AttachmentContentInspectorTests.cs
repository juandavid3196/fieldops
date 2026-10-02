using FieldOps.Application.Features.PublicRequests;

namespace FieldOps.UnitTests.PublicRequests;

public class AttachmentContentInspectorTests
{
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00];

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00];

    private static readonly byte[] Pdf = [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31];

    private static readonly byte[] Text = "hello world"u8.ToArray();

    // BR-13: the type comes from the content signature and must match the extension.
    [Theory]
    [InlineData("photo.jpg", "jpeg", "image/jpeg")]
    [InlineData("PHOTO.JPEG", "jpeg", "image/jpeg")]
    [InlineData("shot.PNG", "png", "image/png")]
    [InlineData("quote.pdf", "pdf", "application/pdf")]
    [InlineData("..\\evil\\quote.Pdf", "pdf", "application/pdf")]
    [InlineData("renamed.jpg", "text", null)]
    [InlineData("image.pdf", "png", null)]
    [InlineData("image", "png", null)]
    [InlineData("empty.png", "empty", null)]
    public void Inspect_DetectsTypeFromContentAndMatchesExtension(string fileName, string content, string? expectedMime)
    {
        byte[] bytes = content switch
        {
            "jpeg" => Jpeg,
            "png" => Png,
            "pdf" => Pdf,
            "empty" => [],
            _ => Text,
        };

        var result = AttachmentContentInspector.Inspect(fileName, bytes);

        Assert.Equal(expectedMime, result.MimeType);
        Assert.Equal(expectedMime is not null, result.IsValid);
    }

    [Fact]
    public void Inspect_ContentOverTenMegabytes_IsRejected()
    {
        var bytes = new byte[AttachmentContentInspector.MaxFileBytes + 1];
        Pdf.CopyTo(bytes, 0);

        Assert.Equal(AttachmentContentInspector.TooLargeMessage, AttachmentContentInspector.Inspect("a.pdf", bytes).Error);
        Assert.True(AttachmentContentInspector.Inspect("a.pdf", bytes.AsSpan(0, AttachmentContentInspector.MaxFileBytes)).IsValid);
    }

    // BR-14.
    [Theory]
    [InlineData("../../etc/passwd.png", "passwd.png")]
    [InlineData("C:\\Users\\me\\scan.pdf", "scan.pdf")]
    [InlineData("  spaced name.jpg  ", "spaced name.jpg")]
    [InlineData("bad\u0000na\tme\r\n.jpg", "badname.jpg")]
    [InlineData("folder/", "attachment")]
    [InlineData("", "attachment")]
    [InlineData(null, "attachment")]
    public void SanitizeFileName_RemovesDirectoriesAndControlCharacters(string? raw, string expected) =>
        Assert.Equal(expected, AttachmentContentInspector.SanitizeFileName(raw));

    [Fact]
    public void SanitizeFileName_TruncatesToMaxLength() =>
        Assert.Equal(
            AttachmentContentInspector.MaxFileNameLength,
            AttachmentContentInspector.SanitizeFileName(new string('a', 400) + ".jpg").Length);
}
