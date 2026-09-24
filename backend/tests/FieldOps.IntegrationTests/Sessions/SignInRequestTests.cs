using System.Net;
using System.Text;
using FieldOps.Api.Middleware;
using FieldOps.IntegrationTests.Api;

namespace FieldOps.IntegrationTests.Sessions;

/// <summary>
/// Requests rejected before any credential check, so no database is needed.
/// </summary>
public class SignInRequestTests
{
    public static TheoryData<string, string, string> SingleRuleFailures => new()
    {
        { """{"email":"","password":"secret"}""", "email", "Enter your email address." },
        { """{"email":"   ","password":"secret"}""", "email", "Enter your email address." },
        { """{"email":"not-an-email","password":"secret"}""", "email", "Enter a valid email address, for example name@company.com." },
        { $$"""{"email":"{{new string('a', 243)}}@example.com","password":"secret"}""", "email", "Enter a valid email address, for example name@company.com." },
        { """{"email":"user@example.com","password":""}""", "password", "Enter your password." },
        { $$"""{"email":"user@example.com","password":"{{new string('p', 129)}}"}""", "password", "Use 128 characters or fewer." },
    };

    [Theory]
    [MemberData(nameof(SingleRuleFailures))]
    public async Task PostSessions_BodyFailingOneRule_Returns400WithThatKeyAndMessage(
        string body,
        string key,
        string message)
    {
        await using var host = SessionTestHost.Create();

        var response = await SessionApi.PostSessionAsync(host.Client, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        SessionApi.AssertNoSessionCookie(response);

        using var problem = await SessionApi.ReadJsonAsync(response);
        var errors = problem.RootElement.GetProperty("errors");
        var property = Assert.Single(errors.EnumerateObject());
        Assert.Equal(key, property.Name);
        Assert.Equal(message, Assert.Single(property.Value.EnumerateArray()).GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.RootElement.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task PostSessions_EmptyObject_Returns400WithBothKeys()
    {
        await using var host = SessionTestHost.Create();

        var response = await SessionApi.PostSessionAsync(host.Client, "{}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = await SessionApi.ReadJsonAsync(response);
        var keys = problem.RootElement.GetProperty("errors").EnumerateObject().Select(p => p.Name).Order();
        Assert.Equal(["email", "password"], keys);
    }

    [Fact]
    public async Task PostSessions_TextPlainContent_Returns415ProblemWithoutCookie()
    {
        await using var host = SessionTestHost.Create();

        var response = await SessionApi.PostSessionAsync(
            host.Client,
            """{"email":"user@example.com","password":"secret"}""",
            mediaType: "text/plain");

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        SessionApi.AssertNoSessionCookie(response);

        using var problem = await SessionApi.ReadJsonAsync(response);
        Assert.Equal(415, problem.RootElement.GetProperty("status").GetInt32());
    }

    [Theory]
    [InlineData("""{"email":"user@example.com","password":"secret"}""")]
    [InlineData("")]
    public async Task PostSessions_WithoutContentType_Returns415ProblemWithoutCookie(string body)
    {
        await using var host = SessionTestHost.Create();

        var response = await SessionApi.PostSessionAsync(host.Client, body, mediaType: null);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        SessionApi.AssertNoSessionCookie(response);

        using var problem = await SessionApi.ReadJsonAsync(response);
        Assert.Equal(415, problem.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(problem.RootElement.GetProperty("traceId").GetString()));
    }

    [Theory]
    [InlineData("""{"email":""")]
    [InlineData("not json")]
    [InlineData("""{"email":"user@example.com","password":"secret","rememberMe":"yes"}""")]
    [InlineData("""{"email":42,"password":"secret"}""")]
    [InlineData("")]
    [InlineData("null")]
    public async Task PostSessions_MalformedJson_Returns400ProblemWithoutFieldKeys(string body)
    {
        await using var host = SessionTestHost.Create();

        var response = await SessionApi.PostSessionAsync(host.Client, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        SessionApi.AssertNoSessionCookie(response);

        var text = await response.Content.ReadAsStringAsync();
        using var problem = System.Text.Json.JsonDocument.Parse(text);
        Assert.Equal(400, problem.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(problem.RootElement.GetProperty("traceId").GetString()));
        Assert.False(problem.RootElement.TryGetProperty("errors", out _));
        Assert.False(problem.RootElement.TryGetProperty("detail", out _));
        Assert.DoesNotContain("JsonException", text);
    }

    // The request size limit is enforced by the real server, so this test
    // runs on Kestrel instead of the in-memory test server.
    [Fact]
    public async Task PostSessions_BodyLargerThan4Kilobytes_Returns413ProblemWithoutCookie()
    {
        var logs = new CapturingLoggerProvider();
        await using var baseFactory = FieldOpsApiFactory.Create();
        await using var factory = logs.AttachTo(baseFactory);
        factory.UseKestrel(0);
        factory.StartServer();
        using var client = factory.CreateClient();

        var body = $$"""{"email":"user@example.com","password":"{{new string('p', 5000)}}"}""";
        using var request = new HttpRequestMessage(HttpMethod.Post, "/sessions")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        SessionApi.AssertNoSessionCookie(response);

        using var problem = await SessionApi.ReadJsonAsync(response);
        Assert.Equal(413, problem.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(problem.RootElement.GetProperty("traceId").GetString()));

        // Written by BadHttpRequestExceptionHandler, not the global 500 handler.
        Assert.Equal(
            "https://tools.ietf.org/html/rfc9110#section-15.5.14",
            problem.RootElement.GetProperty("type").GetString());
        Assert.DoesNotContain(logs.Entries, entry => entry.Category == typeof(GlobalExceptionHandler).FullName);
    }

    // Kestrel reports a zero Content-Length body as unable to have a body,
    // which the in-memory test server does not, so empty bodies are also
    // checked on the real server.
    [Theory]
    [InlineData("application/json", HttpStatusCode.BadRequest)]
    [InlineData(null, HttpStatusCode.UnsupportedMediaType)]
    public async Task PostSessions_EmptyBodyOnKestrel_ReturnsKeylessProblemWithoutCookie(
        string? mediaType,
        HttpStatusCode expectedStatus)
    {
        await using var factory = FieldOpsApiFactory.Create();
        factory.UseKestrel(0);
        factory.StartServer();
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/sessions")
        {
            Content = SessionApi.CreateContent(string.Empty, mediaType),
        };

        var response = await client.SendAsync(request);

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        SessionApi.AssertNoSessionCookie(response);

        using var problem = await SessionApi.ReadJsonAsync(response);
        Assert.Equal((int)expectedStatus, problem.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(problem.RootElement.GetProperty("traceId").GetString()));
        Assert.False(problem.RootElement.TryGetProperty("errors", out _));
    }

    [Fact]
    public async Task PostSessions_BodyOf4KilobytesOnKestrel_IsAccepted()
    {
        await using var factory = FieldOpsApiFactory.Create();
        factory.UseKestrel(0);
        factory.StartServer();
        using var client = factory.CreateClient();

        // Exactly 4096 bytes and invalid only by password length: a 400, not a 413.
        const string prefix = "{\"email\":\"user@example.com\",\"password\":\"";
        var suffix = "\"}";
        var body = prefix + new string('p', 4096 - prefix.Length - suffix.Length) + suffix;
        Assert.Equal(4096, Encoding.UTF8.GetByteCount(body));

        using var request = new HttpRequestMessage(HttpMethod.Post, "/sessions")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
