using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.Api;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Net.Http.Headers;

namespace FieldOps.IntegrationTests.Sessions;

/// <summary>
/// HTTP helpers for the session endpoints. Clients never keep cookies: the
/// session cookie is read from Set-Cookie and forwarded explicitly, because
/// a cookie container drops Secure cookies over plain HTTP.
/// </summary>
public static class SessionApi
{
    public const string CookieName = "fieldops_session";

    private static int s_nextClientIp;

    public static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
            AllowAutoRedirect = false,
        });

    /// <summary>A client IP no other request used, so per-IP limits never interfere.</summary>
    public static string NewClientIp()
    {
        var next = Interlocked.Increment(ref s_nextClientIp);
        return $"10.{(next >> 16) & 0xFF}.{(next >> 8) & 0xFF}.{next & 0xFF}";
    }

    public static Task<HttpResponseMessage> SignInAsync(
        HttpClient client,
        string email,
        string password,
        bool? rememberMe = null,
        string? clientIp = null,
        string? cookie = null,
        string? origin = null)
    {
        var body = new JsonObject
        {
            ["email"] = email,
            ["password"] = password,
        };

        if (rememberMe is not null)
        {
            body["rememberMe"] = rememberMe.Value;
        }

        return PostSessionAsync(client, body.ToJsonString(), clientIp: clientIp, cookie: cookie, origin: origin);
    }

    public static Task<HttpResponseMessage> PostSessionAsync(
        HttpClient client,
        string body,
        string? mediaType = "application/json",
        string? clientIp = null,
        string? cookie = null,
        string? origin = null,
        IEnumerable<KeyValuePair<string, string>>? headers = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/sessions")
        {
            Content = CreateContent(body, mediaType),
        };

        return SendAsync(client, request, clientIp, cookie, origin, headers);
    }

    /// <summary>
    /// UTF-8 body content; a null media type sends no Content-Type header.
    /// </summary>
    public static HttpContent CreateContent(string body, string? mediaType) =>
        mediaType is null
            ? new ByteArrayContent(Encoding.UTF8.GetBytes(body))
            : new StringContent(body, Encoding.UTF8, mediaType);

    public static Task<HttpResponseMessage> GetCurrentAsync(
        HttpClient client,
        string? cookie = null,
        string? origin = null,
        IEnumerable<KeyValuePair<string, string>>? headers = null) =>
        SendAsync(client, new HttpRequestMessage(HttpMethod.Get, "/sessions/current"), null, cookie, origin, headers);

    public static Task<HttpResponseMessage> DeleteCurrentAsync(
        HttpClient client,
        string? cookie = null,
        string? origin = null) =>
        SendAsync(client, new HttpRequestMessage(HttpMethod.Delete, "/sessions/current"), null, cookie, origin, null);

    public static Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpRequestMessage request,
        string? clientIp,
        string? cookie,
        string? origin,
        IEnumerable<KeyValuePair<string, string>>? headers)
    {
        request.Headers.Add(TestClientIpStartupFilter.HeaderName, clientIp ?? NewClientIp());

        if (cookie is not null)
        {
            request.Headers.Add("Cookie", cookie);
        }

        if (origin is not null)
        {
            request.Headers.Add("Origin", origin);
        }

        foreach (var (name, value) in headers ?? [])
        {
            request.Headers.Add(name, value);
        }

        return client.SendAsync(request);
    }

    /// <summary>Every Set-Cookie header for the session cookie.</summary>
    public static IReadOnlyList<SetCookieHeaderValue> GetSessionSetCookies(HttpResponseMessage response) =>
        response.Headers.TryGetValues(HeaderNames.SetCookie, out var values)
            ? values
                .Select(value => SetCookieHeaderValue.Parse(value))
                .Where(cookie => cookie.Name.Equals(CookieName, StringComparison.Ordinal))
                .ToList()
            : [];

    /// <summary>The issued session cookie as a request Cookie header value.</summary>
    public static string GetIssuedCookie(HttpResponseMessage response)
    {
        var cookie = Assert.Single(GetSessionSetCookies(response));
        Assert.False(IsDeletion(cookie));
        return $"{cookie.Name}={cookie.Value}";
    }

    public static bool IsDeletion(SetCookieHeaderValue cookie) =>
        cookie.Expires is { } expires && expires < DateTimeOffset.UnixEpoch.AddYears(1)
        && cookie.Value.Length == 0;

    public static void AssertCookieDeleted(HttpResponseMessage response)
    {
        var cookie = Assert.Single(GetSessionSetCookies(response));
        Assert.True(IsDeletion(cookie), $"Expected a deletion but got {cookie}");
        Assert.Equal("/", cookie.Path.ToString());
    }

    public static void AssertNoSessionCookie(HttpResponseMessage response) =>
        Assert.Empty(GetSessionSetCookies(response));

    public static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    public static async Task<SessionBody> ReadSessionAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<SessionBody>())!;

    /// <summary>The ProblemDetails body without its per-request traceId.</summary>
    public static async Task<string> ReadProblemWithoutTraceIdAsync(HttpResponseMessage response)
    {
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        Assert.True(body.Remove("traceId"));
        return body.ToJsonString();
    }
}

public sealed record SessionBody(SessionBodyUser User, SessionBodyOrganization Organization, SessionBodyRole Role);

public sealed record SessionBodyUser(Guid Id, string FirstName, string LastName, string Email);

public sealed record SessionBodyOrganization(Guid Id, string Name);

public sealed record SessionBodyRole(string Code, string Name);
