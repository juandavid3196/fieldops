using System.Globalization;
using System.Text.RegularExpressions;
using FieldOps.Application.Features.Organizations;
using FieldOps.Application.Validation;
using FluentValidation;

namespace FieldOps.Application.Features.PublicRequests;

/// <summary>
/// Server-side rules of a public submission (BR-03 to BR-08, BR-13). Errors
/// are keyed by the request field paths of the contract.
/// </summary>
public sealed partial class SubmitPublicServiceRequestValidator
    : FieldRulesValidatorBase<PublicSubmissionValidationInput>
{
    public const string PreferenceMessage = "Select at least one method.";

    public const string PublicPhoneMessage = "Enter a phone number with 10 to 15 digits.";

    public const string PropertyTypeMessage = "Select home or business.";

    public const string StateMessage = "Select a valid US state.";

    public const string PostalCodeMessage = "Enter a valid ZIP code, for example 90210 or 90210-1234.";

    public const string CategoryMessage = "Select a valid category.";

    public const string ServiceMessage = "Select a valid service.";

    public const string UrgencyMessage = "Select standard, urgent or emergency.";

    public const string DateModeMessage = "Select as soon as possible, a date or flexible.";

    public const string PreferredDateRequiredMessage = "Choose a date.";

    public const string PreferredDateRangeMessage = "Choose a date from today up to 90 days ahead.";

    public const string PreferredDateNotAllowedMessage = "A date is only allowed when you choose a date.";

    public const string TimeWindowMessage = "Select a time window.";

    public const string ConsentMessage = "You must accept to submit your request.";

    public const string TooManyFilesMessage = "Attach at most 5 files.";

    public const string TotalSizeMessage = "Attachments must not exceed 25 MB in total.";

    private static readonly HashSet<string> Urgencies = new(StringComparer.Ordinal) { "standard", "urgent", "emergency" };

    private static readonly HashSet<string> DateModes = new(StringComparer.Ordinal) { "asap", "date", "flexible" };

    public SubmitPublicServiceRequestValidator()
    {
        RequiredTextRule("contact.firstName", i => i.Command.Contact.FirstName, 100);
        RequiredTextRule("contact.lastName", i => i.Command.Contact.LastName, 100);
        RequiredEmailRule("contact.email", i => i.Command.Contact.Email);
        PublicPhoneRule();

        RuleFor(i => i.Command.Contact)
            .Must(contact => contact.PrefersEmail || contact.PrefersSms)
            .WithMessage(PreferenceMessage)
            .OverridePropertyName("contact.prefersEmail");

        RuleFor(i => i.Command.Property.PropertyType)
            .Must(type => type is "home" or "business").WithMessage(PropertyTypeMessage)
            .OverridePropertyName("property.propertyType");
        RequiredTextRule("property.addressLine1", i => i.Command.Property.AddressLine1, 180);
        OptionalMaxLengthRule("property.addressLine2", i => i.Command.Property.AddressLine2, 180);
        RequiredTextRule("property.city", i => i.Command.Property.City, 100);

        RuleFor(i => i.Command.Property.State)
            .Must(state => state is not null && UsStates.Codes.Contains(state.Trim())).WithMessage(StateMessage)
            .OverridePropertyName("property.state");
        RuleFor(i => i.Command.Property.PostalCode)
            .Must(code => code is not null && PostalCodeRegex().IsMatch(code.Trim())).WithMessage(PostalCodeMessage)
            .OverridePropertyName("property.postalCode");
        OptionalMaxLengthRule("property.accessInstructions", i => i.Command.Property.AccessInstructions, 1000);

        RuleFor(i => i)
            .Must(input => input.Command.Service.CategoryId is { } id
                && input.Form.Categories.Any(category => category.Id == id))
            .WithMessage(CategoryMessage)
            .OverridePropertyName("service.categoryId");
        RuleFor(i => i)
            .Must(ServiceIsValid).WithMessage(ServiceMessage)
            .OverridePropertyName("service.serviceId");
        RequiredTextRule("service.description", i => i.Command.Service.Description, 1000);
        RuleFor(i => i.Command.Service.Urgency)
            .Must(urgency => urgency is not null && Urgencies.Contains(urgency)).WithMessage(UrgencyMessage)
            .OverridePropertyName("service.urgency");

        RuleFor(i => i.Command.Availability.DateMode)
            .Must(mode => mode is not null && DateModes.Contains(mode)).WithMessage(DateModeMessage)
            .OverridePropertyName("availability.dateMode");
        RuleFor(i => i)
            .Custom(ValidatePreferredDate);
        RuleFor(i => i.Command.Availability.TimeWindow)
            .Must(AvailabilityWindowCalculator.IsValidWindow).WithMessage(TimeWindowMessage)
            .OverridePropertyName("availability.timeWindow");
        OptionalMaxLengthRule("availability.schedulingNotes", i => i.Command.Availability.SchedulingNotes, 1000);

        RuleFor(i => i.Command.Consent)
            .Equal(true).WithMessage(ConsentMessage)
            .OverridePropertyName("consent");

        RuleFor(i => i.Command.Attachments)
            .Custom(ValidateAttachments);
    }

    /// <summary>Digits left after removing spaces and <c>()-+.</c>: 10 to 15, nothing else allowed.</summary>
    public static bool IsValidPublicPhone(string phone)
    {
        var stripped = phone.Where(c => c is not (' ' or '(' or ')' or '-' or '+' or '.')).ToArray();

        return stripped.Length is >= 10 and <= 15 && stripped.All(char.IsAsciiDigit);
    }

    private void PublicPhoneRule() =>
        RuleFor(i => (i.Command.Contact.Phone ?? string.Empty).Trim())
            .Cascade(CascadeMode.Stop)
            .Must(value => value.Length > 0).WithMessage(RequiredMessage)
            .Must(value => value.Length <= PhoneMaxLength).WithMessage(TooLongMessage(PhoneMaxLength))
            .Must(IsValidPublicPhone).WithMessage(PublicPhoneMessage)
            .OverridePropertyName("contact.phone");

    private static bool ServiceIsValid(PublicSubmissionValidationInput input)
    {
        var service = input.Command.Service;

        if (service.NotSure)
        {
            return service.ServiceId is null;
        }

        if (service.ServiceId is not { } serviceId || service.CategoryId is not { } categoryId)
        {
            return false;
        }

        return input.Form.Categories.Any(category =>
            category.Id == categoryId && category.Services.Any(item => item.Id == serviceId));
    }

    private static void ValidatePreferredDate(
        PublicSubmissionValidationInput input, ValidationContext<PublicSubmissionValidationInput> context)
    {
        const string key = "availability.preferredDate";
        var availability = input.Command.Availability;
        var raw = availability.PreferredDate?.Trim();

        if (availability.DateMode != "date")
        {
            if (!string.IsNullOrEmpty(raw))
            {
                context.AddFailure(key, PreferredDateNotAllowedMessage);
            }

            return;
        }

        if (string.IsNullOrEmpty(raw))
        {
            context.AddFailure(key, PreferredDateRequiredMessage);
            return;
        }

        if (!TryParseDate(raw, out var date)
            || date < input.Today
            || date > input.Today.AddDays(AvailabilityWindowCalculator.MaxDaysAhead))
        {
            context.AddFailure(key, PreferredDateRangeMessage);
        }
    }

    public static bool TryParseDate(string value, out DateOnly date) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private static void ValidateAttachments(
        IReadOnlyList<PublicAttachmentInput> attachments,
        ValidationContext<PublicSubmissionValidationInput> context)
    {
        if (attachments.Count > AttachmentContentInspector.MaxFiles)
        {
            context.AddFailure("attachments", TooManyFilesMessage);
            return;
        }

        long total = 0;

        for (var index = 0; index < attachments.Count; index++)
        {
            var file = attachments[index];
            total += file.Content.Length;

            var inspection = AttachmentContentInspector.Inspect(file.FileName, file.Content);

            if (!inspection.IsValid)
            {
                context.AddFailure(
                    string.Create(CultureInfo.InvariantCulture, $"attachments[{index}]"),
                    inspection.Error!);
            }
        }

        if (total > AttachmentContentInspector.MaxTotalBytes)
        {
            context.AddFailure("attachments", TotalSizeMessage);
        }
    }

    [GeneratedRegex("^[0-9]{5}(-[0-9]{4})?$")]
    private static partial Regex PostalCodeRegex();
}
