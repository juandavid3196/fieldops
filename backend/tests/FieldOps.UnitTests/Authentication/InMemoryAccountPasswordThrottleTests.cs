using FieldOps.Infrastructure.Authentication;

namespace FieldOps.UnitTests.Authentication;

public class InMemoryAccountPasswordThrottleTests
{
    // Account settings BR-06: 5 failures per user in a fixed 15-minute window, per user id.
    [Fact]
    public void GetRetryAfter_FiveFailuresInFixedWindow_LimitsOnlyThatUserUntilTheWindowEnds()
    {
        var time = new MutableTimeProvider(new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero));
        var throttle = new InMemoryAccountPasswordThrottle(time);
        var user = Guid.NewGuid();

        for (var i = 0; i < 4; i++)
        {
            throttle.RecordFailure(user);
            time.Advance(TimeSpan.FromMinutes(1));
        }

        Assert.Null(throttle.GetRetryAfter(user));

        throttle.RecordFailure(user);

        // The window started at the first failure (T0), so it ends at T0 + 15 min; now is T0 + 4 min.
        Assert.Equal(TimeSpan.FromMinutes(11), throttle.GetRetryAfter(user));
        Assert.Null(throttle.GetRetryAfter(Guid.NewGuid()));

        time.Advance(TimeSpan.FromMinutes(11));
        Assert.Null(throttle.GetRetryAfter(user));

        // A new window starts with the next failure.
        throttle.RecordFailure(user);
        Assert.Null(throttle.GetRetryAfter(user));
    }
}
