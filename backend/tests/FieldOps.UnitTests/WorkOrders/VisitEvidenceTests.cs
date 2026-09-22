using FieldOps.Domain.WorkOrders;

namespace FieldOps.UnitTests.WorkOrders;

public class VisitEvidenceTests
{
    [Fact]
    public void Create_WithValidArguments_SetsProperties()
    {
        var visitId = Guid.NewGuid();
        var uploadedByUserId = Guid.NewGuid();

        var evidence = VisitEvidence.Create(
            visitId,
            "before.jpg",
            "storage/before.jpg",
            "image/jpeg",
            2048,
            VisitEvidenceType.Before,
            uploadedByUserId);

        Assert.Equal(visitId, evidence.VisitId);
        Assert.Equal("before.jpg", evidence.FileName);
        Assert.Equal(2048, evidence.SizeBytes);
        Assert.Equal(VisitEvidenceType.Before, evidence.EvidenceType);
        Assert.Equal(uploadedByUserId, evidence.UploadedByUserId);
        Assert.Null(evidence.Caption);
    }

    [Fact]
    public void Create_WithNonPositiveSizeBytes_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => VisitEvidence.Create(
                Guid.NewGuid(),
                "before.jpg",
                "storage/before.jpg",
                "image/jpeg",
                0,
                VisitEvidenceType.Before,
                Guid.NewGuid()));
    }

    [Fact]
    public void Create_WithEmptyUploadedByUserId_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => VisitEvidence.Create(
                Guid.NewGuid(),
                "before.jpg",
                "storage/before.jpg",
                "image/jpeg",
                2048,
                VisitEvidenceType.Before,
                Guid.Empty));
    }
}
