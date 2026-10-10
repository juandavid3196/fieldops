using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using FieldOps.Application.Features.Email;
using FieldOps.Application.Features.OnlinePayments;
using FieldOps.Application.Features.Organizations;
using FieldOps.Infrastructure.Payments;
using FieldOps.IntegrationTests.Api;
using FieldOps.IntegrationTests.BillingReview;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.InvoiceDelivery;
using FieldOps.IntegrationTests.InvoicePayments;
using FieldOps.IntegrationTests.PasswordResets;
using FieldOps.IntegrationTests.ServiceRequests;
using FieldOps.IntegrationTests.Sessions;
using FieldOps.IntegrationTests.Users;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace FieldOps.IntegrationTests.OnlinePayments;

/// <summary>
/// The scriptable payment provider of the integration tests (customer-invoice-payments). Webhooks are verified with the very
/// same <see cref="StripeWebhookParser"/> as production, signed with <see cref="WebhookSecret"/>; everything else is
/// deterministic and recorded, so no test ever calls Stripe.
/// </summary>
internal sealed class FakePaymentGateway : IPaymentGateway
{
    public const string WebhookSecret = "whsec_integration_test";

    public const string Publishable = "pk_test_integration";

    private readonly ConcurrentQueue<GatewayIntentRequest> _requests = new();

    private readonly ConcurrentQueue<string> _canceled = new();

    public bool CardAvailable { get; set; } = true;

    public bool FailCreate { get; set; }

    public bool FailCardDetails { get; set; }

    public bool ThrowOnCancel { get; set; }

    public GatewayCancelResult CancelResult { get; set; } = GatewayCancelResult.Canceled;

    public GatewayCardDetails Card { get; set; } = new("visa", "4242");

    public IReadOnlyList<GatewayIntentRequest> Requests => [.. _requests];

    public IReadOnlyList<string> Canceled => [.. _canceled];

    public PaymentGatewayCapabilities Capabilities => CardAvailable ? new(true, Publishable) : new(false, null);

    public static string IntentId(Guid attempt) => $"pi_{attempt:N}";

    public static string ClientSecret(string intentId) => $"{intentId}_secret_xyz";

    public Task<GatewayIntent> CreateIntentAsync(GatewayIntentRequest request, CancellationToken cancellationToken)
    {
        if (FailCreate)
        {
            throw new PaymentGatewayUnavailableException("scripted outage");
        }

        _requests.Enqueue(request);
        var intent = IntentId(request.AttemptId);

        return Task.FromResult(new GatewayIntent(intent, ClientSecret(intent)));
    }

    public Task<string> RetrieveClientSecretAsync(string intentId, CancellationToken cancellationToken) =>
        Task.FromResult(ClientSecret(intentId));

    public Task<GatewayCancelResult> CancelIntentAsync(string intentId, CancellationToken cancellationToken)
    {
        _canceled.Enqueue(intentId);

        return ThrowOnCancel ? throw new PaymentGatewayUnavailableException("scripted outage") : Task.FromResult(CancelResult);
    }

    public Task<GatewayCardDetails> GetCardDetailsAsync(string intentId, CancellationToken cancellationToken) =>
        FailCardDetails ? throw new PaymentGatewayUnavailableException("scripted outage") : Task.FromResult(Card);

    public GatewayWebhookParse ParseWebhook(string payload, string? signature) =>
        StripeWebhookParser.Parse(payload, signature, WebhookSecret);
}

/// <summary>One API host over the shared container with a recording email port, the fake provider and log capture.</summary>
internal sealed class OnlineHost : IAsyncDisposable
{
    private readonly SessionTestHost _host;

    private OnlineHost(SessionTestHost host, HttpClient client, RecordingEmailSender sender, FakePaymentGateway gateway)
    {
        _host = host;
        Client = client;
        Sender = sender;
        Gateway = gateway;
    }

    public HttpClient Client { get; }

    public RecordingEmailSender Sender { get; }

    public FakePaymentGateway Gateway { get; }

    public CapturingLoggerProvider Logs => _host.Logs!;

    /// <summary>A host with the fake provider; <paramref name="bankKey"/> false removes the encryption key (bank transfer unavailable).</summary>
    public static OnlineHost Create(CompanySettingsDatabaseFixture database, bool bankKey = true)
    {
        var host = SessionTestHost.Create(database.ConnectionString, captureLogs: true);
        var sender = new RecordingEmailSender();
        var gateway = new FakePaymentGateway();
        var factory = host.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(sender);
            services.RemoveAll<IPaymentGateway>();
            services.AddSingleton<IPaymentGateway>(gateway);

            if (!bankKey)
            {
                services.RemoveAll<IBankDetailsEncryptor>();
                services.AddSingleton<IBankDetailsEncryptor>(new AesGcmBankDetailsEncryptor(Options.Create(new BankDetailsSettings())));
            }
        }));

        return new OnlineHost(host, SessionApi.CreateClient(factory), sender, gateway);
    }

    public async Task<(string Cookie, SeededMember Member)> SignInAsync(
        CompanySettingsDatabaseFixture database, Guid org, short role, string first = "Olivia", string last = "Owner")
    {
        var member = await database.SeedMemberAsync(org, role, first, last);

        return (await CompanySettingsApi.SignInCookieAsync(Client, member.Email), member);
    }

    public Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? cookie, JsonObject? body = null) =>
        CompanySettingsApi.SendRawAsync(Client, method, path, body?.ToJsonString(), cookie);

    public ValueTask DisposeAsync()
    {
        Client.Dispose();

        return _host.DisposeAsync();
    }
}

internal sealed record OnlineInvoice(Guid Id, long Number, Guid Order, long OrderNumber, Guid Customer, Guid Branch, string Token, BillingJob Job)
{
    public string Display => $"INV-{Number}";
}

internal sealed record OnlineSpec
{
    public string Status { get; init; } = "sent";

    public decimal Total { get; init; } = 357.28m;

    public decimal Paid { get; init; }

    public int? DueDays { get; init; } = 5;

    public string? Recipient { get; init; } = "carla@example.com";

    /// <summary>A full job (checklist, photos, sign-off); the technician is its primary assignee.</summary>
    public bool FullJob { get; init; } = true;

    public Guid? Technician { get; init; }

    /// <summary>A job still in progress: no completed visit, so no report, photos or service date.</summary>
    public bool OpenJob { get; init; }
}

internal static class OnlineSeed
{
    public const string Timezone = "UTC";

    public const string BankAccount = "123456789012";

    public const string Routing = "021000021";

    public static readonly string BankKey = FieldOpsApiFactory.TestBankKey;

    private const string Snapshot =
        """{"billTo":{"name":"Carla Customer","email":"carla@example.com","phone":"5551234567","addressLines":["9 Bill Rd"]},"serviceAddress":"1 Seed St, Austin","completionNote":"All done"}""";

    /// <summary>Seeds a job and a sent (or any status) invoice with an active public link, numbered from the organization counters.</summary>
    public static async Task<OnlineInvoice> InvoiceAsync(
        this CompanySettingsDatabaseFixture db, RequestWorld world, Guid user, OnlineSpec? spec = null)
    {
        spec ??= new OnlineSpec();
        var branch = world.BranchA;
        var job = await db.SeedJobAsync(world, user, new JobSpec { Minimal = !spec.FullJob || spec.OpenJob, Technician = spec.Technician, Status = spec.OpenJob ? "in_progress" : "completed" });
        var id = Guid.NewGuid();
        var number = await db.NextInvoiceNumberAsync(world.Org);
        var today = DateOnly.ParseExact(BillingSeed.Today(Timezone), "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var sentAt = spec.Status is "draft" ? (DateTimeOffset?)null : DateTimeOffset.UtcNow.AddDays(-10);

        // status is a test-controlled constant, never user input.
        await db.ExecuteAsync(
            $"""
            INSERT INTO invoices (id, organization_id, branch_id, invoice_number, work_order_id, customer_id, status, issue_date, due_date, currency,
                subtotal, tax_total, total, amount_paid, balance_due, recipient_email, sent_at, created_by_user_id, customer_snapshot)
            VALUES (@id, @org, @branch, @number, @order, @customer, CAST('{spec.Status}' AS invoice_status), @issue, @due, 'USD',
                @total, 0, @total, @paid, @total - @paid, @recipient, @sentAt, @user,
                CASE WHEN '{spec.Status}' IN ('draft', 'void') THEN NULL ELSE CAST(@snap AS jsonb) END);
            UPDATE organizations SET next_invoice_number = @number + 1 WHERE id = @org;
            """,
            ("id", id),
            ("org", world.Org),
            ("branch", branch),
            ("number", number),
            ("order", job.Order),
            ("customer", world.Customer),
            ("issue", today.AddDays(-10)),
            ("due", spec.DueDays is { } due ? today.AddDays(due) : null),
            ("total", spec.Total),
            ("paid", spec.Paid),
            ("recipient", spec.Recipient),
            ("sentAt", sentAt),
            ("user", user),
            ("snap", Snapshot));

        var token = InvoiceApi.NewToken();
        await db.InsertTokenAsync(world.Org, id, user, token);

        return new OnlineInvoice(id, number, job.Order, job.Number, world.Customer, branch, token, job);
    }

    /// <summary>Stores bank details the way the API does: the account number only as AES-GCM ciphertext of the test key.</summary>
    public static async Task SeedBankAsync(this CompanySettingsDatabaseFixture db, Guid org, string email = "billing@acme.test")
    {
        var encryptor = new AesGcmBankDetailsEncryptor(Options.Create(new BankDetailsSettings { EncryptionKey = BankKey }));

        await db.ExecuteAsync(
            """
            UPDATE organizations SET bank_name = 'First Bank', bank_account_number_ciphertext = @c, bank_account_last4 = @l,
                bank_routing_number = @r, bank_details_updated_at = now(), email = @e
            WHERE id = @o
            """,
            ("c", encryptor.Encrypt(BankAccount)),
            ("l", BankAccount[^4..]),
            ("r", Routing),
            ("e", email),
            ("o", org));
    }

    public static async Task<(Guid Attempt, string Intent)> PendingAttemptAsync(
        this CompanySettingsDatabaseFixture db, Guid org, OnlineInvoice invoice, decimal? amount = null, bool expired = false, bool withIntent = true)
    {
        var id = Guid.NewGuid();
        var intent = withIntent ? FakePaymentGateway.IntentId(id) : null;

        await db.ExecuteAsync(
            """
            INSERT INTO invoice_payment_attempts (id, organization_id, invoice_id, method, status, amount, currency, idempotency_key, provider_payment_intent_id, expires_at)
            VALUES (@id, @o, @i, CAST('card_online' AS payment_method), 'pending', COALESCE(@a, (SELECT balance_due FROM invoices WHERE id = @i)), 'USD', gen_random_uuid(), @intent,
                CASE WHEN @expired THEN now() - interval '1 minute' ELSE now() + interval '30 minutes' END)
            """,
            ("id", id),
            ("o", org),
            ("i", invoice.Id),
            ("a", amount),
            ("intent", intent),
            ("expired", expired));

        return (id, intent ?? string.Empty);
    }
}

/// <summary>Public requests, webhook signing and event payloads of the customer payment tests.</summary>
internal static class OnlineApi
{
    public static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, JsonObject body, string? ip = null) =>
        await PostRawAsync(client, path, body.ToJsonString(), ip);

    public static async Task<HttpResponseMessage> PostRawAsync(HttpClient client, string path, string json, string? ip = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/public/invoice-links/{path}")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add(TestClientIpStartupFilter.HeaderName, ip ?? SessionApi.NewClientIp());

        return await client.SendAsync(request);
    }

    public static JsonObject KeyBody(string? token, Guid? key = null) =>
        new() { ["token"] = token, ["idempotencyKey"] = (key ?? Guid.NewGuid()).ToString() };

    public static Task<HttpResponseMessage> CardIntentAsync(HttpClient client, string? token, Guid? key = null, string? ip = null) =>
        PostAsync(client, "payments/card-intent", KeyBody(token, key), ip);

    public static Task<HttpResponseMessage> StatusAsync(HttpClient client, string? token, Guid attempt, string? ip = null) =>
        PostAsync(client, "payments/status", new JsonObject { ["token"] = token, ["attemptId"] = attempt.ToString() }, ip);

    public static Task<HttpResponseMessage> ViewAsync(HttpClient client, string? token) =>
        PostAsync(client, "view", new JsonObject { ["token"] = token });

    /// <summary>Starts a card attempt through the API and returns its id and provider intent id.</summary>
    public static async Task<(Guid Attempt, string Intent)> StartCardAsync(HttpClient client, OnlineInvoice invoice)
    {
        var created = await BillingSeed.ReadAsync(await CardIntentAsync(client, invoice.Token), HttpStatusCode.Created);
        var attempt = created["attemptId"]!.GetValue<Guid>();

        return (attempt, FakePaymentGateway.IntentId(attempt));
    }

    public static string Sign(string payload, string secret = FakePaymentGateway.WebhookSecret, DateTimeOffset? at = null)
    {
        var timestamp = (at ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{payload}"));

        return $"t={timestamp},v1={Convert.ToHexStringLower(mac)}";
    }

    public static async Task<HttpResponseMessage> WebhookAsync(HttpClient client, string payload, string? signature)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/stripe")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };

        if (signature is not null)
        {
            request.Headers.Add("Stripe-Signature", signature);
        }

        return await client.SendAsync(request);
    }

    public static Task<HttpResponseMessage> DeliverAsync(HttpClient client, string payload) => WebhookAsync(client, payload, Sign(payload));

    private static string Event(string id, string type, string payloadObject) =>
        $$$"""{"id":"{{{id}}}","object":"event","api_version":"2020-08-27","created":1700000000,"livemode":false,"type":"{{{type}}}","data":{"object":{{{payloadObject}}}}}""";

    private static string Intent(string intent, long minor, string metadata, string error = "") =>
        $$$"""{"id":"{{{intent}}}","object":"payment_intent","amount":{{{minor}}},"currency":"usd","status":"succeeded","metadata":{{{metadata}}}{{{error}}}}""";

    public static long Minor(decimal amount) => decimal.ToInt64(amount * 100m);

    public static string Metadata(Guid attempt, Guid org, Guid invoice) =>
        $$$"""{"attemptId":"{{{attempt}}}","organizationId":"{{{org}}}","invoiceId":"{{{invoice}}}"}""";

    public static string Succeeded(string eventId, string intent, decimal amount, string metadata = "{}") =>
        Event(eventId, "payment_intent.succeeded", Intent(intent, Minor(amount), metadata));

    public static string Failed(string eventId, string intent, decimal amount, string declineCode = "insufficient_funds") =>
        Event(
            eventId,
            "payment_intent.payment_failed",
            Intent(intent, Minor(amount), "{}", $$$""","last_payment_error":{"type":"card_error","code":"card_declined","decline_code":"{{{declineCode}}}"}"""));

    public static string Canceled(string eventId, string intent, decimal amount) =>
        Event(eventId, "payment_intent.canceled", Intent(intent, Minor(amount), "{}"));

    public static string Refunded(string eventId, string intent, decimal amount, decimal cumulative) =>
        Event(
            eventId,
            "charge.refunded",
            $$$"""{"id":"ch_1","object":"charge","amount":{{{Minor(amount)}}},"amount_refunded":{{{Minor(cumulative)}}},"currency":"usd","payment_intent":"{{{intent}}}","metadata":{}}""");

    public static string Unhandled(string eventId) => Event(eventId, "customer.created", """{"id":"cus_1","object":"customer"}""");

    /// <summary>Everything the payment flow writes for an invoice, to prove that a rejected or replayed request changed nothing.</summary>
    public static Task<string> FlowStateAsync(this CompanySettingsDatabaseFixture db, Guid org, Guid invoice) =>
        db.ScalarAsync<string>(
            """
            SELECT (SELECT COUNT(*) FROM invoice_payment_attempts WHERE invoice_id = @i)
                || '|' || COALESCE((SELECT string_agg(status::text || ':' || COALESCE(failure_category, '-') || ':' || refunded_amount, ',' ORDER BY id) FROM invoice_payment_attempts WHERE invoice_id = @i), '-')
                || '|' || (SELECT COUNT(*) FROM payments WHERE organization_id = @o)
                || '|' || (SELECT next_payment_number FROM organizations WHERE id = @o)
                || '|' || (SELECT COUNT(*) FROM payment_webhook_events WHERE organization_id = @o)
                || '|' || (SELECT COUNT(*) FROM invoice_reviews WHERE invoice_id = @i)
                || '|' || (SELECT COUNT(*) FROM audit_logs WHERE organization_id = @o)
                || '|' || (SELECT status::text || ':' || amount_paid || ':' || balance_due || ':' || updated_at::text FROM invoices WHERE id = @i)
            """,
            ("o", org),
            ("i", invoice));

    public static Task<string> InvoiceMoneyAsync(this CompanySettingsDatabaseFixture db, Guid invoice) =>
        db.ScalarAsync<string>("SELECT status::text || '|' || amount_paid || '|' || balance_due FROM invoices WHERE id = @i", ("i", invoice));

    public static Task<string> AttemptAsync(this CompanySettingsDatabaseFixture db, Guid attempt) =>
        db.ScalarAsync<string>(
            "SELECT status::text || '|' || COALESCE(failure_category, '-') || '|' || refunded_amount || '|' || (payment_id IS NOT NULL) FROM invoice_payment_attempts WHERE id = @a",
            ("a", attempt));

    public static Task<string> EventOutcomeAsync(this CompanySettingsDatabaseFixture db, string eventId) =>
        db.ScalarAsync<string>("SELECT outcome FROM payment_webhook_events WHERE provider_event_id = @e", ("e", eventId));

    public static Task<long> EventCountAsync(this CompanySettingsDatabaseFixture db, string eventId) =>
        db.CountAsync("payment_webhook_events", "provider_event_id = @e", ("e", eventId));

    public static Task<long> AuditCountAsync(this CompanySettingsDatabaseFixture db, Guid org, string action) =>
        db.CountAsync("audit_logs", "organization_id = @o AND action = @a", ("o", org), ("a", action));

    public static async Task<string> OrgAuditTextAsync(this CompanySettingsDatabaseFixture db, Guid org) =>
        string.Join(
            '\n',
            await db.TextsAsync(
                "SELECT action || COALESCE(before_data::text, '') || COALESCE(after_data::text, '') || metadata::text || COALESCE(ip_address::text, '') FROM audit_logs WHERE organization_id = @o ORDER BY id",
                ("o", org)));
}
