using FieldOps.Infrastructure.Authentication;
using FieldOps.Infrastructure.Persistence;
using FieldOps.IntegrationTests.Api;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace FieldOps.IntegrationTests.CompanySettings;

[CollectionDefinition(Name)]
public sealed class CompanySettingsDatabaseCollection : ICollectionFixture<CompanySettingsDatabaseFixture>
{
    public const string Name = "company-settings-database";
}

/// <summary>
/// One disposable PostgreSQL container for organization-settings and branch
/// tests. Migrations are applied only to this throwaway container, never to
/// a local database. Tests seed their own rows with unique names/emails, so
/// they never share data.
/// </summary>
public sealed class CompanySettingsDatabaseFixture : IAsyncLifetime
{
    public const string Password = "Correct horse battery staple 1";

    public const short OwnerRoleId = 1;

    public const short DispatcherRoleId = 2;

    public const short TechnicianRoleId = 3;

    public const short AccountingRoleId = 4;

    public const short OperationsManagerRoleId = 5;

    public const short ViewerRoleId = 6;

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public string ConnectionString => _postgres.GetConnectionString();

    /// <summary>Hash of <see cref="Password"/> in the BR-20 format.</summary>
    public string PasswordHash { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        await using (var factory = FieldOpsApiFactory.Create(connectionString: ConnectionString))
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<FieldOpsDbContext>();
            await dbContext.Database.MigrateAsync();
        }

        PasswordHash = new Pbkdf2PasswordHasher().Hash(Password);
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    public static string NewEmail() => $"user-{Guid.NewGuid():N}@example.com";

    public async Task<Guid> SeedOrganizationAsync(string? name = null, bool isActive = true)
    {
        var id = Guid.NewGuid();

        await ExecuteAsync(
            "INSERT INTO organizations (id, name, is_active) VALUES (@id, @name, @isActive)",
            ("id", id),
            ("name", name ?? $"Organization {id:N}"[..30]),
            ("isActive", isActive));

        return id;
    }

    public async Task<Guid> SeedUserAsync(string? email = null)
    {
        var id = Guid.NewGuid();

        await ExecuteAsync(
            """
            INSERT INTO users (id, email, password_hash, first_name, last_name, status, email_verified_at)
            VALUES (@id, @email, @hash, 'Ada', 'Lovelace', 'active', @verifiedAt)
            """,
            ("id", id),
            ("email", email ?? NewEmail()),
            ("hash", PasswordHash),
            ("verifiedAt", DateTimeOffset.UtcNow));

        return id;
    }

    public async Task<Guid> SeedMembershipAsync(
        Guid organizationId, Guid userId, short roleId, bool isAllBranches = true)
    {
        var id = Guid.NewGuid();

        await ExecuteAsync(
            """
            INSERT INTO organization_users (id, organization_id, user_id, role_id, status, is_all_branches, joined_at)
            VALUES (@id, @organizationId, @userId, @roleId, 'active', @isAllBranches, @joinedAt)
            """,
            ("id", id),
            ("organizationId", organizationId),
            ("userId", userId),
            ("roleId", roleId),
            ("isAllBranches", isAllBranches),
            ("joinedAt", DateTimeOffset.UtcNow));

        return id;
    }

    /// <summary>Seeds an active user with one active membership of the given role in an active organization.</summary>
    public async Task<SeededAccount> SeedAccountAsync(
        short roleId = OwnerRoleId, bool isAllBranches = true, string? organizationName = null, Guid? organizationId = null)
    {
        var email = NewEmail();
        var orgId = organizationId ?? await SeedOrganizationAsync(organizationName);
        var userId = await SeedUserAsync(email);
        var membershipId = await SeedMembershipAsync(orgId, userId, roleId, isAllBranches);

        return new SeededAccount(userId, email, orgId, membershipId);
    }

    public async Task<SeededBranch> SeedBranchAsync(
        Guid organizationId, string? name = null, string? code = null, bool isActive = true)
    {
        var id = Guid.NewGuid();
        var branchCode = code ?? $"B{id:N}".ToUpperInvariant()[..8];

        await ExecuteAsync(
            """
            INSERT INTO branches (id, organization_id, name, code, is_active)
            VALUES (@id, @organizationId, @name, @code, @isActive)
            """,
            ("id", id),
            ("organizationId", organizationId),
            ("name", name ?? $"Branch {id:N}"[..20]),
            ("code", branchCode),
            ("isActive", isActive));

        var updatedAt = await ScalarAsync<DateTimeOffset>(
            "SELECT updated_at FROM branches WHERE id = @id", ("id", id));

        return new SeededBranch(id, branchCode, updatedAt);
    }

    public Task LinkMembershipToBranchAsync(Guid membershipId, Guid branchId) =>
        ExecuteAsync(
            "INSERT INTO organization_user_branches (organization_user_id, branch_id) VALUES (@membershipId, @branchId)",
            ("membershipId", membershipId),
            ("branchId", branchId));

    public Task<DateTimeOffset> GetOrganizationUpdatedAtAsync(Guid organizationId) =>
        ScalarAsync<DateTimeOffset>(
            "SELECT updated_at FROM organizations WHERE id = @id", ("id", organizationId))!;

    public Task<DateTimeOffset> GetBranchUpdatedAtAsync(Guid branchId) =>
        ScalarAsync<DateTimeOffset>(
            "SELECT updated_at FROM branches WHERE id = @id", ("id", branchId))!;

    public Task<int> CountAuditLogsAsync(Guid organizationId, string action) =>
        ScalarAsync<long>(
                "SELECT COUNT(*) FROM audit_logs WHERE organization_id = @organizationId AND action = @action",
                ("organizationId", organizationId),
                ("action", action))
            .ContinueWith(t => (int)t.Result, TaskScheduler.Default);

    /// <summary>The most recent audit row's after_data for the given action, or null if none exists.</summary>
    public async Task<string?> GetLatestAuditAfterDataAsync(Guid organizationId, string action)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = CreateCommand(
            connection,
            """
            SELECT after_data::text FROM audit_logs
            WHERE organization_id = @organizationId AND action = @action
            ORDER BY occurred_at DESC, id DESC
            LIMIT 1
            """,
            [("organizationId", organizationId), ("action", action)]);

        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? null : (string)value;
    }

    /// <summary>Directly inserts <paramref name="count"/> minimal branches, bypassing HTTP (BR-05 cap tests).</summary>
    public async Task SeedManyBranchesAsync(Guid organizationId, int count)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        for (var i = 0; i < count; i++)
        {
            await using var command = new NpgsqlCommand(
                "INSERT INTO branches (id, organization_id, name, code) VALUES (@id, @organizationId, @name, @code)",
                connection);
            command.Parameters.AddWithValue("id", Guid.NewGuid());
            command.Parameters.AddWithValue("organizationId", organizationId);
            command.Parameters.AddWithValue("name", $"Branch {i:0000}");
            command.Parameters.AddWithValue("code", $"B{i:0000000}"[..8]);
            await command.ExecuteNonQueryAsync();
        }
    }

    public async Task ExecuteAsync(string sql, params (string Name, object? Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = CreateCommand(connection, sql, parameters);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<T> ScalarAsync<T>(string sql, params (string Name, object? Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = CreateCommand(connection, sql, parameters);
        var value = await command.ExecuteScalarAsync();

        // Npgsql returns timestamptz columns as DateTime (Kind=Utc), not
        // DateTimeOffset; convert explicitly for the BR-07 concurrency-token
        // helpers below.
        if (typeof(T) == typeof(DateTimeOffset) && value is DateTime dateTime)
        {
            return (T)(object)new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc));
        }

        return (T)value!;
    }

    private static NpgsqlCommand CreateCommand(
        NpgsqlConnection connection, string sql, (string Name, object? Value)[] parameters)
    {
        var command = new NpgsqlCommand(sql, connection);

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return command;
    }
}

public sealed record SeededAccount(Guid UserId, string Email, Guid OrganizationId, Guid MembershipId);

public sealed record SeededBranch(Guid Id, string Code, DateTimeOffset UpdatedAt);
