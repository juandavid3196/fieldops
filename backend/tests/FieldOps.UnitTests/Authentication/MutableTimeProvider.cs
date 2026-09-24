namespace FieldOps.UnitTests.Authentication;

/// <summary>
/// Test clock that only moves when the test advances it.
/// </summary>
internal sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
