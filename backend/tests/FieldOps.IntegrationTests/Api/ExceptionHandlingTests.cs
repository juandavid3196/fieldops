using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Hosting;

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
        Assert.True(problem.RootElement.TryGetProperty("traceId", out _));
        Assert.False(problem.RootElement.TryGetProperty("detail", out _));
        Assert.DoesNotContain(ThrowingTestController.ExceptionMessage, body);
        Assert.DoesNotContain("InvalidOperationException", body);
    }

    [Fact]
    public async Task UnhandledException_InDevelopment_IncludesExceptionMessage()
    {
        await using var factory = FieldOpsApiFactory.Create(Environments.Development);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/test/throw");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(
            ThrowingTestController.ExceptionMessage,
            problem.RootElement.GetProperty("detail").GetString());
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
