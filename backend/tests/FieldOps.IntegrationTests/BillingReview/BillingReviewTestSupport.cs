using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.Customers;
using FieldOps.IntegrationTests.ServiceRequests;
using FieldOps.IntegrationTests.Team;
using Npgsql;

namespace FieldOps.IntegrationTests.BillingReview;

/// <summary>A quote line of a seeded job; <c>Planned</c>/<c>Used</c> create the planned material of a product line.</summary>
internal sealed record LineSpec(
    string Name,
    string Type,
    decimal Quantity,
    string Unit,
    decimal Price,
    decimal Rate = 0m,
    bool Optional = false,
    bool Selected = false,
    decimal? Planned = null,
    decimal? Used = null);

internal sealed record ChecklistSpec(string Label, bool Required, bool Completed);

/// <summary>A completed work order as the mobile flows leave it; the defaults are a job that is ready to invoice.</summary>
internal sealed record JobSpec
{
    public Guid? Branch { get; init; }

    public Guid? Customer { get; init; }

    public string Title { get; init; } = "Fix drain";

    public string Status { get; init; } = "completed";

    public DateTimeOffset? CompletedAt { get; init; }

    public IReadOnlyList<LineSpec>? Lines { get; init; }

    public decimal Discount { get; init; }

    public string? Terms { get; init; }

    public Guid? Technician { get; init; }

    public long WorkSeconds { get; init; } = 7200;

    public bool Photos { get; init; } = true;

    public string? Method { get; init; } = "signed";

    public IReadOnlyList<ChecklistSpec>? Checklist { get; init; }

    public IReadOnlyList<(string Description, decimal Quantity)>? AddedMaterials { get; init; }

    public string? Summary { get; init; } = "All done";

    public bool Minimal { get; init; }
}

internal sealed record BillingJob(
    Guid Order,
    long Number,
    Guid Version,
    Guid Visit,
    Guid Response,
    IReadOnlyList<Guid> Lines,
    decimal Subtotal,
    decimal Discount,
    decimal Tax,
    decimal Total,
    int BilledLines,
    Guid BeforePhoto,
    Guid AfterPhoto);

/// <summary>Seeds and request builders of the completed jobs review integration tests.</summary>
internal static class BillingSeed
{
    public static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==");

    private static readonly LineSpec[] DefaultLines =
    [
        new("Labor", "service", 2m, "h", 100m),
        new("Pipe fitting", "product", 2m, "ea", 10m, 8.25m, Planned: 2m, Used: 2m),
    ];

    public static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    public static async Task<BillingJob> SeedJobAsync(
        this CompanySettingsDatabaseFixture db, RequestWorld world, Guid user, JobSpec? spec = null)
    {
        spec ??= new JobSpec();
        var lines = spec.Lines ?? DefaultLines;
        var branch = spec.Branch ?? world.BranchA;
        var completedAt = (spec.CompletedAt ?? DateTimeOffset.UtcNow.AddHours(-3)).ToUniversalTime();
        var quote = Guid.NewGuid();
        var version = Guid.NewGuid();
        var response = Guid.NewGuid();
        var order = Guid.NewGuid();
        var visit = Guid.NewGuid();
        var lineIds = lines.Select(_ => Guid.NewGuid()).ToList();

        // Line amounts as the quote flow freezes them: the discount belongs to the first regular line.
        var firstRegular = lines.ToList().FindIndex(line => !line.Optional);
        var subtotals = new List<decimal>();
        var taxes = new List<decimal>();
        var totals = new List<decimal>();

        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var subtotal = Round(line.Quantity * line.Price);
            var share = index == firstRegular ? spec.Discount : 0m;
            var tax = Round((subtotal - share) * line.Rate / 100m);

            subtotals.Add(subtotal);
            taxes.Add(tax);
            totals.Add(subtotal - share + tax);
        }

        bool Billed(int index) => !lines[index].Optional || lines[index].Selected;
        var billedIndexes = Enumerable.Range(0, lines.Count).Where(Billed).ToList();
        var responseSubtotal = billedIndexes.Sum(index => subtotals[index]);
        var responseTax = billedIndexes.Sum(index => taxes[index]);
        var responseTotal = billedIndexes.Sum(index => totals[index]);
        var regular = Enumerable.Range(0, lines.Count).Where(index => !lines[index].Optional).ToList();

        var quoteNumber = await db.ScalarAsync<long>(
            "SELECT COALESCE(MAX(quote_number), 0) + 1 FROM quotes WHERE organization_id = @o", ("o", world.Org));
        var number = await db.ScalarAsync<long>(
            "SELECT COALESCE(MAX(work_order_number), 0) + 1 FROM work_orders WHERE organization_id = @o", ("o", world.Org));

        await db.ExecuteAsync(
            """
            SET session_replication_role = replica;
            INSERT INTO quotes (id, organization_id, branch_id, request_id, customer_id, property_id, quote_number, status, current_version_no, created_by_user_id)
            VALUES (@quote, @org, @branch, gen_random_uuid(), @customer, @property, @qnumber, 'approved', 1, @user);
            INSERT INTO quote_versions (id, organization_id, quote_id, version_no, scope, subtotal, discount_total, tax_total, total, currency, terms, is_immutable, created_by_user_id)
            VALUES (@version, @org, @quote, 1, 'Scope', @vsub, @disc, @vtax, @vtotal, 'USD', @terms, true, @user);
            SET session_replication_role = DEFAULT;
            """,
            ("quote", quote),
            ("version", version),
            ("org", world.Org),
            ("branch", branch),
            ("customer", spec.Customer ?? world.Customer),
            ("property", world.Property),
            ("qnumber", quoteNumber),
            ("user", user),
            ("vsub", regular.Sum(index => subtotals[index])),
            ("disc", spec.Discount),
            ("vtax", regular.Sum(index => taxes[index])),
            ("vtotal", regular.Sum(index => totals[index])),
            ("terms", spec.Terms));

        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];

            await db.ExecuteAsync(
                $"""
                INSERT INTO quote_lines (id, organization_id, quote_version_id, line_type, name, description, quantity, unit, unit_price, tax_rate,
                    line_subtotal, line_tax, line_total, sort_order, is_optional)
                VALUES (@id, @org, @version, CAST('{line.Type}' AS catalog_item_type), @name, 'Line description', @qty, @unit, @price, @rate,
                    @sub, @tax, @total, @sort, @optional)
                """,
                ("id", lineIds[index]),
                ("org", world.Org),
                ("version", version),
                ("name", line.Name),
                ("qty", line.Quantity),
                ("unit", line.Unit),
                ("price", line.Price),
                ("rate", line.Rate),
                ("sub", subtotals[index]),
                ("tax", taxes[index]),
                ("total", totals[index]),
                ("sort", index),
                ("optional", line.Optional));
        }

        await db.ExecuteAsync(
            """
            INSERT INTO quote_responses (id, organization_id, quote_version_id, response, responder_name, subtotal, discount_total, tax_total, total)
            VALUES (@response, @org, @version, 'approved', 'Carla Customer', @sub, @disc, @tax, @total)
            """,
            ("response", response),
            ("org", world.Org),
            ("version", version),
            ("sub", responseSubtotal),
            ("disc", spec.Discount),
            ("tax", responseTax),
            ("total", responseTotal));

        for (var index = 0; index < lines.Count; index++)
        {
            if (lines[index].Optional && lines[index].Selected)
            {
                await db.ExecuteAsync(
                    "INSERT INTO quote_response_optional_lines (organization_id, quote_version_id, quote_response_id, quote_line_id) VALUES (@org, @version, @response, @line)",
                    ("org", world.Org),
                    ("version", version),
                    ("response", response),
                    ("line", lineIds[index]));
            }
        }

        // status is a test-controlled constant, never user input.
        await db.ExecuteAsync(
            $"""
            INSERT INTO work_orders (id, organization_id, branch_id, work_order_number, quote_version_id, customer_id, property_id, title, service_category_id,
                status, scope_snapshot, created_by_user_id)
            VALUES (@order, @org, @branch, @number, @version, @customer, @property, @title, @category, CAST('{spec.Status}' AS work_order_status), 'Scope', @user);
            UPDATE organizations SET next_work_order_number = GREATEST(next_work_order_number, @number + 1) WHERE id = @org;
            INSERT INTO visits (id, organization_id, work_order_id, visit_number, status, actual_started_at, actual_completed_at, completion_summary)
            VALUES (@visit, @org, @order, 1, CAST('{(spec.Status == "completed" ? "completed" : "in_progress")}' AS visit_status), @started, @completed, @summary);
            """,
            ("order", order),
            ("visit", visit),
            ("org", world.Org),
            ("branch", branch),
            ("number", number),
            ("version", version),
            ("customer", spec.Customer ?? world.Customer),
            ("property", world.Property),
            ("title", spec.Title),
            ("category", world.Category),
            ("user", user),
            ("started", completedAt.AddSeconds(-spec.WorkSeconds)),
            ("completed", spec.Status == "completed" ? completedAt : null),
            ("summary", spec.Minimal ? null : spec.Summary));

        var before = Guid.NewGuid();
        var after = Guid.NewGuid();

        if (!spec.Minimal)
        {
            await SeedVisitDetailsAsync(db, world, user, spec, lines, lineIds, order, visit, completedAt, before, after);
        }

        return new BillingJob(
            order,
            number,
            version,
            visit,
            response,
            lineIds,
            responseSubtotal,
            spec.Discount,
            responseTax,
            responseTotal,
            billedIndexes.Count,
            before,
            after);
    }

    private static async Task SeedVisitDetailsAsync(
        CompanySettingsDatabaseFixture db,
        RequestWorld world,
        Guid user,
        JobSpec spec,
        IReadOnlyList<LineSpec> lines,
        IReadOnlyList<Guid> lineIds,
        Guid order,
        Guid visit,
        DateTimeOffset completedAt,
        Guid before,
        Guid after)
    {
        if (spec.Technician is { } assigned)
        {
            await db.ExecuteAsync(
                "INSERT INTO visit_assignments (visit_id, technician_id, assigned_by_user_id, is_primary) VALUES (@v, @t, @u, true)",
                ("v", visit),
                ("t", assigned),
                ("u", user));
        }

        if (spec.WorkSeconds > 0)
        {
            // Work time belongs to a technician profile; jobs without an assignee use a throwaway one.
            var worker = spec.Technician ?? await db.SeedTechAsync(world.Org, spec.Branch ?? world.BranchA, "Time", "Keeper");

            await db.ExecuteAsync(
                "INSERT INTO visit_time_entries (visit_id, technician_id, started_at, ended_at, entry_type) VALUES (@v, @t, @s, @e, 'work')",
                ("v", visit),
                ("t", worker),
                ("s", completedAt.AddSeconds(-spec.WorkSeconds)),
                ("e", completedAt));
        }

        var checklist = spec.Checklist ?? [new ChecklistSpec("Shut off water", true, true), new ChecklistSpec("Clean up", true, true)];
        var sort = 0;

        foreach (var item in checklist)
        {
            await db.ExecuteAsync(
                "INSERT INTO visit_checklist_items (visit_id, label, is_required, is_completed, sort_order) VALUES (@v, @l, @r, @c, @s)",
                ("v", visit),
                ("l", item.Label),
                ("r", item.Required),
                ("c", item.Completed),
                ("s", sort++));
        }

        for (var index = 0; index < lines.Count; index++)
        {
            if (lines[index].Planned is not { } planned)
            {
                continue;
            }

            var plannedId = Guid.NewGuid();

            await db.ExecuteAsync(
                """
                INSERT INTO work_order_planned_materials (id, organization_id, work_order_id, quote_line_id, description, quantity, unit, source, sort_order)
                VALUES (@id, @org, @order, @line, @name, @qty, @unit, 'truck_stock', @sort)
                """,
                ("id", plannedId),
                ("org", world.Org),
                ("order", order),
                ("line", lineIds[index]),
                ("name", lines[index].Name),
                ("qty", planned),
                ("unit", lines[index].Unit),
                ("sort", index));

            if (lines[index].Used is { } used && used > 0)
            {
                await db.ExecuteAsync(
                    "INSERT INTO visit_materials (visit_id, planned_material_id, description, quantity, unit, unit_cost) VALUES (@v, @p, @d, @q, @u, 3.5)",
                    ("v", visit),
                    ("p", plannedId),
                    ("d", lines[index].Name),
                    ("q", used),
                    ("u", lines[index].Unit));
            }
        }

        foreach (var (description, quantity) in spec.AddedMaterials ?? [])
        {
            await db.ExecuteAsync(
                "INSERT INTO visit_materials (visit_id, description, quantity, unit, unit_cost) VALUES (@v, @d, @q, 'ea', 9.99)",
                ("v", visit),
                ("d", description),
                ("q", quantity));
        }

        if (spec.Photos)
        {
            await SeedEvidenceAsync(db, user, visit, before, "before", "Before photo");
            await SeedEvidenceAsync(db, user, visit, after, "after", "After photo");
        }

        if (spec.Method is { } method)
        {
            var signed = method == "signed";

            await db.ExecuteAsync(
                """
                INSERT INTO customer_signoffs (visit_id, signer_name, accepted, comments, absence_reason, acknowledgement_method, signer_relationship,
                    signature_content, signature_mime_type, review_confirmed, recorded_by_user_id)
                VALUES (@v, @name, @accepted, @comments, @reason, @method, @relationship, @content, @mime, true, @user)
                """,
                ("v", visit),
                ("name", signed ? "Carla Customer" : null),
                ("accepted", signed || method == "remote_confirmation"),
                ("comments", signed ? "Looks good" : null),
                ("reason", signed ? null : "Not home"),
                ("method", method),
                ("relationship", signed || method == "remote_confirmation" ? "customer" : null),
                ("content", signed ? Png : null),
                ("mime", signed ? "image/png" : null),
                ("user", user));
        }
    }

    public static Task SeedEvidenceAsync(
        this CompanySettingsDatabaseFixture db, Guid user, Guid visit, Guid id, string type, string? caption) =>
        db.ExecuteAsync(
            """
            INSERT INTO visit_evidence (id, visit_id, file_name, content, mime_type, size_bytes, evidence_type, caption, uploaded_by_user_id)
            VALUES (@id, @v, 'photo.png', @c, 'image/png', @s, @t, @caption, @u)
            """,
            ("id", id),
            ("v", visit),
            ("c", Png),
            ("s", Png.Length),
            ("t", type),
            ("caption", caption),
            ("u", user));

    /// <summary>An invoice of the job in any status, numbered from the organization counter like a real one.</summary>
    public static async Task<Guid> SeedBillingInvoiceAsync(
        this CompanySettingsDatabaseFixture db, RequestWorld world, Guid user, BillingJob job, string status, Guid? branch = null)
    {
        var id = Guid.NewGuid();
        var number = await db.ScalarAsync<long>("SELECT next_invoice_number FROM organizations WHERE id = @o", ("o", world.Org));

        // status is a test-controlled constant, never user input.
        await db.ExecuteAsync(
            $"""
            INSERT INTO invoices (id, organization_id, branch_id, invoice_number, work_order_id, customer_id, status, currency, subtotal, tax_total, total, balance_due, created_by_user_id, customer_snapshot)
            VALUES (@id, @org, @branch, @number, @order, @customer, CAST('{status}' AS invoice_status), 'USD', @sub, @tax, @total, @total, @user,
                CASE WHEN '{status}' IN ('draft', 'void') THEN NULL
                     ELSE CAST(@snap AS jsonb) END);
            UPDATE organizations SET next_invoice_number = @number + 1 WHERE id = @org;
            """,
            ("id", id),
            ("org", world.Org),
            ("branch", branch ?? world.BranchA),
            ("number", number),
            ("order", job.Order),
            ("customer", world.Customer),
            ("sub", job.Subtotal),
            ("tax", job.Tax),
            ("total", job.Total),
            ("snap", SeedInvoiceSnapshot.Json),
            ("user", user));

        return id;
    }

    public static Task SeedAuditAsync(
        this CompanySettingsDatabaseFixture db, Guid org, string action, string entityType, Guid entity, Guid? actor, DateTimeOffset at, string? secret = null) =>
        db.ExecuteAsync(
            """
            INSERT INTO audit_logs (organization_id, actor_user_id, action, entity_type, entity_id, before_data, after_data, metadata, ip_address, occurred_at)
            VALUES (@org, @actor, @action, @type, @entity, CAST(@data AS jsonb), CAST(@data AS jsonb), CAST(@data AS jsonb), CAST('203.0.113.9' AS inet), @at)
            """,
            ("org", org),
            ("actor", actor),
            ("action", action),
            ("type", entityType),
            ("entity", entity),
            ("data", $"{{\"note\":\"{secret ?? "none"}\"}}"),
            ("at", at.ToUniversalTime()));

    public static string Today(string timezone) =>
        TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(timezone))
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string DaysFromToday(string timezone, int days) =>
        DateOnly.ParseExact(Today(timezone), "yyyy-MM-dd", CultureInfo.InvariantCulture).AddDays(days)
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string Queue(string query = "") => "/billing-review/queue" + (query.Length == 0 ? string.Empty : "?" + query);

    public static string Detail(Guid order) => $"/billing-review/work-orders/{order}";

    public static JsonObject InvoiceBody(
        string issueDate, string terms = "due_upon_receipt", string? note = null, bool? acknowledge = false) =>
        new()
        {
            ["issueDate"] = issueDate,
            ["paymentTerms"] = terms,
            ["note"] = note,
            ["acknowledgeVariances"] = acknowledge,
        };

    public static Task<HttpResponseMessage> PostInvoiceAsync(RequestsHost host, string? cookie, Guid order, JsonObject body) =>
        host.SendAsync(HttpMethod.Post, Detail(order) + "/invoice", cookie, body);

    public static Task<HttpResponseMessage> PatchReviewAsync(RequestsHost host, string? cookie, Guid order, JsonObject body) =>
        host.SendAsync(HttpMethod.Patch, Detail(order) + "/review", cookie, body);

    public static async Task<JsonNode> ReadAsync(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var text = await response.Content.ReadAsStringAsync();

        Assert.True(
            response.StatusCode == expected,
            $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri?.PathAndQuery}: expected {expected} but was {response.StatusCode} {text}");

        return JsonNode.Parse(text)!;
    }

    public static async Task<JsonNode> ProblemAsync(
        HttpResponseMessage response, HttpStatusCode expected, string? code = null, string? errorKey = null)
    {
        var problem = await ReadAsync(response, expected);

        if (code is not null)
        {
            Assert.Equal(code, problem["code"]!.GetValue<string>());
        }

        if (errorKey is not null)
        {
            Assert.True(problem["errors"]?[errorKey] is not null, $"Expected errors.{errorKey} in {problem.ToJsonString()}");
        }

        return problem;
    }

    public static Task<long> CountAsync(this CompanySettingsDatabaseFixture db, string table, string where, params (string Name, object? Value)[] parameters) =>
        db.ScalarAsync<long>($"SELECT COUNT(*) FROM {table} WHERE {where}", parameters);

    /// <summary>The first column of every row, as text.</summary>
    public static async Task<List<string>> TextsAsync(
        this CompanySettingsDatabaseFixture db, string sql, params (string Name, object? Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        var texts = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            texts.Add(reader.GetString(0));
        }

        return texts;
    }

    public static Task<long> InvoiceCountAsync(this CompanySettingsDatabaseFixture db, Guid org) =>
        db.CountAsync("invoices", "organization_id = @o", ("o", org));

    public static Task<long> NextInvoiceNumberAsync(this CompanySettingsDatabaseFixture db, Guid org) =>
        db.ScalarAsync<long>("SELECT next_invoice_number FROM organizations WHERE id = @o", ("o", org));

    /// <summary>Everything a rejected or unauthorized action must leave untouched.</summary>
    public static Task<string> OrderStateAsync(this CompanySettingsDatabaseFixture db, Guid order) =>
        db.ScalarAsync<string>(
            """
            SELECT w.status::text || '|' || COALESCE(w.billing_review_note, '-') || '|' || COALESCE(w.billing_follow_up_at::text, '-') || '|' || w.updated_at::text
                || '|' || (SELECT COUNT(*) FROM invoices WHERE work_order_id = w.id)
                || '|' || (SELECT COUNT(*) FROM audit_logs WHERE entity_id = w.id)
                || '|' || (SELECT string_agg(status::text, ',') FROM visits WHERE work_order_id = w.id)
                || '|' || (SELECT next_invoice_number FROM organizations WHERE id = w.organization_id)
            FROM work_orders w WHERE w.id = @o
            """,
            ("o", order));

    public static Task<string> AuditTextAsync(this CompanySettingsDatabaseFixture db, Guid org) =>
        db.ScalarAsync<string>(
            """
            SELECT COALESCE(string_agg(action || ' ' || COALESCE(before_data::text, '') || ' ' || COALESCE(after_data::text, '') || ' ' || metadata::text, E'\n'), '')
            FROM audit_logs WHERE organization_id = @o
            """,
            ("o", org));
}
