using System.Net;
using System.Text.Json;
using Testcontainers.PostgreSql;

namespace FieldOps.IntegrationTests.Api;

public class HealthCheckTests
{
    [Fact]
    public async Task Health_WhenDatabaseIsUnreachable_ReturnsServiceUnavailable()
    {
        await using var factory = FieldOpsApiFactory.Create();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        using var report = JsonDocument.Parse(body);
        Assert.Equal("Unhealthy", report.RootElement.GetProperty("status").GetString());
        Assert.Equal("Healthy", GetCheckStatus(report, "self"));
        Assert.Equal("Unhealthy", GetCheckStatus(report, "postgresql"));

        Assert.DoesNotContain("Host=", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("not-a-secret", body);
    }

    internal static string? GetCheckStatus(JsonDocument report, string name) =>
        report.RootElement
            .GetProperty("checks")
            .EnumerateArray()
            .Single(check => check.GetProperty("name").GetString() == name)
            .GetProperty("status")
            .GetString();
}

public class PostgreSqlHealthCheckTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Health_WhenDatabaseIsReachable_ReturnsHealthy()
    {
        await using var factory = FieldOpsApiFactory.Create(
            connectionString: _postgres.GetConnectionString());
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var report = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Healthy", report.RootElement.GetProperty("status").GetString());
        Assert.Equal("Healthy", HealthCheckTests.GetCheckStatus(report, "self"));
        Assert.Equal("Healthy", HealthCheckTests.GetCheckStatus(report, "postgresql"));
    }
}
