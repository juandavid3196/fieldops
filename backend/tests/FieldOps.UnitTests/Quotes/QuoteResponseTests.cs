using System.Net;
using FieldOps.Domain.Quotes;

namespace FieldOps.UnitTests.Quotes;

public class QuoteResponseTests
{
    private static readonly DateTimeOffset Now = new(2030, 5, 6, 7, 8, 9, TimeSpan.Zero);

    [Fact]
    public void Create_WithValidArguments_SetsDefaults()
    {
        var organizationId = Guid.NewGuid();
        var quoteVersionId = Guid.NewGuid();

        var response = QuoteResponse.Create(organizationId, quoteVersionId, QuoteStatus.Approved, " Jane Doe ");

        Assert.NotEqual(Guid.Empty, response.Id);
        Assert.Equal(organizationId, response.OrganizationId);
        Assert.Equal(quoteVersionId, response.QuoteVersionId);
        Assert.Equal(QuoteStatus.Approved, response.Response);
        Assert.Equal("Jane Doe", response.ResponderName);
        Assert.Null(response.ResponderContactId);
        Assert.Null(response.Comment);
        Assert.Null(response.IpAddress);
        Assert.Null(response.Total);
    }

    [Theory]
    [InlineData(QuoteStatus.Draft)]
    [InlineData(QuoteStatus.Sent)]
    [InlineData(QuoteStatus.Expired)]
    [InlineData(QuoteStatus.Cancelled)]
    public void Create_WithDisallowedResponse_Throws(QuoteStatus response)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => QuoteResponse.Create(Guid.NewGuid(), Guid.NewGuid(), response, "Jane Doe"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankResponderName_Throws(string responderName)
    {
        Assert.Throws<ArgumentException>(
            () => QuoteResponse.Create(Guid.NewGuid(), Guid.NewGuid(), QuoteStatus.Approved, responderName));
    }

    // BR-12, BR-14, BR-15: totals exist only on an approval; the comment is the reason or the question.
    [Fact]
    public void Factories_SetTotalsOnlyOnApprovalAndTheCommentOnTheOthers()
    {
        var organizationId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var contactId = Guid.NewGuid();
        var address = IPAddress.Parse("203.0.113.9");

        var approval = QuoteResponse.Approve(organizationId, versionId, "Jane", contactId, address, Now, 190m, 10m, 14.85m, 194.85m);
        var decline = QuoteResponse.Reject(organizationId, versionId, "Jane", null, null, Now, "  Too expensive ");
        var question = QuoteResponse.Ask(organizationId, versionId, "Jane", null, null, Now, "Is parking included?");

        Assert.Equal((QuoteStatus.Approved, 194.85m, contactId, address), (approval.Response, approval.Total, approval.ResponderContactId, approval.IpAddress));
        Assert.Equal((190m, 10m, 14.85m), (approval.Subtotal, approval.DiscountTotal, approval.TaxTotal));
        Assert.Null(approval.Comment);
        Assert.Equal((QuoteStatus.Rejected, "Too expensive"), (decline.Response, decline.Comment));
        Assert.Equal((QuoteStatus.ClarificationRequested, "Is parking included?"), (question.Response, question.Comment));
        Assert.All([decline, question], response => Assert.Null(response.Subtotal ?? response.DiscountTotal ?? response.TaxTotal ?? response.Total));
        Assert.Throws<ArgumentException>(() => QuoteResponse.Reject(organizationId, versionId, "Jane", null, null, Now, " "));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => QuoteResponse.Approve(organizationId, versionId, "Jane", null, null, Now, 1m, 0m, 0m, -1m));
    }
}
