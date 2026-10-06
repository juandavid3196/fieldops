using System.Net;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.Catalog;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.Quotes;
using FieldOps.IntegrationTests.ServiceRequests;

namespace FieldOps.IntegrationTests.QuoteLinks;

/// <summary>Public content, frozen data, photo and logo scoping, headers and option totals (AC-04 to AC-08).</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class QuoteLinkContentTests(CompanySettingsDatabaseFixture database)
{
    private async Task<(Guid Assessment, Guid[] Photos)> SeedCompletedAssessmentAsync(
        Guid org, Guid request, Guid userId, int photoCount, string prefix, int hoursAgo = 2)
    {
        var assessment = await database.SeedAssessmentAsync(
            org, request, DateTimeOffset.UtcNow.AddHours(-hoursAgo - 1), DateTimeOffset.UtcNow.AddHours(-hoursAgo), userId, status: "completed");
        await database.ExecuteAsync(
            "UPDATE assessments SET diagnosis = 'SECRET DIAGNOSIS', recommended_scope = 'SECRET RECOMMENDED SCOPE', completed_at = now() - make_interval(hours => @h) WHERE id = @a",
            ("a", assessment),
            ("h", hoursAgo));
        var photos = new List<Guid>();

        for (var index = 0; index < photoCount; index++)
        {
            var id = Guid.NewGuid();
            var bytes = CatalogSeed.Jpeg(100 + index);
            await database.ExecuteAsync(
                "INSERT INTO assessment_attachments (id, organization_id, assessment_id, file_name, content, mime_type, size_bytes, created_at) VALUES (@id, @o, @a, @n, @c, 'image/jpeg', @s, now() + make_interval(secs => @i))",
                ("id", id),
                ("o", org),
                ("a", assessment),
                ("n", $"SECRET-FILE-{prefix}-{index}.jpg"),
                ("c", bytes),
                ("s", (long)bytes.Length),
                ("i", (double)index));
            photos.Add(id);
        }

        return (assessment, [.. photos]);
    }

    // AC-04, AC-05, AC-06, AC-07: the page shows the customer-facing content only, frozen, with scoped photos and logo.
    [Fact]
    public async Task View_ReturnsOnlyFrozenCustomerFacingContentWithTokenScopedPhotosAndLogo()
    {
        var world = await database.SeedQuoteWorldAsync();
        var foreign = await database.SeedQuoteWorldAsync();
        await using var host = RequestsHost.Create(database);
        var (owner, member) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var (foreignOwner, foreignMember) = await host.SignInAsync(database, foreign.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        await database.ExecuteAsync("UPDATE branches SET phone = '+1 555 777 0000' WHERE id = @b", ("b", world.BranchA));

        var sent = await QuoteLinkApi.SendAsync(database, host, world, owner);
        var sibling = await QuoteLinkApi.SendAsync(database, host, world, owner);
        var foreignSent = await QuoteLinkApi.SendAsync(database, host, foreign, foreignOwner);

        // The latest completed assessment lists its photos (up to 6); an older one and other quotes' photos are not served.
        var (_, older) = await SeedCompletedAssessmentAsync(world.Org, sent.RequestId, member.UserId, 1, "OLD", hoursAgo: 30);
        var (_, photos) = await SeedCompletedAssessmentAsync(world.Org, sent.RequestId, member.UserId, 8, "NEW");
        var (_, siblingPhotos) = await SeedCompletedAssessmentAsync(world.Org, sibling.RequestId, member.UserId, 1, "SIB");
        var (_, foreignPhotos) = await SeedCompletedAssessmentAsync(foreign.Org, foreignSent.RequestId, foreignMember.UserId, 1, "FOR");
        var logo = CatalogSeed.Png(200);
        await database.SeedLogoAsync(world.Org, logo);

        var response = await QuoteLinkApi.PostAsync(host, "view", QuoteLinkApi.Body(sent.Token));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        var text = await response.Content.ReadAsStringAsync();
        var view = JsonNode.Parse(text)!;

        var organizationName = await database.ScalarAsync<string>("SELECT name FROM organizations WHERE id = @o", ("o", world.Org));
        Assert.Equal(organizationName, view["organization"]!["name"]!.GetValue<string>());
        Assert.Equal("+1 555 777 0000", view["organization"]!["phone"]!.GetValue<string>());
        Assert.True(view["organization"]!["hasLogo"]!.GetValue<bool>());

        var info = view["quote"]!;
        Assert.Equal("Q-2036", info["displayNumber"]!.GetValue<string>());
        Assert.Equal(1, info["versionNo"]!.GetValue<int>());
        Assert.Equal("sent", info["status"]!.GetValue<string>());
        Assert.Equal(QuotesApi.Today(), info["sentOn"]!.GetValue<string>());
        Assert.Equal(QuotesApi.Today(30), info["validUntil"]!.GetValue<string>());
        Assert.Equal(await database.ScalarAsync<string>("SELECT scope FROM quote_versions WHERE id = @v", ("v", sent.VersionId)), info["scope"]!.GetValue<string>());
        Assert.Equal("Thanks for choosing us.", info["customerMessage"]!.GetValue<string>());
        Assert.Equal("Pay within 14 days.", info["terms"]!.GetValue<string>());
        Assert.Equal("Carla Customer", view["customer"]!["name"]!.GetValue<string>());
        Assert.Contains("1 Seed St", view["customer"]!["address"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal(["Drain cleaning"], view["scopeItems"]!.AsArray().Select(item => item!.GetValue<string>()).ToArray());

        var lines = view["lines"]!.AsArray();
        Assert.Equal(["Drain cleaning", QuoteLinkApi.OptionalTaxable, QuoteLinkApi.OptionalUntaxed], lines.Select(line => line!["name"]!.GetValue<string>()).ToArray());
        Assert.Null(lines[0]!["id"]);
        Assert.NotNull(lines[1]!["id"]);
        Assert.Equal((2m, "hr", 95m, 190m, "Main line", false), (lines[0]!["quantity"]!.GetValue<decimal>(), lines[0]!["unit"]!.GetValue<string>(), lines[0]!["unitPrice"]!.GetValue<decimal>(), lines[0]!["lineSubtotal"]!.GetValue<decimal>(), lines[0]!["description"]!.GetValue<string>(), lines[0]!["isOptional"]!.GetValue<bool>()));
        Assert.True(lines[2]!["isOptional"]!.GetValue<bool>());

        var totals = view["versionTotals"]!;
        Assert.Equal((190m, 10m, "Tax (8.25%)", 14.85m, 194.85m, "USD"), (totals["subtotal"]!.GetValue<decimal>(), totals["discountTotal"]!.GetValue<decimal>(), totals["taxLabel"]!.GetValue<string>(), totals["taxTotal"]!.GetValue<decimal>(), totals["total"]!.GetValue<decimal>(), totals["currency"]!.GetValue<string>()));
        Assert.Equal(photos.Take(6).Select(id => id.ToString()).ToArray(), view["photos"]!.AsArray().Select(photo => photo!["id"]!.GetValue<string>()).ToArray());
        Assert.Equal(QuotesApi.Today(), view["progress"]!["requestSubmittedOn"]!.GetValue<string>());
        Assert.Equal(QuotesApi.Today(), view["progress"]!["assessmentCompletedOn"]!.GetValue<string>());
        Assert.Null(view["clarification"]);
        Assert.Null(view["response"]);

        // AC-05: nothing internal, no ids of the organization, quote, version, request, customer or catalog, no file names.
        foreach (var forbidden in new[]
        {
            "SECRET", "unitCost", "margin", "internalNote", "diagnosis", "recommendedScope", "technician", "fileName", "catalogItemId", "organizationId",
            "quoteId", "versionId", "requestId", "carla@example.com", "5551234567", "Pat Contact", "+1 555 010 0100",
            world.Org.ToString(), sent.QuoteId.ToString(), sent.VersionId.ToString(), sent.RequestId.ToString(), world.Customer.ToString(), world.Contact.ToString(), world.Property.ToString(), world.BranchA.ToString(),
        })
        {
            Assert.DoesNotContain(forbidden, text, StringComparison.OrdinalIgnoreCase);
        }

        // AC-06: later catalog, tax rate, currency and prefix changes never alter the frozen version; only the prefix follows BR-06.
        await database.ExecuteAsync(
            "UPDATE organizations SET default_tax_rate = 20, currency = 'EUR', quote_prefix = 'Z' WHERE id = @o", ("o", world.Org));
        var later = await QuoteLinkApi.ViewAsync(host, sent.Token);
        Assert.Equal("Z-2036", later["quote"]!["displayNumber"]!.GetValue<string>());
        later["quote"]!["displayNumber"] = "Q-2036";
        Assert.Equal(view.ToJsonString(), later.ToJsonString());

        // AC-07: own photos and logo with the BR-07 headers; every foreign or unlisted photo is the generic 404.
        var served = await QuoteLinkApi.PostAsync(host, $"photos/{photos[0]}", QuoteLinkApi.Body(sent.Token));
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Equal("image/jpeg", served.Content.Headers.ContentType?.MediaType);
        Assert.Equal("inline", served.Content.Headers.ContentDisposition?.ToString());
        Assert.Equal("nosniff", served.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.True(served.Headers.CacheControl is { NoStore: true, Private: true });
        Assert.Equal("no-referrer", served.Headers.GetValues("Referrer-Policy").Single());
        Assert.Equal(CatalogSeed.Jpeg(100), await served.Content.ReadAsByteArrayAsync());

        foreach (var notListed in new[] { photos[6], photos[7], older[0], siblingPhotos[0], foreignPhotos[0], Guid.NewGuid() })
        {
            var denied = await QuoteLinkApi.PostAsync(host, $"photos/{notListed}", QuoteLinkApi.Body(sent.Token));
            Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
            Assert.Equal("quote_link_unavailable", (await RequestsHost.ReadAsync(denied))["code"]!.GetValue<string>());
        }

        var logoResponse = await QuoteLinkApi.PostAsync(host, "logo", QuoteLinkApi.Body(sent.Token));
        Assert.Equal(HttpStatusCode.OK, logoResponse.StatusCode);
        Assert.Equal("image/png", logoResponse.Content.Headers.ContentType?.MediaType);
        Assert.True(logoResponse.Headers.CacheControl is { NoStore: true, Private: true });
        Assert.Equal(logo, await logoResponse.Content.ReadAsByteArrayAsync());

        // The other organization has no logo: its own token gets the generic 404 and no initials source of A, and the view says so.
        Assert.Equal(HttpStatusCode.NotFound, (await QuoteLinkApi.PostAsync(host, "logo", QuoteLinkApi.Body(foreignSent.Token))).StatusCode);
        var foreignView = await QuoteLinkApi.ViewAsync(host, foreignSent.Token);
        Assert.False(foreignView["organization"]!["hasLogo"]!.GetValue<bool>());
        Assert.DoesNotContain(organizationName, foreignView.ToJsonString(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, (await QuoteLinkApi.PostAsync(host, $"photos/{photos[0]}", QuoteLinkApi.Body(foreignSent.Token))).StatusCode);
    }

    // AC-07, AC-08, AC-09: calculate validates the selection against the token's version only and never trusts the browser.
    [Fact]
    public async Task Calculate_SumsFrozenOptionalLinesAndRejectsForeignOrInvalidSelections()
    {
        var world = await database.SeedQuoteWorldAsync();
        var foreign = await database.SeedQuoteWorldAsync();
        await using var host = RequestsHost.Create(database);
        var (owner, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var (foreignOwner, _) = await host.SignInAsync(database, foreign.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var sent = await QuoteLinkApi.SendAsync(database, host, world, owner);
        var sibling = await QuoteLinkApi.SendAsync(database, host, world, owner);
        var foreignSent = await QuoteLinkApi.SendAsync(database, host, foreign, foreignOwner);
        var view = await QuoteLinkApi.ViewAsync(host, sent.Token);
        var optional = QuoteLinkApi.OptionalIds(view);
        var siblingOptional = QuoteLinkApi.OptionalIds(await QuoteLinkApi.ViewAsync(host, sibling.Token));
        var foreignOptional = QuoteLinkApi.OptionalIds(await QuoteLinkApi.ViewAsync(host, foreignSent.Token));
        var regular = await database.ScalarAsync<Guid>(
            "SELECT id FROM quote_lines WHERE quote_version_id = @v AND NOT is_optional", ("v", sent.VersionId));
        var updatedAt = await database.QuoteUpdatedAtAsync(sent.QuoteId);

        // Totals: none equals the version, the 120.00 taxable option adds 9.90 tax, the untaxed one only its amount.
        var cases = new (Guid[] Selected, string Subtotal, string Tax, string Total, string Label)[]
        {
            ([], "190.00", "14.85", "194.85", "Tax (8.25%)"),
            ([optional[0]], "310.00", "24.75", "324.75", "Tax (8.25%)"),
            ([optional[1]], "230.00", "14.85", "234.85", "Tax (8.25%)"),
            ([optional[0], optional[1]], "350.00", "24.75", "364.75", "Tax (8.25%)"),
        };

        foreach (var (selected, subtotal, tax, total, label) in cases)
        {
            var response = await QuoteLinkApi.PostAsync(
                host,
                "calculate",
                QuoteLinkApi.Body(
                    sent.Token,
                    ("selectedOptionalLineIds", QuoteLinkApi.Ids(selected)),
                    ("total", 1m),
                    ("subtotal", 1m),
                    ("organizationId", foreign.Org.ToString()),
                    ("quoteId", foreignSent.QuoteId.ToString())));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var totals = await RequestsHost.ReadAsync(response);
            Assert.Equal(
                (subtotal, "10.00", label, tax, total, "USD"),
                (
                    totals["subtotal"]!.GetValue<decimal>().ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                    totals["discountTotal"]!.GetValue<decimal>().ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                    totals["taxLabel"]!.GetValue<string>(),
                    totals["taxTotal"]!.GetValue<decimal>().ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                    totals["total"]!.GetValue<decimal>().ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                    totals["currency"]!.GetValue<string>()));
        }

        // BR-10: duplicates, regular lines, unknown ids, text, lines of another quote or organization.
        var invalid = new JsonNode?[]
        {
            QuoteLinkApi.Ids(optional[0], optional[0]),
            QuoteLinkApi.Ids(regular),
            QuoteLinkApi.Ids(Guid.NewGuid()),
            new JsonArray("not-an-id"),
            QuoteLinkApi.Ids(optional[0], siblingOptional[0]),
            QuoteLinkApi.Ids(foreignOptional[0]),
        };

        foreach (var selected in invalid)
        {
            foreach (var action in new[] { "calculate", "approve" })
            {
                var body = QuoteLinkApi.Body(sent.Token, ("selectedOptionalLineIds", selected), ("acceptTerms", true));
                var response = await QuoteLinkApi.PostAsync(host, action, body);
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
                var problem = await RequestsHost.ReadAsync(response);
                Assert.Equal("One or more optional items aren't available.", QuotesApi.Error(problem, "selectedOptionalLineIds"));
                Assert.DoesNotContain(foreign.Org.ToString(), problem.ToJsonString(), StringComparison.Ordinal);
            }
        }

        // Nothing was persisted and the quote is untouched.
        Assert.Equal(0, await database.ResponseCountAsync(sent.QuoteId));
        Assert.Equal("sent", await database.QuoteStatusAsync(sent.QuoteId));
        Assert.Equal(updatedAt, await database.QuoteUpdatedAtAsync(sent.QuoteId));
        Assert.Equal(0, await database.ScalarAsync<long>("SELECT COUNT(*) FROM quote_response_optional_lines WHERE organization_id = @o", ("o", world.Org)));
    }
}
