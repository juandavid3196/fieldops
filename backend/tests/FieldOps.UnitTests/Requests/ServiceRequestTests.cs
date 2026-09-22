using FieldOps.Domain.Requests;

namespace FieldOps.UnitTests.Requests;

public class ServiceRequestTests
{
    [Fact]
    public void Create_WithValidArguments_SetsDefaults()
    {
        var organizationId = Guid.NewGuid();

        var request = ServiceRequest.Create(organizationId, 1, " Leaking faucet ");

        Assert.NotEqual(Guid.Empty, request.Id);
        Assert.Equal(organizationId, request.OrganizationId);
        Assert.Equal(1, request.RequestNumber);
        Assert.Equal("Leaking faucet", request.Description);
        Assert.Equal(RequestStatus.New, request.Status);
        Assert.Equal("public_form", request.Source);
        Assert.Null(request.CustomerId);
        Assert.Null(request.CancelledAt);
    }

    [Fact]
    public void Create_WithEmptyOrganizationId_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => ServiceRequest.Create(Guid.Empty, 1, "Description"));
    }

    [Fact]
    public void Create_WithNonPositiveRequestNumber_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ServiceRequest.Create(Guid.NewGuid(), 0, "Description"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankDescription_Throws(string description)
    {
        Assert.Throws<ArgumentException>(
            () => ServiceRequest.Create(Guid.NewGuid(), 1, description));
    }

    [Fact]
    public void Create_WithPreferredStartNotBeforeEnd_Throws()
    {
        var now = DateTimeOffset.UtcNow;

        Assert.Throws<ArgumentException>(
            () => ServiceRequest.Create(
                Guid.NewGuid(),
                1,
                "Description",
                preferredStart: now,
                preferredEnd: now.AddHours(-1)));
    }

    [Fact]
    public void Create_WithOnlyPreferredStart_Succeeds()
    {
        var now = DateTimeOffset.UtcNow;

        var request = ServiceRequest.Create(
            Guid.NewGuid(),
            1,
            "Description",
            preferredStart: now);

        Assert.Equal(now, request.PreferredStart);
        Assert.Null(request.PreferredEnd);
    }
}
