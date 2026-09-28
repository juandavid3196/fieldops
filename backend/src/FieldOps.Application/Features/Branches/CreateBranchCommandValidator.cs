using FieldOps.Application.Features.Organizations;
using FieldOps.Application.Validation;
using FluentValidation;

namespace FieldOps.Application.Features.Branches;

/// <summary>
/// Branch field rules (BR-03). Reuses the same rule builders as
/// organization registration and settings (AS-03); keys are flat, per the
/// API contract.
/// </summary>
public sealed class CreateBranchCommandValidator : FieldRulesValidatorBase<CreateBranchCommand>
{
    public CreateBranchCommandValidator()
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
