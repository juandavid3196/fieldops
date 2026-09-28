using System.Globalization;
using FluentValidation;

namespace FieldOps.Application.Validation;

/// <summary>
/// Shared field-validation rule builders and format predicates (BR-01/BR-03
/// style limits, formats and messages), reused by organization registration,
/// organization settings and branch validators so the rules are defined once
/// (AS-03). Subclasses call the protected builders from their constructor,
/// the same way <c>RegisterOrganizationCommandValidator</c> did before this
/// refactor; behavior and messages are unchanged.
/// </summary>
public abstract class FieldRulesValidatorBase<T> : AbstractValidator<T>
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

    public const int EmailMaxLength = 254;

    public const int PhoneMaxLength = 40;

    protected void RequiredTextRule(string key, Func<T, string?> selector, int maxLength)
    {
        RuleFor(command => (selector(command) ?? string.Empty).Trim())
            .Cascade(CascadeMode.Stop)
            .Must(value => value.Length > 0).WithMessage(RequiredMessage)
            .Must(value => value.Length <= maxLength).WithMessage(TooLongMessage(maxLength))
            .OverridePropertyName(key);
    }

    protected void OptionalMaxLengthRule(string key, Func<T, string?> selector, int maxLength)
    {
        RuleFor(command => (selector(command) ?? string.Empty).Trim())
            .Must(value => value.Length <= maxLength).WithMessage(TooLongMessage(maxLength))
            .OverridePropertyName(key);
    }

    protected void RequiredEmailRule(string key, Func<T, string?> selector)
    {
        RuleFor(command => (selector(command) ?? string.Empty).Trim())
            .Cascade(CascadeMode.Stop)
            .Must(value => value.Length > 0).WithMessage(RequiredMessage)
            .Must(value => value.Length <= EmailMaxLength).WithMessage(TooLongMessage(EmailMaxLength))
            .Must(IsValidEmailFormat).WithMessage(EmailInvalidMessage)
            .OverridePropertyName(key);
    }

    protected void OptionalEmailRule(string key, Func<T, string?> selector)
    {
        RuleFor(command => (selector(command) ?? string.Empty).Trim())
            .Cascade(CascadeMode.Stop)
            .Must(value => value.Length <= EmailMaxLength).WithMessage(TooLongMessage(EmailMaxLength))
            .Must(value => value.Length == 0 || IsValidEmailFormat(value)).WithMessage(EmailInvalidMessage)
            .OverridePropertyName(key);
    }

    protected void RequiredPhoneRule(string key, Func<T, string?> selector)
    {
        RuleFor(command => (selector(command) ?? string.Empty).Trim())
            .Cascade(CascadeMode.Stop)
            .Must(value => value.Length > 0).WithMessage(RequiredMessage)
            .Must(value => value.Length <= PhoneMaxLength).WithMessage(TooLongMessage(PhoneMaxLength))
            .Must(IsValidPhoneFormat).WithMessage(PhoneInvalidMessage)
            .OverridePropertyName(key);
    }

    protected void OptionalPhoneRule(string key, Func<T, string?> selector)
    {
        RuleFor(command => (selector(command) ?? string.Empty).Trim())
            .Cascade(CascadeMode.Stop)
            .Must(value => value.Length <= PhoneMaxLength).WithMessage(TooLongMessage(PhoneMaxLength))
            .Must(value => value.Length == 0 || IsValidPhoneFormat(value)).WithMessage(PhoneInvalidMessage)
            .OverridePropertyName(key);
    }

    protected void TimeZoneRule(string key, Func<T, string?> selector)
    {
        RuleFor(command => selector(command))
            .Must(IsValidIanaTimeZone).WithMessage(TimeZoneMessage)
            .OverridePropertyName(key);
    }

    protected void CurrencyRule(string key, Func<T, string?> selector)
    {
        RuleFor(command => selector(command))
            .Must(IsSupportedCurrency).WithMessage(CurrencyMessage)
            .OverridePropertyName(key);
    }

    protected void CountryRule(string key, Func<T, string?> selector)
    {
        RuleFor(command => selector(command))
            .Must(IsValidCountryCode).WithMessage(CountryMessage)
            .OverridePropertyName(key);
    }

    protected void TaxRateRule(string key, Func<T, decimal?> selector)
    {
        RuleFor(command => selector(command))
            .Cascade(CascadeMode.Stop)
            .NotNull().WithMessage(RequiredMessage)
            .Must(IsValidTaxRate).WithMessage(TaxRateMessage)
            .OverridePropertyName(key);
    }

    protected void PrefixRule(string key, Func<T, string?> selector)
    {
        RuleFor(command => (selector(command) ?? string.Empty).Trim().ToUpperInvariant())
            .Cascade(CascadeMode.Stop)
            .Must(value => value.Length > 0).WithMessage(RequiredMessage)
            .Must(IsValidPrefix).WithMessage(PrefixMessage)
            .OverridePropertyName(key);
    }

    protected void BranchCodeRule(string key, Func<T, string?> selector)
    {
        RuleFor(command => (selector(command) ?? string.Empty).Trim().ToUpperInvariant())
            .Cascade(CascadeMode.Stop)
            .Must(value => value.Length > 0).WithMessage(RequiredMessage)
            .Must(IsValidBranchCode).WithMessage(BranchCodeMessage)
            .OverridePropertyName(key);
    }

    protected void NextInvoiceNumberRule(string key, Func<T, long?> selector)
    {
        RuleFor(command => selector(command))
            .Cascade(CascadeMode.Stop)
            .NotNull().WithMessage(RequiredMessage)
            .Must(value => value is >= 1 and <= 999_999_999_999).WithMessage(NextInvoiceNumberMessage)
            .OverridePropertyName(key);
    }

    protected void UpdatedAtRule(string key, Func<T, string?> selector)
    {
        RuleFor(command => selector(command))
            .Must(value => UpdatedAtValidation.TryParse(value, out _))
            .WithMessage(UpdatedAtValidation.InvalidMessage)
            .OverridePropertyName(key);
    }

    public static string TooLongMessage(int maxLength) =>
        string.Format(CultureInfo.InvariantCulture, "Use {0} characters or fewer.", maxLength);

    public static bool IsValidEmailFormat(string email)
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

    public static bool IsValidPhoneFormat(string phone)
    {
        if (phone.Any(c => !(char.IsDigit(c) || c is ' ' or '+' or '(' or ')' or '-' or '.')))
        {
            return false;
        }

        return phone.Count(char.IsDigit) >= 7;
    }

    public static bool IsValidIanaTimeZone(string? id)
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

    public static bool IsSupportedCurrency(string? currency) =>
        !string.IsNullOrWhiteSpace(currency)
        && Features.Organizations.SupportedCurrencies.Codes.Contains(currency.Trim().ToUpperInvariant());

    public static bool IsValidCountryCode(string? countryCode)
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

    public static bool IsValidTaxRate(decimal? value) =>
        value is { } rate && rate >= 0 && rate <= 100 && Math.Round(rate, 4) == rate;

    public static bool IsValidPrefix(string value) =>
        value.Length is >= 1 and <= 20 && value.All(IsPrefixCharacter);

    public static bool IsValidBranchCode(string value) =>
        value.Length is >= 1 and <= 8 && value.All(IsPrefixCharacter);

    private static bool IsPrefixCharacter(char c) =>
        (c is >= 'A' and <= 'Z') || (c is >= '0' and <= '9') || c == '-';
}
