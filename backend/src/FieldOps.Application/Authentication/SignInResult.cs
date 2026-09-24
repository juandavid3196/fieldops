namespace FieldOps.Application.Authentication;

/// <summary>
/// Outcome of a sign-in attempt.
/// </summary>
public abstract record SignInResult
{
    private SignInResult()
    {
    }

    /// <summary>Credentials verified and the sign-in recorded.</summary>
    public sealed record Succeeded(
        SessionView Session,
        Guid MembershipId,
        DateTimeOffset SignedInAt) : SignInResult;

    /// <summary>Field validation failed; keys are "email" and "password".</summary>
    public sealed record Invalid(
        IReadOnlyDictionary<string, string[]> Errors) : SignInResult;

    /// <summary>Any credential failure; the category is for logs only.</summary>
    public sealed record InvalidCredentials(
        SignInFailureCategory Category,
        Guid? UserId) : SignInResult;

    /// <summary>Too many failed attempts for this email.</summary>
    public sealed record Throttled(TimeSpan RetryAfter) : SignInResult;
}
