using System.Net;
using FieldOps.IntegrationTests.Api;

namespace FieldOps.IntegrationTests.Sessions;

public class SessionCorsTests
{
    public static TheoryData<string> Endpoints => new() { "POST", "GET", "DELETE" };

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task SessionEndpoint_FromConfiguredOriginWithCredentials_AllowsCredentialsAndExposesRetryAfter(string method)
    {
        await using var host = SessionTestHost.Create();

        var response = await SendAsync(host, method, FieldOpsApiFactory.AllowedOrigin);

        Assert.Equal(FieldOpsApiFactory.AllowedOrigin, Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.Equal("true", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Credentials")));
        var exposed = string.Join(',', response.Headers.GetValues("Access-Control-Expose-Headers"));
        Assert.Contains("Retry-After", exposed, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task SessionEndpoint_FromUnknownOriginWithCredentials_ReturnsNoCorsHeaders(string method)
    {
        await using var host = SessionTestHost.Create();

        var response = await SendAsync(host, method, "https://evil.example");

        Assert.DoesNotContain(
            response.Headers,
            header => header.Key.StartsWith("Access-Control-", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Preflight_FromConfiguredOriginForPostSessions_AllowsCredentials()
    {
        await using var host = SessionTestHost.Create();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/sessions");
        request.Headers.Add("Origin", FieldOpsApiFactory.AllowedOrigin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "content-type");

        var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("true", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Credentials")));
    }

    // Credentials are simulated with a (garbage) session cookie.
    private static Task<HttpResponseMessage> SendAsync(SessionTestHost host, string method, string origin) =>
        method switch
        {
            "POST" => SessionApi.PostSessionAsync(host.Client, "{}", cookie: "fieldops_session=x", origin: origin),
            "GET" => SessionApi.GetCurrentAsync(host.Client, "fieldops_session=x", origin),
            _ => SessionApi.DeleteCurrentAsync(host.Client, "fieldops_session=x", origin),
        };
}
