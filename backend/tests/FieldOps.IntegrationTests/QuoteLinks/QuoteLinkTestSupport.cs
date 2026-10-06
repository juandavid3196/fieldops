using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.Api;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.Quotes;
using FieldOps.IntegrationTests.ServiceRequests;
using FieldOps.IntegrationTests.Sessions;

namespace FieldOps.IntegrationTests.QuoteLinks;

/// <summary>A quote sent through the real send flow, with the raw token read from the recorded email.</summary>
internal sealed record SentQuote(Guid QuoteId, Guid RequestId, Guid VersionId, string Token, JsonNode Detail)
{
    public string Hash => QuoteLinkApi.Hash(Token);
}

/// <summary>Request builders and seeds of the customer-quote-approval integration tests.</summary>
internal static class QuoteLinkApi
{
    public const string Base = "/public/quote-links";

    public const string OptionalTaxable = "Camera inspection";

    public const string OptionalUntaxed = "Extra trap";

    public static string Hash(string raw) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));

    public static JsonObject Body(string token, params (string Key, JsonNode? Value)[] extra)
    {
        var body = new JsonObject { ["token"] = token };

        foreach (var (key, value) in extra)
        {
            body[key] = value?.DeepClone();
        }

        return body;
    }

    public static JsonArray Ids(params Guid[] ids) => new([.. ids.Select(id => (JsonNode?)JsonValue.Create(id.ToString()))]);

    public static Task<HttpResponseMessage> PostAsync(
        RequestsHost host, string action, JsonObject body, string? ip = null, string? userAgent = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{Base}/{action}")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add(TestClientIpStartupFilter.HeaderName, ip ?? SessionApi.NewClientIp());

        if (userAgent is not null)
        {
            request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
        }

        return host.Client.SendAsync(request);
    }

    public static async Task<JsonNode> ReadAsync(HttpResponseMessage response) => await RequestsHost.ReadAsync(response);

    /// <summary>The default draft: 190.00 regular (2 h at 95.00, taxable), two optional lines (120.00 taxable, 40.00 untaxed) and a 10.00 discount.</summary>
    public static JsonObject Draft(string? message = "Thanks for choosing us.") =>
        QuotesApi.Draft(
            [
                QuotesApi.Line(name: "Drain cleaning", description: "Main line", quantity: 2m, unit: "hr", unitPrice: 95m, unitCost: 40m),
                QuotesApi.Line(name: OptionalTaxable, unitPrice: 120m, optional: true),
                QuotesApi.Line(name: OptionalUntaxed, unitPrice: 40m, taxable: false, optional: true),
            ],
            discount: 10m,
            message: message,
            note: "SECRET INTERNAL NOTE",
            preset: "custom",
            customText: "Pay within 14 days.");

    public static async Task<SentQuote> SendAsync(
        CompanySettingsDatabaseFixture db,
        RequestsHost host,
        RequestWorld world,
        string ownerCookie,
        JsonObject? draft = null,
        bool linkCustomer = true)
    {
        var request = await db.SeedReadyRequestAsync(world, linkCustomer: linkCustomer);
        var quote = await QuotesApi.CreateAsync(host, ownerCookie, request);
        var sent = await QuotesApi.SendAsync(host, ownerCookie, quote, draft ?? Draft());
        var quoteId = quote["id"]!.GetValue<Guid>();
        var token = QuotesApi.TokenOf(host.Sender.Messages[^1].TextBody);
        var versionId = await db.ScalarAsync<Guid>(
            "SELECT id FROM quote_versions WHERE quote_id = @q AND is_immutable ORDER BY version_no DESC LIMIT 1", ("q", quoteId));

        return new SentQuote(quoteId, request, versionId, token, sent["quote"]!);
    }

    public static async Task<JsonNode> ViewAsync(RequestsHost host, string token)
    {
        var response = await PostAsync(host, "view", Body(token));
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        return await ReadAsync(response);
    }

    /// <summary>The ids of the optional lines in the view, in line order (taxable first, then untaxed).</summary>
    public static Guid[] OptionalIds(JsonNode view) =>
        [.. view["lines"]!.AsArray().Where(line => line!["isOptional"]!.GetValue<bool>()).Select(line => line!["id"]!.GetValue<Guid>())];

    /// <summary>Moves the token (and the clock of its creation) into the past, as if the link expired.</summary>
    public static Task ExpireTokenAsync(this CompanySettingsDatabaseFixture db, string token) =>
        db.ExecuteAsync(
            "UPDATE quote_access_tokens SET created_at = now() - interval '2 days', expires_at = now() - interval '1 day' WHERE token_hash = @h",
            ("h", Hash(token)));

    public static Task<string> QuoteStatusAsync(this CompanySettingsDatabaseFixture db, Guid quoteId) =>
        db.ScalarAsync<string>("SELECT status::text FROM quotes WHERE id = @q", ("q", quoteId));

    public static Task<long> ResponseCountAsync(this CompanySettingsDatabaseFixture db, Guid quoteId, string? response = null) =>
        db.ScalarAsync<long>(
            """
            SELECT COUNT(*) FROM quote_responses r JOIN quote_versions v ON v.id = r.quote_version_id
            WHERE v.quote_id = @q AND (@r::text IS NULL OR r.response::text = @r)
            """,
            ("q", quoteId),
            ("r", response));
}
