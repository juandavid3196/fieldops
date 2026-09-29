namespace FieldOps.Application.Features.Users;

public enum UserResultKind
{
    Succeeded,
    NoContent,
    Invalid,
    NotFound,
    Conflict,
    DeliveryFailed,
}

public readonly record struct NoValue;

/// <summary>Outcome of a users use case, mapped to HTTP by the controller.</summary>
public sealed record UserResult<T>(
    UserResultKind Kind,
    T? Value = default,
    IReadOnlyDictionary<string, string[]>? Errors = null,
    string? Message = null,
    string? ConflictKey = null)
{
    public static UserResult<T> Ok(T value) => new(UserResultKind.Succeeded, value);

    public static UserResult<T> NoOp() => new(UserResultKind.NoContent);

    public static UserResult<T> NotFound() => new(UserResultKind.NotFound);

    public static UserResult<T> DeliveryFailed() => new(UserResultKind.DeliveryFailed);

    public static UserResult<T> Invalid(string key, string message) =>
        new(
            UserResultKind.Invalid,
            Errors: new Dictionary<string, string[]>(StringComparer.Ordinal) { [key] = [message] });

    public static UserResult<T> Invalid(IReadOnlyDictionary<string, string[]> errors) =>
        new(UserResultKind.Invalid, Errors: errors);

    public static UserResult<T> Conflict(string message, string? key = null) =>
        new(UserResultKind.Conflict, Message: message, ConflictKey: key);

    /// <summary>Same failure carried to another value type.</summary>
    public UserResult<TOther> Cast<TOther>() => new(Kind, default, Errors, Message, ConflictKey);
}

public static class UserMessages
{
    public const string InvalidBranch = "Select a valid branch.";

    public const string AlreadyMember = "This person already has access.";

    public const string PendingInvitationExists = "This email already has a pending invitation.";

    public const string NotPending = "This invitation is no longer pending.";

    public const string LastOwner = "The last Owner can't be suspended or downgraded.";

    public const string SelfSuspend = "You can't suspend your own access.";

    public const string DeliveryFailed = "We couldn't send the invitation. Try again.";
}

public static class UserAuditActions
{
    public const string Invited = "user.invited";

    public const string InvitationResent = "user.invitation_resent";

    public const string InvitationRevoked = "user.invitation_revoked";

    public const string AccessUpdated = "user.access_updated";

    public const string Suspended = "user.suspended";

    public const string Reactivated = "user.reactivated";

    public const string InvitationEntity = "user_invitation";

    public const string MemberEntity = "organization_user";
}
