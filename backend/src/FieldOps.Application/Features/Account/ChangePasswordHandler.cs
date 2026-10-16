using System.Net;
using FieldOps.Application.Authentication;
using FieldOps.Application.Features.Organizations;

namespace FieldOps.Application.Features.Account;

/// <summary>Raw input of <c>POST /account/password</c>.</summary>
public sealed record ChangePasswordCommand(string? CurrentPassword, string? NewPassword);

/// <summary>Result of <see cref="ChangePasswordHandler"/>.</summary>
public abstract record ChangePasswordResult
{
    private ChangePasswordResult()
    {
    }

    /// <summary>The change instant, truncated to milliseconds like the session claim (BR-05).</summary>
    public sealed record Succeeded(DateTimeOffset ChangedAt) : ChangePasswordResult;

    public sealed record Invalid(IReadOnlyDictionary<string, string[]> Errors) : ChangePasswordResult;

    public sealed record Throttled(TimeSpan RetryAfter) : ChangePasswordResult;

    public sealed record NotFound : ChangePasswordResult;
}

/// <summary>
/// Changes the session user's password (BR-02, BR-05, BR-06). Order: failure
/// limit, required/length, current password (counts a failure), new password
/// versus email and current, then one save of hash, change instant and audit.
/// </summary>
public sealed class ChangePasswordHandler(
    IAccountStore store,
    IPasswordHasher passwordHasher,
    IAccountPasswordThrottle throttle,
    TimeProvider timeProvider)
{
    public const string PasswordChangedAuditAction = "auth.password_changed";

    public const string CurrentPasswordKey = "currentPassword";

    public const string NewPasswordKey = "newPassword";

    public const int PasswordMaxLength = 128;

    public const int PasswordMinLength = 12;

    public const string RequiredMessage = "This field is required.";

    public const string CurrentTooLongMessage = "Use 128 characters or fewer.";

    public const string CurrentIncorrectMessage = "Your current password is incorrect.";

    public const string EqualsCurrentMessage = "Choose a password that is different from your current password.";

    /// <summary>The time until the user may verify again, or null; checked before anything else (BR-05 step 1).</summary>
    public TimeSpan? GetRetryAfter(Guid userId) => throttle.GetRetryAfter(userId);

    public async Task<ChangePasswordResult> HandleAsync(
        Guid userId,
        Guid organizationId,
        IPAddress? clientIp,
        ChangePasswordCommand command,
        CancellationToken cancellationToken)
    {
        var retryAfter = throttle.GetRetryAfter(userId);

        if (retryAfter is not null)
        {
            return new ChangePasswordResult.Throttled(retryAfter.Value);
        }

        var current = command.CurrentPassword ?? string.Empty;
        var proposed = command.NewPassword ?? string.Empty;
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (current.Length == 0)
        {
            errors[CurrentPasswordKey] = [RequiredMessage];
        }
        else if (current.Length > PasswordMaxLength)
        {
            errors[CurrentPasswordKey] = [CurrentTooLongMessage];
        }

        if (proposed.Length == 0)
        {
            errors[NewPasswordKey] = [RequiredMessage];
        }
        else if (proposed.Length is < PasswordMinLength or > PasswordMaxLength)
        {
            errors[NewPasswordKey] = [RegisterOrganizationCommandValidator.PasswordLengthMessage];
        }

        if (errors.Count > 0)
        {
            return new ChangePasswordResult.Invalid(errors);
        }

        var credential = await store.FindCredentialAsync(userId, cancellationToken);

        if (credential is null)
        {
            return new ChangePasswordResult.NotFound();
        }

        if (!passwordHasher.Verify(current, credential.PasswordHash))
        {
            throttle.RecordFailure(userId);

            return Invalid(CurrentPasswordKey, CurrentIncorrectMessage);
        }

        if (string.Equals(proposed, credential.Email.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return Invalid(NewPasswordKey, RegisterOrganizationCommandValidator.PasswordEqualsEmailMessage);
        }

        if (string.Equals(proposed, current, StringComparison.Ordinal))
        {
            return Invalid(NewPasswordKey, EqualsCurrentMessage);
        }

        // Session claims carry the sign-in instant in milliseconds, so the
        // change instant is truncated the same way (PasswordResetStore).
        var now = timeProvider.GetUtcNow();
        var changedAt = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMillisecond));

        await store.ChangePasswordAsync(
            userId, organizationId, passwordHasher.Hash(proposed), clientIp, changedAt, cancellationToken);

        return new ChangePasswordResult.Succeeded(changedAt);
    }

    private static ChangePasswordResult.Invalid Invalid(string key, string message) =>
        new(new Dictionary<string, string[]>(StringComparer.Ordinal) { [key] = [message] });
}
