namespace FieldOps.Application.Authentication;

/// <summary>
/// Internal reason for a credential failure. Used only for server logs;
/// every category produces the same client response.
/// </summary>
public enum SignInFailureCategory
{
    UnknownEmail,
    PasswordMismatch,
    UserNotActive,
    NoEligibleMembership,
}
