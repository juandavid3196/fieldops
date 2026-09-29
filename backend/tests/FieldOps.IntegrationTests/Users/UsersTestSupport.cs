using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FieldOps.Application.Features.Users;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.Sessions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FieldOps.IntegrationTests.Users;

/// <summary>Records deliveries (and can fail them) instead of the no-op adapter.</summary>
public sealed class RecordingInvitationDelivery : IInvitationDelivery
{
    private readonly List<InvitationDeliveryMessage> _messages = [];

    public bool Fail { get; set; }

    public IReadOnlyList<InvitationDeliveryMessage> Messages
    {
        get
        {
            lock (_messages)
            {
                return [.. _messages];
            }
        }
    }

    public Task SendAsync(InvitationDeliveryMessage message, CancellationToken cancellationToken)
    {
        if (Fail)
        {
            throw new InvalidOperationException("delivery down");
        }

        lock (_messages)
        {
            _messages.Add(message);
        }

        return Task.CompletedTask;
    }

    /// <summary>The raw token embedded in the accept link of a delivered message.</summary>
    public static string TokenOf(InvitationDeliveryMessage message) =>
        Uri.UnescapeDataString(message.AcceptLink[(message.AcceptLink.IndexOf("token=", StringComparison.Ordinal) + 6)..]);

    public static string HashOf(string rawToken) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
}

/// <summary>One API host whose delivery port is the recording adapter.</summary>
public sealed class UsersHost : IAsyncDisposable
{
    private readonly SessionTestHost _host;

    private UsersHost(SessionTestHost host, HttpClient client, RecordingInvitationDelivery delivery)
    {
        _host = host;
        Client = client;
        Delivery = delivery;
    }

    public HttpClient Client { get; }

    public RecordingInvitationDelivery Delivery { get; }

    public CapturingLogs? Logs => _host.Logs is null ? null : new CapturingLogs(_host.Logs);

    public static UsersHost Create(CompanySettingsDatabaseFixture database, bool captureLogs = false)
    {
        var host = SessionTestHost.Create(database.ConnectionString, captureLogs: captureLogs);
        var delivery = new RecordingInvitationDelivery();

        var factory = host.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IInvitationDelivery>();
            services.AddSingleton<IInvitationDelivery>(delivery);
        }));

        return new UsersHost(host, SessionApi.CreateClient(factory), delivery);
    }

    public Task<string> SignInAsync(string email) => CompanySettingsApi.SignInCookieAsync(Client, email);

    public Task<HttpResponseMessage> GetAsync(string path, string? cookie) =>
        CompanySettingsApi.GetAsync(Client, path, cookie);

    public Task<HttpResponseMessage> PostAsync(string path, string? cookie, JsonObject? body = null) =>
        CompanySettingsApi.PostAsync(Client, path, body, cookie);

    public Task<HttpResponseMessage> PutAsync(string path, string? cookie, JsonObject body) =>
        CompanySettingsApi.PutAsync(Client, path, body, cookie);

    public async Task<JsonNode> ReadAsync(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!;

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _host.DisposeAsync();
    }
}

public sealed class CapturingLogs(FieldOps.IntegrationTests.Api.CapturingLoggerProvider provider)
{
    /// <summary>All captured message and exception text, for leak assertions.</summary>
    public string AllText() =>
        string.Join('\n', provider.Entries.Select(entry => $"{entry.Message}\n{entry.Exception}"));
}

public sealed record SeededMember(Guid UserId, Guid MembershipId, string Email);

public static class UsersSeed
{
    public static async Task<SeededMember> SeedMemberAsync(
        this CompanySettingsDatabaseFixture db,
        Guid organizationId,
        short roleId,
        string firstName,
        string lastName,
        string? email = null,
        string status = "active",
        bool isAllBranches = true,
        DateTimeOffset? lastLoginAt = null,
        Guid? existingUserId = null)
    {
        var userId = existingUserId ?? Guid.NewGuid();
        var address = email ?? CompanySettingsDatabaseFixture.NewEmail();

        if (existingUserId is null)
        {
            await db.ExecuteAsync(
                """
                INSERT INTO users (id, email, password_hash, first_name, last_name, status, email_verified_at, last_login_at)
                VALUES (@id, @email, @hash, @first, @last, 'active', now(), @lastLogin)
                """,
                ("id", userId),
                ("email", address),
                ("hash", db.PasswordHash),
                ("first", firstName),
                ("last", lastName),
                ("lastLogin", lastLoginAt));
        }

        var membershipId = Guid.NewGuid();

        // status is a test-controlled constant, never user input.
        await db.ExecuteAsync(
            $"""
            INSERT INTO organization_users (id, organization_id, user_id, role_id, status, is_all_branches, joined_at)
            VALUES (@id, @org, @user, @role, '{status}', @isAll, now())
            """,
            ("id", membershipId),
            ("org", organizationId),
            ("user", userId),
            ("role", roleId),
            ("isAll", isAllBranches));

        return new SeededMember(userId, membershipId, address);
    }

    public static Task SeedTechnicianProfileAsync(
        this CompanySettingsDatabaseFixture db, Guid organizationId, Guid branchId, Guid membershipId, string first, string last) =>
        db.ExecuteAsync(
            """
            INSERT INTO technician_profiles (organization_id, branch_id, organization_user_id, first_name, last_name, status)
            VALUES (@org, @branch, @member, @first, @last, 'active')
            """,
            ("org", organizationId),
            ("branch", branchId),
            ("member", membershipId),
            ("first", first),
            ("last", last));

    public static async Task<Guid> SeedInvitationAsync(
        this CompanySettingsDatabaseFixture db,
        Guid organizationId,
        Guid invitedByUserId,
        string firstName,
        string lastName,
        string email,
        short roleId,
        DateTimeOffset expiresAt,
        bool accepted = false,
        bool revoked = false,
        bool isAllBranches = true,
        Guid[]? branchIds = null,
        string? tokenHash = null,
        bool linkTeamProfile = false)
    {
        var id = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow < expiresAt ? DateTimeOffset.UtcNow : expiresAt.AddDays(-7);

        await db.ExecuteAsync(
            """
            INSERT INTO user_invitations
              (id, organization_id, email, first_name, last_name, role_id, is_all_branches, link_team_profile,
               token_hash, invited_by_user_id, expires_at, accepted_at, revoked_at, created_at)
            VALUES
              (@id, @org, @email, @first, @last, @role, @isAll, @link,
               @hash, @by, @expires, @accepted, @revoked, @created)
            """,
            ("id", id),
            ("org", organizationId),
            ("email", email),
            ("first", firstName),
            ("last", lastName),
            ("role", roleId),
            ("isAll", isAllBranches),
            ("link", linkTeamProfile),
            ("hash", tokenHash ?? $"seed-{Guid.NewGuid():N}"),
            ("by", invitedByUserId),
            ("expires", expiresAt),
            ("accepted", accepted ? DateTimeOffset.UtcNow : null),
            ("revoked", revoked ? DateTimeOffset.UtcNow : null),
            ("created", createdAt));

        foreach (var branchId in branchIds ?? [])
        {
            await db.ExecuteAsync(
                "INSERT INTO invitation_branches (invitation_id, branch_id) VALUES (@i, @b)",
                ("i", id),
                ("b", branchId));
        }

        return id;
    }

    public static async Task<long> CountAsync(this CompanySettingsDatabaseFixture db, string sql, params (string Name, object? Value)[] parameters) =>
        await db.ScalarAsync<long>(sql, parameters);

    public static Task<string> TextAsync(this CompanySettingsDatabaseFixture db, string sql, params (string Name, object? Value)[] parameters) =>
        db.ScalarAsync<string>(sql, parameters);

    public static async Task<long> CountAuditAsync(this CompanySettingsDatabaseFixture db, Guid organizationId, string action) =>
        await db.CountAsync(
            "SELECT COUNT(*) FROM audit_logs WHERE organization_id = @o AND action = @a", ("o", organizationId), ("a", action));

    public static string[] Items(JsonNode list, string property = "email") =>
        [.. list["items"]!.AsArray().Select(item => item![property]!.GetValue<string>())];

    public static JsonObject Body(params (string Key, object? Value)[] fields)
    {
        var body = new JsonObject();

        foreach (var (key, value) in fields)
        {
            body[key] = value switch
            {
                null => null,
                Guid[] guids => new JsonArray([.. guids.Select(guid => (JsonNode)guid.ToString())]),
                IEnumerable<Guid> guids => new JsonArray([.. guids.Select(guid => (JsonNode)guid.ToString())]),
                bool flag => flag,
                int number => number,
                string text => text,
                _ => JsonSerializer.SerializeToNode(value),
            };
        }

        return body;
    }
}
