using FieldOps.Domain.Requests;

namespace FieldOps.UnitTests.Requests;

public class RequestAttachmentTests
{
    [Fact]
    public void Create_WithValidArguments_SetsProperties()
    {
        var organizationId = Guid.NewGuid();
        var requestId = Guid.NewGuid();

        var attachment = RequestAttachment.Create(
            organizationId,
            requestId,
            "photo.jpg",
            "storage/photo.jpg",
            "image/jpeg",
            1024);

        Assert.Equal(organizationId, attachment.OrganizationId);
        Assert.Equal(requestId, attachment.RequestId);
        Assert.Equal("photo.jpg", attachment.FileName);
        Assert.Equal(1024, attachment.SizeBytes);
        Assert.Null(attachment.UploadedByUserId);
    }

    [Fact]
    public void Create_WithNonPositiveSizeBytes_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RequestAttachment.Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "photo.jpg",
                "storage/photo.jpg",
                "image/jpeg",
                0));
    }

    [Theory]
    [InlineData("", "storage/photo.jpg", "image/jpeg")]
    [InlineData("photo.jpg", "", "image/jpeg")]
    [InlineData("photo.jpg", "storage/photo.jpg", "")]
    public void Create_WithBlankRequiredField_Throws(
        string fileName,
        string storageKey,
        string mimeType)
    {
        Assert.Throws<ArgumentException>(
            () => RequestAttachment.Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                fileName,
                storageKey,
                mimeType,
                1024));
    }
}
