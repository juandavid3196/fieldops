using FieldOps.Domain.Requests;

namespace FieldOps.UnitTests.Requests;

public class RequestStatusHistoryTests
{
    [Fact]
    public void Create_WithValidArguments_SetsProperties()
    {
        var organizationId = Guid.NewGuid();
        var requestId = Guid.NewGuid();

        var history = RequestStatusHistory.Create(
            organizationId,
            requestId,
            RequestStatus.New,
            RequestStatus.NeedsReview);

        Assert.Equal(organizationId, history.OrganizationId);
        Assert.Equal(requestId, history.RequestId);
        Assert.Equal(RequestStatus.New, history.FromStatus);
        Assert.Equal(RequestStatus.NeedsReview, history.ToStatus);
    }

    [Fact]
    public void Create_WithoutFromStatus_LeavesItNull()
    {
        var history = RequestStatusHistory.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            fromStatus: null,
            toStatus: RequestStatus.New);

        Assert.Null(history.FromStatus);
    }

    [Fact]
    public void Create_WithEmptyRequestId_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => RequestStatusHistory.Create(
                Guid.NewGuid(),
                Guid.Empty,
                RequestStatus.New,
                RequestStatus.NeedsReview));
    }
}
