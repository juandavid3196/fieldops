using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.Sessions;

namespace FieldOps.IntegrationTests.CompanySettings;

/// <summary>
/// Company setup completion: new organization fields, sequence floors and
/// currency confirmation (FR-07, FR-08, FR-19; AC-07 to AC-09).
/// </summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class OrganizationSettingsCompletionTests(CompanySettingsDatabaseFixture database)
{
    // AC-07.
    [Fact]
    public async Task PutOrganizationSettings_NewFields_NormalizesPersistsAndAuditsOnlyChangedKeys()
    {
        var account = await database.SeedAccountAsync();
        await using var host = SessionTestHost.Create(database.ConnectionString);
        var cookie = await CompanySettingsApi.SignInCookieAsync(host.Client, account.Email);

        var initial = await CompanySettingsApi.ReadAsAsync<OrganizationSettingsBody>(
            await CompanySettingsApi.GetAsync(host.Client, "/organization-settings", cookie));
        Assert.Null(initial.Website);
        Assert.Null(initial.CountryCode);
        Assert.False(initial.PricesIncludeTax);
        Assert.False(initial.HasInvoices);
        Assert.Null(initial.Logo);

        var body = OrganizationSettingsEndpointTests.ValidBody(initial.UpdatedAt.ToString("O"));
        body["website"] = " https://Acme.com ";
        body["countryCode"] = "us";
        body["stateRegion"] = "tx";
        body["pricesIncludeTax"] = true;
        body["nextQuoteNumber"] = 5;
        body["nextWorkOrderNumber"] = 7;
        body["confirmCurrencyChange"] = true;

        var response = await CompanySettingsApi.PutAsync(host.Client, "/organization-settings", body, cookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await CompanySettingsApi.ReadAsAsync<OrganizationSettingsBody>(response);
        Assert.Equal("https://Acme.com", updated.Website);
        Assert.Equal("US", updated.CountryCode);
        Assert.Equal("TX", updated.StateRegion);
        Assert.True(updated.PricesIncludeTax);
        Assert.Equal(5, updated.NextQuoteNumber);
        Assert.Equal(7, updated.NextWorkOrderNumber);

        var reloaded = await CompanySettingsApi.ReadAsAsync<OrganizationSettingsBody>(
            await CompanySettingsApi.GetAsync(host.Client, "/organization-settings", cookie));
        Assert.Equal("US", reloaded.CountryCode);
        Assert.True(reloaded.PricesIncludeTax);

        Assert.Equal(1, await database.CountAuditLogsAsync(account.OrganizationId, "organization.settings_updated"));
        var (_, after, _) = await database.GetLatestAuditAsync(account.OrganizationId, "organization.settings_updated");
        using var afterJson = JsonDocument.Parse(after!);
        Assert.True(afterJson.RootElement.GetProperty("pricesIncludeTax").GetBoolean());
        Assert.Equal("US", afterJson.RootElement.GetProperty("countryCode").GetString());
        Assert.False(afterJson.RootElement.TryGetProperty("confirmCurrencyChange", out _));
        Assert.False(afterJson.RootElement.TryGetProperty("nextInvoiceNumber", out _));
    }

    // AC-08.
    [Theory]
    [InlineData("stateRegion", "", "Select a state.")]
    [InlineData("stateRegion", "ZZ", "Select a state.")]
    [InlineData("website", "not a site", "Enter a valid website.")]
    [InlineData("addressLine1", "", "This field is required.")]
    [InlineData("pricesIncludeTax", "yes", "Enter a valid value.")]
    [InlineData("nextQuoteNumber", 10, "Enter a number greater than the last quote number.")]
    [InlineData("nextWorkOrderNumber", 20, "Enter a number greater than the last work order number.")]
    public async Task PutOrganizationSettings_RuleViolation_Returns400WithKeyAndNoChange(
        string field, object value, string message)
    {
        var account = await database.SeedAccountAsync();
        var branch = await database.SeedBranchAsync(account.OrganizationId);
        await database.SeedDocumentNumbersAsync(
            account.OrganizationId, account.UserId, branch.Id, quoteNumber: 10, workOrderNumber: 20);
        await using var host = SessionTestHost.Create(database.ConnectionString);
        var cookie = await CompanySettingsApi.SignInCookieAsync(host.Client, account.Email);
        var before = await database.GetOrganizationUpdatedAtAsync(account.OrganizationId);

        var body = OrganizationSettingsEndpointTests.ValidBody(before.ToString("O"));
        body[field] = JsonSerializer.SerializeToNode(value);

        var response = await CompanySettingsApi.PutAsync(host.Client, "/organization-settings", body, cookie);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = await CompanySettingsApi.ReadJsonAsync(response);
        var errors = problem.RootElement.GetProperty("errors").GetProperty(field);
        Assert.Contains(message, errors.EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(before, await database.GetOrganizationUpdatedAtAsync(account.OrganizationId));
        Assert.Equal(0, await database.CountAuditLogsAsync(account.OrganizationId, "organization.settings_updated"));
    }

    // AC-09.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PutOrganizationSettings_CurrencyChange_RequiresConfirmationOnlyWhenInvoicesExist(bool hasInvoices)
    {
        var account = await database.SeedAccountAsync();
        var branch = await database.SeedBranchAsync(account.OrganizationId);

        if (hasInvoices)
        {
            await database.SeedDocumentNumbersAsync(
                account.OrganizationId, account.UserId, branch.Id, invoiceNumber: 1);
        }

        await using var host = SessionTestHost.Create(database.ConnectionString);
        var cookie = await CompanySettingsApi.SignInCookieAsync(host.Client, account.Email);

        var current = await CompanySettingsApi.ReadAsAsync<OrganizationSettingsBody>(
            await CompanySettingsApi.GetAsync(host.Client, "/organization-settings", cookie));
        Assert.Equal(hasInvoices, current.HasInvoices);

        var body = OrganizationSettingsEndpointTests.ValidBody(current.UpdatedAt.ToString("O"));
        body["currency"] = "EUR";
        body["nextInvoiceNumber"] = 2;

        var first = await CompanySettingsApi.PutAsync(host.Client, "/organization-settings", body, cookie);

        if (hasInvoices)
        {
            Assert.Equal(HttpStatusCode.Conflict, first.StatusCode);
            using var problem = await CompanySettingsApi.ReadJsonAsync(first);
            Assert.Equal(
                "Confirm the currency change.",
                problem.RootElement.GetProperty("errors").GetProperty("currency").EnumerateArray().Single().GetString());
            Assert.Equal(current.UpdatedAt, await database.GetOrganizationUpdatedAtAsync(account.OrganizationId));
            Assert.Equal(0, await database.CountAuditLogsAsync(account.OrganizationId, "organization.settings_updated"));

            body["confirmCurrencyChange"] = true;
            var second = await CompanySettingsApi.PutAsync(host.Client, "/organization-settings", body, cookie);
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        }
        else
        {
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        }

        var saved = await CompanySettingsApi.ReadAsAsync<OrganizationSettingsBody>(
            await CompanySettingsApi.GetAsync(host.Client, "/organization-settings", cookie));
        Assert.Equal("EUR", saved.Currency);
    }
}
