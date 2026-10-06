using FieldOps.Domain.Quotes;

namespace FieldOps.UnitTests.Quotes;

public class QuoteTests
{
    [Fact]
    public void Create_WithValidArguments_SetsDefaults()
    {
        var organizationId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var propertyId = Guid.NewGuid();
        var createdByUserId = Guid.NewGuid();

        var quote = Quote.Create(organizationId, branchId, requestId, customerId, propertyId, 1, createdByUserId);

        Assert.NotEqual(Guid.Empty, quote.Id);
        Assert.Equal(organizationId, quote.OrganizationId);
        Assert.Equal(branchId, quote.BranchId);
        Assert.Equal(requestId, quote.RequestId);
        Assert.Equal(customerId, quote.CustomerId);
        Assert.Equal(propertyId, quote.PropertyId);
        Assert.Equal(1, quote.QuoteNumber);
        Assert.Equal(createdByUserId, quote.CreatedByUserId);
        Assert.Equal(QuoteStatus.Draft, quote.Status);
        Assert.Equal(0, quote.CurrentVersionNo);
        Assert.Null(quote.ApprovedVersionId);
        Assert.Equal(0, quote.UpdatedAt.Ticks % 10);
    }

    [Fact]
    public void Create_WithNonPositiveQuoteNumber_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Quote.Create(Guid.NewGuid(), null, Guid.NewGuid(), null, null, 0, Guid.NewGuid()));
    }

    [Fact]
    public void Create_WithEmptyCreatedByUserId_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => Quote.Create(Guid.NewGuid(), null, Guid.NewGuid(), null, null, 1, Guid.Empty));
    }

    [Fact]
    public void Touch_TruncatesToMicrosecondsAndAlwaysChangesTheToken()
    {
        var quote = Quote.Create(Guid.NewGuid(), null, Guid.NewGuid(), null, null, 1, Guid.NewGuid());
        var before = quote.UpdatedAt;

        quote.Touch(before);

        Assert.True(quote.UpdatedAt > before);
        Assert.Equal(0, quote.UpdatedAt.Ticks % 10);

        quote.Touch(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero).AddTicks(1234567));

        Assert.Equal(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero).AddTicks(1234560), quote.UpdatedAt);
    }

    [Fact]
    public void MarkSentAndCancel_FollowTheStatusRules()
    {
        var quote = Quote.Create(Guid.NewGuid(), null, Guid.NewGuid(), null, null, 1, Guid.NewGuid());

        quote.MarkSent(1);

        Assert.Equal(QuoteStatus.Sent, quote.Status);
        Assert.Equal(1, quote.CurrentVersionNo);
        Assert.Throws<InvalidOperationException>(quote.Cancel);

        var draft = Quote.Create(Guid.NewGuid(), null, Guid.NewGuid(), null, null, 2, Guid.NewGuid());
        draft.Cancel();

        Assert.Equal(QuoteStatus.Cancelled, draft.Status);
        Assert.Throws<InvalidOperationException>(() => draft.MarkSent(1));
    }
}
