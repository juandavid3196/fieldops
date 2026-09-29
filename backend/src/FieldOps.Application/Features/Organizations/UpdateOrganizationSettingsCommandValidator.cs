using System.Text.Json;
using FieldOps.Application.Validation;
using FluentValidation;

namespace FieldOps.Application.Features.Organizations;

/// <summary>
/// Organization settings field rules (BR-01, BR-04) plus the <c>updatedAt</c>
/// concurrency token (BR-07). Reuses the same rule builders as organization
/// registration (AS-03); keys are flat (no <c>organization.</c> prefix), per
/// the API contract.
/// </summary>
public sealed class UpdateOrganizationSettingsCommandValidator
    : FieldRulesValidatorBase<UpdateOrganizationSettingsCommand>
{
    public const string WebsiteMessage = "Enter a valid website.";

    public const string StateMessage = "Select a state.";

    public const string InvalidValueMessage = "Enter a valid value.";

    public const int WebsiteMaxLength = 255;

    public UpdateOrganizationSettingsCommandValidator()
    {
        RequiredTextRule("name", c => c.Name, 160);
        RequiredTextRule("legalName", c => c.LegalName, 200);
        OptionalMaxLengthRule("taxId", c => c.TaxId, 60);
        RequiredEmailRule("email", c => c.Email);
        RequiredPhoneRule("phone", c => c.Phone);
        TimeZoneRule("timezone", c => c.Timezone);
        CurrencyRule("currency", c => c.Currency);
        TaxRateRule("defaultTaxRate", c => c.DefaultTaxRate);
        PrefixRule("quotePrefix", c => c.QuotePrefix);
        PrefixRule("workOrderPrefix", c => c.WorkOrderPrefix);
        PrefixRule("invoicePrefix", c => c.InvoicePrefix);
        NextInvoiceNumberRule("nextInvoiceNumber", c => c.NextInvoiceNumber);
        NextInvoiceNumberRule("nextQuoteNumber", c => c.NextQuoteNumber);
        NextInvoiceNumberRule("nextWorkOrderNumber", c => c.NextWorkOrderNumber);
        WebsiteRule();
        RequiredTextRule("addressLine1", c => c.AddressLine1, 180);
        RequiredTextRule("city", c => c.City, 100);
        RequiredTextRule("postalCode", c => c.PostalCode, 30);
        RuleFor(command => command.CountryCode)
            .Must(code => IsValidCountryCode(code) && code!.Trim().Length == 2).WithMessage(CountryMessage)
            .OverridePropertyName("countryCode");
        StateRegionRule();
        PricesIncludeTaxRule();
        UpdatedAtRule("updatedAt", c => c.UpdatedAt);
    }

    public static bool IsValidWebsite(string website)
    {
        if (website.Length == 0 || website.Any(char.IsWhiteSpace))
        {
            return false;
        }

        var rest = website;

        if (rest.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            rest = rest["https://".Length..];
        }
        else if (rest.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            rest = rest["http://".Length..];
        }

        var end = rest.IndexOfAny(['/', '?', '#']);
        var authority = end >= 0 ? rest[..end] : rest;
        var colon = authority.IndexOf(':', StringComparison.Ordinal);
        var host = colon >= 0 ? authority[..colon] : authority;

        return host.Length > 0
            && host.Contains('.', StringComparison.Ordinal)
            && !host.StartsWith('.')
            && !host.EndsWith('.');
    }

    private void WebsiteRule()
    {
        RuleFor(command => (command.Website ?? string.Empty).Trim())
            .Cascade(CascadeMode.Stop)
            .Must(value => value.Length <= WebsiteMaxLength).WithMessage(TooLongMessage(WebsiteMaxLength))
            .Must(value => value.Length == 0 || IsValidWebsite(value)).WithMessage(WebsiteMessage)
            .OverridePropertyName("website");
    }

    private void StateRegionRule()
    {
        RuleFor(command => command).Custom((command, context) =>
        {
            var state = (command.StateRegion ?? string.Empty).Trim();

            if (UsStates.IsUnitedStates(command.CountryCode))
            {
                if (!UsStates.Codes.Contains(state.ToUpperInvariant()))
                {
                    context.AddFailure("stateRegion", StateMessage);
                }

                return;
            }

            if (state.Length > 100)
            {
                context.AddFailure("stateRegion", TooLongMessage(100));
            }
        });
    }

    private void PricesIncludeTaxRule()
    {
        RuleFor(command => command).Custom((command, context) =>
        {
            var value = command.PricesIncludeTax;

            if (value is null || value.Value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            {
                context.AddFailure("pricesIncludeTax", RequiredMessage);
            }
            else if (value.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                context.AddFailure("pricesIncludeTax", InvalidValueMessage);
            }
        });
    }
}
