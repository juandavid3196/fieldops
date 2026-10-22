using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.CompanySettings;

namespace FieldOps.IntegrationTests.Customers;

/// <summary>Seeding of the related records of the customer page. Foreign keys to unrelated parents are bypassed on the throwaway container only.</summary>
public static class CustomerRelatedRecordsSeed
{
    public static async Task<Guid> SeedCategoryAsync(this CompanySettingsDatabaseFixture db, Guid organizationId, string name)
    {
        var id = Guid.NewGuid();

        await db.ExecuteAsync(
            "INSERT INTO service_categories (id, organization_id, name) VALUES (@id, @org, @name)",
            ("id", id),
            ("org", organizationId),
            ("name", name));

        return id;
    }

    /// <summary>Gives a seeded request its branch, property and category.</summary>
    public static Task PlaceRequestAsync(
        this CompanySettingsDatabaseFixture db, Guid requestId, Guid? branchId, Guid? propertyId, Guid? categoryId) =>
        db.ExecuteAsync(
            """
            SET session_replication_role = replica;
            UPDATE service_requests SET branch_id = @branch, property_id = @property, category_id = @category WHERE id = @id;
            """,
            ("id", requestId),
            ("branch", branchId),
            ("property", propertyId),
            ("category", categoryId));

    public static Task TitleJobAsync(this CompanySettingsDatabaseFixture db, Guid jobId, string title) =>
        db.ExecuteAsync("UPDATE work_orders SET title = @title WHERE id = @id", ("id", jobId), ("title", title));

    public static Task ExpireVersionAsync(this CompanySettingsDatabaseFixture db, Guid versionId, DateOnly validUntil) =>
        db.ExecuteAsync("UPDATE quote_versions SET valid_until = @valid WHERE id = @id", ("id", versionId), ("valid", validUntil));

    /// <summary>An invoice of a seeded job; returns its id.</summary>
    public static async Task<Guid> SeedJobInvoiceAsync(
        this CompanySettingsDatabaseFixture db,
        Guid organizationId,
        Guid userId,
        Guid branchId,
        Guid customerId,
        Guid jobId,
        long number,
        string status,
        decimal total,
        decimal amountPaid,
        DateOnly? issueDate,
        DateOnly? dueDate,
        DateTimeOffset createdAt)
    {
        var id = Guid.NewGuid();

        // status is a test-controlled constant, never user input.
        await db.ExecuteAsync(
            $"""
            SET session_replication_role = replica;
            INSERT INTO invoices (id, organization_id, branch_id, invoice_number, work_order_id, customer_id, status, issue_date, due_date, currency, subtotal, tax_total, total, amount_paid, balance_due, created_by_user_id, created_at, customer_snapshot)
            VALUES (@id, @org, @branch, @number, @job, @customer, '{status}', @issue, @due, 'USD', @total, 0, @total, @paid, @total - @paid, @user, @created,
                CASE WHEN '{status}' IN ('draft', 'void') THEN NULL
                     ELSE CAST(@snap AS jsonb) END);
            """,
            ("id", id),
            ("org", organizationId),
            ("branch", branchId),
            ("number", number),
            ("job", jobId),
            ("customer", customerId),
            ("issue", issueDate),
            ("due", dueDate),
            ("total", total),
            ("paid", amountPaid),
            ("snap", SeedInvoiceSnapshot.Json),
            ("user", userId),
            ("created", createdAt));

        return id;
    }

    /// <summary>A payment with one allocation per (invoice, amount) pair; returns its id.</summary>
    public static async Task<Guid> SeedPaymentAsync(
        this CompanySettingsDatabaseFixture db,
        Guid organizationId,
        Guid customerId,
        Guid userId,
        long number,
        DateTimeOffset paidAt,
        params (Guid InvoiceId, decimal Amount)[] allocations)
    {
        var id = Guid.NewGuid();
        var total = allocations.Sum(allocation => allocation.Amount);

        await db.ExecuteAsync(
            """
            SET session_replication_role = replica;
            INSERT INTO payments (id, organization_id, customer_id, payment_number, method, amount, currency, paid_at, recorded_by_user_id, received_by_user_id, idempotency_key)
            VALUES (@id, @org, @customer, @number, CAST('cash' AS payment_method), @amount, 'USD', @paid, @user, @user, gen_random_uuid());
            """,
            ("id", id),
            ("org", organizationId),
            ("customer", customerId),
            ("number", number),
            ("amount", total),
            ("paid", paidAt),
            ("user", userId));

        foreach (var (invoiceId, amount) in allocations)
        {
            await db.ExecuteAsync(
                """
                SET session_replication_role = replica;
                INSERT INTO payment_allocations (payment_id, invoice_id, amount) VALUES (@payment, @invoice, @amount);
                """,
                ("payment", id),
                ("invoice", invoiceId),
                ("amount", amount));
        }

        return id;
    }

    public static Task<Guid> PrimaryContactAsync(this CompanySettingsDatabaseFixture db, Guid customerId) =>
        db.ScalarAsync<Guid>("SELECT id FROM customer_contacts WHERE customer_id = @c AND is_primary", ("c", customerId));

    public static async Task<Guid> PrimaryPropertyAsync(this CompanySettingsDatabaseFixture db, Guid customerId) =>
        await db.ScalarAsync<Guid>("SELECT id FROM properties WHERE customer_id = @c AND is_primary", ("c", customerId));

    /// <summary>The numbers or ids of one array of a response, in order.</summary>
    public static string[] Strings(JsonNode body, string array, string field) =>
        [.. body[array]!.AsArray().Select(item => item![field]!.GetValue<string>())];
}
