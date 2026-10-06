using System.Net;
using System.Text;
using FieldOps.Api.Controllers;
using FieldOps.IntegrationTests.Api;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.ServiceRequests;

namespace FieldOps.IntegrationTests.QuoteLinks;

/// <summary>Rate limits, body size and public headers of the quote-link endpoints (AC-18, BR-20, BR-21).</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class QuoteLinkHardeningTests(CompanySettingsDatabaseFixture database)
{
    private const string Malformed = "malformed";

    // AC-18: each group limits its own clients and never starves the others.
    [Fact]
    public async Task RateLimits_RejectTheExcessPerGroupWithRetryAfterAndKeepOtherGroupsWorking()
    {
        await using var host = RequestsHost.Create(database);
        const string ip = "198.51.100.10";
        var approve = QuoteLinkApi.Body(Malformed, ("selectedOptionalLineIds", QuoteLinkApi.Ids()), ("acceptTerms", true));

        // Read group: view, calculate, photos and logo share 60 requests per 5 minutes per client.
        for (var count = 0; count < 60; count++)
        {
            var action = (count % 4) switch { 0 => "view", 1 => "calculate", 2 => $"photos/{Guid.Empty}", _ => "logo" };
            var response = await QuoteLinkApi.PostAsync(host, action, QuoteLinkApi.Body(Malformed), ip);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        var limited = await QuoteLinkApi.PostAsync(host, "view", QuoteLinkApi.Body(Malformed), ip);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(int.Parse(limited.Headers.GetValues("Retry-After").Single(), System.Globalization.CultureInfo.InvariantCulture) >= 1);
        Assert.Equal("no-store", limited.Headers.CacheControl?.ToString());
        Assert.Equal("no-referrer", limited.Headers.GetValues("Referrer-Policy").Single());
        Assert.Equal(429, (await RequestsHost.ReadAsync(limited))["status"]!.GetValue<int>());
        Assert.Equal(HttpStatusCode.TooManyRequests, (await QuoteLinkApi.PostAsync(host, "logo", QuoteLinkApi.Body(Malformed), ip)).StatusCode);

        // Another client is unaffected, and so are the other groups of the limited one.
        Assert.Equal(HttpStatusCode.NotFound, (await QuoteLinkApi.PostAsync(host, "view", QuoteLinkApi.Body(Malformed), "198.51.100.11")).StatusCode);

        // Action group: approve, decline and clarification share 10 per 15 minutes.
        for (var count = 0; count < 10; count++)
        {
            var response = (count % 3) switch
            {
                0 => await QuoteLinkApi.PostAsync(host, "approve", approve, ip),
                1 => await QuoteLinkApi.PostAsync(host, "decline", QuoteLinkApi.Body(Malformed, ("reason", "No")), ip),
                _ => await QuoteLinkApi.PostAsync(host, "clarification", QuoteLinkApi.Body(Malformed, ("message", "Why?")), ip),
            };
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        var actionLimited = await QuoteLinkApi.PostAsync(host, "approve", approve, ip);
        Assert.Equal(HttpStatusCode.TooManyRequests, actionLimited.StatusCode);
        Assert.NotEmpty(actionLimited.Headers.GetValues("Retry-After"));

        // PDF group: 10 per 5 minutes.
        for (var count = 0; count < 10; count++)
        {
            Assert.Equal(HttpStatusCode.NotFound, (await QuoteLinkApi.PostAsync(host, "pdf", QuoteLinkApi.Body(Malformed), ip)).StatusCode);
        }

        var pdfLimited = await QuoteLinkApi.PostAsync(host, "pdf", QuoteLinkApi.Body(Malformed), ip);
        Assert.Equal(HttpStatusCode.TooManyRequests, pdfLimited.StatusCode);
        Assert.NotEmpty(pdfLimited.Headers.GetValues("Retry-After"));

        // A fresh client still has every group.
        const string fresh = "198.51.100.12";
        Assert.Equal(HttpStatusCode.NotFound, (await QuoteLinkApi.PostAsync(host, "pdf", QuoteLinkApi.Body(Malformed), fresh)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await QuoteLinkApi.PostAsync(host, "approve", approve, fresh)).StatusCode);
    }

    // BR-21: the body limit is enforced by the real server and the rejection keeps the public headers.
    [Fact]
    public async Task Body_LargerThan16Kilobytes_Returns413WithPublicHeadersAndNoLeak()
    {
        var logs = new CapturingLoggerProvider();
        await using var baseFactory = FieldOpsApiFactory.Create(connectionString: database.ConnectionString);
        await using var factory = logs.AttachTo(baseFactory);
        factory.UseKestrel(0);
        factory.StartServer();
        using var client = factory.CreateClient();

        var secret = new string('s', QuoteLinksLimit + 1);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/public/quote-links/decline")
        {
            Content = new StringContent($$"""{"token":"{{new string('A', 43)}}","reason":"{{secret}}"}""", Encoding.UTF8, "application/json"),
        };

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl is { NoStore: true });
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.DoesNotContain(secret, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.DoesNotContain(logs.Entries, entry => $"{entry.Message}{entry.Exception}".Contains(new string('A', 43), StringComparison.Ordinal));

        // The photo id of the path is masked in the request log (BR-21).
        var photoId = Guid.NewGuid();
        using var photo = new HttpRequestMessage(HttpMethod.Post, $"/public/quote-links/photos/{photoId}")
        {
            Content = new StringContent($$"""{"token":"{{new string('A', 43)}}"}""", Encoding.UTF8, "application/json"),
        };
        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(photo)).StatusCode);
        Assert.DoesNotContain(logs.Entries, entry => entry.Message.Contains(photoId.ToString(), StringComparison.Ordinal));
        Assert.Contains(logs.Entries, entry => entry.Message.Contains("/public/quote-links/photos/{photoId}", StringComparison.Ordinal));
    }

    private const int QuoteLinksLimit = PublicQuoteLinksController.MaxRequestBodyBytes;
}
