using System.Net;
using FieldOps.Application.Validation;

namespace FieldOps.Application.Features.Account;

/// <summary>Profile field rules (BR-01): trimmed, first failing rule wins per field.</summary>
public static class AccountProfileRules
{
    public const int NameMaxLength = 100;

    public const string RequiredMessage = "This field is required.";

    public const string NameTooLongMessage = "Use 100 characters or fewer.";

    public const string PhoneTooLongMessage = "Use 40 characters or fewer.";

    public const string PhoneInvalidMessage = "Enter a valid phone number.";

    public const string FirstNameKey = "firstName";

    public const string LastNameKey = "lastName";

    public const string PhoneKey = "phone";

    public static AccountProfileValues? Validate(
        UpdateAccountProfileCommand command, out Dictionary<string, string[]> errors)
    {
        errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        var firstName = command.FirstName?.Trim() ?? string.Empty;
        var lastName = command.LastName?.Trim() ?? string.Empty;
        var phone = command.Phone?.Trim() ?? string.Empty;

        CheckName(FirstNameKey, firstName, errors);
        CheckName(LastNameKey, lastName, errors);

        if (phone.Length > FieldRulesValidatorBase<UpdateAccountProfileCommand>.PhoneMaxLength)
        {
            errors[PhoneKey] = [PhoneTooLongMessage];
        }
        else if (phone.Length > 0 && !FieldRulesValidatorBase<UpdateAccountProfileCommand>.IsValidPhoneFormat(phone))
        {
            errors[PhoneKey] = [PhoneInvalidMessage];
        }

        return errors.Count > 0
            ? null
            : new AccountProfileValues(firstName, lastName, phone.Length == 0 ? null : phone);
    }

    private static void CheckName(string key, string value, Dictionary<string, string[]> errors)
    {
        if (value.Length == 0)
        {
            errors[key] = [RequiredMessage];
        }
        else if (value.Length > NameMaxLength)
        {
            errors[key] = [NameTooLongMessage];
        }
    }
}

/// <summary>Reads the session user's profile (FR-05).</summary>
public sealed class GetAccountProfileHandler(IAccountStore store)
{
    public Task<AccountProfileView?> HandleAsync(
        Guid userId, Guid organizationId, Guid membershipId, CancellationToken cancellationToken) =>
        store.GetProfileAsync(userId, organizationId, membershipId, cancellationToken);
}

/// <summary>Result of <see cref="UpdateAccountProfileHandler"/>.</summary>
public abstract record UpdateAccountProfileResult
{
    private UpdateAccountProfileResult()
    {
    }

    public sealed record Succeeded(AccountProfileView Profile) : UpdateAccountProfileResult;

    public sealed record Invalid(IReadOnlyDictionary<string, string[]> Errors) : UpdateAccountProfileResult;

    public sealed record NotFound : UpdateAccountProfileResult;
}

/// <summary>Validates and stores the profile of the session user (FR-05, BR-01, BR-07).</summary>
public sealed class UpdateAccountProfileHandler(IAccountStore store, TimeProvider timeProvider)
{
    public const string ProfileUpdatedAuditAction = "user.profile_updated";

    public async Task<UpdateAccountProfileResult> HandleAsync(
        Guid userId,
        Guid organizationId,
        Guid membershipId,
        IPAddress? clientIp,
        UpdateAccountProfileCommand command,
        CancellationToken cancellationToken)
    {
        var values = AccountProfileRules.Validate(command, out var errors);

        if (values is null)
        {
            return new UpdateAccountProfileResult.Invalid(errors);
        }

        var profile = await store.UpdateProfileAsync(
            userId, organizationId, membershipId, values, clientIp, timeProvider.GetUtcNow(), cancellationToken);

        return profile is null
            ? new UpdateAccountProfileResult.NotFound()
            : new UpdateAccountProfileResult.Succeeded(profile);
    }
}
