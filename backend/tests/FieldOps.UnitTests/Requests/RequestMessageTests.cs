using FieldOps.Domain.Requests;

namespace FieldOps.UnitTests.Requests;

public class RequestMessageTests
{
    [Fact]
    public void Create_WithValidArguments_SetsProperties()
    {
        var organizationId = Guid.NewGuid();
        var requestId = Guid.NewGuid();

        var message = RequestMessage.Create(
            organizationId,
            requestId,
            MessageVisibility.Internal,
            " Dispatcher note ");

        Assert.Equal(organizationId, message.OrganizationId);
        Assert.Equal(requestId, message.RequestId);
        Assert.Equal(MessageVisibility.Internal, message.Visibility);
        Assert.Equal("Dispatcher note", message.Body);
        Assert.Null(message.AuthorUserId);
        Assert.Null(message.AuthorContactId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankBody_Throws(string body)
    {
        Assert.Throws<ArgumentException>(
            () => RequestMessage.Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                MessageVisibility.Customer,
                body));
    }
}
