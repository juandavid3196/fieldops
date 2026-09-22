using FieldOps.Domain.Quotes;

namespace FieldOps.UnitTests.Quotes;

public class QuoteResponseTests
{
    [Fact]
    public void Create_WithValidArguments_SetsDefaults()
    {
        var quoteVersionId = Guid.NewGuid();

        var response = QuoteResponse.Create(
            quoteVersionId,
            QuoteStatus.Approved,
            " Jane Doe ");

        Assert.NotEqual(Guid.Empty, response.Id);
        Assert.Equal(quoteVersionId, response.QuoteVersionId);
        Assert.Equal(QuoteStatus.Approved, response.Response);
        Assert.Equal("Jane Doe", response.ResponderName);
        Assert.Null(response.ResponderContactId);
        Assert.Null(response.Comment);
        Assert.Null(response.IpAddress);
    }

    [Theory]
    [InlineData(QuoteStatus.Draft)]
    [InlineData(QuoteStatus.Sent)]
    [InlineData(QuoteStatus.Expired)]
    [InlineData(QuoteStatus.Cancelled)]
    public void Create_WithDisallowedResponse_Throws(QuoteStatus response)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => QuoteResponse.Create(Guid.NewGuid(), response, "Jane Doe"));
    }

    [Theory]
    [InlineData(QuoteStatus.Approved)]
    [InlineData(QuoteStatus.Rejected)]
    [InlineData(QuoteStatus.ClarificationRequested)]
    public void Create_WithAllowedResponse_Succeeds(QuoteStatus response)
    {
        var quoteResponse = QuoteResponse.Create(Guid.NewGuid(), response, "Jane Doe");

        Assert.Equal(response, quoteResponse.Response);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankResponderName_Throws(string responderName)
    {
        Assert.Throws<ArgumentException>(
            () => QuoteResponse.Create(Guid.NewGuid(), QuoteStatus.Approved, responderName));
    }
}
