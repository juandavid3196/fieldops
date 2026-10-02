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
            "INSERT INTO organizations (id, name, is_active, public_slug) VALUES (@id, @name, @isActive, 'org-' || replace(CAST(@id AS text), '-', ''))",
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
        Guid organizationId, string? name = null, string? code = null, bool isActive = true, bool isMain = false)
    {
        var id = Guid.NewGuid();
        var branchCode = code ?? $"B{id:N}".ToUpperInvariant()[..8];

        await ExecuteAsync(
            """
            INSERT INTO branches (id, organization_id, name, code, is_active, is_main)
            VALUES (@id, @organizationId, @name, @code, @isActive, @isMain)
            """,
            ("id", id),
            ("organizationId", organizationId),
            ("name", name ?? $"Branch {id:N}"[..20]),
            ("code", branchCode),
            ("isActive", isActive),
            ("isMain", isMain));

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

    public Task SeedTechnicianAsync(Guid organizationId, Guid branchId, string status = "active") =>
        ExecuteAsync(
            """
            INSERT INTO technician_profiles (organization_id, branch_id, first_name, last_name, status)
            VALUES (@organizationId, @branchId, 'Tech', 'Nician', @status)
            """,
            ("organizationId", organizationId),
            ("branchId", branchId),
            ("status", status));

    /// <summary>
    /// Seeds a minimal quote, work order and invoice for the organization with
    /// the given numbers. Foreign keys to unrelated parents are bypassed for
    /// this throwaway container only (session_replication_role = replica).
    /// </summary>
    public async Task SeedDocumentNumbersAsync(
        Guid organizationId, Guid userId, Guid branchId, long? quoteNumber = null, long? workOrderNumber = null, long? invoiceNumber = null)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using (var replica = new NpgsqlCommand("SET session_replication_role = replica", connection))
        {
            await replica.ExecuteNonQueryAsync();
        }

        var statements = new List<string>();

        if (quoteNumber is not null)
        {
            statements.Add(
                """
                INSERT INTO quotes (organization_id, request_id, quote_number, status, current_version_no, created_by_user_id)
                VALUES (@org, gen_random_uuid(), @quote, 'draft', 0, @user)
                """);
        }

        if (workOrderNumber is not null)
        {
            statements.Add(
                """
                INSERT INTO work_orders (organization_id, branch_id, work_order_number, quote_version_id, customer_id, property_id, status, priority, scope_snapshot, created_by_user_id)
                VALUES (@org, @branch, @workOrder, gen_random_uuid(), gen_random_uuid(), gen_random_uuid(), 'draft', 3, 'scope', @user)
                """);
        }

        if (invoiceNumber is not null)
        {
            statements.Add(
                """
                INSERT INTO invoices (organization_id, branch_id, invoice_number, work_order_id, customer_id, status, currency, subtotal, tax_total, total, amount_paid, balance_due, created_by_user_id)
                VALUES (@org, @branch, @invoice, gen_random_uuid(), gen_random_uuid(), 'draft', 'USD', 0, 0, 0, 0, 0, @user)
                """);
        }

        foreach (var sql in statements)
        {
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("org", organizationId);
            command.Parameters.AddWithValue("user", userId);
            command.Parameters.AddWithValue("branch", branchId);
            command.Parameters.AddWithValue("quote", quoteNumber ?? 0L);
            command.Parameters.AddWithValue("workOrder", workOrderNumber ?? 0L);
            command.Parameters.AddWithValue("invoice", invoiceNumber ?? 0L);
            await command.ExecuteNonQueryAsync();
        }
    }

    /// <summary>The latest audit row's before/after/metadata JSON text for the action.</summary>
    public async Task<(string? Before, string? After, string? Metadata)> GetLatestAuditAsync(
        Guid organizationId, string action)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = CreateCommand(
            connection,
            """
            SELECT before_data::text, after_data::text, metadata::text FROM audit_logs
            WHERE organization_id = @organizationId AND action = @action
            ORDER BY occurred_at DESC, id DESC
            LIMIT 1
            """,
            [("organizationId", organizationId), ("action", action)]);

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), $"No audit row for {action}");

        return (
            reader.IsDBNull(0) ? null : reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2));
    }

    public async Task<Guid[]> QueryGuidsAsync(string sql, params (string Name, object? Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = CreateCommand(connection, sql, parameters);
        await using var reader = await command.ExecuteReaderAsync();
        var ids = new List<Guid>();

        while (await reader.ReadAsync())
        {
            ids.Add(reader.GetGuid(0));
        }

        return [.. ids];
    }

    public Task SeedLogoAsync(Guid organizationId, byte[] content) =>
        ExecuteAsync(
            "INSERT INTO organization_logos (organization_id, content_type, content, size_bytes) VALUES (@o, 'image/png', @c, @s)",
            ("o", organizationId),
            ("c", content),
            ("s", content.Length));

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
