using System.Net;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.BillingReview;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.OnlinePayments;
using FieldOps.IntegrationTests.QuoteLinks;
using FieldOps.IntegrationTests.Quotes;
using FieldOps.IntegrationTests.ServiceRequests;

namespace FieldOps.IntegrationTests.Portal;

/// <summary>The portal quote and invoice endpoints reuse the shared use cases, authorized by the session (customer portal BR-30, BR-31, AC-20 … AC-22).</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class PortalQuoteInvoiceTests(CompanySettingsDatabaseFixture database)
{
    [Fact]
    public async Task PortalQuotesAndInvoices_ApproveAskDeclineExpireAndPay_ReuseTheSharedUseCasesWithoutTouchingTokens()
    {
        await using var staffHost = RequestsHost.Create(database);
        await using var host = OnlineHost.Create(database);
        var world = await database.SeedQuoteWorldAsync();
        var (staffCookie, staff) = await staffHost.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var user = await database.LinkPortalAsync(world.Org, world.Contact);
        var approve = await QuoteLinkApi.SendAsync(database, staffHost, world, staffCookie);
        var ask = await QuoteLinkApi.SendAsync(database, staffHost, world, staffCookie);
        var decline = await QuoteLinkApi.SendAsync(database, staffHost, world, staffCookie);
        var lapsed = await QuoteLinkApi.SendAsync(database, staffHost, world, staffCookie);
        var invoice = await database.InvoiceAsync(world, staff.UserId, new OnlineSpec());
        var paid = await database.InvoiceAsync(world, staff.UserId, new OnlineSpec { Status = "paid", Paid = 357.28m });
        var payment = await database.SeedPaymentAsync(world.Org, world.Customer, paid.Id, staff.UserId, 357.28m, DateTimeOffset.UtcNow.AddDays(-1));
        var cookie = await PortalApi.CookieAsync(host.Client, user.Email);

        var quoteTokens = await TokenRowsAsync("quote_access_tokens", world.Org);
        var invoiceTokens = await TokenRowsAsync("invoice_access_tokens", world.Org);

        // Approve with an optional item: server totals, the contact as the responder, the portal as the audited actor.
        var view = await PortalApi.ReadAsync(await PortalApi.GetAsync(host.Client, $"/portal/quotes/{approve.QuoteId}", cookie));
        Assert.Equal("sent", view["quote"]!["status"]!.GetValue<string>());
        var optional = QuoteLinkApi.OptionalIds(view);
        var calculated = await PortalApi.ReadAsync(await PortalApi.PostAsync(
            host.Client, $"/portal/quotes/{approve.QuoteId}/calculate", cookie, new JsonObject { ["selectedOptionalLineIds"] = QuoteLinkApi.Ids(optional[0]) }));
        var approveBody = new JsonObject { ["selectedOptionalLineIds"] = QuoteLinkApi.Ids(optional[0]), ["acceptTerms"] = true };
        var approved = await PortalApi.ReadAsync(await PortalApi.PostAsync(host.Client, $"/portal/quotes/{approve.QuoteId}/approve", cookie, approveBody));
        Assert.Equal("approved", approved["quote"]!["status"]!.GetValue<string>());
        Assert.Equal(324.75m, approved["response"]!["totals"]!["total"]!.GetValue<decimal>());
        Assert.Equal(calculated["total"]!.GetValue<decimal>(), approved["response"]!["totals"]!["total"]!.GetValue<decimal>());
        Assert.Equal(
            $"approved|Pat Contact|{world.Contact}",
            await database.ScalarAsync<string>(
                "SELECT response::text || '|' || responder_name || '|' || responder_contact_id FROM quote_responses WHERE quote_version_id = @v", ("v", approve.VersionId)));
        var audit = await database.ScalarAsync<string>(
            "SELECT actor_user_id || ' ' || metadata::text FROM audit_logs WHERE entity_id = @q AND action = 'quote.approved'", ("q", approve.QuoteId));
        Assert.StartsWith(user.UserId.ToString(), audit, StringComparison.Ordinal);
        Assert.Contains("\"channel\":\"portal\"", audit.Replace(" ", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Equal(1, await database.ResponseCountAsync(approve.QuoteId));
        Assert.Equal(HttpStatusCode.OK, (await PortalApi.PostAsync(host.Client, $"/portal/quotes/{approve.QuoteId}/approve", cookie, approveBody)).StatusCode);
        await BillingSeed.ProblemAsync(
            await PortalApi.PostAsync(host.Client, $"/portal/quotes/{approve.QuoteId}/reject", cookie, new JsonObject { ["reason"] = "Changed my mind" }),
            HttpStatusCode.Conflict,
            "quote_already_answered");
        Assert.Equal(1, await database.ResponseCountAsync(approve.QuoteId));

        // A question and a decline from the portal.
        var asked = await PortalApi.ReadAsync(await PortalApi.PostAsync(
            host.Client, $"/portal/quotes/{ask.QuoteId}/clarification", cookie, new JsonObject { ["message"] = "Can you confirm the warranty period?" }));
        Assert.Equal("clarification_requested", asked["quote"]!["status"]!.GetValue<string>());
        var declined = await PortalApi.ReadAsync(await PortalApi.PostAsync(
            host.Client, $"/portal/quotes/{decline.QuoteId}/reject", cookie, new JsonObject { ["reason"] = "Too expensive for us right now" }));
        Assert.Equal("rejected", declined["quote"]!["status"]!.GetValue<string>());

        // Expired by date: read-only "expired" in the list and the detail, 409 quote_expired on every action, nothing written.
        await database.ExecuteAsync(
            "UPDATE quote_versions SET valid_until = (now() AT TIME ZONE 'UTC')::date - 1 WHERE id = @v", ("v", lapsed.VersionId));
        var list = await PortalApi.ReadAsync(await PortalApi.GetAsync(host.Client, "/portal/quotes", cookie));
        Assert.Equal("expired", list["items"]!.AsArray().Single(row => row!["id"]!.GetValue<Guid>() == lapsed.QuoteId)!["status"]!.GetValue<string>());
        Assert.Equal("expired", (await PortalApi.ReadAsync(await PortalApi.GetAsync(host.Client, $"/portal/quotes/{lapsed.QuoteId}", cookie)))["quote"]!["status"]!.GetValue<string>());

        foreach (var (action, body) in new (string, JsonObject)[]
        {
            ("approve", new JsonObject { ["selectedOptionalLineIds"] = new JsonArray(), ["acceptTerms"] = true }),
            ("reject", new JsonObject { ["reason"] = "Not needed any more" }),
            ("clarification", new JsonObject { ["message"] = "Is this still valid?" }),
        })
        {
            await BillingSeed.ProblemAsync(
                await PortalApi.PostAsync(host.Client, $"/portal/quotes/{lapsed.QuoteId}/{action}", cookie, body), HttpStatusCode.Conflict, "quote_expired");
        }

        Assert.Equal("sent", await database.QuoteStatusAsync(lapsed.QuoteId));
        Assert.Equal(0, await database.ResponseCountAsync(lapsed.QuoteId));

        // Card payment attempt: created once, replayed by key, audited as the portal user; no provider call twice.
        var key = Guid.NewGuid();
        var viewed = await PortalApi.ReadAsync(await PortalApi.GetAsync(host.Client, $"/portal/invoices/{invoice.Id}", cookie));
        Assert.Equal(invoice.Display, viewed["number"]!.GetValue<string>());
        var created = await PortalApi.ReadAsync(
            await PortalApi.PostAsync(host.Client, $"/portal/invoices/{invoice.Id}/payment-attempts", cookie, new JsonObject { ["idempotencyKey"] = key.ToString() }),
            HttpStatusCode.Created);
        Assert.Equal(357.28m, created["amount"]!.GetValue<decimal>());
        Assert.NotNull(created["clientSecret"]);
        var replay = await PortalApi.ReadAsync(
            await PortalApi.PostAsync(host.Client, $"/portal/invoices/{invoice.Id}/payment-attempts", cookie, new JsonObject { ["idempotencyKey"] = key.ToString() }));
        Assert.Equal(created["attemptId"]!.GetValue<Guid>(), replay["attemptId"]!.GetValue<Guid>());
        Assert.Single(host.Gateway.Requests);
        Assert.Equal(1, await database.CountAsync("invoice_payment_attempts", "invoice_id = @i", ("i", invoice.Id)));
        var status = await PortalApi.GetAsync(host.Client, $"/portal/invoices/{invoice.Id}/payment-attempts/{created["attemptId"]!.GetValue<Guid>()}", cookie);
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        var attemptAudit = await database.ScalarAsync<string>(
            "SELECT actor_user_id || ' ' || metadata::text FROM audit_logs WHERE organization_id = @o AND action = 'invoice_payment_attempt.created'", ("o", world.Org));
        Assert.StartsWith(user.UserId.ToString(), attemptAudit, StringComparison.Ordinal);

        // A paid invoice: receipt, completion reports, photos and one review, all for the customer's own resources.
        foreach (var path in new[]
        {
            $"/portal/invoices/{paid.Id}/payments/{payment}/receipt",
            $"/portal/invoices/{paid.Id}/completion-report",
            $"/portal/invoices/{paid.Id}/pdf",
            $"/portal/work-orders/{paid.Order}/completion-report",
        })
        {
            var pdf = await PortalApi.GetAsync(host.Client, path, cookie);
            Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
            Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
            Assert.True(pdf.Headers.CacheControl is { NoStore: true, Private: true });
        }

        var photos = await PortalApi.ReadAsync(await PortalApi.GetAsync(host.Client, $"/portal/invoices/{paid.Id}/photos", cookie));
        Assert.Equal(2, photos.AsArray().Count);
        var photo = await PortalApi.GetAsync(host.Client, $"/portal/invoices/{paid.Id}/photos/{photos[0]!["photoId"]!.GetValue<Guid>()}/content", cookie);
        Assert.Equal("image/png", photo.Content.Headers.ContentType?.MediaType);
        var review = new JsonObject { ["rating"] = 5, ["comment"] = "Great work" };
        Assert.Equal(HttpStatusCode.Created, (await PortalApi.PostAsync(host.Client, $"/portal/invoices/{paid.Id}/review", cookie, review)).StatusCode);
        await BillingSeed.ProblemAsync(
            await PortalApi.PostAsync(host.Client, $"/portal/invoices/{paid.Id}/review", cookie, review), HttpStatusCode.Conflict, "review_exists");

        // No public token was created, read or revoked by any of it.
        Assert.Equal(quoteTokens, await TokenRowsAsync("quote_access_tokens", world.Org));
        Assert.Equal(invoiceTokens, await TokenRowsAsync("invoice_access_tokens", world.Org));
    }

    private Task<string> TokenRowsAsync(string table, Guid org) =>
        database.ScalarAsync<string>(
            $"SELECT count(*) || '|' || count(revoked_at) || '|' || COALESCE(string_agg(id::text, ',' ORDER BY id), '') FROM {table} WHERE organization_id = @o",
            ("o", org));
}
