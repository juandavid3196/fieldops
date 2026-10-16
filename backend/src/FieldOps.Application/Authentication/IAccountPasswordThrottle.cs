namespace FieldOps.Application.Authentication;

/// <summary>
/// Per-user throttle of failed current-password verifications in
/// <c>POST /account/password</c> (account settings BR-06): 5 failures in a
/// fixed 15-minute window. A success never resets the count.
/// </summary>
public interface IAccountPasswordThrottle
{
    /// <summary>The time until the window ends while the user is limited, otherwise null.</summary>
    TimeSpan? GetRetryAfter(Guid userId);

    void RecordFailure(Guid userId);
}
