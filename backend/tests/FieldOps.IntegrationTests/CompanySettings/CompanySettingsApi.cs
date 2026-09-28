using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.Sessions;

namespace FieldOps.IntegrationTests.CompanySettings;

/// <summary>
/// HTTP helpers for /organization-settings and /branches, per
/// <see cref="SessionApi"/>: clients never keep cookies, so the session
/// cookie is forwarded explicitly.
/// </summary>
public static class CompanySettingsApi
{
    public static Task<HttpResponseMessage> GetAsync(HttpClient client, string path, string? cookie) =>
        SendAsync(client, HttpMethod.Get, path, null, cookie);

    public static Task<HttpResponseMessage> PutAsync(HttpClient client, string path, JsonObject body, string? cookie) =>
        SendAsync(client, HttpMethod.Put, path, body.ToJsonString(), cookie);

    public static Task<HttpResponseMessage> PostAsync(
        HttpClient client, string path, JsonObject? body, string? cookie) =>
        SendAsync(client, HttpMethod.Post, path, body?.ToJsonString(), cookie);

    public static Task<HttpResponseMessage> SendRawAsync(
        HttpClient client, HttpMethod method, string path, string? body, string? cookie, string? mediaType = "application/json") =>
        SendAsync(client, method, path, body, cookie, mediaType);

    private static Task<HttpResponseMessage> SendAsync(
        HttpClient client, HttpMethod method, string path, string? body, string? cookie, string? mediaType = "application/json")
    {
        var request = new HttpRequestMessage(method, path);

        if (body is not null)
        {
            request.Content = mediaType is null
                ? new ByteArrayContent(Encoding.UTF8.GetBytes(body))
                : new StringContent(body, Encoding.UTF8, mediaType);
        }

        if (cookie is not null)
        {
            request.Headers.Add("Cookie", cookie);
        }

        request.Headers.Add(Api.TestClientIpStartupFilter.HeaderName, SessionApi.NewClientIp());

        return client.SendAsync(request);
    }

    public static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    public static async Task<T> ReadAsAsync<T>(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>())!;
}
