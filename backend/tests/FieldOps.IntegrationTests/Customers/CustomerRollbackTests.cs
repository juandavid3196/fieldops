using System.Net;
using FieldOps.IntegrationTests.CompanySettings;

namespace FieldOps.IntegrationTests.Customers;

/// <summary>
/// Multi-record rollback of create and import (AC-11). A temporary CHECK on the throwaway test container makes
/// the property insert fail after the customer row is written; the constraint is always dropped afterwards.
/// </summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class CustomerRollbackTests(CompanySettingsDatabaseFixture database)
{
    private const string FailingConstraint = "ck_test_force_property_failure";

    [Fact]
    public async Task CreateAndImport_DatabaseFailureMidTransaction_LeavesNoRowsAndNoNewTags()
    {
        var org = await database.SeedOrganizationAsync();
        var branch = await database.SeedBranchAsync(org, isMain: true);
        var existingTag = await database.SeedTagAsync(org, "Existing");

        await using var host = CustomerHost.Create(database);
        var cookie = await host.CookieAsync(database, org, CompanySettingsDatabaseFixture.OwnerRoleId);

        var csv = CustomerSeed.Utf8(CustomerSeed.CsvOf(
            CustomerSeed.CsvHeader,
            $"residential,,Ann,One,,ann@example.com,,,,1 Main St,Austin,TX,78701,{branch.Code},Brand New,,"));

        // The check only guards this organization's rows, so tests of other organizations are unaffected.
        await database.ExecuteAsync(
            $"ALTER TABLE properties ADD CONSTRAINT {FailingConstraint} CHECK (organization_id <> '{org}' OR name <> 'Primary property') NOT VALID");

        HttpResponseMessage create;
        HttpResponseMessage import;

        try
        {
            create = await host.SendAsync(
                HttpMethod.Post, "/customers", cookie,
                CustomerSeed.Body(branch.Id, email: "rollback@example.com", tagIds: [existingTag.ToString()]));
            import = await host.SendCsvAsync("/customers/import", cookie, csv);
        }
        finally
        {
            await database.ExecuteAsync($"ALTER TABLE properties DROP CONSTRAINT {FailingConstraint}");
        }

        Assert.Equal(HttpStatusCode.InternalServerError, create.StatusCode);
        Assert.Equal(HttpStatusCode.InternalServerError, import.StatusCode);

        foreach (var table in new[] { "customers", "customer_contacts", "properties", "customer_tag_assignments" })
        {
            Assert.True(await database.CountRowsAsync(table, org) == 0, $"{table} must be empty");
        }

        Assert.Equal(1L, await database.CountRowsAsync("customer_tags", org));
        Assert.Equal(0L, await database.CountCustomerAuditAsync(org, "customer.created"));
        Assert.Equal(0L, await database.CountCustomerAuditAsync(org, "customer.imported"));
    }
}
