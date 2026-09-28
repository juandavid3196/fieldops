using FieldOps.Application.Features.Organizations;

namespace FieldOps.UnitTests.Organizations;

/// <summary>
/// BR-01 field rules plus the BR-07 <c>updatedAt</c> concurrency token,
/// reused from <see cref="FieldOps.Application.Validation.FieldRulesValidatorBase{T}"/>
/// (AS-03). Covers AC-07 (server validation) and AC-48 (updatedAt).
/// </summary>
public class UpdateOrganizationSettingsCommandValidatorTests
{
    private readonly UpdateOrganizationSettingsCommandValidator _validator = new();

    [Fact]
    public void Validate_FullyValidCommand_HasNoErrors()
    {
        var result = _validator.Validate(Valid());

        Assert.True(result.IsValid);
    }

    public static IEnumerable<object[]> InvalidCases()
    {
        yield return Case(c => c with { Name = "" }, "name", Required);
        yield return Case(c => c with { Name = new string('a', 161) }, "name", TooLong(160));
        yield return Case(c => c with { LegalName = "" }, "legalName", Required);
        yield return Case(c => c with { TaxId = new string('a', 61) }, "taxId", TooLong(60));
        yield return Case(c => c with { Email = "" }, "email", Required);
        yield return Case(c => c with { Email = "not-an-email" }, "email", EmailInvalid);
        yield return Case(c => c with { Phone = "" }, "phone", Required);
        yield return Case(c => c with { Phone = "abc" }, "phone", PhoneInvalid);
        yield return Case(c => c with { Timezone = "" }, "timezone", TimeZoneMessage);
        yield return Case(c => c with { Timezone = "Not/AZone" }, "timezone", TimeZoneMessage);
        yield return Case(c => c with { Currency = "" }, "currency", CurrencyMessage);
        yield return Case(c => c with { Currency = "XAU" }, "currency", CurrencyMessage);
        yield return Case(c => c with { DefaultTaxRate = null }, "defaultTaxRate", Required);
        yield return Case(c => c with { DefaultTaxRate = 100.01m }, "defaultTaxRate", TaxRateMessage);
        yield return Case(c => c with { QuotePrefix = "" }, "quotePrefix", Required);
        yield return Case(c => c with { QuotePrefix = "Q#" }, "quotePrefix", PrefixMessage);
        yield return Case(c => c with { WorkOrderPrefix = "" }, "workOrderPrefix", Required);
        yield return Case(c => c with { InvoicePrefix = "" }, "invoicePrefix", Required);
        yield return Case(c => c with { NextInvoiceNumber = null }, "nextInvoiceNumber", Required);
        yield return Case(c => c with { NextInvoiceNumber = 0 }, "nextInvoiceNumber", NextInvoiceNumberMessage);
        yield return Case(
            c => c with { NextInvoiceNumber = 1_000_000_000_000 }, "nextInvoiceNumber", NextInvoiceNumberMessage);
        yield return Case(c => c with { UpdatedAt = null }, "updatedAt", UpdatedAtInvalid);
        yield return Case(c => c with { UpdatedAt = "" }, "updatedAt", UpdatedAtInvalid);
        yield return Case(c => c with { UpdatedAt = "not-a-timestamp" }, "updatedAt", UpdatedAtInvalid);
    }

    [Theory]
    [MemberData(nameof(InvalidCases))]
    public void Validate_InvalidField_ReturnsExactlyOneErrorWithMessage(
        Func<UpdateOrganizationSettingsCommand, UpdateOrganizationSettingsCommand> mutate,
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
        Func<UpdateOrganizationSettingsCommand, UpdateOrganizationSettingsCommand> mutate,
        string key,
        string message) => [mutate, key, message];

    private static UpdateOrganizationSettingsCommand Valid() => new(
        Guid.NewGuid(),
        "Acme Field Services",
        "Acme Field Services LLC",
        "12-3456789",
        "ops@acme.com",
        "+1 555 123 4567",
        "America/Chicago",
        "USD",
        7.25m,
        "Q",
        "WO",
        "INV",
        1,
        "2026-01-01T00:00:00.000000Z",
        Guid.NewGuid(),
        null);

    private const string Required = RegisterOrganizationCommandValidator.RequiredMessage;

    private const string EmailInvalid = RegisterOrganizationCommandValidator.EmailInvalidMessage;

    private const string PhoneInvalid = RegisterOrganizationCommandValidator.PhoneInvalidMessage;

    private const string TimeZoneMessage = RegisterOrganizationCommandValidator.TimeZoneMessage;

    private const string CurrencyMessage = RegisterOrganizationCommandValidator.CurrencyMessage;

    private const string TaxRateMessage = RegisterOrganizationCommandValidator.TaxRateMessage;

    private const string PrefixMessage = RegisterOrganizationCommandValidator.PrefixMessage;

    private const string NextInvoiceNumberMessage = RegisterOrganizationCommandValidator.NextInvoiceNumberMessage;

    private const string UpdatedAtInvalid = "Enter a valid value.";

    private static string TooLong(int maxLength) => $"Use {maxLength} characters or fewer.";
}
