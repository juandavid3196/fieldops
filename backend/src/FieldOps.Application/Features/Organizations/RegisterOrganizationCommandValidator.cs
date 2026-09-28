using FieldOps.Application.Validation;
using FluentValidation;

namespace FieldOps.Application.Features.Organizations;

/// <summary>
/// Organization registration field rules (BR-03 to BR-17). For every field,
/// the first failing rule in the validation messages table order wins and is
/// the only message reported for that field (BR-25). Rule builders and
/// format predicates live in <see cref="FieldRulesValidatorBase{T}"/> so
/// organization settings and branch validation reuse them without
/// duplicating the logic (AS-03).
/// </summary>
public sealed class RegisterOrganizationCommandValidator
    : FieldRulesValidatorBase<RegisterOrganizationCommand>
{
    public new const string RequiredMessage = FieldRulesValidatorBase<RegisterOrganizationCommand>.RequiredMessage;

    public new const string EmailInvalidMessage =
        FieldRulesValidatorBase<RegisterOrganizationCommand>.EmailInvalidMessage;

    public new const string PhoneInvalidMessage =
        FieldRulesValidatorBase<RegisterOrganizationCommand>.PhoneInvalidMessage;

    public new const string TimeZoneMessage = FieldRulesValidatorBase<RegisterOrganizationCommand>.TimeZoneMessage;

    public new const string CurrencyMessage = FieldRulesValidatorBase<RegisterOrganizationCommand>.CurrencyMessage;

    public new const string CountryMessage = FieldRulesValidatorBase<RegisterOrganizationCommand>.CountryMessage;

    public new const string TaxRateMessage = FieldRulesValidatorBase<RegisterOrganizationCommand>.TaxRateMessage;

    public new const string PrefixMessage = FieldRulesValidatorBase<RegisterOrganizationCommand>.PrefixMessage;

    public new const string BranchCodeMessage =
        FieldRulesValidatorBase<RegisterOrganizationCommand>.BranchCodeMessage;

    public new const string NextInvoiceNumberMessage =
        FieldRulesValidatorBase<RegisterOrganizationCommand>.NextInvoiceNumberMessage;

    public const string PasswordLengthMessage = "Use 12 to 128 characters.";

    public const string PasswordEqualsEmailMessage =
        "Choose a password that is different from your email.";

    public new const int EmailMaxLength = FieldRulesValidatorBase<RegisterOrganizationCommand>.EmailMaxLength;

    public new const int PhoneMaxLength = FieldRulesValidatorBase<RegisterOrganizationCommand>.PhoneMaxLength;

    public RegisterOrganizationCommandValidator()
    {
        RequiredTextRule("organization.name", c => c.Organization.Name, 160);
        RequiredTextRule("organization.legalName", c => c.Organization.LegalName, 200);
        OptionalMaxLengthRule("organization.taxId", c => c.Organization.TaxId, 60);
        RequiredEmailRule("organization.email", c => c.Organization.Email);
        RequiredPhoneRule("organization.phone", c => c.Organization.Phone);
        TimeZoneRule("organization.timezone", c => c.Organization.Timezone);
        CurrencyRule("organization.currency", c => c.Organization.Currency);
        TaxRateRule("organization.defaultTaxRate", c => c.Organization.DefaultTaxRate);
        PrefixRule("organization.quotePrefix", c => c.Organization.QuotePrefix);
        PrefixRule("organization.workOrderPrefix", c => c.Organization.WorkOrderPrefix);
        PrefixRule("organization.invoicePrefix", c => c.Organization.InvoicePrefix);
        NextInvoiceNumberRule("organization.nextInvoiceNumber", c => c.Organization.NextInvoiceNumber);

        RequiredTextRule("branch.name", c => c.Branch.Name, 140);
        BranchCodeRule("branch.code", c => c.Branch.Code);
        OptionalPhoneRule("branch.phone", c => c.Branch.Phone);
        OptionalEmailRule("branch.email", c => c.Branch.Email);
        TimeZoneRule("branch.timezone", c => c.Branch.Timezone);
        RequiredTextRule("branch.addressLine1", c => c.Branch.AddressLine1, 180);
        RequiredTextRule("branch.city", c => c.Branch.City, 100);
        OptionalMaxLengthRule("branch.stateRegion", c => c.Branch.StateRegion, 100);
        RequiredTextRule("branch.postalCode", c => c.Branch.PostalCode, 30);
        CountryRule("branch.countryCode", c => c.Branch.CountryCode);
        BusinessHoursRule();

        RequiredTextRule("owner.firstName", c => c.Owner.FirstName, 100);
        RequiredTextRule("owner.lastName", c => c.Owner.LastName, 100);
        RequiredEmailRule("owner.email", c => c.Owner.Email);
        OptionalPhoneRule("owner.phone", c => c.Owner.Phone);
        PasswordRule();
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
}
