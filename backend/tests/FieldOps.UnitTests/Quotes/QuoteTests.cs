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

    // customer-quote-approval BR-12 to BR-18, BR-26: each customer transition sets a new concurrency value, only a
    // sent or clarification-requested quote answers or expires, and the approved version is the token's.
    [Theory]
    [InlineData("approve", QuoteStatus.Sent, QuoteStatus.Approved)]
    [InlineData("approve", QuoteStatus.ClarificationRequested, QuoteStatus.Approved)]
    [InlineData("reject", QuoteStatus.Sent, QuoteStatus.Rejected)]
    [InlineData("reject", QuoteStatus.ClarificationRequested, QuoteStatus.Rejected)]
    [InlineData("ask", QuoteStatus.Sent, QuoteStatus.ClarificationRequested)]
    [InlineData("expire", QuoteStatus.Sent, QuoteStatus.Expired)]
    [InlineData("expire", QuoteStatus.ClarificationRequested, QuoteStatus.Expired)]
    public void CustomerTransitions_FromAnswerableStatus_ChangeStatusAndTouch(string action, QuoteStatus from, QuoteStatus to)
    {
        var quote = Quote.Create(Guid.NewGuid(), null, Guid.NewGuid(), null, null, 1, Guid.NewGuid());
        quote.MarkSent(1);

        if (from == QuoteStatus.ClarificationRequested)
        {
            quote.RequestClarification(DateTimeOffset.UtcNow);
        }

        var before = quote.UpdatedAt;
        var versionId = Guid.NewGuid();

        Apply(quote, action, versionId);

        Assert.Equal(to, quote.Status);
        Assert.True(quote.UpdatedAt > before);
        Assert.Equal(action == "approve" ? versionId : null, quote.ApprovedVersionId);
    }

    [Theory]
    [InlineData("approve")]
    [InlineData("reject")]
    [InlineData("ask")]
    [InlineData("expire")]
    public void CustomerTransitions_FromFinalOrUnsentStatus_Throw(string action)
    {
        foreach (var status in new[] { QuoteStatus.Draft, QuoteStatus.Approved, QuoteStatus.Rejected, QuoteStatus.Expired, QuoteStatus.Cancelled })
        {
            var quote = Quote.Create(Guid.NewGuid(), null, Guid.NewGuid(), null, null, 1, Guid.NewGuid());

            switch (status)
            {
                case QuoteStatus.Cancelled:
                    quote.Cancel();
                    break;
                case QuoteStatus.Approved:
                    quote.MarkSent(1);
                    quote.Approve(Guid.NewGuid(), DateTimeOffset.UtcNow);
                    break;
                case QuoteStatus.Rejected:
                    quote.MarkSent(1);
                    quote.Reject(DateTimeOffset.UtcNow);
                    break;
                case QuoteStatus.Expired:
                    quote.MarkSent(1);
                    quote.Expire(DateTimeOffset.UtcNow);
                    break;
            }

            var updatedAt = quote.UpdatedAt;

            Assert.Throws<InvalidOperationException>(() => Apply(quote, action, Guid.NewGuid()));
            Assert.Equal(status, quote.Status);
            Assert.Equal(updatedAt, quote.UpdatedAt);
        }
    }

    private static void Apply(Quote quote, string action, Guid versionId)
    {
        var now = DateTimeOffset.UtcNow;

        switch (action)
        {
            case "approve":
                quote.Approve(versionId, now);
                break;
            case "reject":
                quote.Reject(now);
                break;
            case "ask":
                quote.RequestClarification(now);
                break;
            default:
                quote.Expire(now);
                break;
        }
    }
}
