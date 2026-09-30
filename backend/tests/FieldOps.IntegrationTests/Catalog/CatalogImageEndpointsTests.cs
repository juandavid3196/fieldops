using System.Net;
using System.Text;
using FieldOps.IntegrationTests.Api;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.Sessions;

namespace FieldOps.IntegrationTests.Catalog;

/// <summary>Item image retrieval, upload/replace and removal (FR-11, FR-15): AC-11.</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class CatalogImageEndpointsTests(CompanySettingsDatabaseFixture database)
{
    private const string TypeMessage = "Choose a PNG or JPG image.";

    private const string SizeMessage = "Choose an image of 5 MB or smaller.";

    // AC-11: lifecycle, headers, audit once per change, stored bytes and the untouched updated_at.
    [Fact]
    public async Task Image_UploadReplaceGetDeleteTwice_ServesHeadersAuditsEachChangeOnceAndKeepsItemTimestamp()
    {
        var org = await database.SeedOrganizationAsync();
        var id = await database.SeedCatalogItemAsync(org, "product", "With image");
        await using var host = CatalogHost.Create(database);
        var cookie = await host.CookieAsync(database, org, CompanySettingsDatabaseFixture.OperationsManagerRoleId);
        var path = $"/catalog-items/{id}/image";
        var stamp = await database.ItemUpdatedAtAsync(id);

        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Get, path, cookie)).StatusCode);

        var png = CatalogSeed.Png(100);
        var upload = await host.SendFileAsync(HttpMethod.Put, path, cookie, png, "image/png", "anything.exe");
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        var metadata = await CatalogHost.ReadAsync(upload);
        Assert.Equal("image/png", metadata["contentType"]!.GetValue<string>());
        Assert.Equal(100, metadata["sizeBytes"]!.GetValue<int>());
        Assert.Equal(1, await database.CountCatalogAuditAsync(org, "catalog_item.image_updated"));
        var (_, audited, _) = await database.GetLatestAuditAsync(org, "catalog_item.image_updated");
        Assert.DoesNotContain("content\"", audited, StringComparison.Ordinal);

        var served = await host.SendAsync(HttpMethod.Get, path, cookie);
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Equal(png, await served.Content.ReadAsByteArrayAsync());
        Assert.Equal("image/png", served.Content.Headers.ContentType?.MediaType);
        Assert.Equal("nosniff", served.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("default-src 'none'; sandbox", served.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Contains("no-store", served.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.Ordinal);

        var jpeg = CatalogSeed.Jpeg(50);
        Assert.Equal(HttpStatusCode.OK, (await host.SendFileAsync(HttpMethod.Put, path, cookie, jpeg, "image/jpeg")).StatusCode);
        Assert.Equal(2, await database.CountCatalogAuditAsync(org, "catalog_item.image_updated"));
        Assert.Equal(jpeg, await (await host.SendAsync(HttpMethod.Get, path, cookie)).Content.ReadAsByteArrayAsync());

        var detail = await CatalogHost.ReadAsync(await host.SendAsync(HttpMethod.Get, $"/catalog-items/{id}", cookie));
        Assert.True(detail["hasImage"]!.GetValue<bool>());
        Assert.Equal("image/jpeg", detail["image"]!["contentType"]!.GetValue<string>());

        // Rejections change nothing: the stored JPEG and the audit count stay.
        var huge = CatalogSeed.Png(CatalogImageLimit + 1);
        var rejected = new (byte[] Bytes, string Declared, string Message)[]
        {
            (png, "image/jpeg", TypeMessage),
            (jpeg, "image/png", TypeMessage),
            (Encoding.UTF8.GetBytes("plain text renamed to .png"), "image/png", TypeMessage),
            (png, "image/gif", TypeMessage),
            (huge, "image/png", SizeMessage),
        };

        foreach (var (bytes, declared, message) in rejected)
        {
            var response = await host.SendFileAsync(HttpMethod.Put, path, cookie, bytes, declared, "photo.png");
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(message, CatalogSeed.Error(await CatalogHost.ReadAsync(response), "file"));
        }

        var notMultipart = await host.SendAsync(HttpMethod.Put, path, cookie, new System.Text.Json.Nodes.JsonObject { ["file"] = "x" });
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, notMultipart.StatusCode);

        Assert.Equal(2, await database.CountCatalogAuditAsync(org, "catalog_item.image_updated"));
        Assert.Equal(jpeg, await (await host.SendAsync(HttpMethod.Get, path, cookie)).Content.ReadAsByteArrayAsync());

        // Delete twice: the first audits, the second is a silent 204.
        Assert.Equal(HttpStatusCode.NoContent, (await host.SendAsync(HttpMethod.Delete, path, cookie)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await host.SendAsync(HttpMethod.Delete, path, cookie)).StatusCode);
        Assert.Equal(1, await database.CountCatalogAuditAsync(org, "catalog_item.image_removed"));
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Get, path, cookie)).StatusCode);
        Assert.Equal(stamp, await database.ItemUpdatedAtAsync(id));
    }

    // AC-11 (413): a body over the 6 MB envelope is rejected by the real server before the form is read.
    [Fact]
    public async Task UploadImage_BodyOverEnvelope_Returns413ForManagerAndNeverReadsFormWithoutAuthorization()
    {
        var org = await database.SeedOrganizationAsync();
        var id = await database.SeedCatalogItemAsync(org, "product", "Big upload");
        var viewer = await database.SeedAccountAsync(CompanySettingsDatabaseFixture.ViewerRoleId, organizationId: org);
        var manager = await database.SeedAccountAsync(CompanySettingsDatabaseFixture.OperationsManagerRoleId, organizationId: org);

        await using var factory = FieldOpsApiFactory.Create(connectionString: database.ConnectionString);
        factory.UseKestrel(0);
        factory.StartServer();
        factory.ClientOptions.HandleCookies = false;
        factory.ClientOptions.AllowAutoRedirect = false;
        using var client = factory.CreateClient();

        var path = $"/catalog-items/{id}/image";
        var managerCookie = await CompanySettingsApi.SignInCookieAsync(client, manager.Email);
        var viewerCookie = await CompanySettingsApi.SignInCookieAsync(client, viewer.Email);
        var overEnvelope = (6 * 1024 * 1024) + 1;

        var tooLarge = await SendHeadersOnlyAsync(client.BaseAddress!, path, managerCookie, overEnvelope);
        Assert.StartsWith("HTTP/1.1 413", tooLarge, StringComparison.Ordinal);
        Assert.Contains("application/problem+json", tooLarge, StringComparison.OrdinalIgnoreCase);

        // Authorization runs before any form read: the read-only role gets 403, not 413.
        var forbidden = await SendHeadersOnlyAsync(client.BaseAddress!, path, viewerCookie, overEnvelope);
        Assert.StartsWith("HTTP/1.1 403", forbidden, StringComparison.Ordinal);

        Assert.Equal(0, await database.CountCatalogAuditAsync(org, "catalog_item.image_updated"));
    }

    private const int CatalogImageLimit = 5_242_880;

    internal static async Task<string> SendHeadersOnlyAsync(Uri baseAddress, string path, string cookie, long contentLength)
    {
        using var tcp = new System.Net.Sockets.TcpClient();
        await tcp.ConnectAsync(baseAddress.Host, baseAddress.Port);
        await using var stream = tcp.GetStream();

        var head =
            $"{(path.EndsWith("/import", StringComparison.Ordinal) ? "POST" : "PUT")} {path} HTTP/1.1\r\nHost: {baseAddress.Authority}\r\nCookie: {cookie}\r\n"
            + $"{TestClientIpStartupFilter.HeaderName}: {SessionApi.NewClientIp()}\r\n"
            + $"Content-Type: multipart/form-data; boundary=fieldopsboundary\r\nContent-Length: {contentLength}\r\n"
            + "Connection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head));

        using var reader = new StreamReader(stream, Encoding.UTF8);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        return await reader.ReadToEndAsync(timeout.Token);
    }
}
