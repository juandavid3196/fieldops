using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.Customers;
using FieldOps.IntegrationTests.Sessions;
using FieldOps.IntegrationTests.Users;

namespace FieldOps.IntegrationTests.Team;

public sealed record TeamActor(string Cookie, SeededMember Member);

public static class TeamSeed
{
    public const string NoBranchAccess = "Choose a branch you have access to.";

    public static async Task<TeamActor> ActorAsync(
        this CompanySettingsDatabaseFixture db,
        CustomerHost host,
        Guid organizationId,
        short roleId,
        params Guid[] limitedToBranches)
    {
        var member = await db.SeedMemberAsync(
            organizationId, roleId, "Act", "Or", isAllBranches: limitedToBranches.Length == 0);

        foreach (var branchId in limitedToBranches)
        {
            await db.LinkMembershipToBranchAsync(member.MembershipId, branchId);
        }

        return new TeamActor(await CompanySettingsApi.SignInCookieAsync(host.Client, member.Email), member);
    }

    public static async Task<Guid> SeedTechAsync(
        this CompanySettingsDatabaseFixture db,
        Guid organizationId,
        Guid branchId,
        string first,
        string last,
        string status = "active",
        string? email = null,
        string? phone = null,
        string? code = null,
        Guid? membershipId = null,
        string? notes = null)
    {
        var id = Guid.NewGuid();

        // status is a test-controlled constant, never user input.
        await db.ExecuteAsync(
            $"""
            INSERT INTO technician_profiles (id, organization_id, branch_id, organization_user_id, employee_code, first_name, last_name, email, phone, status, notes)
            VALUES (@id, @org, @branch, @member, @code, @first, @last, @email, @phone, '{status}', @notes)
            """,
            ("id", id),
            ("org", organizationId),
            ("branch", branchId),
            ("member", membershipId),
            ("code", code),
            ("first", first),
            ("last", last),
            ("email", email),
            ("phone", phone),
            ("notes", notes));

        return id;
    }

    public static async Task<Guid> SeedSkillAsync(
        this CompanySettingsDatabaseFixture db, Guid organizationId, string name, bool active = true)
    {
        var id = Guid.NewGuid();

        await db.ExecuteAsync(
            "INSERT INTO skills (id, organization_id, name, is_active) VALUES (@id, @org, @name, @active)",
            ("id", id),
            ("org", organizationId),
            ("name", name),
            ("active", active));

        return id;
    }

    public static Task GiveSkillAsync(
        this CompanySettingsDatabaseFixture db, Guid technicianId, Guid skillId, bool primary = false, short? proficiency = null) =>
        db.ExecuteAsync(
            "INSERT INTO technician_skills (technician_id, skill_id, proficiency, is_primary) VALUES (@t, @s, @p, @primary)",
            ("t", technicianId),
            ("s", skillId),
            ("p", proficiency),
            ("primary", primary));

    public static async Task<Guid> SeedSlotAsync(
        this CompanySettingsDatabaseFixture db, Guid technicianId, short dayOfWeek, string start, string end, short capacity = 100)
    {
        var id = Guid.NewGuid();

        await db.ExecuteAsync(
            "INSERT INTO technician_weekly_availability (id, technician_id, day_of_week, start_time, end_time, capacity_percent) VALUES (@id, @t, @d, @s::time, @e::time, @c)",
            ("id", id),
            ("t", technicianId),
            ("d", dayOfWeek),
            ("s", start),
            ("e", end),
            ("c", capacity));

        return id;
    }

    /// <summary>
    /// Seeds a work order, a visit and an active assignment. Foreign keys to unrelated parents are bypassed for
    /// this throwaway container only.
    /// </summary>
    public static async Task<Guid> SeedAssignmentAsync(
        this CompanySettingsDatabaseFixture db,
        Guid organizationId,
        Guid userId,
        Guid branchId,
        Guid technicianId,
        string visitStatus,
        DateTimeOffset? start,
        DateTimeOffset? end,
        long workOrderNumber = 0,
        string scope = "Scope")
    {
        var visitId = Guid.NewGuid();
        var workOrderId = Guid.NewGuid();
        var number = workOrderNumber == 0 ? Random.Shared.NextInt64(1, 1_000_000_000) : workOrderNumber;

        // visitStatus is a test-controlled constant, never user input.
        await db.ExecuteAsync(
            $"""
            SET session_replication_role = replica;
            INSERT INTO work_orders (id, organization_id, branch_id, work_order_number, quote_version_id, customer_id, property_id, status, scope_snapshot, created_by_user_id)
            VALUES (@wo, @org, @branch, @number, gen_random_uuid(), gen_random_uuid(), gen_random_uuid(), 'scheduled', @scope, @user);
            INSERT INTO visits (id, organization_id, work_order_id, visit_number, status, scheduled_start, scheduled_end)
            VALUES (@visit, @org, @wo, 1, '{visitStatus}', @start, @end);
            INSERT INTO visit_assignments (id, visit_id, technician_id, assigned_by_user_id)
            VALUES (gen_random_uuid(), @visit, @tech, @user);
            """,
            ("wo", workOrderId),
            ("visit", visitId),
            ("org", organizationId),
            ("branch", branchId),
            ("number", number),
            ("scope", scope),
            ("user", userId),
            ("tech", technicianId),
            ("start", start),
            ("end", end));

        return visitId;
    }

    public static JsonObject Body(
        Guid branchId,
        string first = "Grace",
        string last = "Hopper",
        string? email = "Grace@Example.com",
        string? phone = "+1 (512) 555-0100",
        string? code = "g-100",
        string? notes = "Prefers mornings") =>
        new()
        {
            ["firstName"] = first,
            ["lastName"] = last,
            ["email"] = email,
            ["phone"] = phone,
            ["employeeCode"] = code,
            ["branchId"] = branchId.ToString(),
            ["notes"] = notes,
        };

    public static string Error(JsonNode problem, string key) =>
        problem["errors"]![key]!.AsArray().Single()!.GetValue<string>();

    public static string[] Names(JsonNode list) =>
        [.. list["items"]!.AsArray().Select(item => item!["fullName"]!.GetValue<string>())];

    public static Task<long> CountAuditAsync(
        this CompanySettingsDatabaseFixture db, Guid organizationId, string action, Guid? entityId = null) =>
        db.ScalarAsync<long>(
            "SELECT COUNT(*) FROM audit_logs WHERE organization_id = @o AND action = @a AND (@e::uuid IS NULL OR entity_id = @e)",
            ("o", organizationId),
            ("a", action),
            ("e", entityId));
}
