using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.BillingReview;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.Customers;
using FieldOps.IntegrationTests.ServiceRequests;
using FieldOps.IntegrationTests.Users;

namespace FieldOps.IntegrationTests.InvoicePayments;

/// <summary>An invoice seeded for the hub tests with the work order it belongs to.</summary>
internal sealed record HubInvoice(Guid Id, long Number, long OrderNumber, Guid Order, Guid Customer, Guid Branch)
{
    public string Display => $"INV-{Number}";

    public string OrderDisplay => $"WO-{OrderNumber}";
}

internal sealed record HubPayment(Guid Id, long Number)
{
    public string Display => $"PAY-{Number}";
}

/// <summary>Dates are days relative to the organization-local today, so the seeds follow the timezone of the test world.</summary>
internal sealed record InvoiceSpec
{
    public string Status { get; init; } = "sent";

    public int IssueDays { get; init; } = -10;

    public int? DueDays { get; init; } = 5;

    public decimal Total { get; init; } = 100m;

    public decimal Paid { get; init; }

    public Guid? Branch { get; init; }

    public Guid? Customer { get; init; }

    public string? Recipient { get; init; } = "carla@example.com";

    public string Title { get; init; } = "Job";
}

internal static class HubSeed
{
    public const string Timezone = "America/Chicago";

    public static DateOnly Today(string timezone = Timezone) =>
        DateOnly.ParseExact(BillingSeed.Today(timezone), "yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string Day(int days, string timezone = Timezone) => Iso(Today(timezone).AddDays(days));

    /// <summary>Seeds a job and its invoice in any stored status, numbered from the organization counter like a real one.</summary>
    public static async Task<HubInvoice> SeedInvoiceAsync(
        this CompanySettingsDatabaseFixture db, RequestWorld world, Guid user, InvoiceSpec spec, string timezone = Timezone)
    {
        var branch = spec.Branch ?? world.BranchA;
        var customer = spec.Customer ?? world.Customer;
        var job = await db.SeedJobAsync(world, user, new JobSpec { Minimal = true, Branch = branch, Customer = customer, Title = spec.Title });
        var id = Guid.NewGuid();
        var number = await db.NextInvoiceNumberAsync(world.Org);
        var today = Today(timezone);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timezone);
        var issue = today.AddDays(spec.IssueDays);
        var noon = issue.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Unspecified);
        var sentAt = spec.Status is "draft" ? (DateTimeOffset?)null : new DateTimeOffset(noon, zone.GetUtcOffset(noon)).ToUniversalTime();

        // status is a test-controlled constant, never user input.
        await db.ExecuteAsync(
            $"""
            INSERT INTO invoices (id, organization_id, branch_id, invoice_number, work_order_id, customer_id, status, issue_date, due_date, currency,
                subtotal, tax_total, total, amount_paid, balance_due, recipient_email, sent_at, created_by_user_id, customer_snapshot)
            VALUES (@id, @org, @branch, @number, @order, @customer, CAST('{spec.Status}' AS invoice_status), @issue, @due, 'USD',
                @total, 0, @total, @paid, @total - @paid, @recipient, @sentAt, @user,
                CASE WHEN '{spec.Status}' IN ('draft', 'void') THEN NULL ELSE CAST(@snap AS jsonb) END);
            UPDATE organizations SET next_invoice_number = @number + 1 WHERE id = @org;
            """,
            ("id", id),
            ("org", world.Org),
            ("branch", branch),
            ("number", number),
            ("order", job.Order),
            ("customer", customer),
            ("issue", issue),
            ("due", spec.DueDays is { } due ? today.AddDays(due) : null),
            ("total", spec.Total),
            ("paid", spec.Paid),
            ("recipient", spec.Recipient),
            ("sentAt", sentAt),
            ("user", user),
            ("snap", SeedInvoiceSnapshot.Json));

        return new HubInvoice(id, number, job.Number, job.Order, customer, branch);
    }

    /// <summary>Inserts a payment with one allocation at local midnight of the day, numbered from the organization counter.</summary>
    public static async Task<HubPayment> SeedPaymentAsync(
        this CompanySettingsDatabaseFixture db,
        Guid org,
        HubInvoice invoice,
        decimal amount,
        int daysFromToday,
        string method,
        string? reference,
        Guid receivedBy,
        Guid recordedBy,
        string? note = null,
        string timezone = Timezone)
    {
        var id = Guid.NewGuid();
        var number = await db.ScalarAsync<long>("SELECT next_payment_number FROM organizations WHERE id = @o", ("o", org));

        // method is a test-controlled constant, never user input.
        await db.ExecuteAsync(
            $"""
            INSERT INTO payments (id, organization_id, customer_id, payment_number, method, amount, currency, paid_at, external_reference, notes,
                recorded_by_user_id, received_by_user_id, idempotency_key)
            VALUES (@id, @org, @customer, @number, CAST('{method}' AS payment_method), @amount, 'USD',
                (CAST(@date AS date) + time '00:00') AT TIME ZONE @tz, @reference, @note, @recorded, @received, gen_random_uuid());
            INSERT INTO payment_allocations (payment_id, invoice_id, amount) VALUES (@id, @invoice, @amount);
            UPDATE organizations SET next_payment_number = @number + 1 WHERE id = @org;
            """,
            ("id", id),
            ("org", org),
            ("customer", invoice.Customer),
            ("number", number),
            ("amount", amount),
            ("date", Today(timezone).AddDays(daysFromToday)),
            ("tz", timezone),
            ("reference", reference),
            ("note", note),
            ("recorded", recordedBy),
            ("received", receivedBy),
            ("invoice", invoice.Id));

        return new HubPayment(id, number);
    }

    public static async Task<string> StampAsync(this CompanySettingsDatabaseFixture db, Guid invoice) =>
        (await db.ScalarAsync<DateTimeOffset>("SELECT updated_at FROM invoices WHERE id = @i", ("i", invoice)))
            .ToString("O", CultureInfo.InvariantCulture);

    /// <summary>Everything a rejected, unauthorized or replayed record request must leave untouched (BR-13 effects).</summary>
    public static Task<string> PaymentStateAsync(this CompanySettingsDatabaseFixture db, Guid org) =>
        db.ScalarAsync<string>(
            """
            SELECT (SELECT COUNT(*) FROM payments WHERE organization_id = @o)
                || '|' || (SELECT COUNT(*) FROM payment_allocations WHERE invoice_id IN (SELECT id FROM invoices WHERE organization_id = @o))
                || '|' || (SELECT next_payment_number FROM organizations WHERE id = @o)
                || '|' || (SELECT COUNT(*) FROM audit_logs WHERE organization_id = @o AND action IN ('payment.recorded', 'invoice.payment_applied'))
                || '|' || COALESCE((SELECT string_agg(status::text || ':' || amount_paid || ':' || balance_due || ':' || updated_at::text, ',' ORDER BY id)
                    FROM invoices WHERE organization_id = @o), '-')
            """,
            ("o", org));
}

internal static class PaymentApi
{
    public static string Path(Guid invoice) => $"/invoices/{invoice}/payments";

    public static JsonObject Body(
        Guid? key = null,
        decimal amount = 10m,
        string? paidDate = null,
        string? method = "cash",
        string? reference = null,
        Guid? receivedBy = null,
        bool sendReceipt = false,
        string? note = null,
        string? updatedAt = null) =>
        new()
        {
            ["idempotencyKey"] = (key ?? Guid.NewGuid()).ToString(),
            ["amount"] = amount,
            ["paidDate"] = paidDate ?? HubSeed.Day(0),
            ["method"] = method,
            ["reference"] = reference,
            ["receivedByUserId"] = receivedBy?.ToString(),
            ["sendReceipt"] = sendReceipt,
            ["note"] = note,
            ["updatedAt"] = updatedAt,
        };

    public static async Task<JsonNode> ReadAsync(HttpResponseMessage response, HttpStatusCode expected) =>
        await BillingSeed.ReadAsync(response, expected);

    public static JsonArray Items(JsonNode page) => page["items"]!.AsArray();

    public static string[] Texts(JsonNode page, string property) =>
        [.. Items(page).Select(item => item![property]!.GetValue<string>())];

    public static async Task<string> WithoutTraceAsync(HttpResponseMessage response)
    {
        var problem = (JsonObject)JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        problem.Remove("traceId");

        return problem.ToJsonString();
    }

    /// <summary>A second active member of the organization, usually the receiver of the money.</summary>
    public static Task<SeededMember> SeedReceiverAsync(
        this CompanySettingsDatabaseFixture db, Guid org, string first = "Rita", string last = "Receiver", string status = "active") =>
        db.SeedMemberAsync(org, CompanySettingsDatabaseFixture.AccountingRoleId, first, last, status: status);
}
