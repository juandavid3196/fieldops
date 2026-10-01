using System.Net;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.CompanySettings;

namespace FieldOps.IntegrationTests.Customers;

/// <summary>Property create, edit, set-primary, archive and reactivate (FR-05 to FR-07, FR-17): AC-05 to AC-10, AC-12.</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class CustomerPropertyEndpointsTests(CompanySettingsDatabaseFixture database)
{
    private const string NoBranchAccess = "Choose a branch you have access to.";

    // AC-05 to AC-10, AC-06, AC-07: create (primary only when none exists), field validation, the branch rule on
    // create/change, edit with names-only audit, set-primary, archive/reactivate and every 409, with audit rows free
    // of addresses, instructions and names.
    [Fact]
    public async Task PropertyLifecycle_FollowsStateAndBranchRulesAndAuditsWithoutSensitiveValues()
    {
        var org = await database.SeedOrganizationAsync();
        var other = await database.SeedOrganizationAsync();
        var branchX = await database.SeedBranchAsync(org, "Xray Branch", isMain: true);
        var branchY = await database.SeedBranchAsync(org, "Yankee Branch");
        var inactiveZ = await database.SeedBranchAsync(org, "Zulu Inactive", isActive: false);
        var foreignBranch = await database.SeedBranchAsync(other, "Foreign Branch", isMain: true);
        var customer = await database.SeedCustomerAsync(org, branchX.Id, "Pat Customer");
        var primaryA = await database.ScalarAsync<Guid>("SELECT id FROM properties WHERE customer_id = @c", ("c", customer));
        var path = $"/customers/{customer}/properties";

        await using var host = CustomerHost.Create(database);
        var owner = await host.CookieAsync(database, org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var dispatcher = await host.CookieAsync(database, org, CompanySettingsDatabaseFixture.DispatcherRoleId, branchX.Id, inactiveZ.Id);

        // AC-05: a new property is active, not primary (an active primary exists), with the org country and chosen branch.
        var created = await host.SendAsync(HttpMethod.Post, path, owner, CustomerDetailSeed.PropertyBody(branchY.Id));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var view = await CustomerHost.ReadAsync(created);
        var propertyP = view["id"]!.GetValue<Guid>();
        Assert.Equal($"{path}/{propertyP}", created.Headers.Location?.ToString());
        Assert.Equal(("Dock Warehouse", false, true, "Yankee Branch", "TX", "Bay 4"), (
            view["name"]!.GetValue<string>(),
            view["isPrimary"]!.GetValue<bool>(),
            view["isActive"]!.GetValue<bool>(),
            view["branch"]!["name"]!.GetValue<string>(),
            view["stateRegion"]!.GetValue<string>(),
            view["addressLine2"]!.GetValue<string>()));
        Assert.Equal("US", await database.ScalarAsync<string>("SELECT country_code FROM properties WHERE id = @p", ("p", propertyP)));
        var (_, createdAfter, _) = await database.GetLatestAuditAsync(org, "property.created");
        Assert.Equal((false, branchY.Id), (JsonNode.Parse(createdAfter!)!["isPrimary"]!.GetValue<bool>(), JsonNode.Parse(createdAfter!)!["branchId"]!.GetValue<Guid>()));
        Assert.Equal("Dock Warehouse", (await CustomerHost.ReadAsync(await host.SendAsync(HttpMethod.Get, $"{path}/{propertyP}", owner)))["name"]!.GetValue<string>());

        // AC-06: invalid fields are 400 with the field message and persist nothing.
        var properties = await database.CountRowsAsync("properties", org);
        var audits = await database.CountPropertyAuditAsync(org);
        var invalid = new (string Key, string Message, Action<JsonObject> Mutate)[]
        {
            ("name", "Enter a property name.", body => body["name"] = "  "),
            ("name", "Use 140 characters or fewer.", body => body["name"] = new string('n', 141)),
            ("addressLine1", "Enter an address.", body => body["addressLine1"] = ""),
            ("addressLine1", "Use 180 characters or fewer.", body => body["addressLine1"] = new string('a', 181)),
            ("addressLine2", "Use 180 characters or fewer.", body => body["addressLine2"] = new string('a', 181)),
            ("city", "Enter a city.", body => body["city"] = ""),
            ("city", "Use 100 characters or fewer.", body => body["city"] = new string('c', 101)),
            ("stateRegion", "Choose a valid state.", body => body["stateRegion"] = "ZZ"),
            ("postalCode", "Enter a valid ZIP code.", body => body["postalCode"] = "1234"),
            ("branchId", "Choose a branch.", body => body["branchId"] = null),
            ("serviceInstructions", "Use 2,000 characters or fewer.", body => body["serviceInstructions"] = new string('s', 2001)),
        };

        foreach (var (key, message, mutate) in invalid)
        {
            var body = CustomerDetailSeed.PropertyBody(branchY.Id);
            mutate(body);

            foreach (var response in new[]
            {
                await host.SendAsync(HttpMethod.Post, path, owner, body),
                await host.SendAsync(HttpMethod.Put, $"{path}/{propertyP}", owner, body),
            })
            {
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
                Assert.Equal(message, CustomerSeed.Error(await CustomerHost.ReadAsync(response), key));
            }
        }

        Assert.Equal((properties, audits), (await database.CountRowsAsync("properties", org), await database.CountPropertyAuditAsync(org)));

        // AC-07: out-of-scope, foreign, inactive, unknown and malformed branches are 400 when set or changed.
        foreach (var branchId in new[] { branchY.Id.ToString(), foreignBranch.Id.ToString(), inactiveZ.Id.ToString(), Guid.NewGuid().ToString(), "not-a-guid" })
        {
            var rejected = await host.SendAsync(HttpMethod.Post, path, dispatcher, CustomerDetailSeed.PropertyBody(Guid.Empty).WithBranch(branchId));
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            Assert.Equal(NoBranchAccess, CustomerSeed.Error(await CustomerHost.ReadAsync(rejected), "branchId"));
        }

        Assert.Equal(properties, await database.CountRowsAsync("properties", org));

        // An unchanged out-of-scope branch is accepted on edit (rename only); a change to one is not.
        var stamp = await database.ScalarAsync<DateTimeOffset>("SELECT updated_at FROM properties WHERE id = @p", ("p", propertyP));
        var renamed = CustomerDetailSeed.PropertyBody(branchY.Id, name: "Dock Renamed");
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(HttpMethod.Put, $"{path}/{propertyP}", dispatcher, renamed)).StatusCode);
        var (_, _, updatedMetadata) = await database.GetLatestAuditAsync(org, "property.updated");
        Assert.Equal(["name"], JsonNode.Parse(updatedMetadata!)!["changedFields"]!.AsArray().Select(field => field!.GetValue<string>()).ToArray());
        Assert.True(await database.ScalarAsync<DateTimeOffset>("SELECT updated_at FROM properties WHERE id = @p", ("p", propertyP)) > stamp);

        // AC-08: a no-op save writes no audit row and keeps updated_at.
        var updates = await database.CountPropertyAuditAsync(org, "property.updated");
        var current = await database.ScalarAsync<DateTimeOffset>("SELECT updated_at FROM properties WHERE id = @p", ("p", propertyP));
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(HttpMethod.Put, $"{path}/{propertyP}", dispatcher, renamed)).StatusCode);
        Assert.Equal(updates, await database.CountPropertyAuditAsync(org, "property.updated"));
        Assert.Equal(current, await database.ScalarAsync<DateTimeOffset>("SELECT updated_at FROM properties WHERE id = @p", ("p", propertyP)));

        var toForeign = await host.SendAsync(HttpMethod.Put, $"{path}/{propertyP}", dispatcher, CustomerDetailSeed.PropertyBody(foreignBranch.Id, name: "Dock Renamed"));
        Assert.Equal(HttpStatusCode.BadRequest, toForeign.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(HttpMethod.Put, $"{path}/{propertyP}", dispatcher, CustomerDetailSeed.PropertyBody(branchX.Id, name: "Dock Renamed"))).StatusCode);
        Assert.Equal(branchX.Id, await database.ScalarAsync<Guid>("SELECT branch_id FROM properties WHERE id = @p", ("p", propertyP)));
        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(HttpMethod.Put, $"{path}/{propertyP}", dispatcher, CustomerDetailSeed.PropertyBody(branchY.Id, name: "Dock Renamed"))).StatusCode);

        // AC-09: set primary moves the single primary; repeating or targeting an archived property is 409.
        Assert.Equal(HttpStatusCode.NoContent, (await host.SendAsync(HttpMethod.Post, $"{path}/{propertyP}/set-primary", owner)).StatusCode);
        Assert.Equal(1L, await database.CountPrimariesAsync(customer));
        Assert.True(await database.ScalarAsync<bool>("SELECT is_primary FROM properties WHERE id = @p", ("p", propertyP)));
        Assert.True(await database.ScalarAsync<bool>("SELECT is_active AND NOT is_primary FROM properties WHERE id = @p", ("p", primaryA)));
        var (primaryBefore, primaryAfter, _) = await database.GetLatestAuditAsync(org, "property.primary_changed");
        Assert.Equal(primaryA, JsonNode.Parse(primaryBefore!)!["primaryPropertyId"]!.GetValue<Guid>());
        Assert.Equal(propertyP, JsonNode.Parse(primaryAfter!)!["primaryPropertyId"]!.GetValue<Guid>());
        Assert.Equal(propertyP, await database.ScalarAsync<Guid>("SELECT entity_id FROM audit_logs WHERE organization_id = @o AND action = 'property.primary_changed'", ("o", org)));

        var repeated = await host.SendAsync(HttpMethod.Post, $"{path}/{propertyP}/set-primary", owner);
        Assert.Equal(HttpStatusCode.Conflict, repeated.StatusCode);
        Assert.Equal("This property is already the primary property.", CustomerDetailSeed.Message(await CustomerHost.ReadAsync(repeated)));

        var list = await CustomerHost.ReadAsync(await host.SendAsync(HttpMethod.Get, path, owner));
        Assert.Equal([propertyP, primaryA], list["items"]!.AsArray().Select(item => item!["id"]!.GetValue<Guid>()).ToArray());

        // AC-10: archive a non-primary, never the primary; repeats and edits of archived properties are 409.
        Assert.Equal(HttpStatusCode.NoContent, (await host.SendAsync(HttpMethod.Post, $"{path}/{primaryA}/archive", owner)).StatusCode);
        var archivePrimary = await host.SendAsync(HttpMethod.Post, $"{path}/{propertyP}/archive", owner);
        Assert.Equal(HttpStatusCode.Conflict, archivePrimary.StatusCode);
        Assert.Equal("Set another property as primary before archiving this one.", CustomerDetailSeed.Message(await CustomerHost.ReadAsync(archivePrimary)));
        Assert.Equal("This property is already archived.", CustomerDetailSeed.Message(await CustomerHost.ReadAsync(await host.SendAsync(HttpMethod.Post, $"{path}/{primaryA}/archive", owner))));
        Assert.Equal("Reactivate this property before making it primary.", CustomerDetailSeed.Message(await CustomerHost.ReadAsync(await host.SendAsync(HttpMethod.Post, $"{path}/{primaryA}/set-primary", owner))));
        Assert.Equal("Reactivate this property to edit it.", CustomerDetailSeed.Message(await CustomerHost.ReadAsync(await host.SendAsync(HttpMethod.Put, $"{path}/{primaryA}", owner, CustomerDetailSeed.PropertyBody(branchX.Id)))));
        var (archivedBefore, archivedAfter, _) = await database.GetLatestAuditAsync(org, "property.archived");
        Assert.Equal((true, false), (JsonNode.Parse(archivedBefore!)!["isActive"]!.GetValue<bool>(), JsonNode.Parse(archivedAfter!)!["isActive"]!.GetValue<bool>()));

        Assert.Equal(HttpStatusCode.NoContent, (await host.SendAsync(HttpMethod.Post, $"{path}/{primaryA}/reactivate", owner)).StatusCode);
        Assert.True(await database.ScalarAsync<bool>("SELECT is_active AND NOT is_primary FROM properties WHERE id = @p", ("p", primaryA)));
        Assert.Equal("This property is already active.", CustomerDetailSeed.Message(await CustomerHost.ReadAsync(await host.SendAsync(HttpMethod.Post, $"{path}/{primaryA}/reactivate", owner))));
        Assert.Equal(1L, await database.CountPrimariesAsync(customer));
        Assert.Equal(2L, await database.ScalarAsync<long>("SELECT COUNT(*) FROM properties WHERE customer_id = @c", ("c", customer)));
        Assert.Equal(1L, await database.CountPropertyAuditAsync(org, "property.reactivated"));

        // BR-07: a customer without an active primary gets its next property as primary; archived customers still accept it.
        var bare = await database.SeedCustomerAsync(org, branchX.Id, "Bare Customer");
        await database.ExecuteAsync("UPDATE properties SET is_active = false, is_primary = false WHERE customer_id = @c", ("c", bare));
        await database.ExecuteAsync("UPDATE customers SET is_active = false WHERE id = @c", ("c", bare));
        var first = await host.SendAsync(HttpMethod.Post, $"/customers/{bare}/properties", owner, CustomerDetailSeed.PropertyBody(branchX.Id));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.True((await CustomerHost.ReadAsync(first))["isPrimary"]!.GetValue<bool>());
        Assert.Equal(1L, await database.CountPrimariesAsync(bare));

        // FR-17, BR-21: audit rows carry ids, flags and field names only.
        var auditText = await database.ScalarAsync<string>(
            "SELECT string_agg(COALESCE(before_data::text, '') || COALESCE(after_data::text, '') || metadata::text, ' ') FROM audit_logs WHERE organization_id = @o AND entity_type = 'property'",
            ("o", org));

        foreach (var secret in new[] { "Harbor", "Secret gate", "Bay 4", "Dock", "Austin", "Primary property" })
        {
            Assert.DoesNotContain(secret, auditText, StringComparison.Ordinal);
        }
    }

    // Atomicity and concurrency of set-primary (BR-07, BR-09): concurrent switches leave exactly one primary and one audit
    // row per success with no 5xx; a failure after the previous primary was cleared rolls everything back.
    [Fact]
    public async Task SetPrimary_ConcurrentRequestsAndMidTransactionFailure_KeepExactlyOnePrimary()
    {
        var org = await database.SeedOrganizationAsync();
        var branch = await database.SeedBranchAsync(org, isMain: true);
        var customer = await database.SeedCustomerAsync(org, branch.Id, "Race Customer");
        var primary = await database.ScalarAsync<Guid>("SELECT id FROM properties WHERE customer_id = @c", ("c", customer));
        var others = new List<Guid>();

        for (var i = 0; i < 4; i++)
        {
            others.Add(await database.SeedPropertyAsync(org, customer, branch.Id, $"Candidate {i}"));
        }

        await using var host = CustomerHost.Create(database);
        var owner = await host.CookieAsync(database, org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var path = $"/customers/{customer}/properties";

        var responses = await Task.WhenAll(
            others.Append(others[0]).Select(id => host.SendAsync(HttpMethod.Post, $"{path}/{id}/set-primary", owner)));

        Assert.All(responses, response => Assert.True(
            response.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.Conflict, $"Unexpected {response.StatusCode}"));
        var succeeded = responses.Count(response => response.StatusCode == HttpStatusCode.NoContent);
        Assert.True(succeeded >= 4);
        Assert.Equal(1L, await database.CountPrimariesAsync(customer));
        Assert.Equal(succeeded, await database.CountPropertyAuditAsync(org, "property.primary_changed"));

        // Failure after the previous primary is cleared: the transaction rolls back, the previous primary is kept.
        var current = await database.ScalarAsync<Guid>("SELECT id FROM properties WHERE customer_id = @c AND is_primary", ("c", customer));
        var boom = await database.SeedPropertyAsync(org, customer, branch.Id, "Boom");
        var audits = await database.CountPropertyAuditAsync(org);
        const string failing = "ck_test_force_primary_failure";
        await database.ExecuteAsync(
            $"ALTER TABLE properties ADD CONSTRAINT {failing} CHECK (organization_id <> '{org}' OR NOT is_primary OR name <> 'Boom') NOT VALID");

        HttpResponseMessage failed;

        try
        {
            failed = await host.SendAsync(HttpMethod.Post, $"{path}/{boom}/set-primary", owner);
        }
        finally
        {
            await database.ExecuteAsync($"ALTER TABLE properties DROP CONSTRAINT {failing}");
        }

        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        Assert.Equal(current, await database.ScalarAsync<Guid>("SELECT id FROM properties WHERE customer_id = @c AND is_primary", ("c", customer)));
        Assert.False(await database.ScalarAsync<bool>("SELECT is_primary FROM properties WHERE id = @p", ("p", boom)));
        Assert.Equal(audits, await database.CountPropertyAuditAsync(org));
        Assert.NotEqual(Guid.Empty, primary);
    }

    // AC-09 (drawer), AC-12, BR-08: GET/PUT /customers/{id} read and write the primary property only, never any property's
    // branch or name; a customer without an active primary gets one on save.
    [Fact]
    public async Task CustomerDrawer_ReadsAndWritesOnlyThePrimaryProperty()
    {
        var org = await database.SeedOrganizationAsync();
        var branchX = await database.SeedBranchAsync(org, "Xray Branch", isMain: true);
        var branchY = await database.SeedBranchAsync(org, "Yankee Branch");
        var branchZ = await database.SeedBranchAsync(org, "Zulu Branch");
        var customer = await database.SeedCustomerAsync(org, branchX.Id, "Drawer Customer", email: "drawer@example.com");
        await database.ExecuteAsync("UPDATE properties SET branch_id = @y, name = 'Headquarters', service_notes = 'Old note' WHERE customer_id = @c", ("y", branchY.Id), ("c", customer));
        var headquarters = await database.ScalarAsync<Guid>("SELECT id FROM properties WHERE customer_id = @c", ("c", customer));
        var second = await database.SeedPropertyAsync(org, customer, branchX.Id, "Second Site", serviceNotes: "Second note");

        await using var host = CustomerHost.Create(database);
        var owner = await host.CookieAsync(database, org, CompanySettingsDatabaseFixture.OwnerRoleId);

        var edit = CustomerSeed.Body(branchZ.Id, email: "drawer@example.com");
        edit["property"]!["addressLine1"] = "77 New Rd";
        edit["serviceInstructions"] = "New note";
        var updated = await host.SendAsync(HttpMethod.Put, $"/customers/{customer}", owner, edit);

        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal(branchZ.Id, (await CustomerHost.ReadAsync(updated))["branchId"]!.GetValue<Guid>());
        Assert.Equal("Headquarters|77 New Rd|New note", await database.ScalarAsync<string>(
            "SELECT name || '|' || address_line1 || '|' || service_notes FROM properties WHERE id = @p", ("p", headquarters)));
        Assert.Equal(branchY.Id, await database.ScalarAsync<Guid>("SELECT branch_id FROM properties WHERE id = @p", ("p", headquarters)));
        Assert.Equal("5 Seed Ave|Second note|Second Site", await database.ScalarAsync<string>(
            "SELECT address_line1 || '|' || service_notes || '|' || name FROM properties WHERE id = @p", ("p", second)));

        // AC-09: after set primary the drawer shows the new primary's address.
        Assert.Equal(HttpStatusCode.NoContent, (await host.SendAsync(HttpMethod.Post, $"/customers/{customer}/properties/{second}/set-primary", owner)).StatusCode);
        var detail = await CustomerHost.ReadAsync(await host.SendAsync(HttpMethod.Get, $"/customers/{customer}", owner));
        Assert.Equal(("5 Seed Ave", "Second note"), (detail["property"]!["addressLine1"]!.GetValue<string>(), detail["serviceInstructions"]!.GetValue<string>()));

        // AC-12: no active primary -> empty address on GET, and PUT creates "Primary property" in the customer's branch.
        var bare = await database.SeedCustomerAsync(org, branchX.Id, "Bare Drawer", email: "bare@example.com");
        await database.ExecuteAsync("UPDATE properties SET is_active = false, is_primary = false WHERE customer_id = @c", ("c", bare));
        var bareDetail = await CustomerHost.ReadAsync(await host.SendAsync(HttpMethod.Get, $"/customers/{bare}", owner));
        Assert.Equal(string.Empty, bareDetail["property"]!["addressLine1"]!.GetValue<string>());

        var saved = await host.SendAsync(HttpMethod.Put, $"/customers/{bare}", owner, CustomerSeed.Body(branchY.Id, email: "bare@example.com"));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal("Primary property|true|true", await database.ScalarAsync<string>(
            "SELECT name || '|' || is_primary::text || '|' || (branch_id = @y)::text FROM properties WHERE customer_id = @c AND is_active", ("c", bare), ("y", branchY.Id)));
        Assert.Equal(1L, await database.CountPropertyAuditAsync(org, "property.created"));
        Assert.Equal(2L, await database.ScalarAsync<long>("SELECT COUNT(*) FROM properties WHERE customer_id = @c", ("c", bare)));
    }
}

internal static class JsonObjectExtensions
{
    public static JsonObject WithBranch(this JsonObject body, string branchId)
    {
        body["branchId"] = branchId;

        return body;
    }
}
