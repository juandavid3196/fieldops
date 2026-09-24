using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Hosting;

namespace FieldOps.IntegrationTests.Api;

public class CorsTests
{
    private const string LocalAngularOrigin = "http://localhost:4200";

    [Fact]
    public async Task Preflight_FromConfiguredOrigin_IsAllowed()
    {
        await using var factory = FieldOpsApiFactory.Create();
        using var client = factory.CreateClient();

        var response = await client.SendAsync(CreatePreflight(FieldOpsApiFactory.AllowedOrigin));

        Assert.True(response.Headers.TryGetValues("Access-Control-Allow-Origin", out var origins));
        Assert.Equal(FieldOpsApiFactory.AllowedOrigin, Assert.Single(origins));
    }

    [Fact]
    public async Task Preflight_FromUnknownOrigin_IsNotAllowed()
    {
        await using var factory = FieldOpsApiFactory.Create();
        using var client = factory.CreateClient();

        var response = await client.SendAsync(CreatePreflight("https://evil.example"));

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Development_AllowsLocalAngularApplication()
    {
        const string angularOrigin = "http://localhost:4200";

        await using var factory = new FieldOpsApiFactory(
            Environments.Development,
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:FieldOpsDatabase"] = FieldOpsApiFactory.UnreachableConnectionString,
            });
        using var client = factory.CreateClient();

        var response = await client.SendAsync(CreatePreflight(angularOrigin));

        Assert.True(response.Headers.TryGetValues("Access-Control-Allow-Origin", out var origins));
        Assert.Equal(angularOrigin, Assert.Single(origins));
    }

    [Fact]
    public async Task Get_FromConfiguredOrigin_ReturnsAllowOriginHeader()
    {
        await using var factory = FieldOpsApiFactory.Create();
        using var client = factory.CreateClient();

        var response = await client.SendAsync(CreateGet("/health/live", FieldOpsApiFactory.AllowedOrigin));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("Access-Control-Allow-Origin", out var origins));
        Assert.Equal(FieldOpsApiFactory.AllowedOrigin, Assert.Single(origins));
    }

    [Fact]
    public async Task UnhandledException_FromLocalAngularApplicationInDevelopment_ReturnsSafeProblemWithAllowOriginHeader()
    {
        await using var factory = CreateDevelopmentFactory();
        using var client = factory.CreateClient();

        var response = await client.SendAsync(CreateGet("/test/throw", LocalAngularOrigin));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.TryGetValues("Access-Control-Allow-Origin", out var origins));
        Assert.Equal(LocalAngularOrigin, Assert.Single(origins));

        using var problem = JsonDocument.Parse(body);
        Assert.Equal("An unexpected error occurred.", problem.RootElement.GetProperty("title").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.RootElement.GetProperty("traceId").GetString()));
        Assert.False(problem.RootElement.TryGetProperty("detail", out _));
        Assert.DoesNotContain(ThrowingTestController.ExceptionMessage, body);
        Assert.DoesNotContain("InvalidOperationException", body);
        Assert.DoesNotContain("   at ", body);
    }

    [Fact]
    public async Task UnhandledException_FromUnknownOrigin_ReturnsProblemWithoutAllowOriginHeader()
    {
        await using var factory = CreateDevelopmentFactory();
        using var client = factory.CreateClient();

        var response = await client.SendAsync(CreateGet("/test/throw", "https://evil.example"));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task UnhandledException_WhenClientDoesNotAcceptJsonFromConfiguredOrigin_ReturnsEmptyBodyWithAllowOriginHeader()
    {
        await using var factory = FieldOpsApiFactory.Create();
        using var client = factory.CreateClient();

        using var request = CreateGet("/test/throw", FieldOpsApiFactory.AllowedOrigin);
        request.Headers.Accept.ParseAdd("text/html");

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Empty(body);
        Assert.True(response.Headers.TryGetValues("Access-Control-Allow-Origin", out var origins));
        Assert.Equal(FieldOpsApiFactory.AllowedOrigin, Assert.Single(origins));
    }

    // Pins the Development origin so developer user-secrets cannot change it.
    private static FieldOpsApiFactory CreateDevelopmentFactory() =>
        FieldOpsApiFactory.Create(Environments.Development, allowedOrigins: LocalAngularOrigin);

    private static HttpRequestMessage CreateGet(string path, string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("Origin", origin);
        return request;
    }

    private static HttpRequestMessage CreatePreflight(string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/health");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "GET");
        return request;
    }
}
