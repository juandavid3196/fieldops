using System.Net;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.Users;

namespace FieldOps.IntegrationTests.Customers;

/// <summary>Branch scope, tenant isolation and the role matrix (FR-11): AC-18, AC-19, AC-20.</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class CustomerSecurityTests(CompanySettingsDatabaseFixture database)
{
    private const string NoBranchAccess = "Choose a branch you have access to.";

    // AC-18, AC-19: a branch-limited dispatcher and accounting user see and change only their branch; foreign and
    // out-of-scope ids are 404 for customers and 400 for branches and tags; owner and viewer see everything.
    [Fact]
    public async Task BranchScopeAndTenantIsolation_HideOutOfScopeAndForeignData()
    {
        var org = await database.SeedOrganizationAsync();
        var other = await database.SeedOrganizationAsync();
        var branchX = await database.SeedBranchAsync(org, "Xray Branch", isMain: true);
        var branchY = await database.SeedBranchAsync(org, "Yankee Branch");
        var inactiveZ = await database.SeedBranchAsync(org, "Aardvark Inactive", isActive: false);
        var foreignBranch = await database.SeedBranchAsync(other, "Foreign Branch", isMain: true);

        var inScope = await database.SeedCustomerAsync(org, branchX.Id, "Xavier In Scope");
        var outOfScope = await database.SeedCustomerAsync(org, branchY.Id, "Yara Out Of Scope");
        var foreign = await database.SeedCustomerAsync(other, foreignBranch.Id, "Foreign Customer");
        var foreignTag = await database.SeedTagAsync(other, "Foreign tag");
        var owner = await database.SeedMemberAsync(org, CompanySettingsDatabaseFixture.OwnerRoleId, "Ola", "Owner");
        await database.SeedInvoiceAsync(other, owner.UserId, foreignBranch.Id, foreign, "overdue", 700m);

        await using var host = CustomerHost.Create(database);
        var dispatcher = await host.CookieAsync(database, org, CompanySettingsDatabaseFixture.DispatcherRoleId, branchX.Id, inactiveZ.Id);
        var accounting = await host.CookieAsync(database, org, CompanySettingsDatabaseFixture.AccountingRoleId, branchX.Id, inactiveZ.Id);
        var ownerCookie = await host.CookieAsync(database, org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var viewer = await host.CookieAsync(database, org, CompanySettingsDatabaseFixture.ViewerRoleId);

        foreach (var cookie in new[] { dispatcher, accounting })
        {
            var list = await CustomerHost.ReadAsync(await host.SendAsync(HttpMethod.Get, "/customers", cookie));
            Assert.Equal(["Xavier In Scope"], CustomerSeed.Names(list));
            Assert.Equal(1, list["tabCounts"]!["all"]!.GetValue<int>());
            Assert.Equal(1, (await CustomerHost.ReadAsync(await host.SendAsync(HttpMethod.Get, "/customers/metrics", cookie)))["totalCustomers"]!.GetValue<int>());
            Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(HttpMethod.Get, $"/customers/{inScope}", cookie)).StatusCode);
            var options = await host.SendAsync(HttpMethod.Get, "/customers/branch-options", cookie);
            Assert.Equal("no-store", options.Headers.CacheControl?.ToString());
            var optionsBody = await CustomerHost.ReadAsync(options);
            Assert.Equal("US", optionsBody["countryCode"]!.GetValue<string>());
            Assert.Equal([branchX.Id.ToString()], optionsBody["branches"]!.AsArray().Select(b => b!["id"]!.GetValue<string>()).ToArray());
            Assert.Equal(["Xray Branch"], optionsBody["branches"]!.AsArray().Select(b => b!["name"]!.GetValue<string>()).ToArray());

            foreach (var hiddenId in new[] { outOfScope, foreign })
            {
                Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Get, $"/customers/{hiddenId}", cookie)).StatusCode);
            }

            // A branch outside the scope, of another organization or unknown is the same 400 everywhere.
            foreach (var branchId in new[] { branchY.Id, foreignBranch.Id, Guid.NewGuid() })
            {
                var listResponse = await host.SendAsync(HttpMethod.Get, $"/customers?branchId={branchId}", cookie);
                Assert.Equal(HttpStatusCode.BadRequest, listResponse.StatusCode);
                Assert.Equal(NoBranchAccess, CustomerSeed.Error(await CustomerHost.ReadAsync(listResponse), "branchId"));
                Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(HttpMethod.Get, $"/customers/metrics?branchId={branchId}", cookie)).StatusCode);
            }
        }

        var before = await database.CountCustomersAsync(org);

        foreach (var branchId in new[] { branchY.Id, foreignBranch.Id, Guid.NewGuid() })
        {
            var response = await host.SendAsync(HttpMethod.Post, "/customers", dispatcher, CustomerSeed.Body(branchId));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(NoBranchAccess, CustomerSeed.Error(await CustomerHost.ReadAsync(response), "branchId"));
        }

        var foreignTagBody = CustomerSeed.Body(branchX.Id, tagIds: [foreignTag.ToString()]);
        var foreignTagResponse = await host.SendAsync(HttpMethod.Post, "/customers", dispatcher, foreignTagBody);
        Assert.Equal(HttpStatusCode.BadRequest, foreignTagResponse.StatusCode);
        Assert.Equal("Choose a valid tag.", CustomerSeed.Error(await CustomerHost.ReadAsync(foreignTagResponse), "tagIds"));
        Assert.Equal(before, await database.CountCustomersAsync(org));

        // Out-of-scope and foreign customers are 404 on every mutation and nothing changes.
        foreach (var hiddenId in new[] { outOfScope, foreign })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Put, $"/customers/{hiddenId}", dispatcher, CustomerSeed.Body(branchX.Id))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Post, $"/customers/{hiddenId}/archive", dispatcher)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Post, $"/customers/{hiddenId}/reactivate", dispatcher)).StatusCode);
        }

        Assert.True(await database.ScalarAsync<bool>("SELECT is_active FROM customers WHERE id = @id", ("id", outOfScope)));
        Assert.Equal("Yara Out Of Scope", await database.ScalarAsync<string>("SELECT display_name FROM customers WHERE id = @id", ("id", outOfScope)));
        Assert.Equal(0L, await database.CountCustomerAuditAsync(org));

        // Owner and viewer see both branches and never the other organization's customer, balance or tags.
        foreach (var cookie in new[] { ownerCookie, viewer })
        {
            var list = await CustomerHost.ReadAsync(await host.SendAsync(HttpMethod.Get, "/customers", cookie));
            Assert.Equal(["Xavier In Scope", "Yara Out Of Scope"], CustomerSeed.Names(list).Order().ToArray());
            Assert.Equal(0m, (await CustomerHost.ReadAsync(await host.SendAsync(HttpMethod.Get, "/customers/metrics", cookie)))["outstandingBalance"]!.GetValue<decimal>());
            Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Get, $"/customers/{foreign}", cookie)).StatusCode);
            Assert.Empty((await CustomerHost.ReadAsync(await host.SendAsync(HttpMethod.Get, "/customer-tags", cookie))).AsArray());
            var options = await CustomerHost.ReadAsync(await host.SendAsync(HttpMethod.Get, "/customers/branch-options", cookie));
            Assert.Equal(
                ["Xray Branch", "Yankee Branch"],
                options["branches"]!.AsArray().Select(b => b!["name"]!.GetValue<string>()).ToArray());
        }

        await database.ExecuteAsync("UPDATE organizations SET country_code = 'CA' WHERE id = @id", ("id", org));
        Assert.Equal("CA", (await CustomerHost.ReadAsync(await host.SendAsync(HttpMethod.Get, "/customers/branch-options", dispatcher)))["countryCode"]!.GetValue<string>());

        // A dispatcher with all branches creates in any branch of the organization.
        var allBranches = await host.CookieAsync(database, org, CompanySettingsDatabaseFixture.DispatcherRoleId);
        Assert.Equal(HttpStatusCode.Created, (await host.SendAsync(HttpMethod.Post, "/customers", allBranches, CustomerSeed.Body(branchY.Id))).StatusCode);
    }

    // AC-20: the role matrix for read, mutate and import endpoints, with technician and anonymous denied everywhere
    // and no data change when denied.
    [Theory]
    [InlineData("owner", true, true, true)]
    [InlineData("dispatcher", true, true, false)]
    [InlineData("operations_manager", true, false, false)]
    [InlineData("accounting", true, false, false)]
    [InlineData("viewer", true, false, false)]
    [InlineData("technician", false, false, false)]
    [InlineData("none", false, false, false)]
    public async Task Endpoints_RoleMatrix_FollowCustomerPoliciesAndChangeNothingWhenDenied(
        string role, bool canRead, bool canMutate, bool canImport)
    {
        var org = await database.SeedOrganizationAsync();
        var branch = await database.SeedBranchAsync(org, isMain: true);
        var customer = await database.SeedCustomerAsync(org, branch.Id, "Matrix Customer", email: "matrix@example.com");
        await using var host = CustomerHost.Create(database);
        string? cookie = null;

        if (role != "none")
        {
            var roleId = role switch
            {
                "owner" => CompanySettingsDatabaseFixture.OwnerRoleId,
                "operations_manager" => CompanySettingsDatabaseFixture.OperationsManagerRoleId,
                "dispatcher" => CompanySettingsDatabaseFixture.DispatcherRoleId,
                "accounting" => CompanySettingsDatabaseFixture.AccountingRoleId,
                "viewer" => CompanySettingsDatabaseFixture.ViewerRoleId,
                _ => CompanySettingsDatabaseFixture.TechnicianRoleId,
            };
            cookie = await host.CookieAsync(database, org, roleId);
        }

        var customers = await database.CountCustomersAsync(org);
        var csv = CustomerSeed.Utf8(CustomerSeed.CsvOf(
            CustomerSeed.CsvHeader,
            $"residential,,Imp,Orted,,imported@example.com,,,,1 Main St,Austin,TX,78701,{branch.Code},,,"));

        async Task AssertAsync(HttpResponseMessage response, bool allowed, string name)
        {
            var expectedDenied = cookie is null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden;

            if (allowed)
            {
                Assert.True(
                    response.StatusCode != HttpStatusCode.Forbidden && response.StatusCode != HttpStatusCode.Unauthorized,
                    $"{role} {name}: {response.StatusCode}");
            }
            else
            {
                Assert.True(response.StatusCode == expectedDenied, $"{role} {name}: {response.StatusCode}");
            }
        }

        await AssertAsync(await host.SendAsync(HttpMethod.Get, "/customers", cookie), canRead, "list");
        await AssertAsync(await host.SendAsync(HttpMethod.Get, "/customers/metrics", cookie), canRead, "metrics");
        await AssertAsync(await host.SendAsync(HttpMethod.Get, "/customers/branch-options", cookie), canRead, "branch-options");
        await AssertAsync(await host.SendAsync(HttpMethod.Get, $"/customers/{customer}", cookie), canRead, "detail");
        await AssertAsync(await host.SendAsync(HttpMethod.Get, "/customer-tags", cookie), canRead, "tags");
        await AssertAsync(await host.SendAsync(HttpMethod.Post, "/customers", cookie, CustomerSeed.Body(branch.Id)), canMutate, "create");
        await AssertAsync(await host.SendAsync(HttpMethod.Put, $"/customers/{customer}", cookie, CustomerSeed.Body(branch.Id)), canMutate, "update");
        await AssertAsync(await host.SendAsync(HttpMethod.Post, $"/customers/{customer}/archive", cookie), canMutate, "archive");
        await AssertAsync(await host.SendAsync(HttpMethod.Post, $"/customers/{customer}/reactivate", cookie), canMutate, "reactivate");
        await AssertAsync(await host.SendAsync(HttpMethod.Post, "/customers/duplicate-check", cookie, new JsonObject { ["email"] = "a@b.co" }), canMutate, "duplicate-check");
        await AssertAsync(await host.SendAsync(HttpMethod.Post, "/customer-tags", cookie, new JsonObject { ["name"] = "Matrix" }), canMutate, "tag");
        await AssertAsync(await host.SendAsync(HttpMethod.Get, "/customers/import/template", cookie), canImport, "template");
        await AssertAsync(await host.SendCsvAsync("/customers/import/preview", cookie, csv), canImport, "preview");
        await AssertAsync(await host.SendCsvAsync("/customers/import", cookie, csv), canImport, "import");

        if (!canMutate)
        {
            Assert.Equal(customers, await database.CountCustomersAsync(org));
            Assert.True(await database.ScalarAsync<bool>("SELECT is_active FROM customers WHERE id = @id", ("id", customer)));
            Assert.Equal(0L, await database.CountCustomerAuditAsync(org));
        }
    }
}
