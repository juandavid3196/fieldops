using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.OnlinePayments;
using FieldOps.IntegrationTests.ServiceRequests;

namespace FieldOps.IntegrationTests.Portal;

/// <summary>Customer and organization isolation of every portal resolution path (customer portal BR-37, AC-11).</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class PortalIsolationTests(CompanySettingsDatabaseFixture database)
{
    private sealed record Resources(Guid Property, Guid Request, Guid Visit, Guid Quote, Guid Invoice, Guid Order, Guid Payment, Guid Photo, Guid Attempt)
    {
        public IEnumerable<Guid> All => [Property, Request, Visit, Quote, Invoice, Order, Payment, Photo, Attempt];
    }

    private sealed record Call(HttpMethod Method, string Path, JsonNode? Body = null);

    [Fact]
    public async Task PortalEndpoints_WithIdsOfAnotherCustomerOrOrganization_ReturnTheIdenticalNotFoundAndChangeNothing()
    {
        await using var host = OnlineHost.Create(database);
        var staff = await database.SeedUserAsync();
        var a1 = await database.SeedWorldAsync();
        var a2 = await database.SeedSiblingAsync(a1, "Second Customer");
        var b1 = await database.SeedWorldAsync();
        var user = await database.LinkPortalAsync(a1.Org, a1.Contact);
        var own = await SeedSetAsync(a1, staff);
        var sameOrganization = await SeedSetAsync(a2, staff);
        var otherOrganization = await SeedSetAsync(b1, staff);
        var unknown = new Resources(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var cookie = await PortalApi.CookieAsync(host.Client, user.Email);

        // Positive controls: the customer's own resources resolve.
        foreach (var path in new[]
        {
            $"/portal/requests/{own.Request}",
            $"/portal/appointments/{own.Visit}",
            $"/portal/quotes/{own.Quote}",
            $"/portal/invoices/{own.Invoice}",
            $"/portal/invoices/{own.Invoice}/payments/{own.Payment}/receipt",
            $"/portal/invoices/{own.Invoice}/photos/{own.Photo}/content",
            $"/portal/invoices/{own.Invoice}/payment-attempts/{own.Attempt}",
            $"/portal/work-orders/{own.Order}/completion-report",
            $"/portal/dashboard?propertyId={own.Property}",
        })
        {
            var response = await PortalApi.GetAsync(host.Client, path, cookie);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{path}: expected 200 but was {(int)response.StatusCode}");
        }

        var calls = new List<Call>();

        foreach (var foreign in new[] { sameOrganization, otherOrganization, unknown })
        {
            calls.AddRange(CallsFor(foreign));
        }

        // The customer's own invoice or quote with an id of another customer's payment, attempt or photo.
        calls.AddRange(
        [
            new(HttpMethod.Get, $"/portal/invoices/{own.Invoice}/payments/{sameOrganization.Payment}/receipt"),
            new(HttpMethod.Get, $"/portal/invoices/{own.Invoice}/payments/{otherOrganization.Payment}/receipt"),
            new(HttpMethod.Get, $"/portal/invoices/{own.Invoice}/photos/{sameOrganization.Photo}/content"),
            new(HttpMethod.Get, $"/portal/invoices/{own.Invoice}/photos/{otherOrganization.Photo}/content"),
            new(HttpMethod.Get, $"/portal/invoices/{own.Invoice}/payment-attempts/{sameOrganization.Attempt}"),
            new(HttpMethod.Get, $"/portal/invoices/{own.Invoice}/payment-attempts/{otherOrganization.Attempt}"),
            new(HttpMethod.Get, $"/portal/quotes/{own.Quote}/photos/{sameOrganization.Photo}/content"),
        ]);

        var before = await StateAsync(a1.Org, b1.Org);
        var bodies = new HashSet<string>();

        foreach (var call in calls)
        {
            var response = await PortalApi.SendAsync(host.Client, call.Method, call.Path, cookie, call.Body);

            Assert.True(
                response.StatusCode == HttpStatusCode.NotFound,
                $"{call.Method} {call.Path}: expected 404 but was {(int)response.StatusCode}");
            await PortalApi.AssertUnavailableAsync(response);
            bodies.Add(await PortalApi.BodyWithoutTraceAsync(response));
        }

        Assert.Single(bodies);
        Assert.Equal(before, await StateAsync(a1.Org, b1.Org));

        // Lists, the dashboard and the updates feed carry only the customer's own data.
        var seen = string.Empty;

        foreach (var path in new[]
        {
            "/portal/dashboard",
            "/portal/requests",
            "/portal/quotes",
            "/portal/invoices",
            "/portal/appointments?scope=upcoming",
            "/portal/appointments?scope=past",
            "/portal/activity",
            "/portal/updates",
            "/portal/properties",
        })
        {
            seen += await (await PortalApi.GetAsync(host.Client, path, cookie)).Content.ReadAsStringAsync();
        }

        foreach (var id in sameOrganization.All.Concat(otherOrganization.All))
        {
            Assert.DoesNotContain(id.ToString(), seen, StringComparison.OrdinalIgnoreCase);
        }

        foreach (var id in new[] { own.Request, own.Quote, own.Invoice, own.Visit, own.Property })
        {
            Assert.Contains(id.ToString(), seen, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static IEnumerable<Call> CallsFor(Resources r)
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(5).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var key = () => new JsonObject { ["idempotencyKey"] = Guid.NewGuid().ToString() };

        return
        [
            new(HttpMethod.Get, $"/portal/requests/{r.Request}"),
            new(HttpMethod.Get, $"/portal/appointments/{r.Visit}"),
            new(HttpMethod.Post, $"/portal/appointments/{r.Visit}/reschedule-requests", new JsonObject { ["preferredDate"] = date, ["timeWindow"] = "any", ["reason"] = "Away" }),
            new(HttpMethod.Get, $"/portal/quotes/{r.Quote}"),
            new(HttpMethod.Get, $"/portal/quotes/{r.Quote}/pdf"),
            new(HttpMethod.Get, $"/portal/quotes/{r.Quote}/photos"),
            new(HttpMethod.Get, $"/portal/quotes/{r.Quote}/photos/{r.Photo}/content"),
            new(HttpMethod.Post, $"/portal/quotes/{r.Quote}/calculate", new JsonObject { ["selectedOptionalLineIds"] = new JsonArray() }),
            new(HttpMethod.Post, $"/portal/quotes/{r.Quote}/approve", new JsonObject { ["selectedOptionalLineIds"] = new JsonArray(), ["acceptTerms"] = true }),
            new(HttpMethod.Post, $"/portal/quotes/{r.Quote}/reject", new JsonObject { ["reason"] = "No thanks" }),
            new(HttpMethod.Post, $"/portal/quotes/{r.Quote}/clarification", new JsonObject { ["message"] = "A question here" }),
            new(HttpMethod.Get, $"/portal/invoices/{r.Invoice}"),
            new(HttpMethod.Get, $"/portal/invoices/{r.Invoice}/pdf"),
            new(HttpMethod.Get, $"/portal/invoices/{r.Invoice}/completion-report"),
            new(HttpMethod.Get, $"/portal/invoices/{r.Invoice}/photos"),
            new(HttpMethod.Get, $"/portal/invoices/{r.Invoice}/photos/{r.Photo}/content"),
            new(HttpMethod.Get, $"/portal/invoices/{r.Invoice}/payments/{r.Payment}/receipt"),
            new(HttpMethod.Get, $"/portal/invoices/{r.Invoice}/payment-attempts/{r.Attempt}"),
            new(HttpMethod.Post, $"/portal/invoices/{r.Invoice}/payment-attempts", key()),
            new(HttpMethod.Post, $"/portal/invoices/{r.Invoice}/bank-transfer-notice", key()),
            new(HttpMethod.Post, $"/portal/invoices/{r.Invoice}/review", new JsonObject { ["rating"] = 5, ["comment"] = "ok" }),
            new(HttpMethod.Get, $"/portal/work-orders/{r.Order}/completion-report"),
            new(
                HttpMethod.Patch,
                $"/portal/properties/{r.Property}",
                new JsonObject { ["name"] = "Hacked", ["isPrimary"] = true, ["updatedAt"] = "2026-10-01T00:00:00.000000+00:00" }),
            new(HttpMethod.Get, $"/portal/dashboard?propertyId={r.Property}"),
            new(HttpMethod.Get, $"/portal/requests?propertyId={r.Property}"),
            new(HttpMethod.Get, $"/portal/appointments?propertyId={r.Property}"),
            new(HttpMethod.Get, $"/portal/quotes?propertyId={r.Property}"),
            new(HttpMethod.Get, $"/portal/invoices?propertyId={r.Property}"),
            new(HttpMethod.Get, $"/portal/activity?propertyId={r.Property}"),
        ];
    }

    private async Task<Resources> SeedSetAsync(RequestWorld world, Guid staff)
    {
        var now = DateTimeOffset.UtcNow;
        var quote = await database.SeedQuoteAsync(world, staff, "sent", DateOnly.FromDateTime(now.UtcDateTime).AddDays(10), now.AddDays(-1));
        var visit = await database.SeedVisitAsync(world, staff, "Water heater", "scheduled", now.AddDays(3));
        var invoice = await database.InvoiceAsync(world, staff, new OnlineSpec());
        var payment = await database.SeedPaymentAsync(world.Org, world.Customer, invoice.Id, staff, 10m, now.AddDays(-1));
        var attempt = await database.PendingAttemptAsync(world.Org, invoice);

        return new Resources(world.Property, quote.Request, visit.Visit, quote.Quote, invoice.Id, invoice.Order, payment, invoice.Job.BeforePhoto, attempt.Attempt);
    }

    // Everything the portal writes: audit, reschedule requests, quote answers and statuses, payment attempts, reviews, properties, requests.
    private Task<string> StateAsync(Guid first, Guid second) =>
        database.ScalarAsync<string>(
            """
            SELECT (SELECT count(*) FROM audit_logs WHERE organization_id IN (@a, @b))
                || '|' || (SELECT count(*) FROM visit_reschedule_requests WHERE organization_id IN (@a, @b))
                || '|' || (SELECT count(*) FROM quote_responses WHERE organization_id IN (@a, @b))
                || '|' || (SELECT string_agg(status::text || updated_at::text, ',' ORDER BY id) FROM quotes WHERE organization_id IN (@a, @b))
                || '|' || (SELECT string_agg(status::text || updated_at::text, ',' ORDER BY id) FROM invoice_payment_attempts WHERE organization_id IN (@a, @b))
                || '|' || (SELECT count(*) FROM invoice_reviews WHERE organization_id IN (@a, @b))
                || '|' || (SELECT string_agg(name || is_primary::text || updated_at::text, ',' ORDER BY id) FROM properties WHERE organization_id IN (@a, @b))
                || '|' || (SELECT count(*) FROM service_requests WHERE organization_id IN (@a, @b))
                || '|' || (SELECT string_agg(status::text || amount_paid::text || balance_due::text, ',' ORDER BY id) FROM invoices WHERE organization_id IN (@a, @b))
            """,
            ("a", first),
            ("b", second));
}
