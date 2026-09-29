using System.Net;
using System.Text;
using System.Text.Json;
using FieldOps.IntegrationTests.Api;
using FieldOps.IntegrationTests.Sessions;

namespace FieldOps.IntegrationTests.CompanySettings;

/// <summary>
/// GET/PUT/DELETE /organization-settings/logo (FR-09; AC-11, AC-12): lifecycle,
/// headers, audit, log hygiene and rejection cases.
/// </summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class OrganizationLogoEndpointTests(CompanySettingsDatabaseFixture database)
{
    private const string LogoPath = "/organization-settings/logo";

    private const string Marker = "LOGO-BYTES-MARKER-9f3a";

    private const string FileMessage = "Choose a JPG, PNG or SVG file.";

    internal static byte[] Png(string marker = Marker) =>
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. Encoding.ASCII.GetBytes(marker)];

    internal static byte[] Svg(string inner = "<rect width=\"4\" height=\"4\"/>", string? root = null) =>
        Encoding.UTF8.GetBytes(
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 4 4\"{root}>{inner}</svg>");

    // AC-11.
    [Fact]
    public async Task Logo_UploadReplaceGetDeleteTwice_ServesHeadersAuditsOncePerChangeAndLogsNoContent()
    {
        var account = await database.SeedAccountAsync();
        await using var host = SessionTestHost.Create(database.ConnectionString, captureLogs: true);
        var cookie = await CompanySettingsApi.SignInCookieAsync(host.Client, account.Email);
        var organizationUpdatedAt = await database.GetOrganizationUpdatedAtAsync(account.OrganizationId);

        var png = Png();
        var upload = await CompanySettingsApi.PutFileAsync(
            host.Client, LogoPath, png, "image/png", cookie, fileName: "secret-file-name.png");
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        var uploaded = await CompanySettingsApi.ReadAsAsync<OrganizationLogoBody>(upload);
        Assert.Equal("image/png", uploaded.ContentType);
        Assert.Equal(png.Length, uploaded.SizeBytes);

        var get = await CompanySettingsApi.GetAsync(host.Client, LogoPath, cookie);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal("image/png", get.Content.Headers.ContentType?.MediaType);
        Assert.Equal(png, await get.Content.ReadAsByteArrayAsync());
        Assert.Equal("nosniff", Assert.Single(get.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal(
            "default-src 'none'; style-src 'unsafe-inline'; sandbox",
            Assert.Single(get.Headers.GetValues("Content-Security-Policy")));
        Assert.Equal("inline", get.Content.Headers.ContentDisposition?.DispositionType);
        Assert.True(get.Headers.CacheControl?.NoStore);

        var settings = await CompanySettingsApi.ReadAsAsync<OrganizationSettingsBody>(
            await CompanySettingsApi.GetAsync(host.Client, "/organization-settings", cookie));
        Assert.Equal("image/png", settings.Logo?.ContentType);

        var svg = Svg();
        var replace = await CompanySettingsApi.PutFileAsync(host.Client, LogoPath, svg, "image/svg+xml", cookie);
        Assert.Equal(HttpStatusCode.OK, replace.StatusCode);
        var replaced = await CompanySettingsApi.GetAsync(host.Client, LogoPath, cookie);
        Assert.Equal("image/svg+xml", replaced.Content.Headers.ContentType?.MediaType);
        Assert.Equal(svg, await replaced.Content.ReadAsByteArrayAsync());

        Assert.Equal(2, await database.CountAuditLogsAsync(account.OrganizationId, "organization.logo_updated"));
        var (before, after, _) = await database.GetLatestAuditAsync(account.OrganizationId, "organization.logo_updated");
        using (var beforeJson = JsonDocument.Parse(before!))
        {
            Assert.Equal("image/png", beforeJson.RootElement.GetProperty("contentType").GetString());
        }

        Assert.DoesNotContain(Marker, after!);

        var firstDelete = await CompanySettingsApi.SendRawAsync(host.Client, HttpMethod.Delete, LogoPath, null, cookie);
        var secondDelete = await CompanySettingsApi.SendRawAsync(host.Client, HttpMethod.Delete, LogoPath, null, cookie);
        Assert.Equal(HttpStatusCode.NoContent, firstDelete.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, secondDelete.StatusCode);
        Assert.Equal(1, await database.CountAuditLogsAsync(account.OrganizationId, "organization.logo_removed"));
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await CompanySettingsApi.GetAsync(host.Client, LogoPath, cookie)).StatusCode);

        Assert.Equal(organizationUpdatedAt, await database.GetOrganizationUpdatedAtAsync(account.OrganizationId));

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

    // AC-12.
    [Theory]
    [InlineData("text-as-png")]
    [InlineData("png-as-jpeg")]
    [InlineData("too-big")]
    [InlineData("svg-script")]
    [InlineData("svg-onload")]
    [InlineData("svg-foreignobject")]
    [InlineData("svg-external-href")]
    [InlineData("svg-doctype")]
    [InlineData("body-too-large")]
    [InlineData("json-body")]
    public async Task PutLogo_Rejection_ReturnsExpectedStatusAndKeepsStoredLogo(string scenario)
    {
        var account = await database.SeedAccountAsync();
        await using var inMemory = SessionTestHost.Create(database.ConnectionString);
        var cookie = await CompanySettingsApi.SignInCookieAsync(inMemory.Client, account.Email);

        var original = Png("ORIGINAL");
        Assert.Equal(
            HttpStatusCode.OK,
            (await CompanySettingsApi.PutFileAsync(inMemory.Client, LogoPath, original, "image/png", cookie)).StatusCode);

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
                    // The server rejects on the declared Content-Length before the
                    // body is read, so send only the headers over a raw socket:
                    // HttpClient would fail writing the rest of the body.
                    var raw = await SendHeadersOnlyAsync(
                        client.BaseAddress!,
                        kestrelCookie,
                        contentLength: (3 * 1024 * 1024) + 1);
                    Assert.True(raw.StartsWith("HTTP/1.1 413", StringComparison.Ordinal), raw);
                    Assert.Contains("application/problem+json", raw, StringComparison.OrdinalIgnoreCase);
                }

                response = new HttpResponseMessage(HttpStatusCode.RequestEntityTooLarge);
                expectedStatus = HttpStatusCode.RequestEntityTooLarge;
                break;

            case "json-body":
                response = await CompanySettingsApi.PutAsync(
                    inMemory.Client, LogoPath, new System.Text.Json.Nodes.JsonObject { ["file"] = "x" }, cookie);
                expectedStatus = HttpStatusCode.UnsupportedMediaType;
                break;

            default:
                var (bytes, declared) = BuildRejectedFile(scenario);
                response = await CompanySettingsApi.PutFileAsync(inMemory.Client, LogoPath, bytes, declared, cookie);

                if (scenario == "too-big")
                {
                    expectedMessage = "Choose a file of 2 MB or smaller.";
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

        var stored = await CompanySettingsApi.GetAsync(inMemory.Client, LogoPath, cookie);
        Assert.Equal(original, await stored.Content.ReadAsByteArrayAsync());
        Assert.Equal(1, await database.CountAuditLogsAsync(account.OrganizationId, "organization.logo_updated"));
    }

    private static async Task<string> SendHeadersOnlyAsync(Uri baseAddress, string cookie, long contentLength)
    {
        using var tcp = new System.Net.Sockets.TcpClient();
        await tcp.ConnectAsync(baseAddress.Host, baseAddress.Port);
        await using var stream = tcp.GetStream();

        var head =
            $"PUT {LogoPath} HTTP/1.1\r\nHost: {baseAddress.Authority}\r\nCookie: {cookie}\r\n"
            + $"{TestClientIpStartupFilter.HeaderName}: {SessionApi.NewClientIp()}\r\n"
            + $"Content-Type: multipart/form-data; boundary=fieldopsboundary\r\nContent-Length: {contentLength}\r\n"
            + "Connection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head));

        using var reader = new StreamReader(stream, Encoding.UTF8);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        return await reader.ReadToEndAsync(timeout.Token);
    }

    private static (byte[] Bytes, string Declared) BuildRejectedFile(string scenario) => scenario switch
    {
        "text-as-png" => (Encoding.UTF8.GetBytes("just some text renamed to .png"), "image/png"),
        "png-as-jpeg" => (Png(), "image/jpeg"),
        "too-big" => (Png().Concat(new byte[OrganizationLogoLimit]).ToArray(), "image/png"),
        "svg-script" => (Svg("<script>alert(1)</script>"), "image/svg+xml"),
        "svg-onload" => (Svg("<rect onload=\"alert(1)\" width=\"1\" height=\"1\"/>"), "image/svg+xml"),
        "svg-foreignobject" => (Svg("<foreignObject><div/></foreignObject>"), "image/svg+xml"),
        "svg-external-href" => (
            Svg("<image href=\"https://evil.example/x.png\" width=\"1\" height=\"1\"/>"), "image/svg+xml"),
        "svg-doctype" => (
            Encoding.UTF8.GetBytes(
                "<!DOCTYPE svg [<!ENTITY x SYSTEM \"file:///etc/passwd\">]>"
                + "<svg xmlns=\"http://www.w3.org/2000/svg\">&x;</svg>"),
            "image/svg+xml"),
        _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
    };

    // 2 MB of padding after the PNG signature exceeds the 2 MB limit by more than one byte.
    private const int OrganizationLogoLimit = 2 * 1024 * 1024;
}
