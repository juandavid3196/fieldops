using FieldOps.Application.Features.Organizations;
using FieldOps.Application.Validation;
using FluentValidation;

namespace FieldOps.Application.Features.Invitations;

/// <summary>Shared BR-03 token shape; the value is never echoed in a message.</summary>
public static class InvitationTokenRules
{
    public const string TokenKey = "token";

    public const string TokenInvalidMessage = "Enter a valid value.";

    public const int TokenLength = 43;

    public static bool IsWellFormed(string? token) =>
        token is { Length: TokenLength } && token.All(IsTokenCharacter);

    private static bool IsTokenCharacter(char c) =>
        c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_';
}

public sealed class ValidateInvitationCommandValidator : AbstractValidator<ValidateInvitationCommand>
{
    public ValidateInvitationCommandValidator()
    {
        RuleFor(command => command.Token)
            .Must(InvitationTokenRules.IsWellFormed)
            .WithMessage(InvitationTokenRules.TokenInvalidMessage)
            .OverridePropertyName(InvitationTokenRules.TokenKey);
    }
}

public sealed class AcceptExistingInvitationCommandValidator : AbstractValidator<AcceptExistingInvitationCommand>
{
    public AcceptExistingInvitationCommandValidator()
    {
        RuleFor(command => command.Token)
            .Must(InvitationTokenRules.IsWellFormed)
            .WithMessage(InvitationTokenRules.TokenInvalidMessage)
            .OverridePropertyName(InvitationTokenRules.TokenKey);
    }
}

/// <summary>
/// BR-03/BR-06 field shape for a new account. The rule that the password must
/// differ from the invitation email needs the invitation, so the handler
/// applies it after the token resolves as usable.
/// </summary>
public sealed class AcceptInvitationCommandValidator : AbstractValidator<AcceptInvitationCommand>
{
    public const string FirstNameRequiredMessage = "Enter a first name.";

    public const string LastNameRequiredMessage = "Enter a last name.";

    public const int NameMaxLength = 100;

    public const int PasswordMinLength = 12;

    public const int PasswordMaxLength = 128;

    public AcceptInvitationCommandValidator()
    {
        RuleFor(command => command.Token)
            .Must(InvitationTokenRules.IsWellFormed)
            .WithMessage(InvitationTokenRules.TokenInvalidMessage)
            .OverridePropertyName(InvitationTokenRules.TokenKey);

        NameRule("firstName", command => command.FirstName, FirstNameRequiredMessage);
        NameRule("lastName", command => command.LastName, LastNameRequiredMessage);

        // Passwords are never trimmed; length counts UTF-16 code units.
        RuleFor(command => command.Password ?? string.Empty)
            .Cascade(CascadeMode.Stop)
            .Must(password => password.Length > 0)
            .WithMessage(FieldRulesValidatorBase<AcceptInvitationCommand>.RequiredMessage)
            .Must(password => password.Length is >= PasswordMinLength and <= PasswordMaxLength)
            .WithMessage(RegisterOrganizationCommandValidator.PasswordLengthMessage)
            .OverridePropertyName("password");
    }

    private void NameRule(string key, Func<AcceptInvitationCommand, string?> selector, string requiredMessage)
    {
        RuleFor(command => (selector(command) ?? string.Empty).Trim())
            .Cascade(CascadeMode.Stop)
            .Must(value => value.Length > 0)
            .WithMessage(requiredMessage)
            .Must(value => value.Length <= NameMaxLength)
            .WithMessage(FieldRulesValidatorBase<AcceptInvitationCommand>.TooLongMessage(NameMaxLength))
            .OverridePropertyName(key);
    }
}
