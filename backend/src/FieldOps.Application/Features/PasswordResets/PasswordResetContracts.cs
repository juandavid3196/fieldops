using FieldOps.Application.Features.Email;
using FieldOps.Application.Features.Users;

namespace FieldOps.Application.Features.PasswordResets;

/// <summary>
/// Inputs carry only the email, token and password: no user or organization
/// identifier is ever accepted from the client.
/// </summary>
public sealed record RequestPasswordResetCommand(string? Email);

public sealed record ValidatePasswordResetCommand(string? Token);

public sealed record ConfirmPasswordResetCommand(string? Token, string? Password);

/// <summary>BR-07: the only data a usable token reveals.</summary>
public sealed record PasswordResetAccountView(string Email);

/// <summary>The user a reset token is created for; never returned to the client.</summary>
public sealed record PasswordResetEligibleUser(Guid UserId, string Email, string FirstName);

public sealed record ConfirmPasswordResetRequest(string TokenHash, string Password, string PasswordHash);

/// <summary>A reset email queued for background dispatch; the link carries the raw token.</summary>
public sealed record PasswordResetEmail(string RecipientEmail, string FirstName, string ResetLink);

public enum PasswordResetResultKind
{
    Succeeded,
    Invalid,
    Gone,
}

/// <summary>Outcome of a password reset use case, mapped to HTTP by the controller.</summary>
public sealed record PasswordResetResult<T>(
    PasswordResetResultKind Kind,
    T? Value = default,
    IReadOnlyDictionary<string, string[]>? Errors = null)
{
    public static PasswordResetResult<T> Ok(T value) => new(PasswordResetResultKind.Succeeded, value);

    public static PasswordResetResult<T> Gone() => new(PasswordResetResultKind.Gone);

    public static PasswordResetResult<T> Invalid(IReadOnlyDictionary<string, string[]> errors) =>
        new(PasswordResetResultKind.Invalid, Errors: errors);

    public static PasswordResetResult<T> Invalid(string key, string message) =>
        Invalid(new Dictionary<string, string[]>(StringComparer.Ordinal) { [key] = [message] });
}

/// <summary>
/// Persistence for password recovery. Create and confirm are each one
/// transaction under a row lock (BR-03, BR-14).
/// </summary>
public interface IPasswordResetStore
{
    /// <summary>Active user with this normalized email, read without a lock (BR-04).</summary>
    Task<PasswordResetEligibleUser?> FindEligibleUserAsync(string normalizedEmail, CancellationToken cancellationToken);

    /// <summary>Like <see cref="FindEligibleUserAsync"/>, and the user also needs at least one active portal link (customer portal BR-15).</summary>
    Task<PasswordResetEligibleUser?> FindEligiblePortalUserAsync(string normalizedEmail, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces the user's unused tokens with one new token. Returns false
    /// when nothing was created (the user stopped being active or a
    /// concurrent request won); true only after the commit.
    /// </summary>
    Task<bool> ReplaceAsync(Guid userId, string tokenHash, CancellationToken cancellationToken);

    /// <summary>Read-only; null unless the token is usable (BR-02).</summary>
    Task<PasswordResetAccountView?> FindUsableAsync(string tokenHash, CancellationToken cancellationToken);

    /// <summary>
    /// Consumes the token and changes the password atomically. Gone when the
    /// token is unusable under the lock; Invalid when the password equals the
    /// account email (the token stays usable).
    /// </summary>
    Task<PasswordResetResult<NoValue>> ConfirmAsync(
        ConfirmPasswordResetRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// Per-normalized-email request limit (BR-12): 3 per 60 minutes, counted for
/// every well-formed email whether or not an account exists. Implementations
/// must not keep or log the email in plain text.
/// </summary>
public interface IPasswordResetEmailThrottle
{
    /// <summary>Counts the request and returns false when the email is over its limit.</summary>
    bool TryAcquire(string normalizedEmail);
}

/// <summary>Bounded, non-blocking hand-off to the background dispatcher (BR-11).</summary>
public interface IPasswordResetEmailQueue
{
    /// <summary>Returns false, dropping the item, when the queue is full.</summary>
    bool TryEnqueue(PasswordResetEmail email);
}

/// <summary>Builds the reset link (BR-10) from the raw token.</summary>
public interface IPasswordResetLinkBuilder
{
    string BuildResetLink(string rawToken);
}

/// <summary>Builds the BR-11 reset email (subject, plain-text and HTML bodies).</summary>
public static class PasswordResetEmailComposer
{
    public const string Subject = "Reset your FieldOps password";

    public static EmailMessage Compose(PasswordResetEmail email)
    {
        var text =
            $"Hi {email.FirstName},\n\n"
            + "We received a request to reset your FieldOps password.\n\n"
            + $"Reset password: {email.ResetLink}\n\n"
            + "This link expires in 30 minutes and can only be used once.\n"
            + "If you didn't request a password reset, you can ignore this email. Your password won't change.\n";

        var html =
            "<!DOCTYPE html><html><body style=\"font-family:Arial,sans-serif;color:#1f2937\">"
            + $"<p>Hi {System.Net.WebUtility.HtmlEncode(email.FirstName)},</p>"
            + "<p>We received a request to reset your FieldOps password.</p>"
            + $"<p><a href=\"{System.Net.WebUtility.HtmlEncode(email.ResetLink)}\" style=\"display:inline-block;padding:12px 20px;"
            + "background:#0f766e;color:#ffffff;text-decoration:none;border-radius:6px\">Reset password</a></p>"
            + "<p>This link expires in 30 minutes and can only be used once.<br/>"
            + "If you didn't request a password reset, you can ignore this email. Your password won't change.</p>"
            + "</body></html>";

        return new EmailMessage(email.RecipientEmail, Subject, text, html);
    }
}
