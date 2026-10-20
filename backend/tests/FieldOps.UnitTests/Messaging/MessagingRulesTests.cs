using FieldOps.Application.Features.Messaging;
using FieldOps.Infrastructure.Authentication;
using FieldOps.UnitTests.Authentication;

namespace FieldOps.UnitTests.Messaging;

public class MessagingRulesTests
{
    [Fact]
    public void Cursor_RoundTripsAndIsBoundToItsFilterAndSearch()
    {
        var hash = MessagingRules.QueryHash("all", "valves");
        var cursor = new ConversationListCursor(new DateTimeOffset(2026, 10, 18, 9, 30, 0, 123, TimeSpan.Zero).AddTicks(40), "boiler repair", Guid.NewGuid());
        var nullCursor = new ConversationListCursor(null, "alpha branch team", Guid.NewGuid());

        Assert.Equal(cursor, MessagingRules.DecodeCursor(MessagingRules.EncodeCursor(hash, cursor), hash));
        Assert.Equal(nullCursor, MessagingRules.DecodeCursor(MessagingRules.EncodeCursor(hash, nullCursor), hash));

        // Another filter, another search (case-insensitive search is the same), garbage and tampered payloads are rejected.
        var encoded = MessagingRules.EncodeCursor(hash, cursor);
        Assert.Null(MessagingRules.DecodeCursor(encoded, MessagingRules.QueryHash("jobs", "valves")));
        Assert.Null(MessagingRules.DecodeCursor(encoded, MessagingRules.QueryHash("all", "other")));
        Assert.NotNull(MessagingRules.DecodeCursor(encoded, MessagingRules.QueryHash("all", "VALVES")));
        Assert.Null(MessagingRules.DecodeCursor("not-a-cursor", hash));
        Assert.Null(MessagingRules.DecodeCursor(encoded[..^4], hash));
        Assert.Equal("Gate code is 4412", MessagingRules.Preview("Gate    code\n is 4412"));
        Assert.Equal(140, MessagingRules.Preview(new string('a', 300)).Length);
    }

    [Fact]
    public void SendThrottle_AllowsThirtyPerMinutePerUserAndOrganizationThenSlides()
    {
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 10, 18, 9, 0, 0, TimeSpan.Zero));
        var throttle = new InMemoryMessageSendThrottle(clock);
        var organization = Guid.NewGuid();
        var user = Guid.NewGuid();

        for (var i = 0; i < 30; i++)
        {
            Assert.Null(throttle.TryAcquire(organization, user));
            clock.Advance(TimeSpan.FromSeconds(1));
        }

        // The 31st attempt waits until the first one leaves the window; other users and organizations are unaffected.
        Assert.Equal(TimeSpan.FromSeconds(30), throttle.TryAcquire(organization, user));
        Assert.Null(throttle.TryAcquire(organization, Guid.NewGuid()));
        Assert.Null(throttle.TryAcquire(Guid.NewGuid(), user));

        clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Null(throttle.TryAcquire(organization, user));
    }
}
