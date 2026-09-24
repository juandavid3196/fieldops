using FieldOps.Infrastructure.Authentication;

namespace FieldOps.UnitTests.Authentication;

public class InMemorySignInThrottleTests
{
    private const string Email = "user@example.com";

    private readonly MutableTimeProvider _time = new(new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero));

    private readonly InMemorySignInThrottle _throttle;

    public InMemorySignInThrottleTests()
    {
        _throttle = new InMemorySignInThrottle(_time);
    }

    [Fact]
    public void GetRetryAfter_NoFailures_ReturnsNull()
    {
        Assert.Null(_throttle.GetRetryAfter(Email));
    }

    [Fact]
    public void GetRetryAfter_FourFailures_ReturnsNull()
    {
        RecordFailures(4, TimeSpan.FromMinutes(1));

        Assert.Null(_throttle.GetRetryAfter(Email));
    }

    [Fact]
    public void GetRetryAfter_FiveFailuresWithinWindow_ReturnsTimeUntilOldestLeaves()
    {
        RecordFailures(5, TimeSpan.FromMinutes(1));

        // Oldest failure at T0; now T0 + 4 min; it leaves at T0 + 15 min.
        Assert.Equal(TimeSpan.FromMinutes(11), _throttle.GetRetryAfter(Email));
    }

    [Fact]
    public void GetRetryAfter_WhenOldestFailureLeavesWindow_ReturnsNull()
    {
        RecordFailures(5, TimeSpan.FromMinutes(1));

        _time.Advance(TimeSpan.FromMinutes(11));

        Assert.Null(_throttle.GetRetryAfter(Email));
    }

    [Fact]
    public void GetRetryAfter_FailuresSpreadBeyondWindow_CountsOnlyRecentOnes()
    {
        RecordFailures(4, TimeSpan.FromMinutes(5));
        _time.Advance(TimeSpan.FromMinutes(1));
        _throttle.RecordFailure(Email);

        // Failures at 0, 5, 10, 15 and 21 min: the first left the window.
        Assert.Null(_throttle.GetRetryAfter(Email));
    }

    [Fact]
    public void GetRetryAfter_DifferentEmail_IsNotThrottled()
    {
        RecordFailures(5, TimeSpan.FromSeconds(1));

        Assert.NotNull(_throttle.GetRetryAfter(Email));
        Assert.Null(_throttle.GetRetryAfter("other@example.com"));
    }

    [Fact]
    public void Reset_AfterFourFailures_StartsCountingAgain()
    {
        RecordFailures(4, TimeSpan.FromSeconds(1));

        _throttle.Reset(Email);
        RecordFailures(4, TimeSpan.FromSeconds(1));
        Assert.Null(_throttle.GetRetryAfter(Email));

        _throttle.RecordFailure(Email);
        Assert.NotNull(_throttle.GetRetryAfter(Email));
    }

    [Fact]
    public void RecordFailure_WhenSweepRuns_KeepsFailuresInsideWindow()
    {
        _throttle.RecordFailure("stale@example.com");
        _time.Advance(TimeSpan.FromMinutes(14));
        RecordFailures(5, TimeSpan.FromSeconds(1));
        _time.Advance(TimeSpan.FromMinutes(2));

        // More than one window since construction: this call sweeps stale keys.
        _throttle.RecordFailure("other@example.com");

        Assert.NotNull(_throttle.GetRetryAfter(Email));
        Assert.Null(_throttle.GetRetryAfter("stale@example.com"));
    }

    private void RecordFailures(int count, TimeSpan interval)
    {
        for (var i = 0; i < count; i++)
        {
            if (i > 0)
            {
                _time.Advance(interval);
            }

            _throttle.RecordFailure(Email);
        }
    }
}
