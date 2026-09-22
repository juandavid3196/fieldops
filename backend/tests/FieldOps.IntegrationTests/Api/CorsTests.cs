using Microsoft.Extensions.Hosting;

namespace FieldOps.IntegrationTests.Api;

public class CorsTests
{
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

    private static HttpRequestMessage CreatePreflight(string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/health");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "GET");
        return request;
    }
}
