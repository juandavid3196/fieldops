using System.Net;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.Users;

namespace FieldOps.IntegrationTests.Customers;

/// <summary>Tenant isolation, branch scope and the role matrix of the customer detail endpoints (FR-16): AC-21, AC-22, AC-23.</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class CustomerDetailSecurityTests(CompanySettingsDatabaseFixture database)
{
    private static readonly HttpMethod Get = HttpMethod.Get;

    private static readonly HttpMethod Post = HttpMethod.Post;

    private static readonly HttpMethod Put = HttpMethod.Put;

    // AC-21: another organization's customer, another organization's or another customer's property, and malformed
    // ids are 404 on every endpoint of the spec and change nothing.
    [Fact]
    public async Task Endpoints_ForeignCustomerOrPropertyOrMalformedId_AreNotFoundAndChangeNothing()
    {
        var orgA = await database.SeedOrganizationAsync();
        var orgB = await database.SeedOrganizationAsync();
        var branchA = await database.SeedBranchAsync(orgA, isMain: true);
        var branchB = await database.SeedBranchAsync(orgB, isMain: true);
        var customerA1 = await database.SeedCustomerAsync(orgA, branchA.Id, "Own Customer");
        var customerA2 = await database.SeedCustomerAsync(orgA, branchA.Id, "Sibling Customer");
        var customerB = await database.SeedCustomerAsync(orgB, branchB.Id, "Other Org Customer");
        var propertyA1 = await database.ScalarAsync<Guid>("SELECT id FROM properties WHERE customer_id = @c", ("c", customerA1));
        var propertyA2 = await database.SeedPropertyAsync(orgA, customerA2, branchA.Id, "Sibling Site");
        var propertyB = await database.ScalarAsync<Guid>("SELECT id FROM properties WHERE customer_id = @c", ("c", customerB));
        var snapshotSql = "SELECT string_agg(id::text || name || is_active::text || is_primary::text || updated_at::text, ',' ORDER BY id) FROM properties WHERE organization_id IN (@a, @b)";
        var before = await database.ScalarAsync<string>(snapshotSql, ("a", orgA), ("b", orgB));
        var notes = await database.CountRowsAsync("customer_notes", orgA) + await database.CountRowsAsync("customer_notes", orgB);

        await using var host = CustomerHost.Create(database);
        var owner = await host.CookieAsync(database, orgA, CompanySettingsDatabaseFixture.OwnerRoleId);

        // Positive control: the caller's own customer and property resolve.
        foreach (var (method, path, body) in CustomerReads(customerA1.ToString()).Concat(PropertyReads(customerA1.ToString(), propertyA1.ToString())))
        {
            Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(method, path, owner, body)).StatusCode);
        }

        var calls = CustomerEndpoints(customerB.ToString(), branchA.Id)
            .Concat(PropertyEndpoints(customerB.ToString(), propertyB.ToString(), branchA.Id))
            .Concat(PropertyEndpoints(customerA1.ToString(), propertyB.ToString(), branchA.Id))
            .Concat(PropertyEndpoints(customerA1.ToString(), propertyA2.ToString(), branchA.Id))
            .Concat(CustomerEndpoints("not-a-guid", branchA.Id))
            .Concat(PropertyEndpoints(customerA1.ToString(), "not-a-guid", branchA.Id))
            .Concat(PropertyEndpoints("not-a-guid", propertyA1.ToString(), branchA.Id));

        foreach (var (method, path, body) in calls)
        {
            var response = await host.SendAsync(method, path, owner, body);
            Assert.True(response.StatusCode == HttpStatusCode.NotFound, $"{method} {path}: {response.StatusCode}");
        }

        Assert.Equal(before, await database.ScalarAsync<string>(snapshotSql, ("a", orgA), ("b", orgB)));
        Assert.Equal(notes, await database.CountRowsAsync("customer_notes", orgA) + await database.CountRowsAsync("customer_notes", orgB));
        Assert.Equal(0L, await database.CountPropertyAuditAsync(orgA) + await database.CountPropertyAuditAsync(orgB));
        Assert.Equal(0L, await database.ScalarAsync<long>("SELECT COUNT(*) FROM audit_logs WHERE action = 'customer_note.created' AND organization_id IN (@a, @b)", ("a", orgA), ("b", orgB)));
    }

    // AC-22: a Dispatcher and an Accounting user limited to branch X get 404 for a customer in branch Y on every
    // endpoint, and 200 for a customer in X whose derived data (work, invoices, property) lives in branch Y.
    [Fact]
    public async Task Endpoints_BranchScopedUsers_HideOtherBranchCustomersButSeeAllDerivedDataOfVisibleOnes()
    {
        var org = await database.SeedOrganizationAsync();
        var branchX = await database.SeedBranchAsync(org, "Xray Branch", isMain: true);
        var branchY = await database.SeedBranchAsync(org, "Yankee Branch");
        var owner = await database.SeedMemberAsync(org, CompanySettingsDatabaseFixture.OwnerRoleId, "Ola", "Owner");
        var customerX = await database.SeedCustomerAsync(org, branchX.Id, "Xavier In Scope");
        var customerY = await database.SeedCustomerAsync(org, branchY.Id, "Yara Out Of Scope");
        var propertyY = await database.ScalarAsync<Guid>("SELECT id FROM properties WHERE customer_id = @c", ("c", customerY));
        var yardSite = await database.SeedPropertyAsync(org, customerX, branchY.Id, "Yard Site");
        var now = DateTimeOffset.UtcNow;
        var job = await database.SeedJobAsync(org, owner.UserId, branchY.Id, customerX, yardSite, Guid.NewGuid(), 9, "completed", "Branch Y job", now.AddDays(-3));
        await database.SeedVisitAsync(org, job, 1, "completed", now.AddDays(-3), now.AddDays(-3).AddHours(1), now.AddDays(-3).AddMinutes(30));
        await database.SeedInvoiceAsync(org, owner.UserId, branchY.Id, customerX, 9, "sent", 250m, 0m, new DateOnly(2026, 5, 1), now.AddDays(-2));
        var yPropertySnapshot = await database.ScalarAsync<string>("SELECT name || is_active::text || is_primary::text FROM properties WHERE id = @p", ("p", propertyY));

        await using var host = CustomerHost.Create(database);
        var dispatcher = await host.CookieAsync(database, org, CompanySettingsDatabaseFixture.DispatcherRoleId, branchX.Id);
        var accounting = await host.CookieAsync(database, org, CompanySettingsDatabaseFixture.AccountingRoleId, branchX.Id);

        foreach (var cookie in new[] { dispatcher, accounting })
        {
            foreach (var (method, path, body) in CustomerEndpoints(customerY.ToString(), branchX.Id)
                .Concat(PropertyEndpoints(customerY.ToString(), propertyY.ToString(), branchX.Id)))
            {
                var response = await host.SendAsync(method, path, cookie, body);
                var denied = cookie == accounting && method != Get ? HttpStatusCode.Forbidden : HttpStatusCode.NotFound;
                Assert.True(response.StatusCode == denied, $"{method} {path}: {response.StatusCode}");
            }

            var detail = await CustomerHost.ReadAsync(await host.SendAsync(Get, $"/customers/{customerX}/detail", cookie));
            Assert.Equal((1, 250m), (detail["summary"]!["totalJobs"]!.GetValue<int>(), detail["outstandingBalance"]!.GetValue<decimal>()));
            var properties = await CustomerHost.ReadAsync(await host.SendAsync(Get, $"/customers/{customerX}/properties", cookie));
            var yard = properties["items"]!.AsArray().Single(item => item!["name"]!.GetValue<string>() == "Yard Site")!;
            Assert.Equal(("Yankee Branch", "Branch Y job"), (yard["branch"]!["name"]!.GetValue<string>(), yard["lastService"]!["summary"]!.GetValue<string>()));
            var recent = await CustomerHost.ReadAsync(await host.SendAsync(Get, $"/customers/{customerX}/recent-work", cookie));
            Assert.Equal(["WO-9"], recent["items"]!.AsArray().Select(item => item!["number"]!.GetValue<string>()).ToArray());

            foreach (var (method, path, body) in CustomerReads(customerX.ToString()))
            {
                Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(method, path, cookie, body)).StatusCode);
            }
        }

        Assert.Equal(yPropertySnapshot, await database.ScalarAsync<string>("SELECT name || is_active::text || is_primary::text FROM properties WHERE id = @p", ("p", propertyY)));
        Assert.Equal(0L, await database.CountRowsAsync("customer_notes", org));
        Assert.Equal(0L, await database.CountPropertyAuditAsync(org));
    }

    // AC-23: read roles read and cannot mutate (403, nothing changes), Owner and Dispatcher do both, Technician is 403
    // everywhere and an anonymous caller is 401.
    [Theory]
    [InlineData("owner", true, true)]
    [InlineData("dispatcher", true, true)]
    [InlineData("operations_manager", true, false)]
    [InlineData("accounting", true, false)]
    [InlineData("viewer", true, false)]
    [InlineData("technician", false, false)]
    [InlineData("none", false, false)]
    public async Task Endpoints_RoleMatrix_FollowReadAndMutatePolicies(string role, bool canRead, bool canMutate)
    {
        var org = await database.SeedOrganizationAsync();
        var branch = await database.SeedBranchAsync(org, isMain: true);
        var customer = await database.SeedCustomerAsync(org, branch.Id, "Matrix Customer");
        var property = await database.SeedPropertyAsync(org, customer, branch.Id, "Matrix Site");
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

        var denied = cookie is null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden;
        var properties = await database.CountRowsAsync("properties", org);
        var snapshot = await database.ScalarAsync<string>("SELECT string_agg(id::text || is_active::text || is_primary::text || name, ',' ORDER BY id) FROM properties WHERE organization_id = @o", ("o", org));

        foreach (var (method, path, body) in CustomerReads(customer.ToString()).Concat(PropertyReads(customer.ToString(), property.ToString())))
        {
            var response = await host.SendAsync(method, path, cookie, body);
            Assert.True(
                canRead ? response.StatusCode == HttpStatusCode.OK : response.StatusCode == denied,
                $"{role} {method} {path}: {response.StatusCode}");
        }

        foreach (var (method, path, body) in MutationEndpoints(customer.ToString(), property.ToString(), branch.Id))
        {
            var response = await host.SendAsync(method, path, cookie, body);
            var allowed = response.StatusCode != HttpStatusCode.Forbidden && response.StatusCode != HttpStatusCode.Unauthorized;
            Assert.True(canMutate ? allowed : response.StatusCode == denied, $"{role} {method} {path}: {response.StatusCode}");
        }

        if (!canMutate)
        {
            Assert.Equal(properties, await database.CountRowsAsync("properties", org));
            Assert.Equal(snapshot, await database.ScalarAsync<string>("SELECT string_agg(id::text || is_active::text || is_primary::text || name, ',' ORDER BY id) FROM properties WHERE organization_id = @o", ("o", org)));
            Assert.Equal(0L, await database.CountRowsAsync("customer_notes", org));
            Assert.Equal(0L, await database.CountPropertyAuditAsync(org));
        }
    }

    private static (HttpMethod Method, string Path, JsonObject? Body)[] CustomerReads(string customer) =>
    [
        (Get, $"/customers/{customer}/detail", null),
        (Get, $"/customers/{customer}/properties", null),
        (Get, $"/customers/{customer}/recent-work", null),
        (Get, $"/customers/{customer}/upcoming-appointments", null),
        (Get, $"/customers/{customer}/notes", null),
        (Get, $"/customers/{customer}/activity", null),
    ];

    private static (HttpMethod Method, string Path, JsonObject? Body)[] PropertyReads(string customer, string property) =>
        [(Get, $"/customers/{customer}/properties/{property}", null)];

    private static (HttpMethod Method, string Path, JsonObject? Body)[] CustomerEndpoints(string customer, Guid branch) =>
    [
        .. CustomerReads(customer),
        (Post, $"/customers/{customer}/properties", CustomerDetailSeed.PropertyBody(branch)),
        (Post, $"/customers/{customer}/notes", new JsonObject { ["note"] = "Cross tenant note" }),
    ];

    private static (HttpMethod Method, string Path, JsonObject? Body)[] PropertyEndpoints(string customer, string property, Guid branch) =>
    [
        .. PropertyReads(customer, property),
        (Put, $"/customers/{customer}/properties/{property}", CustomerDetailSeed.PropertyBody(branch, name: "Hijacked")),
        (Post, $"/customers/{customer}/properties/{property}/set-primary", null),
        (Post, $"/customers/{customer}/properties/{property}/archive", null),
        (Post, $"/customers/{customer}/properties/{property}/reactivate", null),
    ];

    private static (HttpMethod Method, string Path, JsonObject? Body)[] MutationEndpoints(string customer, string property, Guid branch) =>
    [
        (Post, $"/customers/{customer}/properties", CustomerDetailSeed.PropertyBody(branch)),
        (Post, $"/customers/{customer}/notes", new JsonObject { ["note"] = "Role matrix note" }),
        (Put, $"/customers/{customer}/properties/{property}", CustomerDetailSeed.PropertyBody(branch, name: "Role Matrix")),
        (Post, $"/customers/{customer}/properties/{property}/set-primary", null),
        (Post, $"/customers/{customer}/properties/{property}/archive", null),
        (Post, $"/customers/{customer}/properties/{property}/reactivate", null),
    ];
}
