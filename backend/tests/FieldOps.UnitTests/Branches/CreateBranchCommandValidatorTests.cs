using System.Text.Json;
using FieldOps.Application.Features.Branches;
using FieldOps.Application.Features.Organizations;

namespace FieldOps.UnitTests.Branches;

/// <summary>
/// BR-03 field rules, including business hours, reused from
/// <see cref="FieldOps.Application.Validation.FieldRulesValidatorBase{T}"/>
/// and <see cref="BusinessHoursValidator"/> (AS-03). Covers AC-15.
/// </summary>
public class CreateBranchCommandValidatorTests
{
    private readonly CreateBranchCommandValidator _validator = new();

    [Fact]
    public void Validate_FullyValidCommand_HasNoErrors()
    {
        Assert.True(_validator.Validate(Valid()).IsValid);
    }

    public static IEnumerable<object[]> InvalidCases()
    {
        yield return Case(c => c with { Name = "" }, "name", Required);
        yield return Case(c => c with { Name = new string('a', 141) }, "name", TooLong(140));
        yield return Case(c => c with { Code = "" }, "code", Required);
        yield return Case(c => c with { Code = "TOO#LONG" }, "code", BranchCodeMessage);
        yield return Case(c => c with { Code = new string('A', 9) }, "code", BranchCodeMessage);
        yield return Case(c => c with { Phone = "abc" }, "phone", PhoneInvalid);
        yield return Case(c => c with { Email = "not-an-email" }, "email", EmailInvalid);
        yield return Case(c => c with { Timezone = "" }, "timezone", TimeZoneMessage);
        yield return Case(c => c with { AddressLine1 = "" }, "addressLine1", Required);
        yield return Case(c => c with { AddressLine2 = new string('a', 181) }, "addressLine2", TooLong(180));
        yield return Case(c => c with { City = "" }, "city", Required);
        yield return Case(c => c with { StateRegion = new string('a', 101) }, "stateRegion", TooLong(100));
        yield return Case(c => c with { PostalCode = "" }, "postalCode", Required);
        yield return Case(c => c with { CountryCode = "" }, "countryCode", CountryMessage);
        yield return Case(c => c with { CountryCode = "ZZ" }, "countryCode", CountryMessage);
        yield return Case(
            c => c with { BusinessHours = ParseBusinessHours("[]") }, "businessHours", StructuralInvalid);
        yield return Case(
            c => c with { BusinessHours = ParseBusinessHours("""{"monday":{"end":"17:00"}}""") },
            "businessHours.monday.start",
            Required);
        yield return Case(
            c => c with { BusinessHours = ParseBusinessHours("""{"monday":{"start":"17:00","end":"08:00"}}""") },
            "businessHours.monday.end",
            EndNotAfterStart);
    }

    [Theory]
    [MemberData(nameof(InvalidCases))]
    public void Validate_InvalidField_ReturnsExactlyOneErrorWithMessage(
        Func<CreateBranchCommand, CreateBranchCommand> mutate,
        string key,
        string message)
    {
        var command = mutate(Valid());

        var result = _validator.Validate(command);

        var error = Assert.Single(result.Errors);
        Assert.Equal(key, error.PropertyName);
        Assert.Equal(message, error.ErrorMessage);
    }

    private static object[] Case(
        Func<CreateBranchCommand, CreateBranchCommand> mutate, string key, string message) => [mutate, key, message];

    private static JsonElement ParseBusinessHours(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static CreateBranchCommand Valid() => new(
        Guid.NewGuid(),
        "Main Branch",
        "MAIN",
        "+1 555 987 6543",
        "branch@acme.com",
        "America/Chicago",
        "123 Main St",
        "Suite 200",
        "Chicago",
        "IL",
        "60601",
        "US",
        ParseBusinessHours("""{"monday":{"start":"08:00","end":"17:00"}}"""),
        null,
        null,
        Guid.NewGuid(),
        null);

    private const string Required = RegisterOrganizationCommandValidator.RequiredMessage;

    private const string EmailInvalid = RegisterOrganizationCommandValidator.EmailInvalidMessage;

    private const string PhoneInvalid = RegisterOrganizationCommandValidator.PhoneInvalidMessage;

    private const string TimeZoneMessage = RegisterOrganizationCommandValidator.TimeZoneMessage;

    private const string CountryMessage = RegisterOrganizationCommandValidator.CountryMessage;

    private const string BranchCodeMessage = RegisterOrganizationCommandValidator.BranchCodeMessage;

    private const string StructuralInvalid = BusinessHoursValidator.StructuralInvalidMessage;

    private const string EndNotAfterStart = BusinessHoursValidator.EndNotAfterStartMessage;

    private static string TooLong(int maxLength) => $"Use {maxLength} characters or fewer.";
}
