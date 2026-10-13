using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using FieldOps.Application.Features.Users;
using FieldOps.IntegrationTests.Api;
using FieldOps.IntegrationTests.BillingReview;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.Customers;
using FieldOps.IntegrationTests.ServiceRequests;
using FieldOps.IntegrationTests.Sessions;
using FieldOps.IntegrationTests.Team;
using Microsoft.Net.Http.Headers;

namespace FieldOps.IntegrationTests.Portal;

/// <summary>A user linked to a customer contact through the portal.</summary>
internal sealed record PortalUser(Guid UserId, string Email);

/// <summary>A work order of a seeded customer with its single visit.</summary>
internal sealed record PortalVisit(Guid Order, long Number, Guid Visit);

/// <summary>Request builders and response helpers of the customer portal integration tests (separate cookie, JSON only).</summary>
internal static class PortalApi
{
    public const string CookieName = "fieldops_portal_session";

    public static Task<HttpResponseMessage> SendAsync(
        HttpClient client, HttpMethod method, string path, string? cookie, JsonNode? body = null, string? ip = null)
    {
        var request = new HttpRequestMessage(method, path);

        if (body is not null)
        {
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        }

        if (cookie is not null)
        {
            request.Headers.Add("Cookie", cookie);
        }

        request.Headers.Add(TestClientIpStartupFilter.HeaderName, ip ?? SessionApi.NewClientIp());

        return client.SendAsync(request);
    }

    public static Task<HttpResponseMessage> GetAsync(HttpClient client, string path, string? cookie) =>
        SendAsync(client, HttpMethod.Get, path, cookie);

    public static Task<HttpResponseMessage> PostAsync(HttpClient client, string path, string? cookie, JsonNode? body = null) =>
        SendAsync(client, HttpMethod.Post, path, cookie, body ?? new JsonObject());

    public static Task<HttpResponseMessage> SignInAsync(
        HttpClient client, string email, string? password = null, bool? rememberMe = null, string? ip = null)
    {
        var body = new JsonObject { ["email"] = email, ["password"] = password ?? CompanySettingsDatabaseFixture.Password };

        if (rememberMe is not null)
        {
            body["rememberMe"] = rememberMe;
        }

        return SendAsync(client, HttpMethod.Post, "/portal/sessions", null, body, ip);
    }

    public static IReadOnlyList<SetCookieHeaderValue> PortalCookies(HttpResponseMessage response) =>
        response.Headers.TryGetValues(HeaderNames.SetCookie, out var values)
            ? [.. values.Select(value => SetCookieHeaderValue.Parse(value)).Where(cookie => cookie.Name.Equals(CookieName, StringComparison.Ordinal))]
            : [];

    public static string IssuedCookie(HttpResponseMessage response)
    {
        var cookie = Assert.Single(PortalCookies(response));
        Assert.False(SessionApi.IsDeletion(cookie));

        return $"{cookie.Name}={cookie.Value}";
    }

    public static void AssertCookieDeleted(HttpResponseMessage response)
    {
        var cookie = Assert.Single(PortalCookies(response));
        Assert.True(SessionApi.IsDeletion(cookie), $"Expected a deletion but got {cookie}");
    }

    public static async Task<string> CookieAsync(HttpClient client, string email)
    {
        var response = await SignInAsync(client, email);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return IssuedCookie(response);
    }

    public static Task<JsonNode> ReadAsync(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK) =>
        BillingSeed.ReadAsync(response, expected);

    /// <summary>The body of a response without its per-request trace id, for identical-response assertions.</summary>
    public static async Task<string> BodyWithoutTraceAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();

        if (text.Length == 0 || JsonNode.Parse(text) is not JsonObject body)
        {
            return text;
        }

        body.Remove("traceId");

        return body.ToJsonString();
    }

    public static async Task AssertUnavailableAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("portal_resource_unavailable", JsonNode.Parse(await response.Content.ReadAsStringAsync())!["code"]!.GetValue<string>());
    }

    /// <summary>The multipart request of POST /portal/service-requests (one JSON part, optional files).</summary>
    public static Task<HttpResponseMessage> SubmitRequestAsync(HttpClient client, string? cookie, JsonObject body)
    {
        var form = new MultipartFormDataContent { { new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"), "request" } };
        var request = new HttpRequestMessage(HttpMethod.Post, "/portal/service-requests") { Content = form };

        if (cookie is not null)
        {
            request.Headers.Add("Cookie", cookie);
        }

        request.Headers.Add(TestClientIpStartupFilter.HeaderName, SessionApi.NewClientIp());

        return client.SendAsync(request);
    }

    public static JsonObject RequestBody(RequestWorld world, JsonObject property) =>
        new()
        {
            ["property"] = property,
            ["service"] = new JsonObject
            {
                ["categoryId"] = world.Category.ToString(),
                ["serviceId"] = world.Service.ToString(),
                ["notSure"] = false,
                ["description"] = "Kitchen drain is clogged",
                ["urgency"] = "standard",
                ["hasActiveDamage"] = false,
            },
            ["availability"] = new JsonObject
            {
                ["dateMode"] = "asap",
                ["preferredDate"] = null,
                ["timeWindow"] = "any",
                ["schedulingNotes"] = null,
            },
            ["consent"] = true,
            ["website"] = string.Empty,
        };
}

/// <summary>Direct SQL seeds of the customer portal tests; statuses are test-controlled constants, never user input.</summary>
internal static class PortalSeed
{
    public static string NewToken() => InvitationTokens.Generate().Raw;

    public static async Task<PortalUser> LinkPortalAsync(
        this CompanySettingsDatabaseFixture db,
        Guid org,
        Guid contact,
        string? email = null,
        DateTimeOffset? linkedAt = null,
        Guid? existingUser = null)
    {
        var userId = existingUser ?? await db.SeedUserAsync(email);
        var address = await db.ScalarAsync<string>("SELECT email FROM users WHERE id = @u", ("u", userId));

        await db.ExecuteAsync(
            "UPDATE customer_contacts SET portal_user_id = @u, portal_linked_at = @at WHERE organization_id = @o AND id = @c",
            ("u", userId),
            ("at", (linkedAt ?? DateTimeOffset.UtcNow.AddDays(-1)).ToUniversalTime()),
            ("o", org),
            ("c", contact));

        return new PortalUser(userId, address);
    }

    /// <summary>A second customer (with its contact and primary property) of the same organization as <paramref name="world"/>.</summary>
    public static async Task<RequestWorld> SeedSiblingAsync(this CompanySettingsDatabaseFixture db, RequestWorld world, string name = "Other Customer")
    {
        var customer = await db.SeedCustomerAsync(world.Org, world.BranchA, name, email: $"{Guid.NewGuid():N}@example.com", phone: "5559876543");
        var contact = (await db.QueryGuidsAsync("SELECT id FROM customer_contacts WHERE customer_id = @c", ("c", customer))).Single();
        var property = (await db.QueryGuidsAsync("SELECT id FROM properties WHERE customer_id = @c", ("c", customer))).Single();

        return world with { Customer = customer, Contact = contact, Property = property };
    }

    public static async Task<Guid> SeedPropertyAsync(
        this CompanySettingsDatabaseFixture db, RequestWorld world, string name, bool primary = false, bool active = true)
    {
        var id = Guid.NewGuid();

        await db.ExecuteAsync(
            """
            INSERT INTO properties (id, organization_id, customer_id, branch_id, name, address_line1, city, state_region, postal_code, country_code, is_primary, is_active)
            VALUES (@id, @org, @customer, @branch, @name, '5 Elm St', 'Austin', 'TX', '78701', 'US', @primary, @active)
            """,
            ("id", id),
            ("org", world.Org),
            ("customer", world.Customer),
            ("branch", world.BranchA),
            ("name", name),
            ("primary", primary),
            ("active", active));

        return id;
    }

    /// <summary>A quote of the customer through its own request (the indexed customer path of the portal).</summary>
    public static async Task<(Guid Quote, Guid Request, long Number)> SeedQuoteAsync(
        this CompanySettingsDatabaseFixture db,
        RequestWorld world,
        Guid staff,
        string status,
        DateOnly? validUntil,
        DateTimeOffset? sentAt,
        DateTimeOffset? requestCreatedAt = null,
        decimal total = 100m,
        string scope = "Replace the supply line")
    {
        var request = await db.SeedRequestAsync(world, status: "quoted", branch: world.BranchA, createdAt: requestCreatedAt);
        var quote = Guid.NewGuid();
        var number = await db.ScalarAsync<long>(
            "SELECT COALESCE(MAX(quote_number), 0) + 1 FROM quotes WHERE organization_id = @o", ("o", world.Org));

        await db.ExecuteAsync(
            $"""
            INSERT INTO quotes (id, organization_id, branch_id, request_id, customer_id, property_id, quote_number, status, current_version_no, created_by_user_id)
            VALUES (@quote, @org, @branch, @request, @customer, @property, @number, CAST('{status}' AS quote_status), 1, @user);
            INSERT INTO quote_versions (id, organization_id, quote_id, version_no, scope, subtotal, discount_total, tax_total, total, currency, valid_until, sent_at, is_immutable, created_by_user_id)
            VALUES (gen_random_uuid(), @org, @quote, 1, @scope, @total, 0, 0, @total, 'USD', @valid, @sent, @immutable, @user);
            """,
            ("immutable", sentAt is not null),
            ("quote", quote),
            ("org", world.Org),
            ("branch", world.BranchA),
            ("request", request.Id),
            ("customer", world.Customer),
            ("property", world.Property),
            ("number", number),
            ("user", staff),
            ("scope", scope),
            ("total", total),
            ("valid", validUntil),
            ("sent", sentAt?.ToUniversalTime()));

        return (quote, request.Id, number);
    }

    /// <summary>A work order of the customer with one visit; the FKs to the (unneeded) quote request are bypassed in the throwaway container.</summary>
    public static async Task<PortalVisit> SeedVisitAsync(
        this CompanySettingsDatabaseFixture db,
        RequestWorld world,
        Guid staff,
        string title,
        string visitStatus,
        DateTimeOffset? start,
        string orderStatus = "scheduled",
        DateTimeOffset? completedAt = null,
        DateTimeOffset? arrivalStart = null,
        DateTimeOffset? arrivalEnd = null)
    {
        var order = Guid.NewGuid();
        var visit = Guid.NewGuid();
        var number = await db.ScalarAsync<long>(
            "SELECT COALESCE(MAX(work_order_number), 0) + 1 FROM work_orders WHERE organization_id = @o", ("o", world.Org));
        var quoteNumber = await db.ScalarAsync<long>(
            "SELECT COALESCE(MAX(quote_number), 0) + 1 FROM quotes WHERE organization_id = @o", ("o", world.Org));

        await db.ExecuteAsync(
            $"""
            SET session_replication_role = replica;
            INSERT INTO quotes (id, organization_id, branch_id, request_id, customer_id, property_id, quote_number, status, current_version_no, created_by_user_id)
            VALUES (gen_random_uuid(), @org, @branch, gen_random_uuid(), @customer, @property, @qnumber, 'approved', 1, @user);
            INSERT INTO quote_versions (id, organization_id, quote_id, version_no, scope, subtotal, discount_total, tax_total, total, currency, is_immutable, created_by_user_id)
            VALUES (@version, @org, (SELECT id FROM quotes WHERE organization_id = @org AND quote_number = @qnumber), 1, 'Scope', 0, 0, 0, 0, 'USD', true, @user);
            INSERT INTO work_orders (id, organization_id, branch_id, work_order_number, quote_version_id, customer_id, property_id, title, service_category_id, status, scope_snapshot, created_by_user_id)
            VALUES (@order, @org, @branch, @number, @version, @customer, @property, @title, @category, CAST('{orderStatus}' AS work_order_status), 'Scope', @user);
            INSERT INTO visits (id, organization_id, work_order_id, visit_number, status, scheduled_start, scheduled_end, actual_completed_at, arrival_window_start, arrival_window_end)
            VALUES (@visit, @org, @order, 1, CAST('{visitStatus}' AS visit_status), @start, @end, @completed, @aws, @awe);
            SET session_replication_role = DEFAULT;
            """,
            ("org", world.Org),
            ("branch", world.BranchA),
            ("customer", world.Customer),
            ("property", world.Property),
            ("qnumber", quoteNumber),
            ("user", staff),
            ("version", Guid.NewGuid()),
            ("order", order),
            ("number", number),
            ("title", title),
            ("category", world.Category),
            ("visit", visit),
            ("start", start?.ToUniversalTime()),
            ("end", start?.AddHours(2).ToUniversalTime()),
            ("completed", completedAt?.ToUniversalTime()),
            ("aws", arrivalStart?.ToUniversalTime()),
            ("awe", arrivalEnd?.ToUniversalTime()));

        return new PortalVisit(order, number, visit);
    }

    public static Task AssignAsync(this CompanySettingsDatabaseFixture db, Guid visit, Guid technician, Guid staff, DateTimeOffset at) =>
        db.ExecuteAsync(
            "INSERT INTO visit_assignments (visit_id, technician_id, assigned_by_user_id, is_primary, assigned_at) VALUES (@v, @t, @u, true, @at)",
            ("v", visit),
            ("t", technician),
            ("u", staff),
            ("at", at.ToUniversalTime()));

    public static Task SeedVisitHistoryAsync(this CompanySettingsDatabaseFixture db, Guid visit, string to, DateTimeOffset at) =>
        db.ExecuteAsync(
            "INSERT INTO visit_status_history (visit_id, to_status, changed_at) VALUES (@v, CAST(@to AS visit_status), @at)",
            ("v", visit),
            ("to", to),
            ("at", at.ToUniversalTime()));

    /// <summary>A cash payment with a receipt number, allocated to the invoice.</summary>
    public static async Task<Guid> SeedPaymentAsync(
        this CompanySettingsDatabaseFixture db, Guid org, Guid customer, Guid invoice, Guid staff, decimal amount, DateTimeOffset createdAt)
    {
        var id = Guid.NewGuid();
        var number = await db.ScalarAsync<long>(
            "SELECT COALESCE(MAX(payment_number), 0) + 1 FROM payments WHERE organization_id = @o", ("o", org));

        await db.ExecuteAsync(
            """
            INSERT INTO payments (id, organization_id, customer_id, payment_number, method, amount, currency, paid_at, idempotency_key, received_by_user_id, receipt_number, created_at)
            VALUES (@id, @org, @customer, @number, CAST('cash' AS payment_method), @amount, 'USD', @at, gen_random_uuid(), @user, @receipt, @at);
            INSERT INTO payment_allocations (payment_id, invoice_id, amount, created_at) VALUES (@id, @invoice, @amount, @at);
            """,
            ("id", id),
            ("org", org),
            ("customer", customer),
            ("number", number),
            ("amount", amount),
            ("at", createdAt.ToUniversalTime()),
            ("user", staff),
            ("receipt", $"RCT-{id:N}"[..12]),
            ("invoice", invoice));

        return id;
    }

    public static Task SetOrganizationEmailAsync(this CompanySettingsDatabaseFixture db, Guid org, string? email) =>
        db.ExecuteAsync("UPDATE organizations SET email = @e WHERE id = @o", ("e", email), ("o", org));

    public static Task SeedInvitationAsync(
        this CompanySettingsDatabaseFixture db, Guid org, Guid contact, Guid staff, string token, string email) =>
        db.ExecuteAsync(
            """
            INSERT INTO customer_portal_invitations (organization_id, contact_id, email, token_hash, invited_by_user_id, expires_at, created_at)
            VALUES (@org, @contact, @email, @hash, @user, now() + interval '7 days', now())
            """,
            ("org", org),
            ("contact", contact),
            ("email", email.ToLowerInvariant()),
            ("hash", InvitationTokens.Hash(token)),
            ("user", staff));
}
