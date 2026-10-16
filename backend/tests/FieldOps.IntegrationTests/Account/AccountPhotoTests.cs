using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.Api;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.Sessions;

namespace FieldOps.IntegrationTests.Account;

/// <summary>
/// /account/photo (account settings FR-08, FR-09, BR-03, BR-04, BR-07; AC-13 to AC-16):
/// lifecycle, headers, session body, audit, log hygiene and rejection cases.
/// </summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class AccountPhotoTests(CompanySettingsDatabaseFixture database)
{
    private const string PhotoPath = "/account/photo";

    private const string Marker = "PHOTO-BYTES-MARKER-7c1d";

    private const string FileMessage = "Choose a JPG or PNG file.";

    private static byte[] Png(string marker = Marker) =>
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. Encoding.ASCII.GetBytes(marker)];

    private static byte[] Jpeg(string marker = Marker) =>
        [0xFF, 0xD8, 0xFF, 0xE0, .. Encoding.ASCII.GetBytes(marker)];

    // AC-13, AC-15, AC-16 and FR-09.
    [Fact]
    public async Task Photo_UploadReplaceGetDeleteTwice_ServesHeadersUpdatesSessionAuditsAndLeavesUserRow()
    {
        var account = await database.SeedAccountAsync();
        var other = await database.SeedAccountAsync();
        await using var host = SessionTestHost.Create(database.ConnectionString, captureLogs: true);
        var cookie = await CompanySettingsApi.SignInCookieAsync(host.Client, account.Email);
        var otherCookie = await CompanySettingsApi.SignInCookieAsync(host.Client, other.Email);
        Assert.Equal(HttpStatusCode.NotFound, (await CompanySettingsApi.GetAsync(host.Client, PhotoPath, cookie)).StatusCode);
        Assert.Equal(JsonValueKind.Null, (await PhotoUpdatedAtAsync(host.Client, cookie)).ValueKind);

        var png = Png();
        var upload = await CompanySettingsApi.PutFileAsync(
            host.Client, PhotoPath, png, "image/png", cookie, fileName: "secret-file-name.png");
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        Assert.True(upload.Headers.CacheControl?.NoStore);
        using (var uploaded = await CompanySettingsApi.ReadJsonAsync(upload))
        {
            Assert.Equal("image/png", uploaded.RootElement.GetProperty("contentType").GetString());
            Assert.Equal(png.Length, uploaded.RootElement.GetProperty("sizeBytes").GetInt32());
        }

        var get = await CompanySettingsApi.GetAsync(host.Client, PhotoPath, cookie);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal("image/png", get.Content.Headers.ContentType?.MediaType);
        Assert.Equal(png, await get.Content.ReadAsByteArrayAsync());
        Assert.Equal("nosniff", Assert.Single(get.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("default-src 'none'; sandbox", Assert.Single(get.Headers.GetValues("Content-Security-Policy")));
        Assert.Equal("inline", get.Content.Headers.ContentDisposition?.DispositionType);
        Assert.True(get.Headers.CacheControl?.NoStore);

        // FR-09: the session body carries photoUpdatedAt on GET /sessions/current and POST /sessions.
        Assert.NotEqual(JsonValueKind.Null, (await PhotoUpdatedAtAsync(host.Client, cookie)).ValueKind);
        var signIn = await SessionApi.SignInAsync(host.Client, account.Email, CompanySettingsDatabaseFixture.Password);
        using (var body = await SessionApi.ReadJsonAsync(signIn))
        {
            Assert.NotEqual(JsonValueKind.Null, body.RootElement.GetProperty("user").GetProperty("photoUpdatedAt").ValueKind);
        }

        // Sign-in updates users.updated_at itself, so the baseline is taken after it.
        var userUpdatedAt = await UserUpdatedAtAsync(account.UserId);

        using (var profile = await CompanySettingsApi.ReadJsonAsync(
            await CompanySettingsApi.GetAsync(host.Client, "/account/profile", cookie)))
        {
            Assert.Equal("image/png", profile.RootElement.GetProperty("photo").GetProperty("contentType").GetString());
        }

        // Another user can neither read nor be affected by this photo.
        Assert.Equal(HttpStatusCode.NotFound, (await CompanySettingsApi.GetAsync(host.Client, PhotoPath, otherCookie)).StatusCode);

        var jpeg = Jpeg("SECOND");
        Assert.Equal(
            HttpStatusCode.OK,
            (await CompanySettingsApi.PutFileAsync(host.Client, PhotoPath, jpeg, "image/jpeg", cookie)).StatusCode);
        var replaced = await CompanySettingsApi.GetAsync(host.Client, PhotoPath, cookie);
        Assert.Equal("image/jpeg", replaced.Content.Headers.ContentType?.MediaType);
        Assert.Equal(jpeg, await replaced.Content.ReadAsByteArrayAsync());

        Assert.Equal(2, await database.CountAuditLogsAsync(account.OrganizationId, "user.photo_updated"));
        var (before, after, _) = await database.GetLatestAuditAsync(account.OrganizationId, "user.photo_updated");
        Assert.Contains("image/png", before!);
        Assert.DoesNotContain(Marker, after!);
        Assert.True(await database.ScalarAsync<bool>(
            "SELECT bool_and(ip_address IS NOT NULL AND entity_id = @u AND actor_user_id = @u AND entity_type = 'user') FROM audit_logs WHERE organization_id = @o AND action = 'user.photo_updated'",
            ("u", account.UserId),
            ("o", account.OrganizationId)));

        var firstDelete = await CompanySettingsApi.SendRawAsync(host.Client, HttpMethod.Delete, PhotoPath, null, cookie);
        var secondDelete = await CompanySettingsApi.SendRawAsync(host.Client, HttpMethod.Delete, PhotoPath, null, cookie);
        Assert.Equal(HttpStatusCode.NoContent, firstDelete.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, secondDelete.StatusCode);
        Assert.Equal(1, await database.CountAuditLogsAsync(account.OrganizationId, "user.photo_removed"));
        Assert.Equal(HttpStatusCode.NotFound, (await CompanySettingsApi.GetAsync(host.Client, PhotoPath, cookie)).StatusCode);
        Assert.Equal(JsonValueKind.Null, (await PhotoUpdatedAtAsync(host.Client, cookie)).ValueKind);

        // BR-04: photo operations change user_photos only.
        Assert.Equal(userUpdatedAt, await UserUpdatedAtAsync(account.UserId));
        Assert.Equal(0, await database.CountAuditLogsAsync(other.OrganizationId, "user.photo_updated"));

        foreach (var entry in host.Logs!.Entries)
        {
            var text = string.Join(
                "\n",
                new[] { entry.Message, entry.Exception?.ToString() }
                    .Concat(entry.Properties.Values.Select(value => value?.ToString())));
            Assert.DoesNotContain(Marker, text);
            Assert.DoesNotContain("secret-file-name", text);
        }
    }

    // AC-14.
    [Theory]
    [InlineData("text-as-png")]
    [InlineData("png-as-jpeg")]
    [InlineData("gif")]
    [InlineData("too-big")]
    [InlineData("missing-file")]
    [InlineData("json-body")]
    [InlineData("body-too-large")]
    public async Task PutPhoto_Rejection_ReturnsExpectedStatusAndKeepsStoredPhoto(string scenario)
    {
        var account = await database.SeedAccountAsync();
        await using var host = SessionTestHost.Create(database.ConnectionString);
        var cookie = await CompanySettingsApi.SignInCookieAsync(host.Client, account.Email);

        var original = Png("ORIGINAL");
        Assert.Equal(
            HttpStatusCode.OK,
            (await CompanySettingsApi.PutFileAsync(host.Client, PhotoPath, original, "image/png", cookie)).StatusCode);

        HttpResponseMessage response;
        var expectedStatus = HttpStatusCode.BadRequest;
        var expectedMessage = FileMessage;

        switch (scenario)
        {
            case "body-too-large":
                // The size limit is enforced by the real server: run on Kestrel.
                await using (var factory = FieldOpsApiFactory.Create(connectionString: database.ConnectionString))
                {
                    factory.UseKestrel(0);
                    factory.StartServer();
                    factory.ClientOptions.HandleCookies = false;
                    factory.ClientOptions.AllowAutoRedirect = false;
                    using var client = factory.CreateClient();
                    var kestrelCookie = await CompanySettingsApi.SignInCookieAsync(client, account.Email);
                    var raw = await SendHeadersOnlyAsync(client.BaseAddress!, kestrelCookie, (6 * 1024 * 1024) + 1);
                    Assert.StartsWith("HTTP/1.1 413", raw, StringComparison.Ordinal);
                    Assert.Contains("application/problem+json", raw, StringComparison.OrdinalIgnoreCase);
                    Assert.Contains("no-store", raw, StringComparison.OrdinalIgnoreCase);
                }

                response = new HttpResponseMessage(HttpStatusCode.RequestEntityTooLarge);
                expectedStatus = HttpStatusCode.RequestEntityTooLarge;
                break;

            case "json-body":
                response = await CompanySettingsApi.PutAsync(
                    host.Client, PhotoPath, new JsonObject { ["file"] = "x" }, cookie);
                expectedStatus = HttpStatusCode.UnsupportedMediaType;
                break;

            case "missing-file":
                response = await CompanySettingsApi.PutFileAsync(host.Client, PhotoPath, [], "image/png", cookie);
                break;

            default:
                var (bytes, declared) = scenario switch
                {
                    "text-as-png" => (Encoding.UTF8.GetBytes("just some text renamed to .png"), "image/png"),
                    "png-as-jpeg" => (Png(), "image/jpeg"),
                    "gif" => (Encoding.ASCII.GetBytes("GIF89a......"), "image/gif"),
                    "too-big" => (Png().Concat(new byte[5 * 1024 * 1024]).ToArray(), "image/png"),
                    _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
                };
                response = await CompanySettingsApi.PutFileAsync(host.Client, PhotoPath, bytes, declared, cookie);

                if (scenario == "too-big")
                {
                    expectedMessage = "Choose a file of 5 MB or smaller.";
                }

                break;
        }

        Assert.Equal(expectedStatus, response.StatusCode);

        if (expectedStatus == HttpStatusCode.BadRequest)
        {
            using var problem = await CompanySettingsApi.ReadJsonAsync(response);
            Assert.Equal(
                expectedMessage,
                problem.RootElement.GetProperty("errors").GetProperty("file").EnumerateArray().Single().GetString());
        }

        var stored = await CompanySettingsApi.GetAsync(host.Client, PhotoPath, cookie);
        Assert.Equal(original, await stored.Content.ReadAsByteArrayAsync());
        Assert.Equal(1, await database.CountAuditLogsAsync(account.OrganizationId, "user.photo_updated"));
    }

    private static async Task<JsonElement> PhotoUpdatedAtAsync(HttpClient client, string cookie)
    {
        var response = await SessionApi.GetCurrentAsync(client, cookie);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await SessionApi.ReadJsonAsync(response);

        return body.RootElement.GetProperty("user").GetProperty("photoUpdatedAt").Clone();
    }

    private Task<DateTimeOffset> UserUpdatedAtAsync(Guid userId) =>
        database.ScalarAsync<DateTimeOffset>("SELECT updated_at FROM users WHERE id = @id", ("id", userId));

    private static async Task<string> SendHeadersOnlyAsync(Uri baseAddress, string cookie, long contentLength)
    {
        using var tcp = new System.Net.Sockets.TcpClient();
        await tcp.ConnectAsync(baseAddress.Host, baseAddress.Port);
        await using var stream = tcp.GetStream();

        // Only the headers are sent: the server rejects on the declared Content-Length before reading the body.
        var head =
            $"PUT {PhotoPath} HTTP/1.1\r\nHost: {baseAddress.Authority}\r\nCookie: {cookie}\r\n"
            + $"{TestClientIpStartupFilter.HeaderName}: {SessionApi.NewClientIp()}\r\n"
            + $"Content-Type: multipart/form-data; boundary=fieldopsboundary\r\nContent-Length: {contentLength}\r\n"
            + "Connection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head));

        using var reader = new StreamReader(stream, Encoding.UTF8);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        return await reader.ReadToEndAsync(timeout.Token);
    }
}
