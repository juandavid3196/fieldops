using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.ServiceRequests;

namespace FieldOps.IntegrationTests.Quotes;

/// <summary>Request builders and seeds of the quote-builder integration tests (modelled on <see cref="CompanySettingsApi"/>).</summary>
internal static partial class QuotesApi
{
    public const string RecipientEmail = "carla@example.com";

    public static JsonObject Line(
        string type = "service",
        string name = "Drain cleaning",
        decimal quantity = 1m,
        decimal unitPrice = 100m,
        decimal unitCost = 0m,
        bool taxable = true,
        bool optional = false,
        Guid? catalogItemId = null,
        string description = "",
        string unit = "unit") =>
        new()
        {
            ["catalogItemId"] = catalogItemId,
            ["type"] = type,
            ["name"] = name,
            ["description"] = description,
            ["quantity"] = quantity,
            ["unit"] = unit,
            ["unitPrice"] = unitPrice,
            ["unitCost"] = unitCost,
            ["taxable"] = taxable,
            ["isOptional"] = optional,
        };

    public static string Today(int days = 0) =>
        DateTime.UtcNow.Date.AddDays(days).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static JsonObject Draft(
        IEnumerable<JsonObject>? lines = null,
        decimal discount = 0m,
        string? message = null,
        string? note = null,
        string preset = "due_on_completion",
        string? customText = null,
        int validDays = 30) =>
        new()
        {
            ["lines"] = new JsonArray((lines ?? []).ToArray()),
            ["discountTotal"] = discount,
            ["customerMessage"] = message,
            ["internalNote"] = note,
            ["terms"] = new JsonObject { ["preset"] = preset, ["customText"] = customText },
            ["validUntil"] = Today(validDays),
        };

    public static JsonObject With(this JsonObject body, string key, JsonNode? value)
    {
        body[key] = value;

        return body;
    }

    public static JsonObject WithToken(this JsonObject body, JsonNode quote, string? message = "Hi Pat, here is your quote.") =>
        body.With("updatedAt", quote["updatedAt"]!.GetValue<string>()).With("emailMessage", message);

    public static JsonObject UpdatedAt(JsonNode quote) => new() { ["updatedAt"] = quote["updatedAt"]!.GetValue<string>() };

    public static string Error(JsonNode problem, string key) =>
        problem["errors"]![key]!.AsArray().Single()!.GetValue<string>();

    public static string Code(JsonNode problem) => problem["code"]!.GetValue<string>();

    public static async Task<JsonNode> CreateAsync(RequestsHost host, string cookie, Guid requestId)
    {
        var response = await host.SendAsync(HttpMethod.Post, "/quotes", cookie, new JsonObject { ["requestId"] = requestId });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return await RequestsHost.ReadAsync(response);
    }

    public static async Task<JsonNode> GetAsync(RequestsHost host, string cookie, Guid quoteId)
    {
        var response = await host.SendAsync(HttpMethod.Get, $"/quotes/{quoteId}", cookie);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await RequestsHost.ReadAsync(response);
    }

    /// <summary>Saves a draft and returns the refreshed quote detail.</summary>
    public static async Task<JsonNode> SaveAsync(RequestsHost host, string cookie, JsonNode quote, JsonObject draft)
    {
        var response = await host.SendAsync(
            HttpMethod.Put, $"/quotes/{quote["id"]}/draft", cookie, draft.With("updatedAt", quote["updatedAt"]!.GetValue<string>()));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await RequestsHost.ReadAsync(response);
    }

    /// <summary>Sends the quote and returns the response body (<c>quote</c> and <c>emailStatus</c>).</summary>
    public static async Task<JsonNode> SendAsync(RequestsHost host, string cookie, JsonNode quote, JsonObject draft)
    {
        var response = await host.SendAsync(HttpMethod.Post, $"/quotes/{quote["id"]}/send", cookie, draft.WithToken(quote));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await RequestsHost.ReadAsync(response);
    }

    public static async Task<Guid> SeedReadyRequestAsync(
        this CompanySettingsDatabaseFixture db,
        RequestWorld world,
        Guid? branch = null,
        bool linkCustomer = true,
        string? guestEmail = "guest@example.com")
    {
        var request = await db.SeedRequestAsync(
            world, status: "ready_for_quote", branch: branch ?? world.BranchA, linkCustomer: linkCustomer, guestEmail: guestEmail);

        return request.Id;
    }

    /// <summary>The seeded world with quote settings: prefix Q, next number 2036 and an 8.25 % tax rate.</summary>
    public static async Task<RequestWorld> SeedQuoteWorldAsync(this CompanySettingsDatabaseFixture db)
    {
        var world = await db.SeedWorldAsync();
        await db.ExecuteAsync(
            "UPDATE organizations SET quote_prefix = 'Q', next_quote_number = 2036, default_tax_rate = 8.25, currency = 'USD' WHERE id = @o",
            ("o", world.Org));

        return world;
    }

    public static async Task<string> QuoteAuditTextAsync(this CompanySettingsDatabaseFixture db, Guid quoteId, Guid requestId) =>
        await db.ScalarAsync<string>(
            """
            SELECT COALESCE(string_agg(action || ' ' || COALESCE(before_data::text, '') || ' ' || COALESCE(after_data::text, '') || ' ' || metadata::text, E'\n'), '')
            FROM audit_logs WHERE entity_id = @q OR entity_id = @r
            """,
            ("q", quoteId),
            ("r", requestId));

    public static Task<long> QuoteAuditCountAsync(this CompanySettingsDatabaseFixture db, Guid quoteId, string action) =>
        db.ScalarAsync<long>(
            "SELECT COUNT(*) FROM audit_logs WHERE entity_id = @q AND action = @a", ("q", quoteId), ("a", action));

    public static Task<long> LineCountAsync(this CompanySettingsDatabaseFixture db, Guid quoteId) =>
        db.ScalarAsync<long>(
            "SELECT COUNT(*) FROM quote_lines l JOIN quote_versions v ON v.id = l.quote_version_id WHERE v.quote_id = @q", ("q", quoteId));

    public static Task<DateTimeOffset> QuoteUpdatedAtAsync(this CompanySettingsDatabaseFixture db, Guid quoteId) =>
        db.ScalarAsync<DateTimeOffset>("SELECT updated_at FROM quotes WHERE id = @q", ("q", quoteId));

    public static string TokenOf(string emailText)
    {
        var match = TokenPattern().Match(emailText);
        Assert.True(match.Success, "The email must carry the /quote-approval#token= link.");

        return match.Groups[1].Value;
    }

    [GeneratedRegex("/quote-approval#token=([A-Za-z0-9_-]+)")]
    private static partial Regex TokenPattern();
}
