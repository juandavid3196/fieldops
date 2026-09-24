using FieldOps.Infrastructure.Authentication;
using FieldOps.Infrastructure.Persistence;
using FieldOps.IntegrationTests.Api;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace FieldOps.IntegrationTests.Sessions;

[CollectionDefinition(Name)]
public sealed class SessionsDatabaseCollection : ICollectionFixture<SessionsDatabaseFixture>
{
    public const string Name = "sessions-database";
}

/// <summary>
/// One disposable PostgreSQL container for the session tests. Migrations
/// are applied only to this throwaway container, never to a local database.
/// Tests seed their own rows with unique emails, so they never share data.
/// </summary>
public sealed class SessionsDatabaseFixture : IAsyncLifetime
{
    public const string Password = "Correct horse battery staple 1";

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

    public async Task<Guid> SeedUserAsync(
        string email,
        string status = "active",
        string? passwordHash = null,
        bool emailVerified = true)
    {
        var id = Guid.NewGuid();

        await ExecuteAsync(
            """
            INSERT INTO users (id, email, password_hash, first_name, last_name, status, email_verified_at)
            VALUES (@id, @email, @hash, 'Ada', 'Lovelace', CAST(@status AS user_status), @verifiedAt)
            """,
            ("id", id),
            ("email", email),
            ("hash", passwordHash ?? PasswordHash),
            ("status", status),
            ("verifiedAt", emailVerified ? DateTimeOffset.UtcNow : (DateTimeOffset?)null));

        return id;
    }

    public async Task<Guid> SeedMembershipAsync(
        Guid organizationId,
        Guid userId,
        short roleId = 1,
        string status = "active",
        DateTimeOffset? joinedAt = null)
    {
        var id = Guid.NewGuid();

        // is_all_branches is explicit: the migrated column has no DEFAULT
        // (the schema file declares DEFAULT false).
        await ExecuteAsync(
            """
            INSERT INTO organization_users (id, organization_id, user_id, role_id, status, is_all_branches, joined_at)
            VALUES (@id, @organizationId, @userId, @roleId, CAST(@status AS user_status), false, @joinedAt)
            """,
            ("id", id),
            ("organizationId", organizationId),
            ("userId", userId),
            ("roleId", roleId),
            ("status", status),
            ("joinedAt", joinedAt ?? new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero)));

        return id;
    }

    /// <summary>
    /// Seeds an active user with one active owner membership in an active
    /// organization.
    /// </summary>
    public async Task<SeededAccount> SeedAccountAsync(string? organizationName = null)
    {
        var email = NewEmail();
        var organizationId = await SeedOrganizationAsync(organizationName);
        var userId = await SeedUserAsync(email);
        var membershipId = await SeedMembershipAsync(organizationId, userId);

        return new SeededAccount(userId, email, organizationId, membershipId);
    }

    public async Task ExecuteAsync(string sql, params (string Name, object? Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = CreateCommand(connection, sql, parameters);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<T?> ScalarAsync<T>(string sql, params (string Name, object? Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = CreateCommand(connection, sql, parameters);
        var value = await command.ExecuteScalarAsync();

        return value is null or DBNull ? default : (T)value;
    }

    public async Task<List<Dictionary<string, object?>>> QueryAsync(
        string sql,
        params (string Name, object? Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = CreateCommand(connection, sql, parameters);
        await using var reader = await command.ExecuteReaderAsync();

        var rows = new List<Dictionary<string, object?>>();

        while (await reader.ReadAsync())
        {
            var row = new Dictionary<string, object?>(StringComparer.Ordinal);

            for (var i = 0; i < reader.FieldCount; i++)
            {
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }

            rows.Add(row);
        }

        return rows;
    }

    private static NpgsqlCommand CreateCommand(
        NpgsqlConnection connection,
        string sql,
        (string Name, object? Value)[] parameters)
    {
        var command = new NpgsqlCommand(sql, connection);

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return command;
    }
}

public sealed record SeededAccount(
    Guid UserId,
    string Email,
    Guid OrganizationId,
    Guid MembershipId);
