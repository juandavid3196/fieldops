using System.Net;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.Catalog;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.ServiceRequests;

namespace FieldOps.IntegrationTests.Quotes;

/// <summary>Draft save and calculate (quote-builder AC-06 to AC-09, AC-13, AC-14): snapshots, catalog checks, validation, ordering and concurrency.</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class QuoteDraftTests(CompanySettingsDatabaseFixture database)
{
    private static JsonArray Array(JsonNode node, string name) => node[name]!.AsArray();

    [Fact]
    public async Task SaveDraft_StoresBackendSnapshotsAndCalculatesWithoutPersisting()
    {
        var world = await database.SeedQuoteWorldAsync();
        var foreign = await database.SeedQuoteWorldAsync();
        await using var host = RequestsHost.Create(database);
        var (owner, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var service = await database.SeedCatalogItemAsync(world.Org, "service", "Drain cleaning svc", 95m, 30m);
        var inactive = await database.SeedCatalogItemAsync(world.Org, "product", "Copper pipe", 10m, 4m, active: false);
        var foreignItem = await database.SeedCatalogItemAsync(foreign.Org, "service", "Foreign service", 50m);
        var request = await database.SeedReadyRequestAsync(world);
        var quote = await QuotesApi.CreateAsync(host, owner, request);
        var quoteId = quote["id"]!.GetValue<Guid>();
        var path = $"/quotes/{quoteId}";

        // AC-06: catalog snapshots stay editable, an inactive item is accepted and client amounts are ignored.
        var draft = QuotesApi.Draft(
            [
                QuotesApi.Line("service", "Edited service name", 2m, 120m, 35m, catalogItemId: service, description: "Edited"),
                QuotesApi.Line("product", "Copper pipe", 3m, 10m, 4m, catalogItemId: inactive, unit: "m"),
                QuotesApi.Line("service", "Custom visit", 1m, 40m, 10m, taxable: false),
                QuotesApi.Line("product", "Optional filter", 1m, 25m, 5m, optional: true),
            ],
            message: "  Thanks for choosing us  ",
            note: "Internal remark")
            .With("total", 1m)
            .With("organizationId", foreign.Org)
            .With("calculation", new JsonObject { ["total"] = 1m });
        draft["lines"]![0]!["lineSubtotal"] = 999_999m;
        draft["lines"]![0]!["taxRate"] = 99m;

        var saved = await QuotesApi.SaveAsync(host, owner, quote, draft);
        var lines = Array(saved["draft"]!, "lines");

        Assert.Equal(["Edited service name", "Copper pipe", "Custom visit", "Optional filter"], lines.Select(line => line!["name"]!.GetValue<string>()));
        Assert.Equal(["service", "product", "service", "product"], lines.Select(line => line!["type"]!.GetValue<string>()));
        Assert.Equal("Thanks for choosing us", saved["draft"]!["customerMessage"]!.GetValue<string>());
        Assert.NotEqual(quote["updatedAt"]!.GetValue<string>(), saved["updatedAt"]!.GetValue<string>());

        // 240.00 + 30.00 + 40.00 = 310.00; tax 8.25 % on the first two lines only (19.80 + 2.48).
        var calculation = saved["draft"]!["calculation"]!;
        Assert.Equal([240.00m, 30.00m, 40.00m, 25.00m], Array(calculation, "lines").Select(line => line!["lineSubtotal"]!.GetValue<decimal>()));
        Assert.Equal(310.00m, calculation["subtotal"]!.GetValue<decimal>());
        Assert.Equal(22.28m, calculation["taxTotal"]!.GetValue<decimal>());
        Assert.Equal(332.28m, calculation["total"]!.GetValue<decimal>());

        var rows = await database.ScalarAsync<string>(
            """
            SELECT string_agg(l.name || ':' || l.line_type::text || ':' || l.is_optional::text || ':' || l.tax_rate::text || ':' || l.sort_order::text
                || ':' || (l.organization_id = @o)::text || ':' || (l.catalog_item_id IS NOT NULL)::text, ' ' ORDER BY l.sort_order)
            FROM quote_lines l JOIN quote_versions v ON v.id = l.quote_version_id WHERE v.quote_id = @q
            """,
            ("o", world.Org),
            ("q", quoteId));
        Assert.Equal(
            "Edited service name:service:false:8.2500:0:true:true Copper pipe:product:false:8.2500:1:true:true Custom visit:service:false:0.0000:2:true:false Optional filter:product:true:8.2500:3:true:false",
            rows);
        Assert.Equal(95m, await database.ScalarAsync<decimal>("SELECT unit_price FROM catalog_items WHERE id = @i", ("i", service)));
        Assert.Equal(332.28m, await database.ScalarAsync<decimal>("SELECT total FROM quote_versions WHERE quote_id = @q", ("q", quoteId)));
        Assert.Equal(1, await database.QuoteAuditCountAsync(quoteId, "quote.draft_saved"));

        // AC-07: the submitted order persists through sort_order and a reload.
        var reordered = QuotesApi.Draft(
        [
            QuotesApi.Line(name: "Second", unitPrice: 20m),
            QuotesApi.Line(name: "First", unitPrice: 10m),
            QuotesApi.Line(name: "Optional B", unitPrice: 5m, optional: true),
            QuotesApi.Line(name: "Optional A", unitPrice: 6m, optional: true),
        ]);
        var reload = await QuotesApi.SaveAsync(host, owner, saved, reordered);
        Assert.Equal(
            ["Second", "First", "Optional B", "Optional A"],
            Array((await QuotesApi.GetAsync(host, owner, quoteId))["draft"]!, "lines").Select(line => line!["name"]!.GetValue<string>()));

        // AC-09: the design sample, with the optional line of AC-11 outside the totals.
        var sample = await host.SendAsync(
            HttpMethod.Post,
            $"{path}/calculate",
            owner,
            QuotesApi.Draft(
            [
                QuotesApi.Line(quantity: 2m, unitPrice: 95m),
                QuotesApi.Line(unitPrice: 38m),
                QuotesApi.Line(unitPrice: 42m),
                QuotesApi.Line(unitPrice: 65m, taxable: false),
                QuotesApi.Line(unitPrice: 120m, optional: true),
            ]));
        Assert.Equal(HttpStatusCode.OK, sample.StatusCode);
        var result = await RequestsHost.ReadAsync(sample);
        Assert.Equal([190.00m, 38.00m, 42.00m, 65.00m, 120.00m], Array(result, "lines").Select(line => line["lineSubtotal"]!.GetValue<decimal>()));
        Assert.Equal([15.68m, 3.14m, 3.47m, 0.00m, 9.90m], Array(result, "lines").Select(line => line["lineTax"]!.GetValue<decimal>()));
        Assert.Equal(335.00m, result["subtotal"]!.GetValue<decimal>());
        Assert.Equal(22.29m, result["taxTotal"]!.GetValue<decimal>());
        Assert.Equal(357.29m, result["total"]!.GetValue<decimal>());
        Assert.Equal("Tax (8.25%)", result["taxLabel"]!.GetValue<string>());
        Assert.Equal("USD", result["currency"]!.GetValue<string>());

        // AC-10, AC-12: discount allocation, the discount bound and the internal margin.
        var discounted = await RequestsHost.ReadAsync(await host.SendAsync(
            HttpMethod.Post,
            $"{path}/calculate",
            owner,
            QuotesApi.Draft(
                [
                    QuotesApi.Line(unitPrice: 100m, unitCost: 40m),
                    QuotesApi.Line(unitPrice: 50m, unitCost: 20m),
                    QuotesApi.Line(unitPrice: 33.33m),
                    QuotesApi.Line(unitPrice: 80m, unitCost: 500m, optional: true),
                ],
                discount: 10m)));
        Assert.Equal([5.47m, 2.72m, 1.81m, 0m], Array(discounted, "lines").Select(line => line["discountShare"]!.GetValue<decimal>()));
        Assert.Equal(65.4m, discounted["margin"]!["percent"]!.GetValue<decimal>());
        Assert.Equal(113.33m, discounted["margin"]!["grossProfit"]!.GetValue<decimal>());
        var tooMuch = await host.SendAsync(
            HttpMethod.Post, $"{path}/calculate", owner, QuotesApi.Draft([QuotesApi.Line(unitPrice: 100m)], discount: 100.01m));
        Assert.Equal("Discount can't exceed the subtotal.", QuotesApi.Error(await RequestsHost.ReadAsync(tooMuch), "discountTotal"));
        var free = await RequestsHost.ReadAsync(await host.SendAsync(
            HttpMethod.Post, $"{path}/calculate", owner, QuotesApi.Draft([QuotesApi.Line(unitPrice: 0m, unitCost: 5m)])));
        Assert.Null(free["margin"]!["percent"]);

        // AC-13: calculate persisted nothing.
        var afterCalculate = await QuotesApi.GetAsync(host, owner, quoteId);
        Assert.Equal(reload["updatedAt"]!.GetValue<string>(), afterCalculate["updatedAt"]!.GetValue<string>());
        Assert.Equal(4, await database.LineCountAsync(quoteId));
        Assert.Equal(2, await database.QuoteAuditCountAsync(quoteId, "quote.draft_saved"));

        // AC-08: every invalid value is a 400 on its exact key and changes nothing.
        var cases = new (string Key, string Message, Action<JsonObject> Mutate)[]
        {
            ("lines[0].type", "Choose a type.", d => d["lines"]![0]!["type"] = "bundle"),
            ("lines[0].name", "Enter an item name.", d => d["lines"]![0]!["name"] = "   "),
            ("lines[0].name", "Item name must be 160 characters or fewer.", d => d["lines"]![0]!["name"] = new string('n', 161)),
            ("lines[0].description", "Description must be 1,000 characters or fewer.", d => d["lines"]![0]!["description"] = new string('d', 1001)),
            ("lines[0].quantity", "Enter a quantity greater than 0.", d => d["lines"]![0]!["quantity"] = 0m),
            ("lines[0].quantity", "Enter a quantity greater than 0.", d => d["lines"]![0]!["quantity"] = 1.0001m),
            ("lines[0].quantity", "Enter a quantity greater than 0.", d => d["lines"]![0]!["quantity"] = 1_000_000m),
            ("lines[0].unit", "Enter a unit.", d => d["lines"]![0]!["unit"] = ""),
            ("lines[0].unit", "Enter a unit.", d => d["lines"]![0]!["unit"] = new string('u', 41)),
            ("lines[0].unitPrice", "Enter a price of 0 or more.", d => d["lines"]![0]!["unitPrice"] = -1m),
            ("lines[0].unitPrice", "Enter a price of 0 or more.", d => d["lines"]![0]!["unitPrice"] = 1.001m),
            ("lines[0].unitCost", "Enter a cost of 0 or more.", d => d["lines"]![0]!["unitCost"] = -0.01m),
            ("lines[0].taxable", "Choose a tax option.", d => d["lines"]![0]!["taxable"] = null),
            ("lines[0].catalogItemId", "This catalog item isn't available.", d => d["lines"]![0]!["catalogItemId"] = foreignItem),
            ("discountTotal", "Enter a discount of 0 or more.", d => d["discountTotal"] = -1m),
            ("discountTotal", "Discount can't exceed the subtotal.", d => d["discountTotal"] = 100.01m),
            ("customerMessage", "Customer message must be 500 characters or fewer.", d => d["customerMessage"] = new string('m', 501)),
            ("internalNote", "Internal note must be 500 characters or fewer.", d => d["internalNote"] = new string('i', 501)),
            ("terms.customText", "Enter the terms.", d => d["terms"] = new JsonObject { ["preset"] = "custom", ["customText"] = " " }),
            ("terms.customText", "Terms must be 2000 characters or fewer.", d => d["terms"] = new JsonObject { ["preset"] = "custom", ["customText"] = new string('t', 2001) }),
            ("terms.preset", "Choose the payment terms.", d => d["terms"] = new JsonObject { ["preset"] = "net_99" }),
            ("validUntil", "Choose a date between tomorrow and one year from today.", d => d["validUntil"] = QuotesApi.Today()),
            ("validUntil", "Choose a date between tomorrow and one year from today.", d => d["validUntil"] = QuotesApi.Today(366)),
            ("lines", "A quote can have up to 100 lines.", d => d["lines"] = new JsonArray(Enumerable.Range(0, 101).Select(index => (JsonNode)QuotesApi.Line(name: $"L{index}")).ToArray())),
            ("lines", "This quote total is too large.", d =>
            {
                d["lines"]![0]!["quantity"] = 999_999.999m;
                d["lines"]![0]!["unitPrice"] = 999_999_999.99m;
            }),
        };

        foreach (var (key, message, mutate) in cases)
        {
            var body = QuotesApi.Draft([QuotesApi.Line()]);
            mutate(body);

            var rejected = await host.SendAsync(HttpMethod.Put, $"{path}/draft", owner, body.With("updatedAt", reload["updatedAt"]!.GetValue<string>()));

            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            Assert.Equal(message, QuotesApi.Error(await RequestsHost.ReadAsync(rejected), key));
        }

        var unchanged = await QuotesApi.GetAsync(host, owner, quoteId);
        Assert.Equal(reload["updatedAt"]!.GetValue<string>(), unchanged["updatedAt"]!.GetValue<string>());
        Assert.Equal(4, await database.LineCountAsync(quoteId));

        // 404 comes before field validation.
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await host.SendAsync(HttpMethod.Put, $"/quotes/{Guid.NewGuid()}/draft", owner, QuotesApi.Draft([QuotesApi.Line(name: "")]).With("updatedAt", "2030-01-01T00:00:00Z"))).StatusCode);

        // AC-14: a stale token is a 409 with its code; of a concurrent save and send exactly one wins.
        var stale = await host.SendAsync(
            HttpMethod.Put, $"{path}/draft", owner, QuotesApi.Draft([QuotesApi.Line()]).With("updatedAt", quote["updatedAt"]!.GetValue<string>()));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var problem = await RequestsHost.ReadAsync(stale);
        Assert.Equal("quote_changed", QuotesApi.Code(problem));
        Assert.Equal("This quote changed. Refresh to see the latest.", problem["title"]!.GetValue<string>());
        Assert.Equal(4, await database.LineCountAsync(quoteId));

        var race = await Task.WhenAll(
            host.SendAsync(HttpMethod.Put, $"{path}/draft", owner, QuotesApi.Draft([QuotesApi.Line(name: "Race save")]).With("updatedAt", reload["updatedAt"]!.GetValue<string>())),
            host.SendAsync(HttpMethod.Post, $"{path}/send", owner, QuotesApi.Draft([QuotesApi.Line(name: "Race send")]).WithToken(reload)));
        Assert.Equal(1, race.Count(response => response.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, race.Count(response => response.StatusCode == HttpStatusCode.Conflict));
    }
}
