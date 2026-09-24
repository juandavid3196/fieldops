using FluentValidation;

namespace FieldOps.Application.Authentication;

/// <summary>
/// Sign-in field rules. The first failing rule of each field wins and
/// produces the only message reported for that field.
/// </summary>
public sealed class SignInCommandValidator : AbstractValidator<SignInCommand>
{
    public const string EmailKey = "email";

    public const string PasswordKey = "password";

    public const string EmailRequiredMessage = "Enter your email address.";

    public const string EmailInvalidMessage =
        "Enter a valid email address, for example name@company.com.";

    public const string PasswordRequiredMessage = "Enter your password.";

    public const string PasswordTooLongMessage = "Use 128 characters or fewer.";

    public const int EmailMaxLength = 254;

    public const int PasswordMaxLength = 128;

    public SignInCommandValidator()
    {
        RuleFor(command => EmailNormalizer.Normalize(command.Email))
            .Cascade(CascadeMode.Stop)
            .Must(email => email.Length > 0)
            .WithMessage(EmailRequiredMessage)
            .Must(IsValidEmail)
            .WithMessage(EmailInvalidMessage)
            .OverridePropertyName(EmailKey);

        // Passwords are never trimmed: whitespace is part of the value.
        RuleFor(command => command.Password ?? string.Empty)
            .Cascade(CascadeMode.Stop)
            .Must(password => password.Length > 0)
            .WithMessage(PasswordRequiredMessage)
            .Must(password => password.Length <= PasswordMaxLength)
            .WithMessage(PasswordTooLongMessage)
            .OverridePropertyName(PasswordKey);
    }

    /// <summary>
    /// Expects a normalized email: at most 254 characters, exactly one "@"
    /// with a non-empty local part, a domain containing a dot and no
    /// whitespace.
    /// </summary>
    public static bool IsValidEmail(string email)
    {
        if (email.Length > EmailMaxLength || email.Any(char.IsWhiteSpace))
        {
            return false;
        }

        var at = email.IndexOf('@', StringComparison.Ordinal);

        if (at <= 0 || email.IndexOf('@', at + 1) >= 0)
        {
            return false;
        }

        return email[(at + 1)..].Contains('.', StringComparison.Ordinal);
    }
}
