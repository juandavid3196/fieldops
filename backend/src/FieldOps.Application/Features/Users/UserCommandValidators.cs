using FieldOps.Application.Validation;
using FluentValidation;

namespace FieldOps.Application.Features.Users;

public sealed class InviteUserCommandValidator : AbstractValidator<InviteUserCommand>
{
    public const string EmailMessage = "Enter a valid email address.";

    public const string FirstNameMessage = "Enter a first name.";

    public const string LastNameMessage = "Enter a last name.";

    public const string ExpiryMessage = "Select a valid expiry.";

    public const int DefaultExpiresInDays = 7;

    public static readonly IReadOnlyList<int> AllowedExpiryDays = [3, 7, 14];

    public InviteUserCommandValidator()
    {
        RuleFor(command => command).Custom((command, context) =>
        {
            var email = (command.Email ?? string.Empty).Trim();

            if (email.Length == 0
                || email.Length > FieldRulesValidatorBase<InviteUserCommand>.EmailMaxLength
                || !FieldRulesValidatorBase<InviteUserCommand>.IsValidEmailFormat(email))
            {
                context.AddFailure("email", EmailMessage);
            }

            if (!IsValidName(command.FirstName))
            {
                context.AddFailure("firstName", FirstNameMessage);
            }

            if (!IsValidName(command.LastName))
            {
                context.AddFailure("lastName", LastNameMessage);
            }

            AccessValidation.Validate(
                command.RoleCode,
                command.IsAllBranches,
                command.BranchIds,
                (key, message) => context.AddFailure(key, message));

            if (command.ExpiresInDays is { } days && !AllowedExpiryDays.Contains(days))
            {
                context.AddFailure("expiresInDays", ExpiryMessage);
            }
        });
    }

    private static bool IsValidName(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim();

        return trimmed.Length is > 0 and <= 100;
    }
}

public sealed class UpdateAccessCommandValidator : AbstractValidator<UpdateAccessCommand>
{
    public UpdateAccessCommandValidator()
    {
        RuleFor(command => command).Custom((command, context) =>
            AccessValidation.Validate(
                command.RoleCode,
                command.IsAllBranches,
                command.BranchIds,
                (key, message) => context.AddFailure(key, message)));
    }
}
