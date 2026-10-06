using FieldOps.Domain.Requests;

namespace FieldOps.UnitTests.Requests;

public class AssessmentAttachmentTests
{
    private static readonly byte[] Content = [0xFF, 0xD8, 0xFF, 0x00];

    [Fact]
    public void Create_WithValidArguments_StoresContentInline()
    {
        var organizationId = Guid.NewGuid();
        var assessmentId = Guid.NewGuid();

        var attachment = AssessmentAttachment.Create(organizationId, assessmentId, " photo.jpg ", "image/jpeg", Content.Length, Content);

        Assert.Equal(organizationId, attachment.OrganizationId);
        Assert.Equal(assessmentId, attachment.AssessmentId);
        Assert.Equal("photo.jpg", attachment.FileName);
        Assert.Equal(4, attachment.SizeBytes);
        Assert.Equal(Content, attachment.Content);
        Assert.Null(attachment.StorageKey);
    }

    [Fact]
    public void Create_WithEmptyIds_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => AssessmentAttachment.Create(Guid.Empty, Guid.NewGuid(), "photo.jpg", "image/jpeg", 4, Content));
        Assert.Throws<ArgumentException>(
            () => AssessmentAttachment.Create(Guid.NewGuid(), Guid.Empty, "photo.jpg", "image/jpeg", 4, Content));
    }

    [Theory]
    [InlineData("", "image/jpeg", 4)]
    [InlineData("photo.jpg", "", 4)]
    [InlineData("photo.jpg", "image/jpeg", 0)]
    public void Create_WithBlankRequiredField_Throws(string fileName, string mimeType, int contentLength)
    {
        Assert.Throws<ArgumentException>(
            () => AssessmentAttachment.Create(
                Guid.NewGuid(), Guid.NewGuid(), fileName, mimeType, contentLength, new byte[contentLength]));
    }
}
