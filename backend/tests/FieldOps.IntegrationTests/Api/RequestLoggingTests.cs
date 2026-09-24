using System.Net;
using System.Text.Json;
using FieldOps.Api.Middleware;
using Microsoft.Extensions.Logging;

namespace FieldOps.IntegrationTests.Api;

public class RequestLoggingTests
{
    // Not hexadecimal, so it can never appear inside a logged trace id.
    private const string QueryValue = "query-secret";

    [Fact]
    public async Task Request_WithQueryString_LogsPathWithoutQuery()
    {
        var logs = new CapturingLoggerProvider();
        await using var baseFactory = FieldOpsApiFactory.Create();
        await using var factory = logs.AttachTo(baseFactory);
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/does-not-exist?token={QueryValue}");
        await response.Content.ReadAsStringAsync();

        var entry = Assert.Single(RequestEntries(logs));
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("GET", entry.Message);
        Assert.Contains("/does-not-exist", entry.Message);
        Assert.Contains("404", entry.Message);
        Assert.DoesNotContain("token", entry.Message);
        Assert.DoesNotContain(QueryValue, entry.Message);
    }

    [Fact]
    public async Task HealthLive_WhenHealthy_LogsRequestAtDebug()
    {
        var logs = new CapturingLoggerProvider();
        await using var baseFactory = FieldOpsApiFactory.Create();
        await using var factory = logs.AttachTo(baseFactory);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live");
        await response.Content.ReadAsStringAsync();

        var entry = Assert.Single(RequestEntries(logs));
        Assert.Equal(LogLevel.Debug, entry.Level);
        Assert.Contains("/health/live", entry.Message);
        Assert.Contains("200", entry.Message);
    }

    [Fact]
    public async Task Request_WhenUnhandledExceptionOccurs_LogsAtErrorWithStatus500()
    {
        var logs = new CapturingLoggerProvider();
        await using var baseFactory = FieldOpsApiFactory.Create();
        await using var factory = logs.AttachTo(baseFactory);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/test/throw");
        await response.Content.ReadAsStringAsync();

        var entry = Assert.Single(RequestEntries(logs));
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Contains("/test/throw", entry.Message);
        Assert.Contains("500", entry.Message);
        Assert.Null(entry.Exception);
    }

    [Fact]
    public async Task Request_WhenUnhandledExceptionOccurs_LogsTraceIdMatchingProblemDetails()
    {
        var logs = new CapturingLoggerProvider();
        await using var baseFactory = FieldOpsApiFactory.Create();
        await using var factory = logs.AttachTo(baseFactory);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/test/throw");
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var problemTraceId = problem.RootElement.GetProperty("traceId").GetString();

        var entry = Assert.Single(RequestEntries(logs));
        Assert.False(string.IsNullOrWhiteSpace(problemTraceId));
        Assert.Equal(problemTraceId, entry.Properties["TraceId"]);
        Assert.Contains(problemTraceId, entry.Message);
    }

    [Fact]
    public async Task Request_WhenSuccessful_LogsAtInformation()
    {
        var logs = new CapturingLoggerProvider();
        await using var baseFactory = FieldOpsApiFactory.Create();
        await using var factory = logs.AttachTo(baseFactory);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/test/logging/ok");
        await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var entry = Assert.Single(RequestEntries(logs));
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Contains("/test/logging/ok", entry.Message);
        Assert.Contains("200", entry.Message);
    }

    [Fact]
    public async Task HealthReady_WhenDatabaseIsUnreachable_LogsRequestAtErrorWithStatus503()
    {
        var logs = new CapturingLoggerProvider();
        await using var baseFactory = FieldOpsApiFactory.Create();
        await using var factory = logs.AttachTo(baseFactory);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready");
        await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var entry = Assert.Single(RequestEntries(logs));
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Contains("/health/ready", entry.Message);
        Assert.Contains("503", entry.Message);
    }

    [Fact]
    public async Task Request_WhenExceptionEscapesAfterResponseStarted_LogsStartedStatusCode()
    {
        var logs = new CapturingLoggerProvider();
        await using var baseFactory = FieldOpsApiFactory.Create();
        await using var factory = logs.AttachTo(baseFactory);
        using var client = factory.CreateClient();

        // The client sees a 200 followed by an aborted body; reading the body
        // until it fails guarantees the pipeline (and its log) has finished.
        await Record.ExceptionAsync(async () =>
        {
            using var response = await client.GetAsync(
                "/test/logging/throw-after-start",
                HttpCompletionOption.ResponseHeadersRead);
            await response.Content.ReadAsStringAsync();
        });

        var entry = Assert.Single(RequestEntries(logs));
        Assert.Contains("/test/logging/throw-after-start", entry.Message);
        Assert.Equal(200, entry.Properties["StatusCode"]);
        Assert.Null(entry.Exception);
    }

    private static IEnumerable<CapturedLogEntry> RequestEntries(CapturingLoggerProvider logs) =>
        logs.Entries.Where(entry => entry.Category == typeof(RequestLoggingMiddleware).FullName);
}
