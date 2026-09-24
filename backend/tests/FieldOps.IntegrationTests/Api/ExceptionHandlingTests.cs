using System.Net;
using System.Text.Json;
using FieldOps.Api.Middleware;

namespace FieldOps.IntegrationTests.Api;

public class ExceptionHandlingTests
{
    [Fact]
    public async Task UnhandledException_ReturnsProblemDetailsWithoutExceptionDetails()
    {
        await using var factory = FieldOpsApiFactory.Create();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/test/throw");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var problem = JsonDocument.Parse(body);
        Assert.Equal(500, problem.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("An unexpected error occurred.", problem.RootElement.GetProperty("title").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.RootElement.GetProperty("traceId").GetString()));
        Assert.False(problem.RootElement.TryGetProperty("detail", out _));
        Assert.DoesNotContain(ThrowingTestController.ExceptionMessage, body);
        Assert.DoesNotContain("InvalidOperationException", body);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData(FieldOpsApiFactory.TestingEnvironment)]
    [InlineData("Production")]
    public async Task UnhandledException_InAnyEnvironment_ReturnsGenericProblemWithoutExceptionDetails(
        string environment)
    {
        await using var factory = FieldOpsApiFactory.Create(environment);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/test/throw");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var problem = JsonDocument.Parse(body);
        Assert.Equal("An unexpected error occurred.", problem.RootElement.GetProperty("title").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.RootElement.GetProperty("traceId").GetString()));
        Assert.False(problem.RootElement.TryGetProperty("detail", out _));
        Assert.DoesNotContain(ThrowingTestController.ExceptionMessage, body);
        Assert.DoesNotContain("InvalidOperationException", body);
        Assert.DoesNotContain("   at ", body);
    }

    [Fact]
    public async Task UnhandledException_WhenHandled_LogsExceptionOnce()
    {
        var logs = new CapturingLoggerProvider();
        await using var baseFactory = FieldOpsApiFactory.Create();
        await using var factory = logs.AttachTo(baseFactory);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/test/throw");
        await response.Content.ReadAsStringAsync();

        var entry = Assert.Single(logs.Entries, entry => entry.Exception is not null);
        Assert.Equal(typeof(GlobalExceptionHandler).FullName, entry.Category);
        var exception = Assert.IsType<InvalidOperationException>(entry.Exception);
        Assert.Equal(ThrowingTestController.ExceptionMessage, exception.Message);
    }

    [Fact]
    public async Task UnhandledException_WhenClientDoesNotAcceptJson_Returns500AndLogsOnce()
    {
        var logs = new CapturingLoggerProvider();
        await using var baseFactory = FieldOpsApiFactory.Create();
        await using var factory = logs.AttachTo(baseFactory);
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/test/throw");
        request.Headers.Accept.ParseAdd("text/html");

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Empty(body);
        Assert.DoesNotContain(ThrowingTestController.ExceptionMessage, body);

        var entry = Assert.Single(logs.Entries, entry => entry.Exception is not null);
        Assert.IsType<InvalidOperationException>(entry.Exception);
    }

    [Fact]
    public async Task UnknownRoute_ReturnsProblemDetails()
    {
        await using var factory = FieldOpsApiFactory.Create();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}
