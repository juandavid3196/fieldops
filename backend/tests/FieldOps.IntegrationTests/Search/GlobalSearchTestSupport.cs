using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.ServiceRequests;

namespace FieldOps.IntegrationTests.Search;

/// <summary>Direct-SQL seeds of the global search tests: every row gets an explicit number, branch and updated_at.</summary>
internal static class GlobalSearchSeed
{
    public static async Task<Guid> CustomerAsync(
        this CompanySettingsDatabaseFixture db,
        Guid org,
        Guid branch,
        string name,
        string type = "person",
        string? legal = null,
        string? email = null,
        string? phone = null,
        string? notes = null,
        string? taxId = null,
        bool active = true,
        DateTimeOffset? updated = null)
    {
        var id = Guid.NewGuid();

        // type is a test-controlled constant, never user input.
        await db.ExecuteAsync(
            $"""
            INSERT INTO customers (id, organization_id, type, branch_id, display_name, legal_name, primary_email, primary_phone, notes, tax_id, is_active, updated_at)
            VALUES (@id, @org, CAST('{type}' AS customer_type), @branch, @name, @legal, @email, @phone, @notes, @tax, @active, @updated)
            """,
            ("id", id),
            ("org", org),
            ("branch", branch),
            ("name", name),
            ("legal", legal),
            ("email", email),
            ("phone", phone),
            ("notes", notes),
            ("tax", taxId),
            ("active", active),
            ("updated", (updated ?? DateTimeOffset.UtcNow).ToUniversalTime()));

        return id;
    }

    public static async Task<Guid> RequestAsync(
        this CompanySettingsDatabaseFixture db,
        Guid org,
        Guid? branch,
        long number,
        string? guest = null,
        Guid? customer = null,
        Guid? category = null,
        Guid? item = null,
        string status = "new",
        string description = "Routine description",
        DateTimeOffset? updated = null)
    {
        var id = Guid.NewGuid();

        // status is a test-controlled constant, never user input.
        await db.ExecuteAsync(
            $"""
            INSERT INTO service_requests (id, organization_id, branch_id, request_number, customer_id, category_id, catalog_item_id, guest_name, description, status, updated_at)
            VALUES (@id, @org, @branch, @number, @customer, @category, @item, @guest, @description, CAST('{status}' AS request_status), @updated)
            """,
            ("id", id),
            ("org", org),
            ("branch", branch),
            ("number", number),
            ("customer", customer),
            ("category", category),
            ("item", item),
            ("guest", guest),
            ("description", description),
            ("updated", (updated ?? DateTimeOffset.UtcNow).ToUniversalTime()));

        return id;
    }

    public static async Task<Guid> QuoteAsync(
        this CompanySettingsDatabaseFixture db,
        Guid org,
        Guid? branch,
        Guid request,
        Guid? customer,
        long number,
        Guid user,
        string status = "sent",
        DateTimeOffset? updated = null)
    {
        var id = Guid.NewGuid();

        // status is a test-controlled constant, never user input.
        await db.ExecuteAsync(
            $"""
            INSERT INTO quotes (id, organization_id, branch_id, request_id, customer_id, quote_number, status, current_version_no, created_by_user_id, updated_at)
            VALUES (@id, @org, @branch, @request, @customer, @number, CAST('{status}' AS quote_status), 0, @user, @updated)
            """,
            ("id", id),
            ("org", org),
            ("branch", branch),
            ("request", request),
            ("customer", customer),
            ("number", number),
            ("user", user),
            ("updated", (updated ?? DateTimeOffset.UtcNow).ToUniversalTime()));

        return id;
    }

    /// <summary>A work order; its quote version, property and category are not needed by the search, so the FKs are bypassed in the throwaway container.</summary>
    public static async Task<Guid> WorkOrderAsync(
        this CompanySettingsDatabaseFixture db,
        Guid org,
        Guid branch,
        Guid customer,
        long number,
        string title,
        Guid user,
        string status = "scheduled",
        DateTimeOffset? updated = null)
    {
        var id = Guid.NewGuid();

        // status is a test-controlled constant, never user input.
        await db.ExecuteAsync(
            $"""
            SET session_replication_role = replica;
            INSERT INTO work_orders (id, organization_id, branch_id, work_order_number, quote_version_id, customer_id, property_id, title, service_category_id,
                status, scope_snapshot, created_by_user_id, updated_at)
            VALUES (@id, @org, @branch, @number, gen_random_uuid(), @customer, gen_random_uuid(), @title, gen_random_uuid(),
                CAST('{status}' AS work_order_status), 'Scope', @user, @updated);
            SET session_replication_role = DEFAULT;
            """,
            ("id", id),
            ("org", org),
            ("branch", branch),
            ("number", number),
            ("customer", customer),
            ("title", title),
            ("user", user),
            ("updated", (updated ?? DateTimeOffset.UtcNow).ToUniversalTime()));

        return id;
    }

    public static async Task<Guid> InvoiceAsync(
        this CompanySettingsDatabaseFixture db,
        Guid org,
        Guid branch,
        Guid customer,
        long number,
        Guid user,
        decimal total = 100m,
        DateOnly? due = null,
        string status = "void",
        DateTimeOffset? updated = null)
    {
        var id = Guid.NewGuid();

        // status is a test-controlled constant, never user input.
        await db.ExecuteAsync(
            $"""
            SET session_replication_role = replica;
            INSERT INTO invoices (id, organization_id, branch_id, invoice_number, work_order_id, customer_id, status, due_date, currency, subtotal, tax_total, total,
                balance_due, created_by_user_id, customer_snapshot, updated_at)
            VALUES (@id, @org, @branch, @number, gen_random_uuid(), @customer, CAST('{status}' AS invoice_status), @due, 'USD', @total, 0, @total,
                @total, @user, CAST(@snapshot AS jsonb), @updated);
            SET session_replication_role = DEFAULT;
            """,
            ("id", id),
            ("org", org),
            ("branch", branch),
            ("number", number),
            ("customer", customer),
            ("due", due),
            ("total", total),
            ("user", user),
            ("snapshot", status is "draft" or "void" ? null : "{}"),
            ("updated", (updated ?? DateTimeOffset.UtcNow).ToUniversalTime()));

        return id;
    }

    /// <summary>The items of a response as (entityType, entityId) pairs, in response order.</summary>
    public static (string Type, Guid Id)[] Keys(JsonNode body) =>
        [.. body["items"]!.AsArray().Select(item => (item!["entityType"]!.GetValue<string>(), item["entityId"]!.GetValue<Guid>()))];

    public static async Task<JsonNode> SearchAsync(
        this RequestsHost host, string cookie, string query, string extra = "", System.Net.HttpStatusCode expected = System.Net.HttpStatusCode.OK)
    {
        var response = await host.SendAsync(HttpMethod.Get, $"/search/global?q={Uri.EscapeDataString(query)}{extra}", cookie);
        var text = await response.Content.ReadAsStringAsync();

        Assert.True(expected == response.StatusCode, $"Expected {expected} but got {(int)response.StatusCode}: {text}");

        return JsonNode.Parse(text)!;
    }
}
