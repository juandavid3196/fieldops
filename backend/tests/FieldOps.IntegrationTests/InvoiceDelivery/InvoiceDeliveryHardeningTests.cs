using System.Globalization;
using System.Net;
using System.Text;
using FieldOps.Api.Controllers;
using FieldOps.IntegrationTests.Api;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.ServiceRequests;

namespace FieldOps.IntegrationTests.InvoiceDelivery;

/// <summary>Rate limits, body size and public headers of the invoice-link endpoints (invoice-draft-delivery AC-14, BR-21).</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class InvoiceDeliveryHardeningTests(CompanySettingsDatabaseFixture database)
{
    private const string Malformed = "malformed";

    // AC-14: the read group (view and logo) and the PDF group limit each client on their own and answer 429 with Retry-After.
    [Fact]
    public async Task RateLimits_RejectTheExcessPerGroupWithRetryAfter_AndBodiesOver16KilobytesAre413()
    {
        await using (var host = RequestsHost.Create(database))
        {
            const string ip = "198.51.100.40";

            for (var count = 0; count < 60; count++)
            {
                var response = await InvoiceApi.PublicAsync(host.Client, count % 2 == 0 ? "view" : "logo", Malformed, ip);
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            }

            var limited = await InvoiceApi.PublicAsync(host.Client, "view", Malformed, ip);
            Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
            Assert.True(int.Parse(limited.Headers.GetValues("Retry-After").Single(), CultureInfo.InvariantCulture) >= 1);
            Assert.Equal("no-store", limited.Headers.CacheControl?.ToString());
            Assert.Equal("no-referrer", limited.Headers.GetValues("Referrer-Policy").Single());
            Assert.Equal(HttpStatusCode.TooManyRequests, (await InvoiceApi.PublicAsync(host.Client, "logo", Malformed, ip)).StatusCode);

            // Another client is unaffected, and the PDF group of the limited one has its own budget of 10.
            Assert.Equal(HttpStatusCode.NotFound, (await InvoiceApi.PublicAsync(host.Client, "view", Malformed, "198.51.100.41")).StatusCode);

            for (var count = 0; count < 10; count++)
            {
                Assert.Equal(HttpStatusCode.NotFound, (await InvoiceApi.PublicAsync(host.Client, "pdf", Malformed, ip)).StatusCode);
            }

            var pdfLimited = await InvoiceApi.PublicAsync(host.Client, "pdf", Malformed, ip);
            Assert.Equal(HttpStatusCode.TooManyRequests, pdfLimited.StatusCode);
            Assert.NotEmpty(pdfLimited.Headers.GetValues("Retry-After"));
            Assert.Equal(HttpStatusCode.NotFound, (await InvoiceApi.PublicAsync(host.Client, "pdf", Malformed, "198.51.100.42")).StatusCode);
        }

        // BR-21: the body limit is enforced by the real server and the rejection keeps the public headers.
        await using var factory = FieldOpsApiFactory.Create(connectionString: database.ConnectionString);
        factory.UseKestrel(0);
        factory.StartServer();
        using var client = factory.CreateClient();

        var padding = new string('s', PublicInvoiceLinksController.MaxRequestBodyBytes + 1);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/public/invoice-links/view")
        {
            Content = new StringContent($$"""{"token":"{{new string('A', 43)}}","padding":"{{padding}}"}""", Encoding.UTF8, "application/json"),
        };

        var tooLarge = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, tooLarge.StatusCode);
        Assert.True(tooLarge.Headers.CacheControl is { NoStore: true });
        Assert.Equal("no-referrer", tooLarge.Headers.GetValues("Referrer-Policy").Single());
        Assert.DoesNotContain(padding, await tooLarge.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }
}
