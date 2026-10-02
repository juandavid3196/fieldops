using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FieldOps.IntegrationTests.Api;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.Customers;
using FieldOps.IntegrationTests.Sessions;

namespace FieldOps.IntegrationTests.PublicRequests;

[Collection(CompanySettingsDatabaseCollection.Name)]
public partial class PublicServiceRequestSubmissionTests(CompanySettingsDatabaseFixture database)
{
    // AC-09, AC-15 (valid files), AC-18, AC-19 (success): the happy path with new records.
    [Fact]
    public async Task Submit_ValidRequestWithAttachments_CreatesRowsHistoryAuditAndSendsOneEmail()
    {
        var org = await database.SeedPublicOrgAsync();
        var users = await database.ScalarAsync<long>("SELECT COUNT(*) FROM users");
        var jpeg = PublicRequestSeed.Jpeg(300);
        var png = PublicRequestSeed.Png(120);
        var pdf = PublicRequestSeed.Pdf(80);

        await using var host = PublicRequestHost.Create(database);

        // The JSON part is sent as a file part (a browser Blob), organization/branch ids are ignored.
        var response = await host.PostAsync(
            org.Slug.ToUpperInvariant(),
            PublicRequestSeed.ValidBody(org),
            [("photo.jpg", jpeg), ("..\\x/scan.PNG", png), ("doc.pdf", pdf)],
            requestAsFile: true);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        PublicRequestSeed.AssertNoStore(response);
        Assert.Null(response.Headers.Location);

        var created = await PublicRequestSeed.ReadAsync(response);
        Assert.Equal(["requestNumber"], created.AsObject().Select(p => p.Key));
        Assert.Equal("REQ-1", created["requestNumber"]!.GetValue<string>());

        var requestId = await database.ScalarAsync<Guid>(
            "SELECT id FROM service_requests WHERE organization_id = @o", ("o", org.Id));

        Assert.Equal("new", await Text("status::text", "service_requests", requestId));
        Assert.Equal("public_form", await Text("source", "service_requests", requestId));
        Assert.True(await IsNull("branch_id", "service_requests", requestId));
        Assert.Equal("Ada Lovelace", await Text("guest_name", "service_requests", requestId));
        Assert.Equal("visitor@example.com", await Text("guest_email", "service_requests", requestId));
        Assert.Equal("(555) 123-4567", await Text("guest_phone", "service_requests", requestId));
        Assert.Equal("Kitchen drain is clogged", await Text("description", "service_requests", requestId));
        Assert.Equal("urgent", await Text("urgency", "service_requests", requestId));
        Assert.True(await database.ScalarAsync<bool>(
            "SELECT has_active_damage FROM service_requests WHERE id = @i", ("i", requestId)));
        Assert.Equal(org.ServiceId, await database.ScalarAsync<Guid>(
            "SELECT catalog_item_id FROM service_requests WHERE id = @i", ("i", requestId)));
        Assert.Equal(org.CategoryId, await database.ScalarAsync<Guid>(
            "SELECT category_id FROM service_requests WHERE id = @i", ("i", requestId)));
        Assert.True(await IsNull("preferred_start", "service_requests", requestId));
        Assert.True(await database.ScalarAsync<bool>(
            "SELECT consent_at > now() - interval '1 minute' FROM service_requests WHERE id = @i", ("i", requestId)));

        var address = JsonNode.Parse(await Text("service_address::text", "service_requests", requestId))!;
        Assert.Equal("12 Analytical St", address["line1"]!.GetValue<string>());
        Assert.Equal("Apt 3", address["line2"]!.GetValue<string>());
        Assert.Equal("TX", address["state"]!.GetValue<string>());
        Assert.Equal("US", address["countryCode"]!.GetValue<string>());
        Assert.Equal("home", address["propertyType"]!.GetValue<string>());

        var availability = JsonNode.Parse(await Text("availability_preferences::text", "service_requests", requestId))!;
        Assert.Equal("asap", availability["dateMode"]!.GetValue<string>());
        Assert.Null(availability["preferredDate"]);
        Assert.Equal("morning", availability["timeWindow"]!.GetValue<string>());
        Assert.Equal("Call first", availability["schedulingNotes"]!.GetValue<string>());

        // Customer, primary contact and primary property (BR-09, BR-10); no account rows (FR-08).
        var customerId = await database.ScalarAsync<Guid>(
            "SELECT customer_id FROM service_requests WHERE id = @i", ("i", requestId));
        Assert.Equal("person", await Text("type::text", "customers", customerId));
        Assert.Equal("Ada Lovelace", await Text("display_name", "customers", customerId));
        Assert.Equal(org.BranchId, await database.ScalarAsync<Guid>(
            "SELECT branch_id FROM customers WHERE id = @i", ("i", customerId)));
        Assert.Equal(1L, await database.CountAsync("customer_contacts", org.Id));
        Assert.True(await database.ScalarAsync<bool>(
            "SELECT is_primary AND prefers_email AND NOT prefers_sms AND email = 'visitor@example.com' AND last_name = 'Lovelace' FROM customer_contacts WHERE customer_id = @c",
            ("c", customerId)));
        Assert.True(await database.ScalarAsync<bool>(
            """
            SELECT name = 'Home' AND is_primary AND country_code = 'US' AND state_region = 'TX' AND postal_code = '78701'
                AND access_instructions = 'Gate code 1234' AND branch_id IS NULL AND address_line2 = 'Apt 3'
            FROM properties WHERE customer_id = @c
            """,
            ("c", customerId)));
        Assert.Equal(users, await database.ScalarAsync<long>("SELECT COUNT(*) FROM users"));
        Assert.Equal(0L, await database.CountAsync("organization_users", org.Id));

        // Attachments: detected type, sanitized name, size, content, no uploader.
        Assert.Equal(3L, await database.CountAsync("request_attachments", org.Id));
        await AssertAttachment("photo.jpg", "image/jpeg", jpeg);
        await AssertAttachment("scan.PNG", "image/png", png);
        await AssertAttachment("doc.pdf", "application/pdf", pdf);
        Assert.Equal(0L, await database.ScalarAsync<long>(
            "SELECT COUNT(*) FROM request_attachments WHERE organization_id = @o AND uploaded_by_user_id IS NOT NULL",
            ("o", org.Id)));

        // History and audit (BR-15): one row each, without personal data.
        Assert.Equal(1L, await database.CountAsync("request_status_history", org.Id));
        Assert.True(await database.ScalarAsync<bool>(
            "SELECT from_status IS NULL AND to_status = 'new' AND changed_by_user_id IS NULL AND reason IS NULL FROM request_status_history WHERE request_id = @r",
            ("r", requestId)));
        Assert.Equal(1, await database.CountAuditLogsAsync(org.Id, "service_request.created"));
        Assert.True(await database.ScalarAsync<bool>(
            "SELECT entity_type = 'service_request' AND entity_id = @r AND actor_user_id IS NULL AND branch_id IS NULL AND ip_address IS NULL FROM audit_logs WHERE organization_id = @o AND action = 'service_request.created'",
            ("r", requestId),
            ("o", org.Id)));

        var after = await database.GetLatestAuditAfterDataAsync(org.Id, "service_request.created");
        var afterJson = JsonNode.Parse(after!)!.AsObject();
        Assert.Equal(
            ["attachmentCount", "hasActiveDamage", "requestNumber", "source", "status", "urgency"],
            afterJson.Select(p => p.Key).Order());
        Assert.Equal("REQ-1", afterJson["requestNumber"]!.GetValue<string>());
        Assert.Equal(3, afterJson["attachmentCount"]!.GetValue<int>());

        foreach (var personal in new[] { "Ada", "Lovelace", "visitor@", "Analytical", "drain", "Call first", "photo.jpg", "555" })
        {
            Assert.DoesNotContain(personal, after, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Equal(2L, await database.NextNumberAsync(org.Id));

        // One confirmation to the submitted address (BR-19), without address or description.
        var email = Assert.Single(host.Sender.Messages);
        Assert.Equal("visitor@example.com", email.To);
        Assert.Equal("We received your request REQ-1", email.Subject);

        foreach (var body in new[] { email.TextBody, email.HtmlBody })
        {
            Assert.Contains("Ada", body, StringComparison.Ordinal);
            Assert.Contains("REQ-1", body, StringComparison.Ordinal);
            Assert.Contains("Submitting this request does not confirm a price or appointment.", body, StringComparison.Ordinal);
            Assert.DoesNotContain("Analytical", body, StringComparison.Ordinal);
            Assert.DoesNotContain("clogged", body, StringComparison.Ordinal);
            Assert.DoesNotContain("photo.jpg", body, StringComparison.Ordinal);
        }

        async Task AssertAttachment(string name, string mime, byte[] content)
        {
            Assert.Equal(mime, await database.ScalarAsync<string>(
                "SELECT mime_type FROM request_attachments WHERE request_id = @r AND file_name = @n", ("r", requestId), ("n", name)));
            Assert.Equal((long)content.Length, await database.ScalarAsync<long>(
                "SELECT size_bytes FROM request_attachments WHERE request_id = @r AND file_name = @n", ("r", requestId), ("n", name)));
            Assert.Equal(content, await database.ScalarAsync<byte[]>(
                "SELECT content FROM request_attachments WHERE request_id = @r AND file_name = @n", ("r", requestId), ("n", name)));
        }
    }

    // AC-10, AC-11 (other-organization match is the tenant-isolation case).
    [Fact]
    public async Task Submit_ContactResolution_ReusesOnlyASingleActiveMatchInTheSameOrganization()
    {
        var org = await database.SeedPublicOrgAsync();
        var other = await database.SeedPublicOrgAsync();
        await using var host = PublicRequestHost.Create(database);
        var numbers = new List<string>();

        // Single match, any case: reused and left unchanged; the new property is not primary (a primary exists).
        var single = await database.SeedCustomerAsync(org.Id, org.BranchId, "Existing Customer", email: "Single@Example.com");
        var singleContact = await database.ScalarAsync<Guid>("SELECT id FROM customer_contacts WHERE customer_id = @c", ("c", single));
        var beforeCustomer = await database.ScalarAsync<string>("SELECT row(customers.*)::text FROM customers WHERE id = @c", ("c", single));
        var beforeContact = await database.ScalarAsync<string>("SELECT row(customer_contacts.*)::text FROM customer_contacts WHERE id = @c", ("c", singleContact));

        var customersBefore = await database.CountAsync("customers", org.Id);
        numbers.Add(await SubmitAsync(host, org, "single@example.com"));

        Assert.Equal(customersBefore, await database.CountAsync("customers", org.Id));
        Assert.Equal(single, await RequestCustomer(org, "REQ-1"));
        Assert.Equal(singleContact, await database.ScalarAsync<Guid>(
            "SELECT contact_id FROM service_requests WHERE organization_id = @o AND request_number = 1", ("o", org.Id)));
        Assert.Equal(beforeCustomer, await database.ScalarAsync<string>("SELECT row(customers.*)::text FROM customers WHERE id = @c", ("c", single)));
        Assert.Equal(beforeContact, await database.ScalarAsync<string>("SELECT row(customer_contacts.*)::text FROM customer_contacts WHERE id = @c", ("c", singleContact)));
        Assert.Equal(2L, await database.ScalarAsync<long>("SELECT COUNT(*) FROM properties WHERE customer_id = @c", ("c", single)));
        Assert.Equal(1L, await database.ScalarAsync<long>("SELECT COUNT(*) FROM properties WHERE customer_id = @c AND is_primary", ("c", single)));

        // Single match whose customer has no active primary property: the new property becomes primary.
        var noPrimary = await database.SeedCustomerAsync(org.Id, org.BranchId, "No Primary", email: "noprimary@example.com");
        await database.ExecuteAsync("UPDATE properties SET is_primary = false WHERE customer_id = @c", ("c", noPrimary));
        numbers.Add(await SubmitAsync(host, org, "NOPRIMARY@example.com"));
        Assert.Equal(noPrimary, await RequestCustomer(org, "REQ-2"));
        Assert.Equal(1L, await database.ScalarAsync<long>(
            "SELECT COUNT(*) FROM properties WHERE customer_id = @c AND is_primary AND name = 'Home'", ("c", noPrimary)));

        // Two active contacts share the email: a new customer, existing records untouched.
        var dupA = await database.SeedCustomerAsync(org.Id, org.BranchId, "Dup A", email: "dup@example.com");
        var dupB = await database.SeedCustomerAsync(org.Id, org.BranchId, "Dup B", email: "dup@example.com");
        numbers.Add(await SubmitAsync(host, org, "dup@example.com"));
        var dupNew = await RequestCustomer(org, "REQ-3");
        Assert.DoesNotContain(dupNew, new[] { dupA, dupB });
        Assert.Equal(1L, await database.ScalarAsync<long>("SELECT COUNT(*) FROM customer_contacts WHERE customer_id = @c", ("c", dupA)));
        Assert.Equal(1L, await database.ScalarAsync<long>("SELECT COUNT(*) FROM properties WHERE customer_id = @c", ("c", dupA)));

        // The only match belongs to another organization: nothing is linked or modified there.
        var foreign = await database.SeedCustomerAsync(other.Id, other.BranchId, "Foreign", email: "foreign@example.com");
        var foreignRow = await database.ScalarAsync<string>("SELECT row(customers.*)::text FROM customers WHERE id = @c", ("c", foreign));
        numbers.Add(await SubmitAsync(host, org, "foreign@example.com", propertyType: "business"));
        var foreignNew = await RequestCustomer(org, "REQ-4");
        Assert.NotEqual(foreign, foreignNew);
        Assert.Equal(org.Id, await database.ScalarAsync<Guid>("SELECT organization_id FROM customers WHERE id = @c", ("c", foreignNew)));
        Assert.Equal("company", await database.ScalarAsync<string>("SELECT type::text FROM customers WHERE id = @c", ("c", foreignNew)));
        Assert.Equal("Business", await database.ScalarAsync<string>("SELECT name FROM properties WHERE customer_id = @c", ("c", foreignNew)));
        Assert.Equal(foreignRow, await database.ScalarAsync<string>("SELECT row(customers.*)::text FROM customers WHERE id = @c", ("c", foreign)));
        Assert.Equal(0L, await database.CountAsync("service_requests", other.Id));

        // An archived customer does not count as a match.
        var archived = await database.SeedCustomerAsync(org.Id, org.BranchId, "Archived", email: "archived@example.com", active: false);
        numbers.Add(await SubmitAsync(host, org, "archived@example.com"));
        Assert.NotEqual(archived, await RequestCustomer(org, "REQ-5"));

        Assert.Equal(["REQ-1", "REQ-2", "REQ-3", "REQ-4", "REQ-5"], numbers);
    }

    // AC-13, AC-14: server rules per field and cross-organization catalog ids; nothing persisted.
    [Fact]
    public async Task Submit_InvalidFieldsAndForeignCatalogIds_Return400WithFieldKeysAndPersistNothing()
    {
        var org = await database.SeedPublicOrgAsync(timezone: "America/Chicago");
        var other = await database.SeedPublicOrgAsync();
        var inactiveCategory = await database.SeedCategoryAsync(org.Id, "Retired", active: false);
        var inactiveCategoryService = await database.SeedServiceAsync(org.Id, inactiveCategory, "Retired service");
        var inactiveService = await database.SeedServiceAsync(org.Id, org.CategoryId, "Old service", active: false);
        var product = await database.SeedServiceAsync(org.Id, org.CategoryId, "Copper pipe", type: "product");
        var electrical = await database.SeedCategoryAsync(org.Id, "Electrical");
        var electricalService = await database.SeedServiceAsync(org.Id, electrical, "Wiring");
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(
            DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("America/Chicago")).DateTime);

        (string Key, Action<JsonObject> Mutate)[] cases =
        [
            ("contact.firstName", b => b["contact"]!["firstName"] = "  "),
            ("contact.lastName", b => b["contact"]!["lastName"] = new string('l', 101)),
            ("contact.email", b => b["contact"]!["email"] = "not-an-email"),
            ("contact.email", b => b["contact"]!["email"] = new string('e', 250) + "@x.co"),
            ("contact.phone", b => b["contact"]!["phone"] = "555-1234"),
            ("contact.phone", b => b["contact"]!["phone"] = "555-123-4567 ext 9"),
            ("contact.prefersEmail", b => b["contact"]!["prefersEmail"] = false),
            ("property.propertyType", b => b["property"]!["propertyType"] = "office"),
            ("property.addressLine1", b => b["property"]!["addressLine1"] = string.Empty),
            ("property.addressLine2", b => b["property"]!["addressLine2"] = new string('a', 181)),
            ("property.city", b => b["property"]!["city"] = string.Empty),
            ("property.state", b => b["property"]!["state"] = "ZZ"),
            ("property.postalCode", b => b["property"]!["postalCode"] = "7870"),
            ("property.accessInstructions", b => b["property"]!["accessInstructions"] = new string('i', 1001)),
            ("service.description", b => b["service"]!["description"] = string.Empty),
            ("service.description", b => b["service"]!["description"] = new string('d', 1001)),
            ("service.urgency", b => b["service"]!["urgency"] = "whenever"),
            ("service.serviceId", b => b["service"]!["notSure"] = true),
            ("service.serviceId", b => b["service"]!["serviceId"] = null),
            ("service.categoryId", b => b["service"]!["categoryId"] = null),
            ("availability.dateMode", b => b["availability"]!["dateMode"] = "later"),
            ("availability.preferredDate", b => b["availability"]!["dateMode"] = "date"),
            ("availability.preferredDate", b => Date(b, today.AddDays(-1))),
            ("availability.preferredDate", b => Date(b, today.AddDays(91))),
            ("availability.preferredDate", b => b["availability"]!["preferredDate"] = "2026-13-40"),
            ("availability.preferredDate", b => b["availability"]!["preferredDate"] = today.ToString("yyyy-MM-dd")),
            ("availability.timeWindow", b => b["availability"]!["timeWindow"] = "night"),
            ("availability.schedulingNotes", b => b["availability"]!["schedulingNotes"] = new string('n', 1001)),
            ("consent", b => b["consent"] = false),
        ];

        // BR-05: foreign, inactive, wrong-type, wrong-category and unknown ids give the same field error.
        (string Key, Action<JsonObject> Mutate)[] catalogCases =
        [
            ("service.categoryId", b => b["service"]!["categoryId"] = other.CategoryId.ToString()),
            ("service.categoryId", b => b["service"]!["categoryId"] = inactiveCategory.ToString()),
            ("service.categoryId", b => b["service"]!["categoryId"] = Guid.NewGuid().ToString()),
            ("service.serviceId", b => b["service"]!["serviceId"] = other.ServiceId.ToString()),
            ("service.serviceId", b => b["service"]!["serviceId"] = inactiveService.ToString()),
            ("service.serviceId", b => b["service"]!["serviceId"] = product.ToString()),
            ("service.serviceId", b => b["service"]!["serviceId"] = electricalService.ToString()),
            ("service.serviceId", b => b["service"]!["serviceId"] = Guid.NewGuid().ToString()),
            ("service.serviceId", b =>
            {
                b["service"]!["categoryId"] = inactiveCategory.ToString();
                b["service"]!["serviceId"] = inactiveCategoryService.ToString();
            }),
        ];

        await using var host = PublicRequestHost.Create(database);
        var before = await database.SnapshotAsync(org.Id);
        var otherBefore = await database.SnapshotAsync(other.Id);
        var failures = new List<string>();
        var catalogMessages = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var (key, mutate, isCatalog) in cases.Select(c => (c.Key, c.Mutate, false))
                     .Concat(catalogCases.Select(c => (c.Key, c.Mutate, true))))
        {
            var body = PublicRequestSeed.ValidBody(org);
            mutate(body);

            var response = await host.PostAsync(org.Slug, body);
            var problem = await PublicRequestSeed.ReadAsync(response);
            var message = PublicRequestSeed.ErrorOf(problem, key);

            if (response.StatusCode != HttpStatusCode.BadRequest || message is null)
            {
                failures.Add($"{key}: {(int)response.StatusCode} {body.ToJsonString()[..40]}");
            }
            else if (isCatalog)
            {
                catalogMessages.TryAdd(key, []);
                catalogMessages[key].Add(message);
            }
        }

        Assert.Empty(failures);
        Assert.All(catalogMessages.Values, messages => Assert.Single(messages));
        Assert.Equal(before, await database.SnapshotAsync(org.Id));
        Assert.Equal(otherBefore, await database.SnapshotAsync(other.Id));
        Assert.Empty(host.Sender.Messages);

        // Positive control at the boundary: today + 90 days is accepted and stored in the organization time zone.
        var valid = PublicRequestSeed.ValidBody(org);
        valid["availability"]!["dateMode"] = "date";
        valid["availability"]!["preferredDate"] = today.AddDays(90).ToString("yyyy-MM-dd");
        valid["availability"]!["timeWindow"] = "afternoon";
        var accepted = await host.PostAsync(org.Slug, valid);

        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);

        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");
        var noon = today.AddDays(90).ToDateTime(new TimeOnly(12, 0));
        var expectedStart = new DateTimeOffset(noon, zone.GetUtcOffset(noon));
        Assert.Equal(
            expectedStart.UtcDateTime,
            await database.ScalarAsync<DateTime>(
                "SELECT preferred_start FROM service_requests WHERE organization_id = @o", ("o", org.Id)));
    }

    // AC-15 (rejected content), AC-16: attachment content and limits; nothing persisted; then 413.
    [Fact]
    public async Task Submit_InvalidAttachmentsAndOversizeBody_Return400Or413AndPersistNothing()
    {
        var org = await database.SeedPublicOrgAsync();
        var text = Encoding.UTF8.GetBytes("plain text pretending to be an image");
        var valid = PublicRequestSeed.Jpeg();

        (string Key, (string, byte[])[] Files)[] cases =
        [
            ("attachments[0]", [("notes.jpg", text)]),
            ("attachments[0]", [("scan.pdf", PublicRequestSeed.Png())]),
            ("attachments[0]", [("empty.png", [])]),
            ("attachments[1]", [("ok.jpg", valid), ("bad.png", text)]),
            ("attachments[0]", [("noextension", valid)]),
            ("attachments", Enumerable.Range(0, 6).Select(i => ($"f{i}.jpg", valid)).ToArray()),
            ("attachments[0]", [("big.pdf", PublicRequestSeed.Pdf((10 * 1024 * 1024) + 1))]),
            ("attachments", Enumerable.Range(0, 3).Select(i => ($"part{i}.pdf", PublicRequestSeed.Pdf(8_738_134))).ToArray()),
        ];

        await using var host = PublicRequestHost.Create(database);
        var before = await database.SnapshotAsync(org.Id);
        var failures = new List<string>();

        foreach (var (key, files) in cases)
        {
            var response = await host.PostAsync(org.Slug, PublicRequestSeed.ValidBody(org), files);
            var message = response.StatusCode == HttpStatusCode.BadRequest
                ? PublicRequestSeed.ErrorOf(await PublicRequestSeed.ReadAsync(response), key)
                : null;

            if (message is null)
            {
                failures.Add($"{key}: {(int)response.StatusCode} with {files.Length} file(s)");
            }
        }

        Assert.Empty(failures);
        Assert.Equal(before, await database.SnapshotAsync(org.Id));
        Assert.Empty(host.Sender.Messages);

        // Boundary: exactly 10 MB is accepted (BR-13) and stored with its full size.
        var exact = PublicRequestSeed.Pdf(10 * 1024 * 1024);
        var accepted = await host.PostAsync(org.Slug, PublicRequestSeed.ValidBody(org), [("exact.pdf", exact)]);

        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        Assert.Equal(10_485_760L, await database.ScalarAsync<long>(
            "SELECT size_bytes FROM request_attachments WHERE organization_id = @o", ("o", org.Id)));

        // BR-18: a body over 26 MB is rejected by the real server before the form is read.
        await using var kestrel = FieldOpsApiFactory.Create(connectionString: database.ConnectionString);
        kestrel.UseKestrel(0);
        kestrel.StartServer();
        using var client = kestrel.CreateClient();

        var raw = await SendHeadersOnlyAsync(client.BaseAddress!, org.Slug, 27_262_976L + 1);

        Assert.StartsWith("HTTP/1.1 413", raw, StringComparison.Ordinal);
        Assert.Contains("application/problem+json", raw, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1L, await database.CountAsync("service_requests", org.Id));
    }

    private static void Date(JsonObject body, DateOnly date)
    {
        body["availability"]!["dateMode"] = "date";
        body["availability"]!["preferredDate"] = date.ToString("yyyy-MM-dd");
    }

    private async Task<string> SubmitAsync(
        PublicRequestHost host, PublicOrg org, string email, string propertyType = "home")
    {
        var body = PublicRequestSeed.ValidBody(org, email);
        body["property"]!["propertyType"] = propertyType;

        var response = await host.PostAsync(org.Slug, body);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await PublicRequestSeed.ReadAsync(response);
        Assert.Equal(["requestNumber"], created.AsObject().Select(p => p.Key));

        var number = created["requestNumber"]!.GetValue<string>();
        Assert.Matches(RequestNumberPattern(), number);

        return number;
    }

    private Task<Guid> RequestCustomer(PublicOrg org, string requestNumber) =>
        database.ScalarAsync<Guid>(
            "SELECT customer_id FROM service_requests WHERE organization_id = @o AND request_number = @n",
            ("o", org.Id),
            ("n", long.Parse(requestNumber[4..], System.Globalization.CultureInfo.InvariantCulture)));

    // column and table are test-controlled constants, never user input.
    private Task<string> Text(string column, string table, Guid id) =>
        database.ScalarAsync<string>($"SELECT {column} FROM {table} WHERE id = @i", ("i", id));

    private Task<bool> IsNull(string column, string table, Guid id) =>
        database.ScalarAsync<bool>($"SELECT {column} IS NULL FROM {table} WHERE id = @i", ("i", id));

    private static async Task<string> SendHeadersOnlyAsync(Uri baseAddress, string slug, long contentLength)
    {
        using var tcp = new System.Net.Sockets.TcpClient();
        await tcp.ConnectAsync(baseAddress.Host, baseAddress.Port);
        await using var stream = tcp.GetStream();

        var head =
            $"POST /public/organizations/{slug}/service-requests HTTP/1.1\r\nHost: {baseAddress.Authority}\r\n"
            + $"{TestClientIpStartupFilter.HeaderName}: {SessionApi.NewClientIp()}\r\n"
            + $"Content-Type: multipart/form-data; boundary=fieldopsboundary\r\nContent-Length: {contentLength}\r\n"
            + "Connection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head));

        using var reader = new StreamReader(stream, Encoding.UTF8);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        return await reader.ReadToEndAsync(timeout.Token);
    }

    [GeneratedRegex("^REQ-[0-9]+$")]
    private static partial Regex RequestNumberPattern();
}
