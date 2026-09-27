using System.Globalization;
using FluentValidation;

namespace FieldOps.Application.Features.Organizations;

/// <summary>
/// Organization registration field rules (BR-03 to BR-17). For every field,
/// the first failing rule in the validation messages table order wins and is
/// the only message reported for that field (BR-25).
/// </summary>
public sealed class RegisterOrganizationCommandValidator : AbstractValidator<RegisterOrganizationCommand>
{
    public const string RequiredMessage = "This field is required.";

    public const string EmailInvalidMessage =
        "Enter a valid email address, for example name@company.com.";

    public const string PhoneInvalidMessage = "Enter a valid phone number.";

    public const string TimeZoneMessage = "Select a time zone.";

    public const string CurrencyMessage = "Select a currency.";

    public const string CountryMessage = "Select a country.";

    public const string TaxRateMessage = "Enter a rate between 0 and 100 with up to 4 decimals.";

    public const string PrefixMessage = "Use 1–20 characters: letters A–Z, numbers and hyphens.";

    public const string BranchCodeMessage = "Use 1–8 characters: letters A–Z, numbers and hyphens.";

    public const string NextInvoiceNumberMessage =
        "Enter a whole number from 1 to 999,999,999,999.";

    public const string PasswordLengthMessage = "Use 12 to 128 characters.";

    public const string PasswordEqualsEmailMessage =
        "Choose a password that is different from your email.";

    public const int EmailMaxLength = 254;

    public const int PhoneMaxLength = 40;

    public RegisterOrganizationCommandValidator()
    {
        RequiredTextRule("organization.name", c => c.Organization.Name, 160);
        RequiredTextRule("organization.legalName", c => c.Organization.LegalName, 200);
        OptionalMaxLengthRule("organization.taxId", c => c.Organization.TaxId, 60);
        RequiredEmailRule("organization.email", c => c.Organization.Email);
        RequiredPhoneRule("organization.phone", c => c.Organization.Phone);
        TimeZoneRule("organization.timezone", c => c.Organization.Timezone);
        CurrencyRule();
        TaxRateRule();
        PrefixRule("organization.quotePrefix", c => c.Organization.QuotePrefix);
        PrefixRule("organization.workOrderPrefix", c => c.Organization.WorkOrderPrefix);
        PrefixRule("organization.invoicePrefix", c => c.Organization.InvoicePrefix);
        NextInvoiceNumberRule();

        RequiredTextRule("branch.name", c => c.Branch.Name, 140);
        BranchCodeRule();
        OptionalPhoneRule("branch.phone", c => c.Branch.Phone);
        OptionalEmailRule("branch.email", c => c.Branch.Email);
        TimeZoneRule("branch.timezone", c => c.Branch.Timezone);
        RequiredTextRule("branch.addressLine1", c => c.Branch.AddressLine1, 180);
        RequiredTextRule("branch.city", c => c.Branch.City, 100);
        OptionalMaxLengthRule("branch.stateRegion", c => c.Branch.StateRegion, 100);
        RequiredTextRule("branch.postalCode", c => c.Branch.PostalCode, 30);
        CountryRule();
        BusinessHoursRule();

        RequiredTextRule("owner.firstName", c => c.Owner.FirstName, 100);
        RequiredTextRule("owner.lastName", c => c.Owner.LastName, 100);
        RequiredEmailRule("owner.email", c => c.Owner.Email);
        OptionalPhoneRule("owner.phone", c => c.Owner.Phone);
        PasswordRule();
    }

    private void RequiredTextRule(
        string key,
        Func<RegisterOrganizationCommand, string?> selector,
        int maxLength)
    {
        RuleFor(command => (selector(command) ?? string.Empty).Trim())
            .Cascade(CascadeMode.Stop)
            .Must(value => value.Length > 0).WithMessage(RequiredMessage)
            .Must(value => value.Length <= maxLength).WithMessage(TooLongMessage(maxLength))
            .OverridePropertyName(key);
    }

    private void OptionalMaxLengthRule(
        string key,
        Func<RegisterOrganizationCommand, string?> selector,
        int maxLength)
    {
        RuleFor(command => (selector(command) ?? string.Empty).Trim())
            .Must(value => value.Length <= maxLength).WithMessage(TooLongMessage(maxLength))
            .OverridePropertyName(key);
    }

    private void RequiredEmailRule(string key, Func<RegisterOrganizationCommand, string?> selector)
    {
        RuleFor(command => (selector(command) ?? string.Empty).Trim())
            .Cascade(CascadeMode.Stop)
            .Must(value => value.Length > 0).WithMessage(RequiredMessage)
            .Must(value => value.Length <= EmailMaxLength).WithMessage(TooLongMessage(EmailMaxLength))
            .Must(IsValidEmailFormat).WithMessage(EmailInvalidMessage)
            .OverridePropertyName(key);
    }

    private void OptionalEmailRule(string key, Func<RegisterOrganizationCommand, string?> selector)
    {
        RuleFor(command => (selector(command) ?? string.Empty).Trim())
            .Cascade(CascadeMode.Stop)
            .Must(value => value.Length <= EmailMaxLength).WithMessage(TooLongMessage(EmailMaxLength))
            .Must(value => value.Length == 0 || IsValidEmailFormat(value)).WithMessage(EmailInvalidMessage)
            .OverridePropertyName(key);
    }

    private void RequiredPhoneRule(string key, Func<RegisterOrganizationCommand, string?> selector)
    {
        RuleFor(command => (selector(command) ?? string.Empty).Trim())
            .Cascade(CascadeMode.Stop)
            .Must(value => value.Length > 0).WithMessage(RequiredMessage)
            .Must(value => value.Length <= PhoneMaxLength).WithMessage(TooLongMessage(PhoneMaxLength))
            .Must(IsValidPhoneFormat).WithMessage(PhoneInvalidMessage)
            .OverridePropertyName(key);
    }

    private void OptionalPhoneRule(string key, Func<RegisterOrganizationCommand, string?> selector)
    {
        RuleFor(command => (selector(command) ?? string.Empty).Trim())
            .Cascade(CascadeMode.Stop)
            .Must(value => value.Length <= PhoneMaxLength).WithMessage(TooLongMessage(PhoneMaxLength))
            .Must(value => value.Length == 0 || IsValidPhoneFormat(value)).WithMessage(PhoneInvalidMessage)
            .OverridePropertyName(key);
    }

    private void TimeZoneRule(string key, Func<RegisterOrganizationCommand, string?> selector)
    {
        RuleFor(command => selector(command))
            .Must(IsValidIanaTimeZone).WithMessage(TimeZoneMessage)
            .OverridePropertyName(key);
    }

    private void CurrencyRule()
    {
        RuleFor(command => command.Organization.Currency)
            .Must(IsSupportedCurrency).WithMessage(CurrencyMessage)
            .OverridePropertyName("organization.currency");
    }

    private void CountryRule()
    {
        RuleFor(command => command.Branch.CountryCode)
            .Must(IsValidCountryCode).WithMessage(CountryMessage)
            .OverridePropertyName("branch.countryCode");
    }

    private void TaxRateRule()
    {
        RuleFor(command => command.Organization.DefaultTaxRate)
            .Cascade(CascadeMode.Stop)
            .NotNull().WithMessage(RequiredMessage)
            .Must(IsValidTaxRate).WithMessage(TaxRateMessage)
            .OverridePropertyName("organization.defaultTaxRate");
    }

    private void PrefixRule(string key, Func<RegisterOrganizationCommand, string?> selector)
    {
        RuleFor(command => (selector(command) ?? string.Empty).Trim().ToUpperInvariant())
            .Cascade(CascadeMode.Stop)
            .Must(value => value.Length > 0).WithMessage(RequiredMessage)
            .Must(IsValidPrefix).WithMessage(PrefixMessage)
            .OverridePropertyName(key);
    }

    private void NextInvoiceNumberRule()
    {
        RuleFor(command => command.Organization.NextInvoiceNumber)
            .Cascade(CascadeMode.Stop)
            .NotNull().WithMessage(RequiredMessage)
            .Must(value => value is >= 1 and <= 999_999_999_999).WithMessage(NextInvoiceNumberMessage)
            .OverridePropertyName("organization.nextInvoiceNumber");
    }

    private void BranchCodeRule()
    {
        RuleFor(command => (command.Branch.Code ?? string.Empty).Trim().ToUpperInvariant())
            .Cascade(CascadeMode.Stop)
            .Must(value => value.Length > 0).WithMessage(RequiredMessage)
            .Must(IsValidBranchCode).WithMessage(BranchCodeMessage)
            .OverridePropertyName("branch.code");
    }

    private void BusinessHoursRule()
    {
        RuleFor(command => command).Custom((command, context) =>
        {
            BusinessHoursValidator.TryValidate(
                command.Branch.BusinessHours,
                (key, message) => context.AddFailure(key, message),
                out _);
        });
    }

    private void PasswordRule()
    {
        RuleFor(command => command)
            .Custom((command, context) =>
            {
                var password = command.Owner.Password ?? string.Empty;

                if (password.Length == 0)
                {
                    context.AddFailure("owner.password", RequiredMessage);
                    return;
                }

                if (password.Length is < 12 or > 128)
                {
                    context.AddFailure("owner.password", PasswordLengthMessage);
                    return;
                }

                var email = (command.Owner.Email ?? string.Empty).Trim();

                if (string.Equals(password, email, StringComparison.OrdinalIgnoreCase))
                {
                    context.AddFailure("owner.password", PasswordEqualsEmailMessage);
                }
            });
    }

    private static string TooLongMessage(int maxLength) =>
        string.Format(CultureInfo.InvariantCulture, "Use {0} characters or fewer.", maxLength);

    private static bool IsValidEmailFormat(string email)
    {
        if (email.Length == 0 || email.Any(char.IsWhiteSpace))
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

    private static bool IsValidPhoneFormat(string phone)
    {
        if (phone.Any(c => !(char.IsDigit(c) || c is ' ' or '+' or '(' or ')' or '-' or '.')))
        {
            return false;
        }

        return phone.Count(char.IsDigit) >= 7;
    }

    private static bool IsValidIanaTimeZone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id).HasIanaId;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }

    private static bool IsSupportedCurrency(string? currency) =>
        !string.IsNullOrWhiteSpace(currency)
        && SupportedCurrencies.Codes.Contains(currency.Trim().ToUpperInvariant());

    private static bool IsValidCountryCode(string? countryCode)
    {
        if (string.IsNullOrWhiteSpace(countryCode))
        {
            return false;
        }

        try
        {
            _ = new RegionInfo(countryCode.Trim().ToUpperInvariant());
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool IsValidTaxRate(decimal? value) =>
        value is { } rate && rate >= 0 && rate <= 100 && Math.Round(rate, 4) == rate;

    private static bool IsValidPrefix(string value) =>
        value.Length is >= 1 and <= 20 && value.All(IsPrefixCharacter);

    private static bool IsValidBranchCode(string value) =>
        value.Length is >= 1 and <= 8 && value.All(IsPrefixCharacter);

    private static bool IsPrefixCharacter(char c) =>
        (c is >= 'A' and <= 'Z') || (c is >= '0' and <= '9') || c == '-';
}
