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

    [Fact]
    public async Task HealthLive_WhenDatabaseIsUnreachable_ReturnsHealthy()
    {
        await using var factory = FieldOpsApiFactory.Create();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var report = JsonDocument.Parse(body);
        Assert.Equal("Healthy", report.RootElement.GetProperty("status").GetString());
        var check = Assert.Single(report.RootElement.GetProperty("checks").EnumerateArray());
        Assert.Equal("self", check.GetProperty("name").GetString());
        Assert.Equal("Healthy", check.GetProperty("status").GetString());
        Assert.DoesNotContain("postgresql", body);
    }

    [Fact]
    public async Task HealthReady_WhenDatabaseIsUnreachable_ReturnsServiceUnavailable()
    {
        await using var factory = FieldOpsApiFactory.Create();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        using var report = JsonDocument.Parse(body);
        Assert.Equal("Unhealthy", report.RootElement.GetProperty("status").GetString());
        Assert.Equal("Unhealthy", GetCheckStatus(report, "postgresql"));

        Assert.DoesNotContain("Host=", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("not-a-secret", body);
        Assert.DoesNotContain("Npgsql", body);
        Assert.DoesNotContain("Exception", body);
        Assert.DoesNotContain("127.0.0.1", body);
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task HealthEndpoint_WhenRequested_ReturnsUncachedJson(string path)
    {
        await using var factory = FieldOpsApiFactory.Create();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(path);

        var cacheControl = response.Headers.CacheControl;
        Assert.NotNull(cacheControl);
        Assert.True(cacheControl.NoStore);
        Assert.True(cacheControl.NoCache);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
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

    [Fact]
    public async Task HealthReady_WhenDatabaseIsReachable_ReturnsHealthy()
    {
        await using var factory = FieldOpsApiFactory.Create(
            connectionString: _postgres.GetConnectionString());
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var report = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Healthy", report.RootElement.GetProperty("status").GetString());
        Assert.Equal("Healthy", HealthCheckTests.GetCheckStatus(report, "self"));
        Assert.Equal("Healthy", HealthCheckTests.GetCheckStatus(report, "postgresql"));
    }
}
