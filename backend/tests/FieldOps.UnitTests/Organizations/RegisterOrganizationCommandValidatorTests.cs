using System.Text.Json;
using FieldOps.Application.Features.Organizations;

namespace FieldOps.UnitTests.Organizations;

/// <summary>
/// One validator, one rule catalog (BR-03 to BR-17): a single focused theory
/// class per field group, covering each field's first-failing-rule message
/// (BR-25) rather than every boundary combination.
/// </summary>
public class RegisterOrganizationCommandValidatorTests
{
    private readonly RegisterOrganizationCommandValidator _validator = new();

    [Fact]
    public void Validate_FullyValidCommand_HasNoErrors()
    {
        var result = _validator.Validate(Valid());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_AllDaysClosed_HasNoErrors()
    {
        var command = Valid() with
        {
            Branch = Valid().Branch with { BusinessHours = ParseBusinessHours("{}") },
        };

        Assert.True(_validator.Validate(command).IsValid);
    }

    public static IEnumerable<object[]> InvalidCases()
    {
        yield return Case(v => v with { Organization = v.Organization with { Name = "" } }, "organization.name", Required);
        yield return Case(v => v with { Organization = v.Organization with { Name = new string('a', 161) } }, "organization.name", TooLong(160));
        yield return Case(v => v with { Organization = v.Organization with { LegalName = "" } }, "organization.legalName", Required);
        yield return Case(v => v with { Organization = v.Organization with { LegalName = new string('a', 201) } }, "organization.legalName", TooLong(200));
        yield return Case(v => v with { Organization = v.Organization with { TaxId = new string('a', 61) } }, "organization.taxId", TooLong(60));
        yield return Case(v => v with { Organization = v.Organization with { Email = "" } }, "organization.email", Required);
        yield return Case(v => v with { Organization = v.Organization with { Email = new string('a', 250) + "@a.co" } }, "organization.email", TooLong(254));
        yield return Case(v => v with { Organization = v.Organization with { Email = "not-an-email" } }, "organization.email", EmailInvalid);
        yield return Case(v => v with { Branch = v.Branch with { Email = "not-an-email" } }, "branch.email", EmailInvalid);
        yield return Case(v => v with { Owner = v.Owner with { Email = "" } }, "owner.email", Required);
        yield return Case(v => v with { Owner = v.Owner with { Email = "not-an-email" } }, "owner.email", EmailInvalid);
        yield return Case(v => v with { Organization = v.Organization with { Phone = "" } }, "organization.phone", Required);
        yield return Case(v => v with { Organization = v.Organization with { Phone = new string('1', 41) } }, "organization.phone", TooLong(40));
        yield return Case(v => v with { Organization = v.Organization with { Phone = "abc" } }, "organization.phone", PhoneInvalid);
        yield return Case(v => v with { Branch = v.Branch with { Phone = "abc" } }, "branch.phone", PhoneInvalid);
        yield return Case(v => v with { Owner = v.Owner with { Phone = "abc" } }, "owner.phone", PhoneInvalid);
        yield return Case(v => v with { Organization = v.Organization with { Timezone = "" } }, "organization.timezone", TimeZoneMessage);
        yield return Case(v => v with { Organization = v.Organization with { Timezone = "Not/AZone" } }, "organization.timezone", TimeZoneMessage);
        yield return Case(v => v with { Organization = v.Organization with { Timezone = "Eastern Standard Time" } }, "organization.timezone", TimeZoneMessage);
        yield return Case(v => v with { Branch = v.Branch with { Timezone = "" } }, "branch.timezone", TimeZoneMessage);
        yield return Case(v => v with { Organization = v.Organization with { Currency = "" } }, "organization.currency", CurrencyMessage);
        yield return Case(v => v with { Organization = v.Organization with { Currency = "XAU" } }, "organization.currency", CurrencyMessage);
        yield return Case(v => v with { Branch = v.Branch with { CountryCode = "" } }, "branch.countryCode", CountryMessage);
        yield return Case(v => v with { Branch = v.Branch with { CountryCode = "ZZ" } }, "branch.countryCode", CountryMessage);
        yield return Case(v => v with { Branch = v.Branch with { AddressLine1 = "" } }, "branch.addressLine1", Required);
        yield return Case(v => v with { Branch = v.Branch with { AddressLine1 = new string('a', 181) } }, "branch.addressLine1", TooLong(180));
        yield return Case(v => v with { Branch = v.Branch with { City = "" } }, "branch.city", Required);
        yield return Case(v => v with { Branch = v.Branch with { City = new string('a', 101) } }, "branch.city", TooLong(100));
        yield return Case(v => v with { Branch = v.Branch with { PostalCode = "" } }, "branch.postalCode", Required);
        yield return Case(v => v with { Branch = v.Branch with { PostalCode = new string('a', 31) } }, "branch.postalCode", TooLong(30));
        yield return Case(v => v with { Branch = v.Branch with { StateRegion = new string('a', 101) } }, "branch.stateRegion", TooLong(100));
        yield return Case(v => v with { Organization = v.Organization with { DefaultTaxRate = null } }, "organization.defaultTaxRate", Required);
        yield return Case(v => v with { Organization = v.Organization with { DefaultTaxRate = -1m } }, "organization.defaultTaxRate", TaxRateMessage);
        yield return Case(v => v with { Organization = v.Organization with { DefaultTaxRate = 100.01m } }, "organization.defaultTaxRate", TaxRateMessage);
        yield return Case(v => v with { Organization = v.Organization with { DefaultTaxRate = 1.23456m } }, "organization.defaultTaxRate", TaxRateMessage);
        yield return Case(v => v with { Organization = v.Organization with { QuotePrefix = "" } }, "organization.quotePrefix", Required);
        yield return Case(v => v with { Organization = v.Organization with { QuotePrefix = "Q#" } }, "organization.quotePrefix", PrefixMessage);
        yield return Case(v => v with { Organization = v.Organization with { QuotePrefix = new string('Q', 21) } }, "organization.quotePrefix", PrefixMessage);
        yield return Case(v => v with { Organization = v.Organization with { WorkOrderPrefix = "" } }, "organization.workOrderPrefix", Required);
        yield return Case(v => v with { Organization = v.Organization with { WorkOrderPrefix = "W#" } }, "organization.workOrderPrefix", PrefixMessage);
        yield return Case(v => v with { Organization = v.Organization with { InvoicePrefix = "" } }, "organization.invoicePrefix", Required);
        yield return Case(v => v with { Organization = v.Organization with { InvoicePrefix = "I#" } }, "organization.invoicePrefix", PrefixMessage);
        yield return Case(v => v with { Organization = v.Organization with { NextInvoiceNumber = null } }, "organization.nextInvoiceNumber", Required);
        yield return Case(v => v with { Organization = v.Organization with { NextInvoiceNumber = 0 } }, "organization.nextInvoiceNumber", NextInvoiceNumberMessage);
        yield return Case(v => v with { Organization = v.Organization with { NextInvoiceNumber = 1_000_000_000_000 } }, "organization.nextInvoiceNumber", NextInvoiceNumberMessage);
        yield return Case(v => v with { Branch = v.Branch with { Name = "" } }, "branch.name", Required);
        yield return Case(v => v with { Branch = v.Branch with { Name = new string('a', 141) } }, "branch.name", TooLong(140));
        yield return Case(v => v with { Branch = v.Branch with { Code = "" } }, "branch.code", Required);
        yield return Case(v => v with { Branch = v.Branch with { Code = "TOO#LONG" } }, "branch.code", BranchCodeMessage);
        yield return Case(v => v with { Branch = v.Branch with { Code = new string('A', 9) } }, "branch.code", BranchCodeMessage);
        yield return Case(v => v with { Owner = v.Owner with { FirstName = "" } }, "owner.firstName", Required);
        yield return Case(v => v with { Owner = v.Owner with { FirstName = new string('a', 101) } }, "owner.firstName", TooLong(100));
        yield return Case(v => v with { Owner = v.Owner with { LastName = "" } }, "owner.lastName", Required);
        yield return Case(v => v with { Owner = v.Owner with { LastName = new string('a', 101) } }, "owner.lastName", TooLong(100));
        yield return Case(v => v with { Owner = v.Owner with { Password = "" } }, "owner.password", Required);
        yield return Case(v => v with { Owner = v.Owner with { Password = new string('p', 11) } }, "owner.password", PasswordLengthMessage);
        yield return Case(v => v with { Owner = v.Owner with { Password = new string('p', 129) } }, "owner.password", PasswordLengthMessage);
        yield return Case(
            v => v with { Owner = v.Owner with { Email = "owner@acme.com", Password = "OWNER@ACME.COM" } },
            "owner.password",
            PasswordEqualsEmailMessage);

        yield return Case(
            v => v with { Branch = v.Branch with { BusinessHours = ParseBusinessHours("""{"funday":{"start":"08:00","end":"17:00"}}""") } },
            "branch.businessHours",
            StructuralInvalid);
        yield return Case(
            v => v with { Branch = v.Branch with { BusinessHours = ParseBusinessHours("""{"monday":{"start":"08:00","end":"17:00","extra":true}}""") } },
            "branch.businessHours",
            StructuralInvalid);
        yield return Case(
            v => v with { Branch = v.Branch with { BusinessHours = ParseBusinessHours("[]") } },
            "branch.businessHours",
            StructuralInvalid);
        yield return Case(
            v => v with { Branch = v.Branch with { BusinessHours = ParseBusinessHours("""{"monday":{"end":"17:00"}}""") } },
            "branch.businessHours.monday.start",
            Required);
        yield return Case(
            v => v with { Branch = v.Branch with { BusinessHours = ParseBusinessHours("""{"monday":{"start":"8:00","end":"17:00"}}""") } },
            "branch.businessHours.monday.start",
            StructuralInvalid);
        yield return Case(
            v => v with { Branch = v.Branch with { BusinessHours = ParseBusinessHours("""{"monday":{"start":"17:00","end":"08:00"}}""") } },
            "branch.businessHours.monday.end",
            EndNotAfterStart);
    }

    [Theory]
    [MemberData(nameof(InvalidCases))]
    public void Validate_InvalidField_ReturnsExactlyOneErrorWithBr25Message(
        Func<RegisterOrganizationCommand, RegisterOrganizationCommand> mutate,
        string key,
        string message)
    {
        var command = mutate(Valid());

        var result = _validator.Validate(command);

        var error = Assert.Single(result.Errors);
        Assert.Equal(key, error.PropertyName);
        Assert.Equal(message, error.ErrorMessage);
    }

    // AC-39: length fails before the equals-email rule, even when both would fail.
    [Fact]
    public void Validate_ShortPasswordEqualToEmail_ReturnsOnlyLengthMessage()
    {
        var command = Valid() with
        {
            Owner = Valid().Owner with { Email = "ab@cd.io", Password = "ab@cd.io" },
        };

        var error = Assert.Single(_validator.Validate(command).Errors);
        Assert.Equal("owner.password", error.PropertyName);
        Assert.Equal(PasswordLengthMessage, error.ErrorMessage);
    }

    private static object[] Case(
        Func<RegisterOrganizationCommand, RegisterOrganizationCommand> mutate,
        string key,
        string message) => [mutate, key, message];

    private static JsonElement ParseBusinessHours(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static RegisterOrganizationCommand Valid() => new(
        new RegisterOrganizationCommand.OrganizationInput(
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
            1),
        new RegisterOrganizationCommand.BranchInput(
            "Main Branch",
            "MAIN",
            "+1 555 987 6543",
            "branch@acme.com",
            "America/Chicago",
            "123 Main St",
            "Chicago",
            "IL",
            "60601",
            "US",
            ParseBusinessHours("""{"monday":{"start":"08:00","end":"17:00"}}""")),
        new RegisterOrganizationCommand.OwnerInput(
            "Ada",
            "Lovelace",
            "ada@acme.com",
            "correct horse battery",
            "+1 555 111 2222"),
        ClientIp: null);

    private const string Required = RegisterOrganizationCommandValidator.RequiredMessage;

    private const string EmailInvalid = RegisterOrganizationCommandValidator.EmailInvalidMessage;

    private const string PhoneInvalid = RegisterOrganizationCommandValidator.PhoneInvalidMessage;

    private const string TimeZoneMessage = RegisterOrganizationCommandValidator.TimeZoneMessage;

    private const string CurrencyMessage = RegisterOrganizationCommandValidator.CurrencyMessage;

    private const string CountryMessage = RegisterOrganizationCommandValidator.CountryMessage;

    private const string TaxRateMessage = RegisterOrganizationCommandValidator.TaxRateMessage;

    private const string PrefixMessage = RegisterOrganizationCommandValidator.PrefixMessage;

    private const string BranchCodeMessage = RegisterOrganizationCommandValidator.BranchCodeMessage;

    private const string NextInvoiceNumberMessage = RegisterOrganizationCommandValidator.NextInvoiceNumberMessage;

    private const string PasswordLengthMessage = RegisterOrganizationCommandValidator.PasswordLengthMessage;

    private const string PasswordEqualsEmailMessage = RegisterOrganizationCommandValidator.PasswordEqualsEmailMessage;

    private const string StructuralInvalid = BusinessHoursValidator.StructuralInvalidMessage;

    private const string EndNotAfterStart = BusinessHoursValidator.EndNotAfterStartMessage;

    private static string TooLong(int maxLength) => $"Use {maxLength} characters or fewer.";
}
