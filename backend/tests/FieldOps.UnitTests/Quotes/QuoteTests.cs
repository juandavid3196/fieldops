using FieldOps.Domain.Quotes;

namespace FieldOps.UnitTests.Quotes;

public class QuoteTests
{
    [Fact]
    public void Create_WithValidArguments_SetsDefaults()
    {
        var organizationId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var createdByUserId = Guid.NewGuid();

        var quote = Quote.Create(organizationId, requestId, 1, createdByUserId);

        Assert.NotEqual(Guid.Empty, quote.Id);
        Assert.Equal(organizationId, quote.OrganizationId);
        Assert.Equal(requestId, quote.RequestId);
        Assert.Equal(1, quote.QuoteNumber);
        Assert.Equal(createdByUserId, quote.CreatedByUserId);
        Assert.Equal(QuoteStatus.Draft, quote.Status);
        Assert.Equal(0, quote.CurrentVersionNo);
        Assert.Null(quote.ApprovedVersionId);
        Assert.Null(quote.CustomerId);
        Assert.Null(quote.PropertyId);
        Assert.Null(quote.BranchId);
    }

    [Fact]
    public void Create_WithNonPositiveQuoteNumber_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Quote.Create(Guid.NewGuid(), Guid.NewGuid(), 0, Guid.NewGuid()));
    }

    [Fact]
    public void Create_WithEmptyCreatedByUserId_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => Quote.Create(Guid.NewGuid(), Guid.NewGuid(), 1, Guid.Empty));
    }
}
