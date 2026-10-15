using System.Data.Common;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.ServiceRequests;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FieldOps.IntegrationTests.Notifications;

internal static class NotificationSeed
{
    public const short Owner = CompanySettingsDatabaseFixture.OwnerRoleId;
    public const short Dispatcher = CompanySettingsDatabaseFixture.DispatcherRoleId;
    public const short Technician = CompanySettingsDatabaseFixture.TechnicianRoleId;
    public const short Accounting = CompanySettingsDatabaseFixture.AccountingRoleId;
    public const short Operations = CompanySettingsDatabaseFixture.OperationsManagerRoleId;
    public const short Viewer = CompanySettingsDatabaseFixture.ViewerRoleId;

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    /// <summary>Inserts one notification exactly as the writer stores it (BR-06); the text arguments are test constants.</summary>
    public static async Task<Guid> SeedNotificationAsync(
        this CompanySettingsDatabaseFixture db,
        Guid org,
        Guid user,
        string code,
        DateTimeOffset createdAt,
        bool read = false,
        Guid? branch = null,
        string kind = "request",
        Guid? entityId = null,
        string? title = null,
        string description = "A description",
        string label = "REQ-1",
        string? secondary = null,
        string channel = "in_app")
    {
        var id = Guid.NewGuid();
        var payload = JsonSerializer.Serialize(
            new
            {
                type = code[..code.IndexOf('.', StringComparison.Ordinal)],
                description,
                entityKind = kind,
                entityId = entityId ?? Guid.NewGuid(),
                entityLabel = label,
                secondaryLabel = secondary,
                branchId = branch,
            },
            Web);

        await db.ExecuteAsync(
            """
            INSERT INTO notifications (id, organization_id, recipient_user_id, channel, template_code, subject, payload, status, scheduled_at, sent_at, created_at)
            VALUES (@id, @org, @user, @channel, @code, @subject, CAST(@payload AS jsonb), CAST(@status AS notification_status), @at, @at, @at)
            """,
            ("id", id),
            ("org", org),
            ("user", user),
            ("channel", channel),
            ("code", code),
            ("subject", title ?? $"Title {code}"),
            ("payload", payload),
            ("status", read ? "read" : "sent"),
            ("at", createdAt.ToUniversalTime()));

        return id;
    }

    public static Task<string> StatusAsync(this CompanySettingsDatabaseFixture db, Guid id) =>
        db.ScalarAsync<string>("SELECT status::text FROM notifications WHERE id = @id", ("id", id));

    public static async Task<JsonNode> GetAsync(RequestsHost host, string cookie, string path)
    {
        var response = await host.SendAsync(HttpMethod.Get, path, cookie);

        return await ReadAsync(response);
    }

    public static async Task<JsonNode> PostAsync(RequestsHost host, string cookie, string path)
    {
        var response = await host.SendAsync(HttpMethod.Post, path, cookie);

        return await ReadAsync(response);
    }

    public static async Task<JsonNode> ReadAsync(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"{response.RequestMessage?.RequestUri?.PathAndQuery}: {(int)response.StatusCode} {body}");

        return JsonNode.Parse(body)!;
    }

    public static Guid[] Ids(JsonNode page) =>
        [.. page["items"]!.AsArray().Select(item => item!["id"]!.GetValue<Guid>())];

    public static (int All, int Unread) Counts(JsonNode page) =>
        (page["counts"]!["all"]!.GetValue<int>(), page["counts"]!["unread"]!.GetValue<int>());

    public static (int Unread, int Today) Summary(JsonNode summary) =>
        (summary["unread"]!.GetValue<int>(), summary["today"]!.GetValue<int>());

    /// <summary>The identical 404 problem of a missing, foreign, hidden or other-recipient notification, without the trace id.</summary>
    public static async Task<string> NotFoundBodyAsync(HttpResponseMessage response, string code)
    {
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        problem.Remove("traceId");
        Assert.Equal(code, problem["code"]!.GetValue<string>());

        return problem.ToJsonString();
    }
}

/// <summary>Counts the commands a request sends, so the tests can prove the count does not depend on the page size (BR-24).</summary>
internal sealed class CommandCounter : DbCommandInterceptor
{
    private int _count;

    public int Count => Volatile.Read(ref _count);

    public void Reset() => Volatile.Write(ref _count, 0);

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Interlocked.Increment(ref _count);

        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _count);

        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        Interlocked.Increment(ref _count);

        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _count);

        return ValueTask.FromResult(result);
    }
}
