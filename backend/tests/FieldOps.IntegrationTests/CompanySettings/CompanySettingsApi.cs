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

    public static async Task<string> SignInCookieAsync(HttpClient client, string email) =>
        SessionApi.GetIssuedCookie(
            await SessionApi.SignInAsync(client, email, CompanySettingsDatabaseFixture.Password));

    /// <summary>Sends a multipart/form-data request whose <c>file</c> part has the given bytes and declared type.</summary>
    public static Task<HttpResponseMessage> PutFileAsync(
        HttpClient client, string path, byte[] bytes, string declaredType, string? cookie, string fileName = "logo.bin")
    {
        var part = new ByteArrayContent(bytes);
        part.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(declaredType);

        var form = new MultipartFormDataContent { { part, "file", fileName } };

        var request = new HttpRequestMessage(HttpMethod.Put, path) { Content = form };

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
