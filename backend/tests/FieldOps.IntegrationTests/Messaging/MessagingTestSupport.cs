using System.Net;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.ServiceRequests;
using FieldOps.IntegrationTests.Team;
using FieldOps.IntegrationTests.TechnicianVisits;
using FieldOps.IntegrationTests.Users;

namespace FieldOps.IntegrationTests.Messaging;

/// <summary>A signed-in member of the messaging tests; <c>Profile</c> is set for technicians.</summary>
internal sealed record Member(string Name, string Cookie, Guid UserId, Guid Membership, Guid? Profile = null);

internal static class MessagingSeed
{
    public static async Task<Member> StaffAsync(
        this CompanySettingsDatabaseFixture db,
        TechnicianHost host,
        Guid org,
        short role,
        string first,
        bool allBranches = true,
        params Guid[] branches)
    {
        var member = await db.SeedMemberAsync(org, role, first, "Staff", isAllBranches: allBranches);

        foreach (var branch in branches)
        {
            await db.LinkMembershipToBranchAsync(member.MembershipId, branch);
        }

        return new Member(first, await host.CookieAsync(member.Email), member.UserId, member.MembershipId);
    }

    public static async Task<Member> TechAsync(
        this CompanySettingsDatabaseFixture db, TechnicianHost host, Guid org, Guid branch, string first, string status = "active")
    {
        var (cookie, member) = await host.SignInAsync(db, org, CompanySettingsDatabaseFixture.TechnicianRoleId, first);
        var profile = await db.SeedTechAsync(org, branch, first, "Tech", status, membershipId: member.MembershipId);

        return new Member(first, cookie, member.UserId, member.MembershipId, profile);
    }

    public static Task UnassignTechAsync(this CompanySettingsDatabaseFixture db, Guid visit, Guid technician) =>
        db.ExecuteAsync(
            "UPDATE visit_assignments SET unassigned_at = now() WHERE visit_id = @v AND technician_id = @t",
            ("v", visit),
            ("t", technician));

    public static JsonObject Message(string body, Guid? clientMessageId = null) =>
        new() { ["clientMessageId"] = (clientMessageId ?? Guid.NewGuid()).ToString(), ["body"] = body };

    public static Task<HttpResponseMessage> PostMessageAsync(
        this TechnicianHost host, Member member, Guid conversation, string body, Guid? clientMessageId = null) =>
        host.SendAsync(HttpMethod.Post, $"/messages/conversations/{conversation}/messages", member.Cookie, Message(body, clientMessageId));

    public static async Task<Guid> ResolveJobAsync(this TechnicianHost host, Member member, Guid workOrder)
    {
        var response = await host.SendAsync(
            HttpMethod.Post, "/messages/conversations/resolve", member.Cookie, new JsonObject { ["type"] = "job", ["workOrderId"] = workOrder });

        return (await TechnicianVisitSeed.ReadAsync(response))["conversationId"]!.GetValue<Guid>();
    }

    public static async Task<Guid> ResolveTeamAsync(this TechnicianHost host, Member member, Guid branch)
    {
        var response = await host.SendAsync(
            HttpMethod.Post, "/messages/conversations/resolve", member.Cookie, new JsonObject { ["type"] = "team", ["branchId"] = branch });

        return (await TechnicianVisitSeed.ReadAsync(response))["conversationId"]!.GetValue<Guid>();
    }

    public static async Task<JsonNode> ListAsync(
        this TechnicianHost host, Member member, string query = "", HttpStatusCode expected = HttpStatusCode.OK) =>
        await TechnicianVisitSeed.ReadAsync(await host.GetAsync($"/messages/conversations{query}", member.Cookie), expected);

    public static async Task<JsonNode> SummaryAsync(this TechnicianHost host, Member member) =>
        await TechnicianVisitSeed.ReadAsync(await host.GetAsync("/messages/summary", member.Cookie));

    public static string[] Titles(JsonNode list) =>
        [.. list["items"]!.AsArray().Select(item => item!["title"]!.GetValue<string>())];

    /// <summary>Rows of the three tables for the organizations, as one text: conversations, messages and read states.</summary>
    public static Task<string> StateAsync(this CompanySettingsDatabaseFixture db, params Guid[] orgs) =>
        db.ScalarAsync<string>(
            """
            SELECT 'c=' || (SELECT COUNT(*) FROM conversations WHERE organization_id = ANY(@o))
                || '|m=' || (SELECT COUNT(*) FROM conversation_messages WHERE organization_id = ANY(@o))
                || '|r=' || (SELECT COUNT(*) FROM conversation_read_states WHERE organization_id = ANY(@o))
                || '|a=' || (SELECT COUNT(*) FROM audit_logs WHERE organization_id = ANY(@o))
            """,
            ("o", orgs));

    /// <summary>A conversation with one message inserted directly, for list and paging scenarios.</summary>
    public static async Task<Guid> SeedConversationAsync(
        this CompanySettingsDatabaseFixture db,
        Guid org,
        Guid branch,
        Guid? workOrder,
        Guid author,
        string body,
        DateTimeOffset at)
    {
        var id = Guid.NewGuid();

        await db.ExecuteAsync(
            """
            INSERT INTO conversations (id, organization_id, branch_id, type, work_order_id, last_message_at)
            VALUES (@id, @org, @branch, CASE WHEN CAST(@wo AS uuid) IS NULL THEN 'team' ELSE 'job' END, @wo, @at);
            INSERT INTO conversation_messages (organization_id, conversation_id, author_user_id, client_message_id, body, created_at)
            VALUES (@org, @id, @author, gen_random_uuid(), @body, @at);
            """,
            ("id", id),
            ("org", org),
            ("branch", branch),
            ("wo", workOrder),
            ("author", author),
            ("body", body),
            ("at", at));

        return id;
    }

    public static Task AddMessagesAsync(
        this CompanySettingsDatabaseFixture db, Guid org, Guid conversation, Guid author, int count, DateTimeOffset first) =>
        db.ExecuteAsync(
            """
            INSERT INTO conversation_messages (organization_id, conversation_id, author_user_id, client_message_id, body, created_at)
            SELECT @org, @c, @author, gen_random_uuid(), 'm' || n, @first + make_interval(secs => n / 2)
            FROM generate_series(1, @count) AS n;
            UPDATE conversations SET last_message_at = (SELECT max(created_at) FROM conversation_messages WHERE conversation_id = @c) WHERE id = @c
            """,
            ("org", org),
            ("c", conversation),
            ("author", author),
            ("count", count),
            ("first", first));

}
