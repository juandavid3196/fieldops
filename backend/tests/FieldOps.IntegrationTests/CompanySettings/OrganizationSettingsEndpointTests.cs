using System.Net;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.Sessions;

namespace FieldOps.IntegrationTests.CompanySettings;

/// <summary>
/// GET/PUT /organization-settings (FR-03, FR-04). Budget: ≤6 methods
/// (AC-01, AC-02, AC-06, AC-07, AC-08, AC-47, AC-48, AC-51).
/// </summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class OrganizationSettingsEndpointTests(CompanySettingsDatabaseFixture database)
{
    [Theory]
    [InlineData(CompanySettingsDatabaseFixture.OwnerRoleId, true)]
    [InlineData(CompanySettingsDatabaseFixture.ViewerRoleId, false)]
    public async Task GetOrganizationSettings_OwnerOrViewer_ReturnsSettingsWithExpectedCanManage(
        short roleId, bool expectedCanManage)
    {
        var account = await database.SeedAccountAsync(roleId);
        await using var host = SessionTestHost.Create(database.ConnectionString);
        var signIn = await SessionApi.SignInAsync(host.Client, account.Email, CompanySettingsDatabaseFixture.Password);
        var cookie = SessionApi.GetIssuedCookie(signIn);

        var response = await CompanySettingsApi.GetAsync(host.Client, "/organization-settings", cookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var body = await CompanySettingsApi.ReadAsAsync<OrganizationSettingsBody>(response);
        Assert.Equal(expectedCanManage, body.CanManage);
    }

    // AC-06.
    [Fact]
    public async Task PutOrganizationSettings_ValidBody_NormalizesFieldsAdvancesUpdatedAtAndWritesOneAuditRow()
    {
        var account = await database.SeedAccountAsync();
        await using var host = SessionTestHost.Create(database.ConnectionString);
        var signIn = await SessionApi.SignInAsync(host.Client, account.Email, CompanySettingsDatabaseFixture.Password);
        var cookie = SessionApi.GetIssuedCookie(signIn);

        var current = await CompanySettingsApi.ReadAsAsync<OrganizationSettingsBody>(
            await CompanySettingsApi.GetAsync(host.Client, "/organization-settings", cookie));

        var body = ValidBody(current.UpdatedAt.ToString("O"));
        body["email"] = " NEW-OPS@ACME.COM ";
        body["invoicePrefix"] = "inv2";

        var response = await CompanySettingsApi.PutAsync(host.Client, "/organization-settings", body, cookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await CompanySettingsApi.ReadAsAsync<OrganizationSettingsBody>(response);
        Assert.Equal("new-ops@acme.com", updated.Email);
        Assert.Equal("INV2", updated.InvoicePrefix);
        Assert.True(updated.UpdatedAt > current.UpdatedAt);

        Assert.Equal(1, await database.CountAuditLogsAsync(account.OrganizationId, "organization.settings_updated"));
    }

    // AC-08.
    [Fact]
    public async Task PutOrganizationSettings_StaleUpdatedAt_Returns409WithoutFieldErrorsOrChange()
    {
        var account = await database.SeedAccountAsync();
        await using var host = SessionTestHost.Create(database.ConnectionString);
        var signIn = await SessionApi.SignInAsync(host.Client, account.Email, CompanySettingsDatabaseFixture.Password);
        var cookie = SessionApi.GetIssuedCookie(signIn);

        var staleTimestamp = DateTimeOffset.UtcNow.AddDays(-1).ToString("O");
        var body = ValidBody(staleTimestamp);

        var response = await CompanySettingsApi.PutAsync(host.Client, "/organization-settings", body, cookie);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var problem = await CompanySettingsApi.ReadJsonAsync(response);
        Assert.False(problem.RootElement.TryGetProperty("errors", out _));
        Assert.Equal(0, await database.CountAuditLogsAsync(account.OrganizationId, "organization.settings_updated"));
    }

    // AC-07.
    [Theory]
    [InlineData("name", "")]
    [InlineData("email", "not-an-email")]
    [InlineData("updatedAt", "not-a-timestamp")]
    public async Task PutOrganizationSettings_InvalidField_Returns400WithFieldKeyAndNoChange(
        string field, string invalidValue)
    {
        var account = await database.SeedAccountAsync();
        await using var host = SessionTestHost.Create(database.ConnectionString);
        var signIn = await SessionApi.SignInAsync(host.Client, account.Email, CompanySettingsDatabaseFixture.Password);
        var cookie = SessionApi.GetIssuedCookie(signIn);

        var current = await CompanySettingsApi.ReadAsAsync<OrganizationSettingsBody>(
            await CompanySettingsApi.GetAsync(host.Client, "/organization-settings", cookie));

        var body = ValidBody(current.UpdatedAt.ToString("O"));
        body[field] = invalidValue;

        var response = await CompanySettingsApi.PutAsync(host.Client, "/organization-settings", body, cookie);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = await CompanySettingsApi.ReadJsonAsync(response);
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty(field, out _));
        Assert.Equal(current.UpdatedAt, await database.GetOrganizationUpdatedAtAsync(account.OrganizationId));
    }

    // AC-47 (non-JSON Content-Type -> 415; malformed JSON -> 400 without field
    // keys). The 413 body-size case needs a real Kestrel server per
    // docs/backend/api-configuration.md and is out of this budget; BR-09's
    // size limit is exercised by the sibling registration endpoint's own
    // Kestrel-backed test, which proves the shared [RequestSizeLimit]/
    // BadHttpRequestExceptionHandler wiring this endpoint reuses unchanged.
    [Theory]
    [InlineData("text/plain", "{}", HttpStatusCode.UnsupportedMediaType)]
    [InlineData("application/json", "not json", HttpStatusCode.BadRequest)]
    public async Task PutOrganizationSettings_Br09Violation_ReturnsExpectedStatusAndNoChange(
        string mediaType, string body, HttpStatusCode expectedStatus)
    {
        var account = await database.SeedAccountAsync();
        await using var host = SessionTestHost.Create(database.ConnectionString);
        var signIn = await SessionApi.SignInAsync(host.Client, account.Email, CompanySettingsDatabaseFixture.Password);
        var cookie = SessionApi.GetIssuedCookie(signIn);
        var before = await database.GetOrganizationUpdatedAtAsync(account.OrganizationId);

        var response = await CompanySettingsApi.SendRawAsync(
            host.Client, HttpMethod.Put, "/organization-settings", body, cookie, mediaType);

        Assert.Equal(expectedStatus, response.StatusCode);

        if (expectedStatus == HttpStatusCode.BadRequest)
        {
            using var problem = await CompanySettingsApi.ReadJsonAsync(response);
            Assert.False(problem.RootElement.TryGetProperty("errors", out _));
        }

        Assert.Equal(before, await database.GetOrganizationUpdatedAtAsync(account.OrganizationId));
    }

    // AC-51 (mandatory: no log leakage of sensitive request data).
    [Fact]
    public async Task PutOrganizationSettings_ValidAndInvalidRequests_NeverLogSensitiveValues()
    {
        var account = await database.SeedAccountAsync();
        await using var host = SessionTestHost.Create(database.ConnectionString, captureLogs: true);
        var signIn = await SessionApi.SignInAsync(host.Client, account.Email, CompanySettingsDatabaseFixture.Password);
        var cookie = SessionApi.GetIssuedCookie(signIn);

        var current = await CompanySettingsApi.ReadAsAsync<OrganizationSettingsBody>(
            await CompanySettingsApi.GetAsync(host.Client, "/organization-settings", cookie));

        const string taxId = "98-7654321-secret";
        const string email = "sensitive-owner@acme.com";
        const string phone = "+1 555 444 3333";

        var validBody = ValidBody(current.UpdatedAt.ToString("O"));
        validBody["taxId"] = taxId;
        validBody["email"] = email;
        validBody["phone"] = phone;

        var success = await CompanySettingsApi.PutAsync(host.Client, "/organization-settings", validBody, cookie);
        Assert.Equal(HttpStatusCode.OK, success.StatusCode);

        var invalid = await CompanySettingsApi.SendRawAsync(
            host.Client, HttpMethod.Put, "/organization-settings", "not json", cookie);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var forbidden = new[] { taxId, email, phone };
        var entries = host.Logs!.Entries;
        Assert.NotEmpty(entries);

        foreach (var entry in entries)
        {
            var text = string.Join(
                "\n",
                new[] { entry.Message, entry.Exception?.ToString() }
                    .Concat(entry.Properties.Values.Select(value => value?.ToString())));

            foreach (var value in forbidden)
            {
                Assert.DoesNotContain(value, text, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    internal static JsonObject ValidBody(string updatedAt) => new()
    {
        ["name"] = "Acme Field Services",
        ["legalName"] = "Acme Field Services LLC",
        ["taxId"] = "12-3456789",
        ["email"] = "ops@acme.com",
        ["phone"] = "+1 555 123 4567",
        ["timezone"] = "America/Chicago",
        ["currency"] = "USD",
        ["defaultTaxRate"] = 7.25m,
        ["quotePrefix"] = "Q",
        ["workOrderPrefix"] = "WO",
        ["invoicePrefix"] = "INV",
        ["nextInvoiceNumber"] = 1,
        ["nextQuoteNumber"] = 1,
        ["nextWorkOrderNumber"] = 1,
        ["website"] = "https://acme.com",
        ["addressLine1"] = "1 Main St",
        ["city"] = "Austin",
        ["stateRegion"] = "TX",
        ["postalCode"] = "78701",
        ["countryCode"] = "US",
        ["pricesIncludeTax"] = false,
        ["updatedAt"] = updatedAt,
    };
}

public sealed record OrganizationSettingsBody(
    string Name,
    string? LegalName,
    string? TaxId,
    string? Email,
    string? Phone,
    string Timezone,
    string Currency,
    decimal DefaultTaxRate,
    string QuotePrefix,
    string WorkOrderPrefix,
    string InvoicePrefix,
    long NextInvoiceNumber,
    long NextQuoteNumber,
    long NextWorkOrderNumber,
    string? Website,
    string? AddressLine1,
    string? City,
    string? StateRegion,
    string? PostalCode,
    string? CountryCode,
    bool PricesIncludeTax,
    bool HasInvoices,
    OrganizationLogoBody? Logo,
    DateTimeOffset UpdatedAt,
    bool CanManage);

public sealed record OrganizationLogoBody(string ContentType, int SizeBytes, DateTimeOffset UpdatedAt);
