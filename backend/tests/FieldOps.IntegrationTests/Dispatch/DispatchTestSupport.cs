using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.ServiceRequests;
using FieldOps.IntegrationTests.Team;

namespace FieldOps.IntegrationTests.Dispatch;

/// <summary>A seeded work order with its first visit.</summary>
internal sealed record SeededOrder(Guid Order, Guid Visit, long Number);

/// <summary>Seeds and request builders of the dispatch calendar integration tests.</summary>
internal static class DispatchSeed
{
    /// <summary>A UTC instant <paramref name="days"/> days from today at <paramref name="hour"/>:<paramref name="minute"/>.</summary>
    public static DateTimeOffset Future(int days, int hour, int minute = 0) =>
        new(DateTime.UtcNow.Date.AddDays(days).AddHours(hour).AddMinutes(minute), TimeSpan.Zero);

    public static string Date(DateTimeOffset instant) => instant.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string Time(DateTimeOffset instant) => instant.ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>
    /// A work order and its visit #1, bypassing the quote flow: foreign keys to the quote version are skipped for
    /// this throwaway container only.
    /// </summary>
    public static async Task<SeededOrder> SeedOrderAsync(
        this CompanySettingsDatabaseFixture db,
        RequestWorld world,
        Guid userId,
        Guid? branch = null,
        string status = "ready_to_schedule",
        string title = "Fix drain",
        string jobType = "one_time",
        string? frequency = null,
        short? count = null,
        DateTimeOffset? preferredStart = null,
        DateTimeOffset? preferredEnd = null,
        short priority = 3,
        int? minutes = 120,
        bool notify = true,
        bool details = true,
        Guid[]? skills = null,
        string[]? tasks = null,
        string visitStatus = "unscheduled",
        DateTimeOffset? start = null,
        DateTimeOffset? end = null,
        Guid? customer = null,
        Guid? property = null)
    {
        var order = Guid.NewGuid();
        var visit = Guid.NewGuid();
        var number = await db.ScalarAsync<long>(
            "SELECT COALESCE(MAX(work_order_number), 0) + 1 FROM work_orders WHERE organization_id = @o", ("o", world.Org));

        // status, jobType and visitStatus are test-controlled constants, never user input.
        await db.ExecuteAsync(
            $"""
            SET session_replication_role = replica;
            INSERT INTO work_orders (id, organization_id, branch_id, work_order_number, quote_version_id, customer_id, property_id, title, job_type,
                service_category_id, estimated_duration_minutes, recurrence_frequency, recurrence_count, notify_customer_when_scheduled,
                send_technician_details, status, priority, scope_snapshot, preferred_start, preferred_end, created_by_user_id)
            VALUES (@order, @org, @branch, @number, gen_random_uuid(), @customer, @property, @title, '{jobType}', @category, @minutes, @frequency, @count,
                @notify, @details, CAST('{status}' AS work_order_status), @priority, 'Scope', @pstart, @pend, @user);
            INSERT INTO visits (id, organization_id, work_order_id, visit_number, status, scheduled_start, scheduled_end, preferred_start, preferred_end)
            VALUES (@visit, @org, @order, 1, CAST('{visitStatus}' AS visit_status), @start, @end, @pstart, @pend);
            """,
            ("order", order),
            ("visit", visit),
            ("org", world.Org),
            ("branch", branch ?? world.BranchA),
            ("number", number),
            ("customer", customer ?? world.Customer),
            ("property", property ?? world.Property),
            ("title", title),
            ("category", world.Category),
            ("minutes", minutes),
            ("frequency", frequency),
            ("count", count),
            ("notify", notify),
            ("details", details),
            ("priority", priority),
            ("pstart", preferredStart),
            ("pend", preferredEnd),
            ("start", start),
            ("end", end),
            ("user", userId));

        foreach (var skill in skills ?? [])
        {
            await db.ExecuteAsync(
                "INSERT INTO work_order_required_skills (work_order_id, skill_id) VALUES (@w, @s)", ("w", order), ("s", skill));
        }

        var sort = 0;

        foreach (var task in tasks ?? [])
        {
            await db.ExecuteAsync(
                "INSERT INTO work_order_checklist_templates (work_order_id, label, sort_order) VALUES (@w, @l, @s)",
                ("w", order),
                ("l", task),
                ("s", sort++));
        }

        await db.ExecuteAsync(
            "UPDATE organizations SET next_work_order_number = GREATEST(next_work_order_number, @n + 1) WHERE id = @o", ("n", number), ("o", world.Org));

        return new SeededOrder(order, visit, number);
    }

    /// <summary>An extra visit of a work order, with an optional active assignment.</summary>
    public static async Task<Guid> SeedVisitAsync(
        this CompanySettingsDatabaseFixture db,
        Guid org,
        Guid order,
        int number,
        string status,
        DateTimeOffset? start = null,
        DateTimeOffset? end = null,
        Guid? technician = null,
        Guid? user = null)
    {
        var id = Guid.NewGuid();

        // status is a test-controlled constant, never user input.
        await db.ExecuteAsync(
            $"""
            INSERT INTO visits (id, organization_id, work_order_id, visit_number, status, scheduled_start, scheduled_end)
            VALUES (@id, @org, @order, @number, CAST('{status}' AS visit_status), @start, @end)
            """,
            ("id", id),
            ("org", org),
            ("order", order),
            ("number", number),
            ("start", start),
            ("end", end));

        if (technician is { } tech)
        {
            await db.AssignAsync(id, tech, user!.Value);
        }

        return id;
    }

    public static Task AssignAsync(this CompanySettingsDatabaseFixture db, Guid visit, Guid technician, Guid user, bool primary = true) =>
        db.ExecuteAsync(
            "INSERT INTO visit_assignments (visit_id, technician_id, assigned_by_user_id, is_primary) VALUES (@v, @t, @u, @p)",
            ("v", visit),
            ("t", technician),
            ("u", user),
            ("p", primary));

    /// <summary>A technician available 08:00 to 18:00 every day with a 12:00 to 13:00 break.</summary>
    public static async Task<Guid> SeedAvailableTechAsync(
        this CompanySettingsDatabaseFixture db,
        Guid org,
        Guid branch,
        string first,
        string last,
        string status = "active",
        bool withBreak = true,
        params Guid[] skills)
    {
        var tech = await db.SeedTechAsync(org, branch, first, last, status);

        for (short day = 0; day < 7; day++)
        {
            var slot = await db.SeedSlotAsync(tech, day, "08:00", "18:00");

            if (withBreak)
            {
                await db.SeedBreakAsync(slot, "12:00", "13:00");
            }
        }

        foreach (var skill in skills)
        {
            await db.GiveSkillAsync(tech, skill, primary: skill == skills[0]);
        }

        return tech;
    }

    public static JsonObject Body(
        DateTimeOffset start,
        string updatedAt,
        int minutes = 60,
        Guid[]? technicians = null,
        Guid? primary = null,
        string window = "start_plus_2h",
        string? note = null,
        bool notify = false,
        bool details = true,
        string? reason = null) =>
        new()
        {
            ["date"] = Date(start),
            ["start"] = Time(start),
            ["end"] = Time(start.AddMinutes(minutes)),
            ["arrivalWindow"] = window,
            ["technicianIds"] = new JsonArray([.. (technicians ?? []).Select(id => (JsonNode?)JsonValue.Create(id))]),
            ["primaryTechnicianId"] = technicians is { Length: > 0 } ? (primary ?? technicians[0]) : null,
            ["dispatchNote"] = note,
            ["notifyCustomer"] = notify,
            ["sendTechnicianDetails"] = details,
            ["overrideReason"] = reason,
            ["updatedAt"] = updatedAt,
        };

    public static JsonObject EvaluationBody(DateTimeOffset start, int minutes = 60, params Guid[] technicians) =>
        new()
        {
            ["date"] = Date(start),
            ["start"] = Time(start),
            ["end"] = Time(start.AddMinutes(minutes)),
            ["technicianIds"] = new JsonArray([.. technicians.Select(id => (JsonNode?)JsonValue.Create(id))]),
            ["primaryTechnicianId"] = technicians.Length > 0 ? technicians[0] : null,
        };

    public static Task<HttpResponseMessage> GetVisitAsync(RequestsHost host, string? cookie, Guid visit) =>
        host.SendAsync(HttpMethod.Get, $"/dispatch/visits/{visit}", cookie);

    public static Task<HttpResponseMessage> PutAsync(RequestsHost host, string? cookie, Guid visit, JsonObject body) =>
        host.SendAsync(HttpMethod.Put, $"/dispatch/visits/{visit}", cookie, body);

    public static Task<HttpResponseMessage> EvaluateAsync(RequestsHost host, string? cookie, Guid visit, JsonObject body) =>
        host.SendAsync(HttpMethod.Post, $"/dispatch/visits/{visit}/evaluation", cookie, body);

    /// <summary>The stored concurrency token of a visit, as the drawer would read it.</summary>
    public static async Task<string> TokenAsync(RequestsHost host, string cookie, Guid visit)
    {
        var detail = await ReadAsync(await GetVisitAsync(host, cookie, visit));

        return detail["updatedAt"]!.GetValue<string>();
    }

    public static async Task<JsonNode> ReadAsync(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        Assert.True(
            response.StatusCode == expected,
            $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri?.AbsolutePath}: expected {expected} but was {response.StatusCode} {await response.Content.ReadAsStringAsync()}");

        return await RequestsHost.ReadAsync(response);
    }

    public static async Task<JsonNode> ProblemAsync(HttpResponseMessage response, HttpStatusCode expected, string? code = null, string? errorKey = null)
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

    public static Task<string> VisitStateAsync(this CompanySettingsDatabaseFixture db, Guid visit) =>
        db.ScalarAsync<string>(
            """
            SELECT status::text || '|' || COALESCE(scheduled_start::text, '-') || '|'
                || (SELECT COUNT(*) FROM visit_assignments WHERE visit_id = @v AND unassigned_at IS NULL) || '|'
                || (SELECT COUNT(*) FROM visit_status_history WHERE visit_id = @v) || '|'
                || (SELECT COUNT(*) FROM audit_logs WHERE entity_id = @v AND action = 'visit.dispatched')
            FROM visits WHERE id = @v
            """,
            ("v", visit));

    public static Task<long> ActiveAssignmentsAsync(this CompanySettingsDatabaseFixture db, Guid visit) =>
        db.ScalarAsync<long>("SELECT COUNT(*) FROM visit_assignments WHERE visit_id = @v AND unassigned_at IS NULL", ("v", visit));

    public static Task<string> AuditTextAsync(this CompanySettingsDatabaseFixture db, Guid entity) =>
        db.ScalarAsync<string>(
            """
            SELECT COALESCE(string_agg(action || ' ' || COALESCE(before_data::text, '') || ' ' || COALESCE(after_data::text, '') || ' ' || metadata::text, E'\n'), '')
            FROM audit_logs WHERE entity_id = @e
            """,
            ("e", entity));

    public static string[] Codes(JsonNode? conflicts) =>
        [.. conflicts!.AsArray().Select(item => item!["code"]!.GetValue<string>()).Distinct().Order()];
}
