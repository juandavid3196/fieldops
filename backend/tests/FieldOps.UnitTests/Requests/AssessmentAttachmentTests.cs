using FieldOps.Domain.Requests;

namespace FieldOps.UnitTests.Requests;

public class AssessmentAttachmentTests
{
    [Fact]
    public void Create_WithValidArguments_SetsProperties()
    {
        var assessmentId = Guid.NewGuid();

        var attachment = AssessmentAttachment.Create(
            assessmentId,
            "report.pdf",
            "storage/report.pdf",
            "application/pdf",
            2048);

        Assert.Equal(assessmentId, attachment.AssessmentId);
        Assert.Equal("report.pdf", attachment.FileName);
        Assert.Equal(2048, attachment.SizeBytes);
    }

    [Fact]
    public void Create_WithEmptyAssessmentId_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => AssessmentAttachment.Create(
                Guid.Empty,
                "report.pdf",
                "storage/report.pdf",
                "application/pdf",
                2048));
    }

    [Theory]
    [InlineData("", "storage/report.pdf", "application/pdf")]
    [InlineData("report.pdf", "", "application/pdf")]
    [InlineData("report.pdf", "storage/report.pdf", "")]
    public void Create_WithBlankRequiredField_Throws(
        string fileName,
        string storageKey,
        string mimeType)
    {
        Assert.Throws<ArgumentException>(
            () => AssessmentAttachment.Create(
                Guid.NewGuid(),
                fileName,
                storageKey,
                mimeType,
                2048));
    }
}
