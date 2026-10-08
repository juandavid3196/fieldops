using System.Text;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.Api;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.Sessions;
using FieldOps.IntegrationTests.Users;

namespace FieldOps.IntegrationTests.Customers;

/// <summary>One API host over the shared container, with helpers to sign members in and call the customer endpoints.</summary>
public sealed class CustomerHost : IAsyncDisposable
{
    private readonly SessionTestHost _host;

    private CustomerHost(SessionTestHost host) => _host = host;

    public HttpClient Client => _host.Client;

    public static CustomerHost Create(CompanySettingsDatabaseFixture database) =>
        new(SessionTestHost.Create(database.ConnectionString));

    /// <summary>Seeds a member of the role (optionally limited to branches) and returns a signed-in cookie.</summary>
    public async Task<string> CookieAsync(
        CompanySettingsDatabaseFixture database, Guid organizationId, short roleId, params Guid[] limitedToBranches)
    {
        var member = await database.SeedMemberAsync(
            organizationId, roleId, "Cus", "Tomer", isAllBranches: limitedToBranches.Length == 0);

        foreach (var branchId in limitedToBranches)
        {
            await database.LinkMembershipToBranchAsync(member.MembershipId, branchId);
        }

        return await CompanySettingsApi.SignInCookieAsync(Client, member.Email);
    }

    public Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? cookie, JsonObject? body = null) =>
        CompanySettingsApi.SendRawAsync(Client, method, path, body?.ToJsonString(), cookie);

    public static async Task<JsonNode> ReadAsync(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!;

    public Task<HttpResponseMessage> SendCsvAsync(string path, string? cookie, byte[] bytes, string fileName = "customers.csv")
    {
        var part = new ByteArrayContent(bytes);
        part.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/csv");

        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new MultipartFormDataContent { { part, "file", fileName } },
        };

        if (cookie is not null)
        {
            request.Headers.Add("Cookie", cookie);
        }

        request.Headers.Add(TestClientIpStartupFilter.HeaderName, SessionApi.NewClientIp());

        return Client.SendAsync(request);
    }

    public ValueTask DisposeAsync() => _host.DisposeAsync();
}

public static class CustomerSeed
{
    public const string CsvHeader =
        "type,company_name,first_name,last_name,title,email,mobile_phone,prefers_email,prefers_sms,address_line1,city,state,zip_code,branch_code,tags,service_instructions,internal_note";

    public static JsonObject Body(
        Guid branchId,
        string type = "residential",
        string first = "Ada",
        string last = "Lovelace",
        string email = "ada@example.com",
        string? phone = null,
        string? companyName = null,
        string[]? tagIds = null) =>
        new()
        {
            ["type"] = type,
            ["companyName"] = companyName,
            ["contact"] = new JsonObject
            {
                ["firstName"] = first,
                ["lastName"] = last,
                ["title"] = type == "commercial" ? "Manager" : null,
                ["email"] = email,
                ["phone"] = phone,
                ["prefersEmail"] = true,
                ["prefersSms"] = false,
            },
            ["property"] = new JsonObject
            {
                ["addressLine1"] = "1 Main St",
                ["city"] = "Austin",
                ["stateRegion"] = "tx",
                ["postalCode"] = "78701",
            },
            ["serviceInstructions"] = "Ring twice",
            ["internalNote"] = "Pays late",
            ["branchId"] = branchId.ToString(),
            ["tagIds"] = new JsonArray([.. (tagIds ?? []).Select(id => (JsonNode?)JsonValue.Create(id))]),
        };

    public static async Task<Guid> SeedCustomerAsync(
        this CompanySettingsDatabaseFixture db,
        Guid organizationId,
        Guid branchId,
        string displayName,
        string type = "person",
        string? email = null,
        string? phone = null,
        bool active = true,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? updatedAt = null)
    {
        var id = Guid.NewGuid();
        var created = createdAt ?? DateTimeOffset.UtcNow;

        // type is a test-controlled constant, never user input.
        await db.ExecuteAsync(
            $"""
            INSERT INTO customers (id, organization_id, type, branch_id, display_name, primary_email, primary_phone, is_active, created_at, updated_at)
            VALUES (@id, @org, '{type}', @branch, @name, @email, @phone, @active, @created, @updated);
            INSERT INTO customer_contacts (id, organization_id, customer_id, first_name, last_name, email, phone, is_primary, is_active)
            VALUES (gen_random_uuid(), @org, @id, 'Pat', 'Contact', @email, @phone, true, true);
            INSERT INTO properties (id, organization_id, customer_id, branch_id, name, address_line1, city, country_code, is_primary)
            VALUES (gen_random_uuid(), @org, @id, @branch, 'Primary property', '1 Seed St', 'Austin', 'US', true);
            """,
            ("id", id),
            ("org", organizationId),
            ("branch", branchId),
            ("name", displayName),
            ("email", email),
            ("phone", phone),
            ("active", active),
            ("created", created),
            ("updated", updatedAt ?? created));

        return id;
    }

    public static async Task<Guid> SeedTagAsync(this CompanySettingsDatabaseFixture db, Guid organizationId, string name)
    {
        var id = Guid.NewGuid();

        await db.ExecuteAsync(
            "INSERT INTO customer_tags (id, organization_id, name, normalized_name) VALUES (@id, @org, @name, @normalized)",
            ("id", id),
            ("org", organizationId),
            ("name", name),
            ("normalized", name.ToLowerInvariant()));

        return id;
    }

    public static Task AssignTagAsync(this CompanySettingsDatabaseFixture db, Guid organizationId, Guid customerId, Guid tagId) =>
        db.ExecuteAsync(
            "INSERT INTO customer_tag_assignments (organization_id, customer_id, tag_id) VALUES (@org, @customer, @tag)",
            ("org", organizationId),
            ("customer", customerId),
            ("tag", tagId));

    /// <summary>
    /// Seeds a work order of the customer with an optional visit. Foreign keys to unrelated parents
    /// (quote version, property) are bypassed for this throwaway container only.
    /// </summary>
    public static async Task<Guid> SeedWorkAsync(
        this CompanySettingsDatabaseFixture db,
        Guid organizationId,
        Guid userId,
        Guid branchId,
        Guid customerId,
        string workOrderStatus,
        string? scope = null,
        string? visitStatus = null,
        DateTimeOffset? scheduledStart = null,
        DateTimeOffset? scheduledEnd = null,
        DateTimeOffset? completedAt = null)
    {
        var workOrderId = Guid.NewGuid();

        // Statuses are test-controlled constants, never user input.
        await db.ExecuteAsync(
            $"""
            SET session_replication_role = replica;
            INSERT INTO work_orders (id, organization_id, branch_id, work_order_number, quote_version_id, customer_id, property_id, status, scope_snapshot, created_by_user_id)
            VALUES (@wo, @org, @branch, @number, gen_random_uuid(), @customer, gen_random_uuid(), '{workOrderStatus}', @scope, @user);
            """,
            ("wo", workOrderId),
            ("org", organizationId),
            ("branch", branchId),
            ("number", Random.Shared.NextInt64(1, 1_000_000_000)),
            ("customer", customerId),
            ("scope", scope ?? "Scope"),
            ("user", userId));

        if (visitStatus is not null)
        {
            await db.ExecuteAsync(
                $"""
                SET session_replication_role = replica;
                INSERT INTO visits (id, organization_id, work_order_id, visit_number, status, scheduled_start, scheduled_end, actual_completed_at)
                VALUES (gen_random_uuid(), @org, @wo, 1, '{visitStatus}', @start, @end, @completed);
                """,
                ("org", organizationId),
                ("wo", workOrderId),
                ("start", scheduledStart),
                ("end", scheduledEnd),
                ("completed", completedAt));
        }

        return workOrderId;
    }

    public static Task SeedInvoiceAsync(
        this CompanySettingsDatabaseFixture db,
        Guid organizationId,
        Guid userId,
        Guid branchId,
        Guid customerId,
        string status,
        decimal balance) =>
        db.ExecuteAsync(
            $"""
            SET session_replication_role = replica;
            INSERT INTO invoices (id, organization_id, branch_id, invoice_number, work_order_id, customer_id, status, currency, subtotal, tax_total, total, amount_paid, balance_due, created_by_user_id, customer_snapshot)
            VALUES (gen_random_uuid(), @org, @branch, @number, gen_random_uuid(), @customer, '{status}', 'USD', @balance, 0, @balance, 0, @balance, @user,
                CASE WHEN '{status}' IN ('draft', 'void') THEN NULL
                     ELSE CAST(@snap AS jsonb) END);
            """,
            ("org", organizationId),
            ("branch", branchId),
            ("number", Random.Shared.NextInt64(1, 1_000_000_000)),
            ("customer", customerId),
            ("balance", balance),
            ("snap", SeedInvoiceSnapshot.Json),
            ("user", userId));

    public static Task<long> CountCustomersAsync(this CompanySettingsDatabaseFixture db, Guid organizationId) =>
        db.ScalarAsync<long>("SELECT COUNT(*) FROM customers WHERE organization_id = @o", ("o", organizationId));

    // table is a test-controlled constant, never user input.
    public static Task<long> CountRowsAsync(this CompanySettingsDatabaseFixture db, string table, Guid organizationId) =>
        db.ScalarAsync<long>($"SELECT COUNT(*) FROM {table} WHERE organization_id = @o", ("o", organizationId));

    public static Task<long> CountCustomerAuditAsync(
        this CompanySettingsDatabaseFixture db, Guid organizationId, string? action = null) =>
        db.ScalarAsync<long>(
            "SELECT COUNT(*) FROM audit_logs WHERE organization_id = @o AND entity_type LIKE 'customer%' AND (@a::text IS NULL OR action = @a)",
            ("o", organizationId),
            ("a", action));

    public static string[] Names(JsonNode list) => UsersSeed.Items(list, "displayName");

    public static string Error(JsonNode problem, string key) =>
        problem["errors"]![key]!.AsArray().Single()!.GetValue<string>();

    public static string CsvOf(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    public static byte[] Utf8(string text) => new UTF8Encoding(false).GetBytes(text);
}

/// <summary>Minimal valid frozen snapshot that satisfies ck_invoices_customer_snapshot for seeded non-draft invoices.</summary>
public static class SeedInvoiceSnapshot
{
    public const string Json =
        """{"billTo":{"name":"Seed","email":null,"phone":null,"addressLines":[]},"serviceAddress":null,"completionNote":null}""";
}
