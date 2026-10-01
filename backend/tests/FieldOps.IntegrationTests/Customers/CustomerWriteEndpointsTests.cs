using System.Net;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.CompanySettings;

namespace FieldOps.IntegrationTests.Customers;

/// <summary>POST/PUT/archive/reactivate, tags and the duplicate check (FR-05 to FR-09): AC-08, AC-10, AC-12, AC-14 to AC-17.</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class CustomerWriteEndpointsTests(CompanySettingsDatabaseFixture database)
{
    // AC-08, AC-15, AC-16: create persists every row with an audit row without PII; edit audits field names only
    // and a no-op writes nothing; archive/reactivate toggle with 409 on repeat.
    [Fact]
    public async Task CreateEditArchive_PersistAtomicallyAuditWithoutPiiAndRejectRepeats()
    {
        var org = await database.SeedOrganizationAsync();
        await database.ExecuteAsync("UPDATE organizations SET country_code = 'CA' WHERE id = @o", ("o", org));
        var branch = await database.SeedBranchAsync(org, isMain: true);
        var otherBranch = await database.SeedBranchAsync(org);
        var gold = await database.SeedTagAsync(org, "Gold");
        var silver = await database.SeedTagAsync(org, "Silver");
        await database.SeedCustomerAsync(org, branch.Id, "Existing Twin", email: "twin@example.com");

        await using var host = CustomerHost.Create(database);
        var cookie = await host.CookieAsync(database, org, CompanySettingsDatabaseFixture.DispatcherRoleId, branch.Id, otherBranch.Id);

        var body = CustomerSeed.Body(branch.Id, email: "  Twin@Example.com ", phone: "+1 (512) 555-7832", tagIds: [gold.ToString(), silver.ToString(), gold.ToString()]);
        body["property"]!["stateRegion"] = "Ontario";
        body["property"]!["postalCode"] = "M5V 2T6";
        body["contact"]!["prefersSms"] = true;

        // A duplicate email never blocks creation (BR-14).
        var created = await host.SendAsync(HttpMethod.Post, "/customers", cookie, body);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await CustomerHost.ReadAsync(created))["id"]!.GetValue<Guid>();
        Assert.Equal($"/customers/{id}", created.Headers.Location?.ToString());

        Assert.Equal("person|Ada Lovelace|twin@example.com|5125557832|true", await database.ScalarAsync<string>(
            "SELECT type::text || '|' || display_name || '|' || primary_email || '|' || primary_phone || '|' || (branch_id = @b)::text FROM customers WHERE id = @id",
            ("id", id), ("b", branch.Id)));
        Assert.Equal("Ada|Lovelace|true|true|true", await database.ScalarAsync<string>(
            "SELECT first_name || '|' || last_name || '|' || prefers_email::text || '|' || prefers_sms::text || '|' || is_primary::text FROM customer_contacts WHERE customer_id = @id",
            ("id", id)));
        Assert.Equal("Primary property|CA|true|Ontario|Ring twice|true", await database.ScalarAsync<string>(
            "SELECT name || '|' || country_code || '|' || (branch_id = @b)::text || '|' || state_region || '|' || service_notes || '|' || is_primary::text FROM properties WHERE customer_id = @id",
            ("id", id), ("b", branch.Id)));
        Assert.Equal(2L, await database.CountRowsAsync("customer_tag_assignments", org));

        var (_, after, _) = await database.GetLatestAuditAsync(org, "customer.created");
        Assert.Equal(2, JsonNode.Parse(after!)!["tagCount"]!.GetValue<int>());
        Assert.DoesNotContain("twin@", after, StringComparison.Ordinal);
        Assert.DoesNotContain("5125557832", after, StringComparison.Ordinal);

        var detail = await CustomerHost.ReadAsync(await host.SendAsync(HttpMethod.Get, $"/customers/{id}", cookie));
        Assert.Equal("residential", detail["type"]!.GetValue<string>());
        Assert.Equal("lead", detail["lifecycle"]!.GetValue<string>());
        Assert.Equal(["Gold", "Silver"], detail["tags"]!.AsArray().Select(tag => tag!["name"]!.GetValue<string>()).ToArray());
        Assert.Equal("5125557832", detail["contact"]!["phone"]!.GetValue<string>());
        Assert.Equal("Pays late", detail["internalNote"]!.GetValue<string>());

        // Edit: only names of changed fields are audited.
        var edit = CustomerSeed.Body(otherBranch.Id, type: "residential", first: "Grace", last: "Hopper", email: "grace@example.com", tagIds: [silver.ToString()]);
        var updated = await host.SendAsync(HttpMethod.Put, $"/customers/{id}", cookie, edit);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var updatedDetail = await CustomerHost.ReadAsync(updated);
        Assert.Equal("residential", updatedDetail["type"]!.GetValue<string>());
        Assert.Equal(otherBranch.Id, updatedDetail["branchId"]!.GetValue<Guid>());
        Assert.Single(updatedDetail["tags"]!.AsArray());
        Assert.Equal(1L, await database.CountRowsAsync("customer_tag_assignments", org));
        // BR-08: the drawer never changes a property's branch, only the customer's.
        Assert.Equal(branch.Id, await database.ScalarAsync<Guid>("SELECT branch_id FROM properties WHERE customer_id = @id", ("id", id)));

        var (_, _, metadata) = await database.GetLatestAuditAsync(org, "customer.updated");
        Assert.Contains("changedFields", metadata, StringComparison.Ordinal);
        Assert.Contains("\"email\"", metadata, StringComparison.Ordinal);
        Assert.Contains("\"tags\"", metadata, StringComparison.Ordinal);
        Assert.DoesNotContain("grace@", metadata, StringComparison.Ordinal);

        // A no-op save writes no audit row and does not touch updated_at.
        var stamp = await database.ScalarAsync<DateTimeOffset>("SELECT updated_at FROM customers WHERE id = @id", ("id", id));
        var audits = await database.CountCustomerAuditAsync(org, "customer.updated");
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(HttpMethod.Put, $"/customers/{id}", cookie, edit)).StatusCode);
        Assert.Equal(audits, await database.CountCustomerAuditAsync(org, "customer.updated"));
        Assert.Equal(stamp, await database.ScalarAsync<DateTimeOffset>("SELECT updated_at FROM customers WHERE id = @id", ("id", id)));

        // BR-11, AC-15: type is required on edit and may switch; the id, contact, property and tags are kept.
        var untyped = CustomerSeed.Body(otherBranch.Id, first: "Grace", last: "Hopper", email: "grace@example.com", tagIds: [silver.ToString()]);
        untyped.Remove("type");
        var missingType = await host.SendAsync(HttpMethod.Put, $"/customers/{id}", cookie, untyped);
        Assert.Equal(HttpStatusCode.BadRequest, missingType.StatusCode);
        Assert.Equal("Choose a customer type.", CustomerSeed.Error(await CustomerHost.ReadAsync(missingType), "type"));

        var noCompany = CustomerSeed.Body(otherBranch.Id, type: "commercial", first: "Grace", last: "Hopper", email: "grace@example.com", tagIds: [silver.ToString()]);
        var companyMissing = await host.SendAsync(HttpMethod.Put, $"/customers/{id}", cookie, noCompany);
        Assert.Equal(HttpStatusCode.BadRequest, companyMissing.StatusCode);
        Assert.Equal("Enter a company name.", CustomerSeed.Error(await CustomerHost.ReadAsync(companyMissing), "companyName"));

        var contactId = await database.ScalarAsync<Guid>("SELECT id FROM customer_contacts WHERE customer_id = @id", ("id", id));
        var propertyId = await database.ScalarAsync<Guid>("SELECT id FROM properties WHERE customer_id = @id", ("id", id));
        noCompany["companyName"] = "Hopper Co";
        var toCommercial = await host.SendAsync(HttpMethod.Put, $"/customers/{id}", cookie, noCompany);
        Assert.Equal(HttpStatusCode.OK, toCommercial.StatusCode);
        var commercialDetail = await CustomerHost.ReadAsync(toCommercial);
        Assert.Equal(id, commercialDetail["id"]!.GetValue<Guid>());
        Assert.Equal("commercial", commercialDetail["type"]!.GetValue<string>());
        Assert.Equal("company|Hopper Co|Manager|1", await database.ScalarAsync<string>(
            "SELECT c.type::text || '|' || c.display_name || '|' || k.title || '|' || (SELECT COUNT(*) FROM customer_tag_assignments WHERE customer_id = c.id)::text FROM customers c JOIN customer_contacts k ON k.customer_id = c.id WHERE c.id = @id",
            ("id", id)));
        Assert.Equal(contactId, await database.ScalarAsync<Guid>("SELECT id FROM customer_contacts WHERE customer_id = @id", ("id", id)));
        Assert.Equal(propertyId, await database.ScalarAsync<Guid>("SELECT id FROM properties WHERE customer_id = @id", ("id", id)));
        var (_, _, typeMetadata) = await database.GetLatestAuditAsync(org, "customer.updated");
        Assert.Contains("\"type\"", typeMetadata, StringComparison.Ordinal);
        Assert.Contains("\"companyName\"", typeMetadata, StringComparison.Ordinal);
        Assert.DoesNotContain("Hopper Co", typeMetadata, StringComparison.Ordinal);

        var toResidential = await host.SendAsync(HttpMethod.Put, $"/customers/{id}", cookie, edit);
        Assert.Equal(HttpStatusCode.OK, toResidential.StatusCode);
        Assert.Equal("person|Grace Hopper|", await database.ScalarAsync<string>(
            "SELECT c.type::text || '|' || c.display_name || '|' || COALESCE(k.title, '') FROM customers c JOIN customer_contacts k ON k.customer_id = c.id WHERE c.id = @id",
            ("id", id)));

        // Archive and reactivate toggle is_active, keep contacts and properties, and reject a repeat with 409.
        var contacts = await database.CountRowsAsync("customer_contacts", org);
        var properties = await database.CountRowsAsync("properties", org);
        Assert.Equal(HttpStatusCode.NoContent, (await host.SendAsync(HttpMethod.Post, $"/customers/{id}/archive", cookie)).StatusCode);
        Assert.False(await database.ScalarAsync<bool>("SELECT is_active FROM customers WHERE id = @id", ("id", id)));
        var repeat = await host.SendAsync(HttpMethod.Post, $"/customers/{id}/archive", cookie);
        Assert.Equal(HttpStatusCode.Conflict, repeat.StatusCode);
        Assert.Equal("This customer is already archived.", (await CustomerHost.ReadAsync(repeat))["title"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(HttpMethod.Put, $"/customers/{id}", cookie, edit)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await host.SendAsync(HttpMethod.Post, $"/customers/{id}/reactivate", cookie)).StatusCode);
        var again = await host.SendAsync(HttpMethod.Post, $"/customers/{id}/reactivate", cookie);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("This customer is already active.", (await CustomerHost.ReadAsync(again))["title"]!.GetValue<string>());
        Assert.Equal(contacts, await database.CountRowsAsync("customer_contacts", org));
        Assert.Equal(properties, await database.CountRowsAsync("properties", org));
        Assert.Equal(1L, await database.CountCustomerAuditAsync(org, "customer.archived"));
        Assert.Equal(1L, await database.CountCustomerAuditAsync(org, "customer.reactivated"));
        Assert.False(JsonNode.Parse((await database.GetLatestAuditAsync(org, "customer.archived")).After!)!["isActive"]!.GetValue<bool>());

        // Commercial: display name is the company name and the contact keeps its title.
        var commercial = await host.SendAsync(
            HttpMethod.Post, "/customers", cookie,
            CustomerSeed.Body(branch.Id, type: "commercial", companyName: "Acme Corp", email: "acme@example.com"));
        Assert.Equal(HttpStatusCode.Created, commercial.StatusCode);
        var commercialId = (await CustomerHost.ReadAsync(commercial))["id"]!.GetValue<Guid>();
        Assert.Equal("company|Acme Corp|Manager", await database.ScalarAsync<string>(
            "SELECT c.type::text || '|' || c.display_name || '|' || k.title FROM customers c JOIN customer_contacts k ON k.customer_id = c.id WHERE c.id = @id",
            ("id", commercialId)));
    }

    // AC-10: every BR-10 to BR-13 rule answers 400 with errors.<field> and persists nothing.
    [Fact]
    public async Task Create_InvalidValues_ReturnFieldErrorsAndPersistNothing()
    {
        var org = await database.SeedOrganizationAsync();
        var branch = await database.SeedBranchAsync(org, isMain: true);
        await using var host = CustomerHost.Create(database);
        var cookie = await host.CookieAsync(database, org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var tags = Enumerable.Range(1, 11).Select(index => database.SeedTagAsync(org, $"Tag {index}")).ToArray();
        var tagIds = (await Task.WhenAll(tags)).Select(id => id.ToString()).ToArray();

        var cases = new (string Field, string Message, Action<JsonObject> Mutate)[]
        {
            ("type", "Choose a customer type.", body => body["type"] = "other"),
            ("companyName", "Enter a company name.", body => { body["type"] = "commercial"; body["companyName"] = " "; }),
            ("companyName", "Use 180 characters or fewer.", body => { body["type"] = "commercial"; body["companyName"] = new string('c', 181); }),
            ("title", "Use 100 characters or fewer.", body => { body["type"] = "commercial"; body["companyName"] = "Co"; body["contact"]!["title"] = new string('t', 101); }),
            ("firstName", "Enter a first name.", body => body["contact"]!["firstName"] = ""),
            ("firstName", "Use 100 characters or fewer.", body => body["contact"]!["firstName"] = new string('f', 101)),
            ("lastName", "Enter a last name.", body => body["contact"]!["lastName"] = null),
            ("lastName", "Use 180 characters or fewer for the full name.", body => { body["contact"]!["firstName"] = new string('f', 100); body["contact"]!["lastName"] = new string('l', 90); }),
            ("email", "Enter an email.", body => body["contact"]!["email"] = " "),
            ("email", "Enter a valid email.", body => body["contact"]!["email"] = "not-an-email"),
            ("phone", "Enter a valid phone number.", body => body["contact"]!["phone"] = "12345"),
            ("phone", "Enter a valid phone number.", body => body["contact"]!["phone"] = "512-555-78x2"),
            ("preferredCommunication", "Choose at least one communication method.", body => { body["contact"]!["prefersEmail"] = false; body["contact"]!["prefersSms"] = false; }),
            ("preferredCommunication", "Add a mobile phone to use SMS.", body => body["contact"]!["prefersSms"] = true),
            ("addressLine1", "Enter an address.", body => body["property"]!["addressLine1"] = ""),
            ("city", "Use 100 characters or fewer.", body => body["property"]!["city"] = new string('c', 101)),
            ("stateRegion", "Choose a valid state.", body => body["property"]!["stateRegion"] = "ZZ"),
            ("postalCode", "Enter a valid ZIP code.", body => body["property"]!["postalCode"] = "1234"),
            ("serviceInstructions", "Use 2,000 characters or fewer.", body => body["serviceInstructions"] = new string('s', 2001)),
            ("internalNote", "Use 2,000 characters or fewer.", body => body["internalNote"] = new string('n', 2001)),
            ("branchId", "Choose a branch.", body => body["branchId"] = null),
            ("tagIds", "Choose up to 10 tags.", body => body["tagIds"] = new JsonArray([.. tagIds.Select(id => (JsonNode?)JsonValue.Create(id))])),
            ("tagIds", "Choose a valid tag.", body => body["tagIds"] = new JsonArray(JsonValue.Create("nope"))),
        };

        foreach (var (field, message, mutate) in cases)
        {
            var body = CustomerSeed.Body(branch.Id);
            mutate(body);

            var response = await host.SendAsync(HttpMethod.Post, "/customers", cookie, body);
            Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{field}/{message}: {response.StatusCode}");
            Assert.Equal(message, CustomerSeed.Error(await CustomerHost.ReadAsync(response), field));
        }

        Assert.Equal(0L, await database.CountCustomersAsync(org));
        Assert.Equal(0L, await database.CountRowsAsync("customer_contacts", org));
        Assert.Equal(0L, await database.CountCustomerAuditAsync(org, "customer.created"));
    }

    // AC-17: a new name is 201, any other casing returns the existing tag with 200, and an audit row is written once.
    [Fact]
    public async Task Tags_CreateReturnsExistingForCaseInsensitiveMatchAndListsOwnOrganizationOnly()
    {
        var org = await database.SeedOrganizationAsync();
        var other = await database.SeedOrganizationAsync();
        await database.SeedTagAsync(other, "Foreign");
        await database.SeedTagAsync(org, "Zebra");

        await using var host = CustomerHost.Create(database);
        var cookie = await host.CookieAsync(database, org, CompanySettingsDatabaseFixture.DispatcherRoleId);

        var created = await host.SendAsync(HttpMethod.Post, "/customer-tags", cookie, new JsonObject { ["name"] = "  VIP Gold " });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var tag = await CustomerHost.ReadAsync(created);
        Assert.Equal("VIP Gold", tag["name"]!.GetValue<string>());

        var existing = await host.SendAsync(HttpMethod.Post, "/customer-tags", cookie, new JsonObject { ["name"] = "vip gold" });
        Assert.Equal(HttpStatusCode.OK, existing.StatusCode);
        Assert.Equal(tag["id"]!.GetValue<Guid>(), (await CustomerHost.ReadAsync(existing))["id"]!.GetValue<Guid>());
        Assert.Equal(1L, await database.CountCustomerAuditAsync(org, "customer_tag.created"));
        Assert.Equal(2L, await database.CountRowsAsync("customer_tags", org));

        foreach (var name in new[] { " ", new string('x', 41) })
        {
            var invalid = await host.SendAsync(HttpMethod.Post, "/customer-tags", cookie, new JsonObject { ["name"] = name });
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.NotNull((await CustomerHost.ReadAsync(invalid))["errors"]!["name"]);
        }

        var list = await host.SendAsync(HttpMethod.Get, "/customer-tags", cookie);
        Assert.Equal(["VIP Gold", "Zebra"], (await CustomerHost.ReadAsync(list)).AsArray().Select(item => item!["name"]!.GetValue<string>()).ToArray());
    }

    // AC-12, AC-14: archived matches included, one entry per customer, up to three by name, out-of-scope matches redacted,
    // excludeCustomerId honored only for a visible customer.
    [Fact]
    public async Task DuplicateCheck_MatchesAcrossArchivedAndScopeWithRedactionAndExclusion()
    {
        var org = await database.SeedOrganizationAsync();
        var other = await database.SeedOrganizationAsync();
        var visible = await database.SeedBranchAsync(org, isMain: true);
        var hidden = await database.SeedBranchAsync(org);
        var otherBranch = await database.SeedBranchAsync(other, isMain: true);

        var current = await database.SeedCustomerAsync(org, visible.Id, "Bee Current", email: "dup@example.com", phone: "5125550101");
        await database.SeedCustomerAsync(org, visible.Id, "Aaa Archived", email: "dup@example.com", active: false);
        await database.SeedCustomerAsync(org, hidden.Id, "Zed Hidden", phone: "5125550101");
        await database.SeedCustomerAsync(org, visible.Id, "Cee Third", email: "dup@example.com");
        await database.SeedCustomerAsync(org, visible.Id, "Dee Fourth", email: "dup@example.com");
        var foreign = await database.SeedCustomerAsync(other, otherBranch.Id, "Foreign Twin", email: "dup@example.com");

        await using var host = CustomerHost.Create(database);
        var cookie = await host.CookieAsync(database, org, CompanySettingsDatabaseFixture.DispatcherRoleId, visible.Id);

        async Task<JsonArray> CheckAsync(JsonObject body)
        {
            var response = await host.SendAsync(HttpMethod.Post, "/customers/duplicate-check", cookie, body);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            return (await CustomerHost.ReadAsync(response))["matches"]!.AsArray();
        }

        // Email and phone both match one customer: one entry, email wins; at most three, ordered by name.
        var both = await CheckAsync(new JsonObject { ["email"] = " DUP@example.com ", ["phone"] = "(512) 555-0101" });
        Assert.Equal(["Aaa Archived", "Bee Current", "Cee Third"], both.Select(match => match!["displayName"]!.GetValue<string>()).ToArray());
        Assert.Equal("archived", both[0]!["displayStatus"]!.GetValue<string>());
        Assert.Equal("email", both[1]!["matchedField"]!.GetValue<string>());
        Assert.Equal("(512) 555-0101", both[1]!["primaryPhone"]!.GetValue<string>());
        Assert.Equal(1, both[1]!["propertyCount"]!.GetValue<int>());
        Assert.True(both[1]!["inScope"]!.GetValue<bool>());
        Assert.DoesNotContain(foreign.ToString(), both.ToJsonString(), StringComparison.Ordinal);

        // The out-of-scope phone match carries no id and no contact data.
        var hiddenMatch = (await CheckAsync(new JsonObject { ["phone"] = "512-555-0101", ["excludeCustomerId"] = current.ToString() }))
            .Single()!.AsObject();
        Assert.Equal("Zed Hidden", hiddenMatch["displayName"]!.GetValue<string>());
        Assert.Equal("phone", hiddenMatch["matchedField"]!.GetValue<string>());
        Assert.False(hiddenMatch["inScope"]!.GetValue<bool>());
        Assert.Equal(["displayName", "displayStatus", "inScope", "matchedField"], hiddenMatch.Select(pair => pair.Key).Order().ToArray());

        // An exclude id of another organization is ignored without error; an invalid email matches nothing.
        var ignored = await CheckAsync(new JsonObject { ["phone"] = "5125550101", ["excludeCustomerId"] = foreign.ToString() });
        Assert.Equal(2, ignored.Count);
        Assert.Empty(await CheckAsync(new JsonObject { ["email"] = "not-an-email", ["phone"] = "123" }));
    }
}
