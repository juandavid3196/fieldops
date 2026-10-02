using System.Net;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.Api;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.Customers;
using FieldOps.IntegrationTests.PublicRequests;

namespace FieldOps.IntegrationTests.ServiceRequests;

/// <summary>Staff attachment upload and authenticated download, and internal request creation: AC-20, AC-21, AC-23.</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class ServiceRequestAttachmentAndCreationTests(CompanySettingsDatabaseFixture database)
{
    private static string Error(JsonNode problem, string key) =>
        problem["errors"]![key]!.AsArray().Single()!.GetValue<string>();

    // AC-20: upload limits and content validation (managers only, open requests), download headers and 404 for foreign ids.
    [Fact]
    public async Task Attachments_ValidateUploadsAndServeAuthenticatedDownloads()
    {
        var world = await database.SeedWorldAsync();
        await using var host = RequestsHost.Create(database);
        var (owner, ownerMember) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var (viewer, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.ViewerRoleId);

        var request = await database.SeedRequestAsync(world);
        var jpeg = PublicRequestSeed.Jpeg(300);
        var pdf = PublicRequestSeed.Pdf(200);
        var image = await database.SeedAttachmentAsync(world.Org, request.Id, "seed-1.jpg", "image/jpeg", jpeg);
        var document = await database.SeedAttachmentAsync(world.Org, request.Id, "seed-2.pdf", "application/pdf", pdf);
        await database.SeedAttachmentAsync(world.Org, request.Id, "seed-3.png", "image/png", PublicRequestSeed.Png(100));
        var path = $"/service-requests/{request.Id}/attachments";

        var uploaded = await host.UploadAsync(path, owner, ("new-photo.jpg", PublicRequestSeed.Jpeg(120)), ("new-doc.pdf", PublicRequestSeed.Pdf(90)));
        Assert.Equal(HttpStatusCode.OK, uploaded.StatusCode);
        Assert.Equal(5, (await RequestsHost.ReadAsync(uploaded))["attachments"]!.AsArray().Count);
        Assert.Equal(2, await database.ScalarAsync<long>(
            "SELECT COUNT(*) FROM request_attachments WHERE request_id = @r AND uploaded_by_user_id = @u", ("r", request.Id), ("u", ownerMember.UserId)));
        var (_, _, metadata) = await database.GetLatestAuditAsync(world.Org, "service_request.attachments_added");
        Assert.Equal(2, JsonNode.Parse(metadata!)!["count"]!.GetValue<int>());
        Assert.DoesNotContain("new-photo", await database.AuditTextAsync(request.Id));

        var sixth = await host.UploadAsync(path, owner, ("sixth.png", PublicRequestSeed.Png(80)));
        Assert.Equal(HttpStatusCode.BadRequest, sixth.StatusCode);
        Assert.Equal("A request can have up to 5 files.", Error(await RequestsHost.ReadAsync(sixth), "attachments"));
        Assert.Equal(5, await database.CountAsync("request_attachments", request.Id));

        var fresh = await database.SeedRequestAsync(world);
        var freshPath = $"/service-requests/{fresh.Id}/attachments";
        var renamed = await host.UploadAsync(freshPath, owner, ("notes.jpg", ServiceRequestSeed.Bytes("this is plain text")));
        Assert.Equal(HttpStatusCode.BadRequest, renamed.StatusCode);
        Assert.NotNull(Error(await RequestsHost.ReadAsync(renamed), "attachments[0]"));
        Assert.Equal(HttpStatusCode.BadRequest, (await host.UploadAsync(freshPath, owner, ("empty.png", []))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.UploadAsync(freshPath, owner, ("wrong.png", PublicRequestSeed.Jpeg(60)))).StatusCode);
        var many = Enumerable.Range(0, 6).Select(index => ($"file-{index}.jpg", PublicRequestSeed.Jpeg(50))).ToArray();
        var tooMany = await host.UploadAsync(freshPath, owner, many);
        Assert.Equal("A request can have up to 5 files.", Error(await RequestsHost.ReadAsync(tooMany), "attachments"));
        Assert.Equal(HttpStatusCode.BadRequest, (await host.UploadAsync(freshPath, owner)).StatusCode);
        Assert.Equal(0, await database.CountAsync("request_attachments", fresh.Id));
        Assert.Equal(0, await database.CountAsync("audit_logs", fresh.Id));

        var closed = await database.SeedRequestAsync(world, status: "cancelled");
        Assert.Equal(HttpStatusCode.Conflict, (await host.UploadAsync($"/service-requests/{closed.Id}/attachments", owner, ("a.jpg", PublicRequestSeed.Jpeg(60)))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.UploadAsync(freshPath, viewer, ("a.jpg", PublicRequestSeed.Jpeg(60)))).StatusCode);

        // Over 26 MB the real server rejects the declared body before the form is read.
        await using (var kestrel = FieldOpsApiFactory.Create(connectionString: database.ConnectionString))
        {
            kestrel.UseKestrel(0);
            kestrel.StartServer();
            kestrel.ClientOptions.HandleCookies = false;
            kestrel.ClientOptions.AllowAutoRedirect = false;
            using var client = kestrel.CreateClient();
            var kestrelCookie = await CompanySettingsApi.SignInCookieAsync(client, ownerMember.Email);
            var raw = await SendHeadersOnlyAsync(
                client.BaseAddress!, freshPath, kestrelCookie, 27_262_976L + 1);

            Assert.StartsWith("HTTP/1.1 413", raw, StringComparison.Ordinal);
            Assert.Equal(0, await database.CountAsync("request_attachments", fresh.Id));
        }

        // Download: any read role, BR-16 headers, exact content; unknown or mismatched ids are 404.
        var imageResponse = await host.SendAsync(HttpMethod.Get, $"{path}/{image}", viewer);
        Assert.Equal(HttpStatusCode.OK, imageResponse.StatusCode);
        Assert.Equal("image/jpeg", imageResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal("inline", imageResponse.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("nosniff", imageResponse.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.True(imageResponse.Headers.CacheControl is { NoStore: true, Private: true });
        Assert.Equal(jpeg, await imageResponse.Content.ReadAsByteArrayAsync());

        var documentResponse = await host.SendAsync(HttpMethod.Get, $"{path}/{document}", viewer);
        Assert.Equal("application/pdf", documentResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", documentResponse.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal(pdf, await documentResponse.Content.ReadAsByteArrayAsync());

        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Get, $"{path}/{Guid.NewGuid()}", viewer)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Get, $"{freshPath}/{image}", viewer)).StatusCode);
    }

    // AC-21, AC-23: internal creation with eligibility of customer, contact, property and catalog ids, numbering, snapshots and no email.
    [Fact]
    public async Task InternalCreation_ValidatesIdsStoresSnapshotsAndConsumesNumbersOnlyOnSuccess()
    {
        var world = await database.SeedWorldAsync();
        var foreign = await database.SeedWorldAsync();
        var otherCategory = await database.SeedCategoryAsync(world.Org, "Electrical");
        var otherService = await database.SeedServiceAsync(world.Org, otherCategory, "Panel upgrade");
        var other = await database.SeedCustomerAsync(world.Org, world.BranchA, "Other Customer");
        var otherContact = (await database.QueryGuidsAsync("SELECT id FROM customer_contacts WHERE customer_id = @c", ("c", other))).Single();
        var otherProperty = (await database.QueryGuidsAsync("SELECT id FROM properties WHERE customer_id = @c", ("c", other))).Single();
        var bravo = await database.SeedCustomerAsync(world.Org, world.BranchB, "Bravo Customer");

        var branchBProperty = Guid.NewGuid();
        var noBranchProperty = Guid.NewGuid();
        await database.ExecuteAsync(
            """
            INSERT INTO properties (id, organization_id, customer_id, branch_id, name, address_line1, city, country_code, is_primary)
            VALUES (@b, @org, @c, @branchB, 'Bravo site', '2 Other St', 'Austin', 'US', false),
                   (@n, @org, @c, NULL, 'Open site', '3 Free St', 'Austin', 'US', false)
            """,
            ("b", branchBProperty),
            ("n", noBranchProperty),
            ("org", world.Org),
            ("c", world.Customer),
            ("branchB", world.BranchB));

        await using var host = RequestsHost.Create(database);
        var (owner, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var (dispatcher, dispatcherMember) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.DispatcherRoleId, "Dina", "Dispatcher", world.BranchA);

        var preferred = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(3);

        JsonObject Valid() =>
            new()
            {
                ["customerId"] = world.Customer,
                ["contactId"] = world.Contact,
                ["propertyId"] = world.Property,
                ["categoryId"] = world.Category,
                ["serviceId"] = world.Service,
                ["notSure"] = false,
                ["description"] = "Water heater is leaking badly",
                ["urgency"] = "urgent",
                ["hasActiveDamage"] = true,
                ["availability"] = new JsonObject
                {
                    ["dateMode"] = "date",
                    ["preferredDate"] = preferred.ToString("yyyy-MM-dd"),
                    ["timeWindow"] = "morning",
                    ["schedulingNotes"] = "Call before arriving",
                },
            };

        var created = await host.SendAsync(HttpMethod.Post, "/service-requests", dispatcher, Valid());
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var detail = await RequestsHost.ReadAsync(created);
        var id = Guid.Parse(detail["id"]!.GetValue<string>());
        Assert.Equal("REQ-1", detail["number"]!.GetValue<string>());
        Assert.Equal("new", detail["status"]!.GetValue<string>());
        Assert.Equal("internal", detail["source"]!.GetValue<string>());
        Assert.Equal("urgent", detail["urgency"]!.GetValue<string>());
        Assert.Equal("Alpha Branch", detail["branch"]!["name"]!.GetValue<string>());
        Assert.Equal("Pat Contact", detail["contact"]!["name"]!.GetValue<string>());
        Assert.Equal("1 Seed St", detail["serviceAddress"]!["line1"]!.GetValue<string>());
        Assert.Equal("home", detail["serviceAddress"]!["propertyType"]!.GetValue<string>());
        Assert.Equal("date", detail["availability"]!["dateMode"]!.GetValue<string>());
        Assert.True(detail["hasActiveDamage"]!.GetValue<bool>());
        Assert.Equal("Created by Dina Dispatcher", detail["activity"]![0]!["detail"]!.GetValue<string>());
        Assert.Equal("Pat Contact", await database.ScalarAsync<string>("SELECT guest_name FROM service_requests WHERE id = @r", ("r", id)));
        Assert.Equal("carla@example.com", await database.ScalarAsync<string>("SELECT guest_email FROM service_requests WHERE id = @r", ("r", id)));
        Assert.True(await database.ScalarAsync<bool>("SELECT consent_at IS NULL FROM service_requests WHERE id = @r", ("r", id)));
        Assert.Equal(
            new DateTimeOffset(preferred.Year, preferred.Month, preferred.Day, 8, 0, 0, TimeSpan.Zero),
            await database.ScalarAsync<DateTimeOffset>("SELECT preferred_start FROM service_requests WHERE id = @r", ("r", id)));
        Assert.Equal(dispatcherMember.UserId, await database.ScalarAsync<Guid>(
            "SELECT changed_by_user_id FROM request_status_history WHERE request_id = @r AND from_status IS NULL", ("r", id)));
        Assert.Equal(1, await database.CountAsync("request_status_history", id));
        Assert.Equal(1, await database.AuditCountAsync(id, "service_request.created"));
        Assert.Equal(1, await database.CountAsync("audit_logs", id));
        var (_, after, _) = await database.GetLatestAuditAsync(world.Org, "service_request.created");
        Assert.Equal("internal", JsonNode.Parse(after!)!["source"]!.GetValue<string>());
        Assert.Equal("REQ-1", JsonNode.Parse(after!)!["requestNumber"]!.GetValue<string>());
        var auditText = await database.AuditTextAsync(id);
        Assert.DoesNotContain("Water heater", auditText);
        Assert.DoesNotContain("Pat", auditText);
        Assert.DoesNotContain("carla", auditText);
        Assert.Empty(host.Sender.Messages);

        var newColumn = (await RequestsHost.ReadAsync(await host.SendAsync(HttpMethod.Get, "/service-requests/pipeline", dispatcher)))["columns"]![0]!["items"]!.AsArray();
        Assert.Contains(newColumn, item => item!["id"]!.GetValue<string>() == id.ToString());

        // A property without a branch leaves the request branchless; the next number is consecutive.
        var branchless = Valid();
        branchless["propertyId"] = noBranchProperty;
        var branchlessResponse = await host.SendAsync(HttpMethod.Post, "/service-requests", owner, branchless);
        Assert.Equal(HttpStatusCode.Created, branchlessResponse.StatusCode);
        var branchlessDetail = await RequestsHost.ReadAsync(branchlessResponse);
        Assert.Null(branchlessDetail["branch"]);
        Assert.Equal("REQ-2", branchlessDetail["number"]!.GetValue<string>());

        // Invalid ids and values: 400 on the field, nothing persisted and no number consumed.
        var rejected = new (string Key, Action<JsonObject> Change)[]
        {
            ("customerId", body => body["customerId"] = bravo),
            ("customerId", body => body["customerId"] = foreign.Customer),
            ("customerId", body => body["customerId"] = Guid.NewGuid()),
            ("contactId", body => body["contactId"] = otherContact),
            ("contactId", body => body["contactId"] = foreign.Contact),
            ("propertyId", body => body["propertyId"] = otherProperty),
            ("propertyId", body => body["propertyId"] = branchBProperty),
            ("categoryId", body => body["categoryId"] = foreign.Category),
            ("serviceId", body => body["serviceId"] = otherService),
            ("serviceId", body => body["notSure"] = true),
            ("description", body => body["description"] = "   "),
            ("urgency", body => body["urgency"] = "critical"),
            ("availability.dateMode", body => body["availability"]!["dateMode"] = "someday"),
            ("availability.preferredDate", body => body["availability"]!["preferredDate"] = preferred.AddDays(-10).ToString("yyyy-MM-dd")),
            ("availability.preferredDate", body => body["availability"]!["preferredDate"] = preferred.AddDays(100).ToString("yyyy-MM-dd")),
            ("availability.timeWindow", body => body["availability"]!["timeWindow"] = "midnight"),
        };

        foreach (var (key, change) in rejected)
        {
            var body = Valid();
            change(body);
            var response = await host.SendAsync(HttpMethod.Post, "/service-requests", dispatcher, body);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.NotNull(Error(await RequestsHost.ReadAsync(response), key));
        }

        var outOfBranch = Valid();
        outOfBranch["propertyId"] = branchBProperty;
        Assert.Equal(
            "Choose a property in a branch you have access to.",
            Error(await RequestsHost.ReadAsync(await host.SendAsync(HttpMethod.Post, "/service-requests", dispatcher, outOfBranch)), "propertyId"));
        Assert.Equal(2, await database.ScalarAsync<long>("SELECT COUNT(*) FROM service_requests WHERE organization_id = @o", ("o", world.Org)));
        Assert.Equal(3, await database.ScalarAsync<long>("SELECT next_request_number FROM organizations WHERE id = @o", ("o", world.Org)));
        Assert.Empty(host.Sender.Messages);

    }

    // The server rejects on the declared Content-Length before the body is read, so only the headers are sent.
    private static async Task<string> SendHeadersOnlyAsync(Uri baseAddress, string path, string cookie, long contentLength)
    {
        using var tcp = new System.Net.Sockets.TcpClient();
        await tcp.ConnectAsync(baseAddress.Host, baseAddress.Port);
        await using var stream = tcp.GetStream();

        var head =
            $"POST {path} HTTP/1.1\r\nHost: {baseAddress.Authority}\r\nCookie: {cookie}\r\n"
            + $"{TestClientIpStartupFilter.HeaderName}: {Sessions.SessionApi.NewClientIp()}\r\n"
            + $"Content-Type: multipart/form-data; boundary=fieldopsboundary\r\nContent-Length: {contentLength}\r\n"
            + "Connection: close\r\n\r\n";
        await stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes(head));

        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        return await reader.ReadToEndAsync(timeout.Token);
    }
}
