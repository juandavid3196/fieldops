using FieldOps.Domain.Requests;

namespace FieldOps.UnitTests.Requests;

public class AssessmentTests
{
    [Fact]
    public void Create_WithValidArguments_SetsDefaults()
    {
        var organizationId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var createdByUserId = Guid.NewGuid();
        var start = DateTimeOffset.UtcNow;
        var end = start.AddHours(1);

        var assessment = Assessment.Create(organizationId, requestId, start, end, createdByUserId);

        Assert.Equal(organizationId, assessment.OrganizationId);
        Assert.Equal(requestId, assessment.RequestId);
        Assert.Equal(start, assessment.ScheduledStart);
        Assert.Equal(end, assessment.ScheduledEnd);
        Assert.Equal(createdByUserId, assessment.CreatedByUserId);
        Assert.Equal(AssessmentStatus.Scheduled, assessment.Status);
        Assert.Null(assessment.TechnicianId);
    }

    [Fact]
    public void Create_WithScheduledStartNotBeforeEnd_Throws()
    {
        var start = DateTimeOffset.UtcNow;

        Assert.Throws<ArgumentException>(
            () => Assessment.Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                start,
                start.AddHours(-1),
                Guid.NewGuid()));
    }

    [Fact]
    public void Create_WithEmptyCreatedByUserId_Throws()
    {
        var start = DateTimeOffset.UtcNow;

        Assert.Throws<ArgumentException>(
            () => Assessment.Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                start,
                start.AddHours(1),
                Guid.Empty));
    }
}
