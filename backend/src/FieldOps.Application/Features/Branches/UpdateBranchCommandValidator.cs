using FieldOps.Application.Features.Organizations;
using FieldOps.Application.Validation;
using FluentValidation;

namespace FieldOps.Application.Features.Branches;

/// <summary>
/// Branch field rules (BR-03) plus the <c>updatedAt</c> concurrency token
/// (BR-07). Reuses the same rule builders as organization registration and
/// settings (AS-03).
/// </summary>
public sealed class UpdateBranchCommandValidator : FieldRulesValidatorBase<UpdateBranchCommand>
{
    public UpdateBranchCommandValidator()
    {
        RequiredTextRule("name", c => c.Name, 140);
        BranchCodeRule("code", c => c.Code);
        OptionalPhoneRule("phone", c => c.Phone);
        OptionalEmailRule("email", c => c.Email);
        TimeZoneRule("timezone", c => c.Timezone);
        RequiredTextRule("addressLine1", c => c.AddressLine1, 180);
        OptionalMaxLengthRule("addressLine2", c => c.AddressLine2, 180);
        RequiredTextRule("city", c => c.City, 100);
        OptionalMaxLengthRule("stateRegion", c => c.StateRegion, 100);
        RequiredTextRule("postalCode", c => c.PostalCode, 30);
        CountryRule("countryCode", c => c.CountryCode);
        BusinessHoursRule();
        UpdatedAtRule("updatedAt", c => c.UpdatedAt);
        BranchBillingRules();
    }

    private void BranchBillingRules()
    {
        RuleFor(command => command).Custom((command, context) =>
        {
            if (!ServicePostalCodes.TryRead(command.ServicePostalCodes, required: true, out _, out var codesError))
            {
                context.AddFailure("servicePostalCodes", codesError!);
            }

            if (!ServicePostalCodes.TryReadBoolean(
                    command.UsesCompanyBilling, required: true, defaultValue: true, out _, out var billingError))
            {
                context.AddFailure("usesCompanyBilling", billingError!);
            }
        });
    }

    private void BusinessHoursRule()
    {
        RuleFor(command => command).Custom((command, context) =>
        {
            BusinessHoursValidator.TryValidate(
                command.BusinessHours,
                (key, message) => context.AddFailure(key, message),
                out _,
                rootKey: "businessHours");
        });
    }
}
