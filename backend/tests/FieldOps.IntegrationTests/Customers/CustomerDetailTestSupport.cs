using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.CompanySettings;

namespace FieldOps.IntegrationTests.Customers;

/// <summary>Seeding and request helpers of the customer detail tests. Foreign keys to unrelated parents are bypassed on the throwaway container only.</summary>
public static class CustomerDetailSeed
{
    public static JsonObject PropertyBody(
        Guid branchId,
        string name = "Dock Warehouse",
        string address = "9 Harbor Rd",
        string? instructions = "Secret gate code 4411") =>
        new()
        {
            ["name"] = name,
            ["addressLine1"] = address,
            ["addressLine2"] = "Bay 4",
            ["city"] = "Austin",
            ["stateRegion"] = "tx",
            ["postalCode"] = "78701",
            ["branchId"] = branchId.ToString(),
            ["serviceInstructions"] = instructions,
        };

    public static async Task<Guid> SeedPropertyAsync(
        this CompanySettingsDatabaseFixture db,
        Guid organizationId,
        Guid customerId,
        Guid? branchId,
        string name,
        bool isActive = true,
        bool isPrimary = false,
        DateTimeOffset? createdAt = null,
        string? serviceNotes = null)
    {
        var id = Guid.NewGuid();

        await db.ExecuteAsync(
            """
            INSERT INTO properties (id, organization_id, customer_id, branch_id, name, address_line1, city, country_code, service_notes, is_active, is_primary, created_at)
            VALUES (@id, @org, @customer, @branch, @name, '5 Seed Ave', 'Austin', 'US', @notes, @active, @primary, @created)
            """,
            ("id", id),
            ("org", organizationId),
            ("customer", customerId),
            ("branch", branchId),
            ("name", name),
            ("notes", serviceNotes),
            ("active", isActive),
            ("primary", isPrimary),
            ("created", createdAt ?? DateTimeOffset.UtcNow));

        return id;
    }

    public static async Task<Guid> SeedTechnicianProfileAsync(
        this CompanySettingsDatabaseFixture db, Guid organizationId, Guid branchId, string first, string last)
    {
        var id = Guid.NewGuid();

        await db.ExecuteAsync(
            "INSERT INTO technician_profiles (id, organization_id, branch_id, first_name, last_name, status) VALUES (@id, @org, @branch, @first, @last, 'active')",
            ("id", id),
            ("org", organizationId),
            ("branch", branchId),
            ("first", first),
            ("last", last));

        return id;
    }

    public static async Task<Guid> SeedRequestAsync(
        this CompanySettingsDatabaseFixture db,
        Guid organizationId,
        Guid customerId,
        long number,
        string description,
        string status,
        DateTimeOffset createdAt)
    {
        var id = Guid.NewGuid();

        // status is a test-controlled constant, never user input.
        await db.ExecuteAsync(
            $"""
            SET session_replication_role = replica;
            INSERT INTO service_requests (id, organization_id, request_number, customer_id, description, status, created_at)
            VALUES (@id, @org, @number, @customer, @description, '{status}', @created);
            """,
            ("id", id),
            ("org", organizationId),
            ("number", number),
            ("customer", customerId),
            ("description", description),
            ("created", createdAt));

        return id;
    }

    /// <summary>A quote with its current version (none when <paramref name="withVersion"/> is false); returns both ids.</summary>
    public static async Task<(Guid QuoteId, Guid VersionId)> SeedQuoteAsync(
        this CompanySettingsDatabaseFixture db,
        Guid organizationId,
        Guid userId,
        Guid customerId,
        Guid requestId,
        long number,
        string status,
        string scope,
        decimal total,
        DateTimeOffset createdAt,
        DateTimeOffset? versionSentAt,
        bool withVersion = true)
    {
        var quoteId = Guid.NewGuid();
        var versionId = Guid.NewGuid();

        // status is a test-controlled constant, never user input.
        await db.ExecuteAsync(
            $"""
            SET session_replication_role = replica;
            INSERT INTO quotes (id, organization_id, request_id, customer_id, quote_number, status, current_version_no, created_by_user_id, created_at)
            VALUES (@quote, @org, @request, @customer, @number, '{status}', @versionNo, @user, @created);
            """,
            ("quote", quoteId),
            ("org", organizationId),
            ("request", requestId),
            ("customer", customerId),
            ("number", number),
            ("versionNo", withVersion ? 1 : 0),
            ("user", userId),
            ("created", createdAt));

        if (withVersion)
        {
            await db.ExecuteAsync(
                """
                SET session_replication_role = replica;
                INSERT INTO quote_versions (id, organization_id, quote_id, version_no, scope, subtotal, tax_total, total, currency, sent_at, created_by_user_id)
                VALUES (@version, @org, @quote, 1, @scope, @total, 0, @total, 'USD', @sent, @user);
                """,
                ("version", versionId),
                ("org", organizationId),
                ("quote", quoteId),
                ("scope", scope),
                ("total", total),
                ("sent", versionSentAt),
                ("user", userId));
        }

        return (quoteId, versionId);
    }

    public static async Task<Guid> SeedJobAsync(
        this CompanySettingsDatabaseFixture db,
        Guid organizationId,
        Guid userId,
        Guid branchId,
        Guid customerId,
        Guid propertyId,
        Guid quoteVersionId,
        long number,
        string status,
        string scope,
        DateTimeOffset createdAt)
    {
        var id = Guid.NewGuid();

        // status is a test-controlled constant, never user input.
        await db.ExecuteAsync(
            $"""
            SET session_replication_role = replica;
            INSERT INTO work_orders (id, organization_id, branch_id, work_order_number, quote_version_id, customer_id, property_id, status, scope_snapshot, created_by_user_id, created_at)
            VALUES (@id, @org, @branch, @number, @version, @customer, @property, '{status}', @scope, @user, @created);
            """,
            ("id", id),
            ("org", organizationId),
            ("branch", branchId),
            ("number", number),
            ("version", quoteVersionId),
            ("customer", customerId),
            ("property", propertyId),
            ("scope", scope),
            ("user", userId),
            ("created", createdAt));

        return id;
    }

    public static async Task<Guid> SeedVisitAsync(
        this CompanySettingsDatabaseFixture db,
        Guid organizationId,
        Guid workOrderId,
        int number,
        string status,
        DateTimeOffset? start,
        DateTimeOffset? end,
        DateTimeOffset? completedAt = null)
    {
        var id = Guid.NewGuid();

        // status is a test-controlled constant, never user input.
        await db.ExecuteAsync(
            $"""
            SET session_replication_role = replica;
            INSERT INTO visits (id, organization_id, work_order_id, visit_number, status, scheduled_start, scheduled_end, actual_completed_at)
            VALUES (@id, @org, @wo, @number, '{status}', @start, @end, @completed);
            """,
            ("id", id),
            ("org", organizationId),
            ("wo", workOrderId),
            ("number", number),
            ("start", start),
            ("end", end),
            ("completed", completedAt));

        return id;
    }

    public static Task SeedAssignmentAsync(
        this CompanySettingsDatabaseFixture db,
        Guid visitId,
        Guid technicianId,
        Guid assignedByUserId,
        bool isPrimary = true,
        DateTimeOffset? unassignedAt = null) =>
        db.ExecuteAsync(
            """
            SET session_replication_role = replica;
            INSERT INTO visit_assignments (id, visit_id, technician_id, assigned_by_user_id, is_primary, unassigned_at)
            VALUES (gen_random_uuid(), @visit, @tech, @user, @primary, @unassigned);
            """,
            ("visit", visitId),
            ("tech", technicianId),
            ("user", assignedByUserId),
            ("primary", isPrimary),
            ("unassigned", unassignedAt));

    public static Task SeedInvoiceAsync(
        this CompanySettingsDatabaseFixture db,
        Guid organizationId,
        Guid userId,
        Guid branchId,
        Guid customerId,
        long number,
        string status,
        decimal total,
        decimal amountPaid,
        DateOnly? issueDate,
        DateTimeOffset createdAt) =>
        db.ExecuteAsync(
            $"""
            SET session_replication_role = replica;
            INSERT INTO invoices (id, organization_id, branch_id, invoice_number, work_order_id, customer_id, status, issue_date, currency, subtotal, tax_total, total, amount_paid, balance_due, created_by_user_id, created_at)
            VALUES (gen_random_uuid(), @org, @branch, @number, gen_random_uuid(), @customer, '{status}', @issue, 'USD', @total, 0, @total, @paid, @total - @paid, @user, @created);
            """,
            ("org", organizationId),
            ("branch", branchId),
            ("number", number),
            ("customer", customerId),
            ("issue", issueDate),
            ("total", total),
            ("paid", amountPaid),
            ("user", userId),
            ("created", createdAt));

    public static Task SeedAuditAsync(
        this CompanySettingsDatabaseFixture db,
        Guid organizationId,
        Guid? actorUserId,
        string action,
        string entityType,
        Guid? entityId,
        DateTimeOffset occurredAt,
        string? beforeData = null,
        string? afterData = null,
        string metadata = "{}") =>
        db.ExecuteAsync(
            """
            INSERT INTO audit_logs (organization_id, actor_user_id, action, entity_type, entity_id, before_data, after_data, metadata, ip_address, occurred_at)
            VALUES (@org, @actor, @action, @type, @entity, @before::jsonb, @after::jsonb, @metadata::jsonb, '10.1.2.3'::inet, @occurred)
            """,
            ("org", organizationId),
            ("actor", actorUserId),
            ("action", action),
            ("type", entityType),
            ("entity", entityId),
            ("before", beforeData),
            ("after", afterData),
            ("metadata", metadata),
            ("occurred", occurredAt));

    public static Task<long> CountPropertyAuditAsync(
        this CompanySettingsDatabaseFixture db, Guid organizationId, string? action = null) =>
        db.ScalarAsync<long>(
            "SELECT COUNT(*) FROM audit_logs WHERE organization_id = @o AND entity_type = 'property' AND (@a::text IS NULL OR action = @a)",
            ("o", organizationId),
            ("a", action));

    public static Task<long> CountPrimariesAsync(this CompanySettingsDatabaseFixture db, Guid customerId) =>
        db.ScalarAsync<long>("SELECT COUNT(*) FROM properties WHERE customer_id = @c AND is_primary", ("c", customerId));

    public static string Message(JsonNode problem) => problem["title"]!.GetValue<string>();
}
