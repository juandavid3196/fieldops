using FieldOps.Application.Authentication;
using FieldOps.Application.Features.Invitations;
using FieldOps.Application.Features.Organizations;
using FieldOps.Application.Validation;
using FluentValidation;

namespace FieldOps.Application.Features.PasswordResets;

/// <summary>BR-05: sign-in BR-01 normalization, format and messages.</summary>
public sealed class RequestPasswordResetCommandValidator : AbstractValidator<RequestPasswordResetCommand>
{
    public RequestPasswordResetCommandValidator()
    {
        RuleFor(command => EmailNormalizer.Normalize(command.Email))
            .Cascade(CascadeMode.Stop)
            .Must(email => email.Length > 0)
            .WithMessage(SignInCommandValidator.EmailRequiredMessage)
            .Must(SignInCommandValidator.IsValidEmail)
            .WithMessage(SignInCommandValidator.EmailInvalidMessage)
            .OverridePropertyName(SignInCommandValidator.EmailKey);
    }
}

/// <summary>BR-08 token shape; the value is never echoed.</summary>
public sealed class ValidatePasswordResetCommandValidator : AbstractValidator<ValidatePasswordResetCommand>
{
    public ValidatePasswordResetCommandValidator()
    {
        RuleFor(command => command.Token)
            .Must(InvitationTokenRules.IsWellFormed)
            .WithMessage(InvitationTokenRules.TokenInvalidMessage)
            .OverridePropertyName(InvitationTokenRules.TokenKey);
    }
}

/// <summary>
/// BR-08/BR-09 field shape. The rule that the password must differ from the
/// account email needs the token's user, so the handler applies it after the
/// token resolves as usable.
/// </summary>
public sealed class ConfirmPasswordResetCommandValidator : AbstractValidator<ConfirmPasswordResetCommand>
{
    public const int PasswordMinLength = 12;

    public const int PasswordMaxLength = 128;

    public ConfirmPasswordResetCommandValidator()
    {
        RuleFor(command => command.Token)
            .Must(InvitationTokenRules.IsWellFormed)
            .WithMessage(InvitationTokenRules.TokenInvalidMessage)
            .OverridePropertyName(InvitationTokenRules.TokenKey);

        // Passwords are never trimmed; length counts UTF-16 code units.
        RuleFor(command => command.Password ?? string.Empty)
            .Cascade(CascadeMode.Stop)
            .Must(password => password.Length > 0)
            .WithMessage(FieldRulesValidatorBase<ConfirmPasswordResetCommand>.RequiredMessage)
            .Must(password => password.Length is >= PasswordMinLength and <= PasswordMaxLength)
            .WithMessage(RegisterOrganizationCommandValidator.PasswordLengthMessage)
            .OverridePropertyName("password");
    }
}
