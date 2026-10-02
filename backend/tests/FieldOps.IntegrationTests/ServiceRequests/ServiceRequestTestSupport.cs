using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.Api;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.Customers;
using FieldOps.IntegrationTests.PasswordResets;
using FieldOps.IntegrationTests.PublicRequests;
using FieldOps.IntegrationTests.Sessions;
using FieldOps.IntegrationTests.Team;
using FieldOps.IntegrationTests.Users;

namespace FieldOps.IntegrationTests.ServiceRequests;

/// <summary>One API host over the shared container with a recording email port and log capture.</summary>
public sealed class RequestsHost : IAsyncDisposable
{
    private readonly PublicRequestHost _inner;

    private RequestsHost(PublicRequestHost inner) => _inner = inner;

    public HttpClient Client => _inner.Client;

    public RecordingEmailSender Sender => _inner.Sender;

    public CapturingLoggerProvider Logs => _inner.Logs!;

    public static RequestsHost Create(CompanySettingsDatabaseFixture database) =>
        new(PublicRequestHost.Create(database, captureLogs: true));

    /// <summary>Seeds a member of the role (optionally limited to branches) and returns a signed-in cookie.</summary>
    public async Task<(string Cookie, SeededMember Member)> SignInAsync(
        CompanySettingsDatabaseFixture database,
        Guid organizationId,
        short roleId,
        string first = "Sam",
        string last = "Staff",
        params Guid[] limitedToBranches)
    {
        var member = await database.SeedMemberAsync(
            organizationId, roleId, first, last, isAllBranches: limitedToBranches.Length == 0);

        foreach (var branchId in limitedToBranches)
        {
            await database.LinkMembershipToBranchAsync(member.MembershipId, branchId);
        }

        return (await CompanySettingsApi.SignInCookieAsync(Client, member.Email), member);
    }

    public Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? cookie, JsonObject? body = null) =>
        CompanySettingsApi.SendRawAsync(Client, method, path, body?.ToJsonString(), cookie);

    public Task<HttpResponseMessage> UploadAsync(
        string path, string? cookie, params (string FileName, byte[] Content)[] files)
    {
        var form = new MultipartFormDataContent();

        foreach (var (fileName, bytes) in files)
        {
            // The declared type is deliberately wrong: it is never trusted.
            var part = new ByteArrayContent(bytes);
            part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(part, "attachments", fileName);
        }

        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = form };

        if (cookie is not null)
        {
            request.Headers.Add("Cookie", cookie);
        }

        request.Headers.Add(TestClientIpStartupFilter.HeaderName, SessionApi.NewClientIp());

        return Client.SendAsync(request);
    }

    public static async Task<JsonNode> ReadAsync(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!;

    public ValueTask DisposeAsync() => _inner.DisposeAsync();
}

/// <summary>An organization with two branches, a category with a service and one customer in branch A.</summary>
public sealed record RequestWorld(
    Guid Org,
    Guid BranchA,
    Guid BranchB,
    Guid Category,
    Guid Service,
    Guid Customer,
    Guid Contact,
    Guid Property);

public static class ServiceRequestSeed
{
    public static async Task<RequestWorld> SeedWorldAsync(
        this CompanySettingsDatabaseFixture db, string timezone = "UTC", string? phone = "+1 555 010 0100")
    {
        var org = await db.SeedOrganizationAsync($"Acme {Guid.NewGuid():N}"[..20]);
        await db.ExecuteAsync(
            "UPDATE organizations SET timezone = @tz, phone = @phone WHERE id = @id",
            ("tz", timezone),
            ("phone", phone),
            ("id", org));

        var branchA = await db.SeedBranchAsync(org, "Alpha Branch", isMain: true);
        var branchB = await db.SeedBranchAsync(org, "Bravo Branch");
        var category = await db.SeedCategoryAsync(org, "Plumbing");
        var service = await db.SeedServiceAsync(org, category, "Drain cleaning");
        var customer = await db.SeedCustomerAsync(org, branchA.Id, "Carla Customer", email: "carla@example.com", phone: "5551234567");
        var contact = (await db.QueryGuidsAsync("SELECT id FROM customer_contacts WHERE customer_id = @c", ("c", customer))).Single();
        var property = (await db.QueryGuidsAsync("SELECT id FROM properties WHERE customer_id = @c", ("c", customer))).Single();

        return new RequestWorld(org, branchA.Id, branchB.Id, category, service, customer, contact, property);
    }

    /// <summary>Inserts a request directly (bypassing HTTP) and returns its id and number.</summary>
    public static async Task<(Guid Id, long Number)> SeedRequestAsync(
        this CompanySettingsDatabaseFixture db,
        RequestWorld world,
        string status = "new",
        string urgency = "standard",
        string source = "public_form",
        Guid? branch = null,
        DateTimeOffset? createdAt = null,
        bool linkCustomer = true,
        string? guestName = "Guest Person",
        string? guestEmail = "guest@example.com",
        Guid? assignee = null,
        string description = "Leaking pipe under the sink",
        string? availability = "{\"dateMode\":\"asap\",\"preferredDate\":null,\"timeWindow\":\"any\",\"schedulingNotes\":null}",
        bool withHistory = true,
        bool withService = true,
        Guid? userForHistory = null)
    {
        var id = Guid.NewGuid();
        var created = (createdAt ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var number = await db.ScalarAsync<long>(
            "SELECT COALESCE(MAX(request_number), 0) + 1 FROM service_requests WHERE organization_id = @o", ("o", world.Org));

        // status and source are test-controlled constants, never user input.
        await db.ExecuteAsync(
            $"""
            INSERT INTO service_requests (id, organization_id, branch_id, request_number, customer_id, contact_id, property_id, category_id, catalog_item_id,
                guest_name, guest_email, guest_phone, service_address, description, status, source, assigned_dispatcher_user_id, urgency,
                availability_preferences, created_at, updated_at)
            VALUES (@id, @org, @branch, @number, @customer, @contact, @property, @category, @item,
                @gname, @gemail, '555 000 1111', CAST(@address AS jsonb),
                @desc, CAST('{status}' AS request_status), '{source}', @assignee, '{urgency}', CAST(@avail AS jsonb), @created, @created)
            """,
            ("id", id),
            ("org", world.Org),
            ("branch", branch),
            ("number", number),
            ("customer", linkCustomer ? world.Customer : null),
            ("contact", linkCustomer ? world.Contact : null),
            ("property", linkCustomer ? world.Property : null),
            ("category", world.Category),
            ("item", withService ? world.Service : null),
            ("gname", guestName),
            ("gemail", guestEmail),
            ("desc", description),
            ("address", "{\"line1\":\"1 Seed St\",\"city\":\"Austin\",\"state\":\"TX\",\"postalCode\":\"78701\",\"countryCode\":\"US\",\"propertyType\":\"home\"}"),
            ("assignee", assignee),
            ("avail", availability),
            ("created", created));

        if (withHistory)
        {
            await db.SeedHistoryAsync(world.Org, id, null, "new", created, userForHistory);

            if (status != "new")
            {
                await db.SeedHistoryAsync(world.Org, id, "new", status, created.AddSeconds(1), userForHistory);
            }
        }

        return (id, number);
    }

    public static Task SeedHistoryAsync(
        this CompanySettingsDatabaseFixture db, Guid org, Guid request, string? from, string to, DateTimeOffset at, Guid? user = null) =>
        db.ExecuteAsync(
            """
            INSERT INTO request_status_history (organization_id, request_id, from_status, to_status, changed_by_user_id, changed_at)
            VALUES (@org, @req, CAST(@from AS request_status), CAST(@to AS request_status), @user, @at)
            """,
            ("org", org),
            ("req", request),
            ("from", from),
            ("to", to),
            ("user", user),
            ("at", at.ToUniversalTime()));

    public static Task SeedMessageAsync(
        this CompanySettingsDatabaseFixture db,
        Guid org,
        Guid request,
        string visibility,
        string body,
        DateTimeOffset at,
        Guid? authorUser = null,
        Guid? authorContact = null) =>
        db.ExecuteAsync(
            """
            INSERT INTO request_messages (organization_id, request_id, author_user_id, author_contact_id, visibility, body, created_at)
            VALUES (@org, @req, @user, @contact, CAST(@vis AS message_visibility), @body, @at)
            """,
            ("org", org),
            ("req", request),
            ("user", authorUser),
            ("contact", authorContact),
            ("vis", visibility),
            ("body", body),
            ("at", at.ToUniversalTime()));

    public static async Task<Guid> SeedAssessmentAsync(
        this CompanySettingsDatabaseFixture db,
        Guid org,
        Guid request,
        DateTimeOffset start,
        DateTimeOffset end,
        Guid createdBy,
        Guid? technician = null,
        string status = "scheduled")
    {
        var id = Guid.NewGuid();

        // status is a test-controlled constant, never user input.
        await db.ExecuteAsync(
            $"""
            INSERT INTO assessments (id, organization_id, request_id, technician_id, scheduled_start, scheduled_end, status, created_by_user_id)
            VALUES (@id, @org, @req, @tech, @start, @end, CAST('{status}' AS assessment_status), @user)
            """,
            ("id", id),
            ("org", org),
            ("req", request),
            ("tech", technician),
            ("start", start.ToUniversalTime()),
            ("end", end.ToUniversalTime()),
            ("user", createdBy));

        return id;
    }

    public static async Task<Guid> SeedAttachmentAsync(
        this CompanySettingsDatabaseFixture db, Guid org, Guid request, string fileName, string mimeType, byte[] content)
    {
        var id = Guid.NewGuid();

        await db.ExecuteAsync(
            """
            INSERT INTO request_attachments (id, organization_id, request_id, file_name, content, mime_type, size_bytes)
            VALUES (@id, @org, @req, @name, @content, @mime, @size)
            """,
            ("id", id),
            ("org", org),
            ("req", request),
            ("name", fileName),
            ("content", content),
            ("mime", mimeType),
            ("size", (long)content.Length));

        return id;
    }

    public static Task<string> StatusOfAsync(this CompanySettingsDatabaseFixture db, Guid request) =>
        db.ScalarAsync<string>("SELECT status::text FROM service_requests WHERE id = @id", ("id", request));

    // table is a test-controlled constant, never user input.
    public static Task<long> CountAsync(this CompanySettingsDatabaseFixture db, string table, Guid request)
    {
        var sql = table == "audit_logs"
            ? "SELECT COUNT(*) FROM audit_logs WHERE entity_id = @r"
            : $"SELECT COUNT(*) FROM {table} WHERE request_id = @r";

        return db.ScalarAsync<long>(sql, ("r", request));
    }

    public static Task<long> AuditCountAsync(this CompanySettingsDatabaseFixture db, Guid request, string action) =>
        db.ScalarAsync<long>(
            "SELECT COUNT(*) FROM audit_logs WHERE entity_id = @r AND action = @a", ("r", request), ("a", action));

    /// <summary>The audit rows of a request as one lowercase text blob, for PII leak assertions.</summary>
    public static async Task<string> AuditTextAsync(this CompanySettingsDatabaseFixture db, Guid request) =>
        await db.ScalarAsync<string>(
            """
            SELECT COALESCE(string_agg(action || ' ' || COALESCE(before_data::text, '') || ' ' || COALESCE(after_data::text, '') || ' ' || metadata::text, E'\n'), '')
            FROM audit_logs WHERE entity_id = @r
            """,
            ("r", request));

    public static string Local(DateTimeOffset utc, string pattern = "yyyy-MM-dd'T'HH:mm") =>
        utc.ToString(pattern, System.Globalization.CultureInfo.InvariantCulture);

    public static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);
}
