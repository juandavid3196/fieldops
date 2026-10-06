using System.Net;
using System.Text;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.Quotes;
using FieldOps.IntegrationTests.ServiceRequests;

namespace FieldOps.IntegrationTests.QuoteLinks;

/// <summary>PDF download, the send guard of an approved quote and the derived expired status (AC-17, AC-20).</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class QuoteLinkPdfAndStaffTests(CompanySettingsDatabaseFixture database)
{
    private static async Task<byte[]> AssertPdfAsync(HttpResponseMessage response, string fileName)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal($"attachment; filename=\"{fileName}\"", response.Content.Headers.ContentDisposition?.ToString());
        Assert.True(response.Headers.CacheControl is { NoStore: true, Private: true });
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(bytes, 0, 5));

        return bytes;
    }

    // AC-17: the PDF is an attachment of the stored version in every state where the link shows content.
    [Fact]
    public async Task Pdf_IsGeneratedFromStoredDataInEveryVisibleState()
    {
        var world = await database.SeedQuoteWorldAsync();
        await using var host = RequestsHost.Create(database);
        var (owner, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var sent = await QuoteLinkApi.SendAsync(database, host, world, owner);
        var declined = await QuoteLinkApi.SendAsync(database, host, world, owner);
        var optional = QuoteLinkApi.OptionalIds(await QuoteLinkApi.ViewAsync(host, sent.Token));

        // Browser data is ignored: only the token is read.
        var before = await AssertPdfAsync(
            await QuoteLinkApi.PostAsync(host, "pdf", QuoteLinkApi.Body(sent.Token, ("total", 1m), ("organizationId", Guid.NewGuid().ToString()))),
            "Q-2036-v1.pdf");

        await QuoteLinkApi.PostAsync(
            host, "approve", QuoteLinkApi.Body(sent.Token, ("selectedOptionalLineIds", QuoteLinkApi.Ids(optional[0])), ("acceptTerms", true)));
        var after = await AssertPdfAsync(await QuoteLinkApi.PostAsync(host, "pdf", QuoteLinkApi.Body(sent.Token)), "Q-2036-v1.pdf");
        Assert.NotEqual(before, after);

        await QuoteLinkApi.PostAsync(host, "decline", QuoteLinkApi.Body(declined.Token, ("reason", "No")));
        await AssertPdfAsync(await QuoteLinkApi.PostAsync(host, "pdf", QuoteLinkApi.Body(declined.Token)), "Q-2037-v1.pdf");

        // The stored version is what is rendered: a later setting change does not alter the file name or the content type.
        await database.ExecuteAsync("UPDATE organizations SET quote_prefix = 'Z' WHERE id = @o", ("o", world.Org));
        await AssertPdfAsync(await QuoteLinkApi.PostAsync(host, "pdf", QuoteLinkApi.Body(sent.Token)), "Z-2036-v1.pdf");
    }

    // AC-20: the expired chip is derived, and an approved quote cannot send a revision.
    [Fact]
    public async Task StaffView_DerivesExpired_AndAnApprovedQuoteRefusesTheRevisionSend()
    {
        var world = await database.SeedQuoteWorldAsync();
        await using var host = RequestsHost.Create(database);
        var (owner, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);

        var lapsed = await QuoteLinkApi.SendAsync(database, host, world, owner);
        await database.ExecuteAsync(
            "UPDATE quote_versions SET valid_until = (now() AT TIME ZONE 'UTC')::date - 1 WHERE id = @v", ("v", lapsed.VersionId));
        var detail = await QuotesApi.GetAsync(host, owner, lapsed.QuoteId);
        Assert.Equal("sent", detail["status"]!.GetValue<string>());
        Assert.Equal("expired", detail["displayStatus"]!.GetValue<string>());
        Assert.Equal("sent", await database.QuoteStatusAsync(lapsed.QuoteId));

        // A revision draft created before the customer approves version 1.
        var approved = await QuoteLinkApi.SendAsync(database, host, world, owner);
        var revised = await host.SendAsync(
            HttpMethod.Post, $"/quotes/{approved.QuoteId}/revise", owner, QuotesApi.UpdatedAt(await QuotesApi.GetAsync(host, owner, approved.QuoteId)));
        Assert.Equal(HttpStatusCode.OK, revised.StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await QuoteLinkApi.PostAsync(
                host, "approve", QuoteLinkApi.Body(approved.Token, ("selectedOptionalLineIds", QuoteLinkApi.Ids()), ("acceptTerms", true)))).StatusCode);

        var current = await QuotesApi.GetAsync(host, owner, approved.QuoteId);
        Assert.Equal("approved", current["displayStatus"]!.GetValue<string>());
        var updatedAt = await database.QuoteUpdatedAtAsync(approved.QuoteId);
        var tokens = await database.ScalarAsync<long>(
            "SELECT COUNT(*) FROM quote_access_tokens WHERE organization_id = @o", ("o", world.Org));
        var send = await host.SendAsync(
            HttpMethod.Post,
            $"/quotes/{approved.QuoteId}/send",
            owner,
            QuotesApi.Draft([QuotesApi.Line(name: "Revised work")]).WithToken(current));
        Assert.Equal(HttpStatusCode.Conflict, send.StatusCode);
        Assert.Equal("quote_changed", QuotesApi.Code(await RequestsHost.ReadAsync(send)));
        Assert.Equal("approved", await database.QuoteStatusAsync(approved.QuoteId));
        Assert.Equal(updatedAt, await database.QuoteUpdatedAtAsync(approved.QuoteId));
        Assert.Equal(tokens, await database.ScalarAsync<long>("SELECT COUNT(*) FROM quote_access_tokens WHERE organization_id = @o", ("o", world.Org)));
        Assert.Equal(
            1,
            await database.ScalarAsync<long>(
                "SELECT COUNT(*) FROM quote_versions WHERE quote_id = @q AND NOT is_immutable", ("q", approved.QuoteId)));

        // The draft stays discardable.
        var discard = await host.SendAsync(
            HttpMethod.Post, $"/quotes/{approved.QuoteId}/discard-draft", owner, QuotesApi.UpdatedAt(current));
        Assert.Equal(HttpStatusCode.OK, discard.StatusCode);
        Assert.Equal(
            0,
            await database.ScalarAsync<long>(
                "SELECT COUNT(*) FROM quote_versions WHERE quote_id = @q AND NOT is_immutable", ("q", approved.QuoteId)));
        Assert.Equal("approved", await database.QuoteStatusAsync(approved.QuoteId));
    }
}
