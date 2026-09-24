namespace FieldOps.Application.Authentication;

/// <summary>
/// Per-email throttle of failed sign-in attempts. Callers pass the
/// normalized email; implementations must not keep it in plain text.
/// </summary>
public interface ISignInThrottle
{
    /// <summary>
    /// Returns the time until the email may try again, or null when it is
    /// not throttled.
    /// </summary>
    TimeSpan? GetRetryAfter(string normalizedEmail);

    void RecordFailure(string normalizedEmail);

    void Reset(string normalizedEmail);
}
