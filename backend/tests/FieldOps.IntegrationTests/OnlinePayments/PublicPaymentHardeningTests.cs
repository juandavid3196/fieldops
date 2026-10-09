using System.Net;
using System.Text.Json.Nodes;
using FieldOps.Api.Controllers;
using FieldOps.IntegrationTests.Api;
using FieldOps.IntegrationTests.BillingReview;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.InvoiceDelivery;
using FieldOps.IntegrationTests.InvoicePayments;
using FieldOps.IntegrationTests.ServiceRequests;
using FieldOps.IntegrationTests.Users;

namespace FieldOps.IntegrationTests.OnlinePayments;

/// <summary>Token states and foreign ids, rate limits and size caps, and what audit rows and logs may contain (customer-invoice-payments AC-01, AC-15, AC-16).</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class PublicPaymentHardeningTests(CompanySettingsDatabaseFixture database)
{
    private static string NewEvent() => $"evt_{Guid.NewGuid():N}";

    private static readonly string[] Endpoints =
    [
        "view", "payments/card-intent", "payments/status", "payments/bank-transfer-notice", "receipt", "completion-report", "photos", "photos/content", "review",
    ];

    private static JsonObject BodyFor(string endpoint, string? token, Guid? id = null) => endpoint switch
    {
        "payments/card-intent" or "payments/bank-transfer-notice" => OnlineApi.KeyBody(token),
        "payments/status" => new JsonObject { ["token"] = token, ["attemptId"] = (id ?? Guid.NewGuid()).ToString() },
        "receipt" => new JsonObject { ["token"] = token, ["paymentId"] = (id ?? Guid.NewGuid()).ToString() },
        "photos/content" => new JsonObject { ["token"] = token, ["photoId"] = (id ?? Guid.NewGuid()).ToString() },
        "review" => new JsonObject { ["token"] = token, ["rating"] = 5, ["comment"] = null },
        _ => new JsonObject { ["token"] = token },
    };

    [Fact]
    public async Task PublicEndpoints_ReturnTheIdenticalNotFoundForEveryUnusableTokenAndForeignIdWithoutWriting()
    {
        var world = await database.SeedWorldAsync();
        var other = await database.SeedWorldAsync();
        await database.SeedBankAsync(world.Org);
        await using var host = OnlineHost.Create(database);
        var (_, member) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var otherMember = await database.SeedMemberAsync(other.Org, CompanySettingsDatabaseFixture.OwnerRoleId, "Oscar", "Other");

        var invoice = await database.InvoiceAsync(world, member.UserId);
        var sibling = await database.InvoiceAsync(world, member.UserId);
        var foreign = await database.InvoiceAsync(other, otherMember.UserId);
        var draft = await database.InvoiceAsync(world, member.UserId, new OnlineSpec { Status = "draft" });
        var voided = await database.InvoiceAsync(world, member.UserId, new OnlineSpec { Status = "void" });

        var revoked = InvoiceApi.NewToken();
        var expired = InvoiceApi.NewToken();
        await database.InsertTokenAsync(world.Org, invoice.Id, member.UserId, revoked);
        await database.ExecuteAsync("UPDATE invoice_access_tokens SET revoked_at = now() WHERE token_hash = @h", ("h", InvoiceApi.Hash(revoked)));
        await database.InsertTokenAsync(world.Org, invoice.Id, member.UserId, expired, expired: true);

        var (foreignAttempt, _) = await OnlineApi.StartCardAsync(host.Client, foreign);
        var (siblingAttempt, siblingIntent) = await OnlineApi.StartCardAsync(host.Client, sibling);
        Assert.Equal(HttpStatusCode.OK, (await OnlineApi.DeliverAsync(host.Client, OnlineApi.Succeeded(NewEvent(), siblingIntent, 357.28m))).StatusCode);
        var siblingPayment = await database.ScalarAsync<Guid>("SELECT payment_id FROM invoice_payment_attempts WHERE id = @a", ("a", siblingAttempt));
        var foreignPhoto = foreign.Job.BeforePhoto;

        var states = new[] { world.Org, other.Org }.Select(org => database.FlowStateAsync(org, invoice.Id)).ToArray();
        var before = (await Task.WhenAll(states)).Concat([await database.FlowStateAsync(world.Org, sibling.Id), await database.FlowStateAsync(other.Org, foreign.Id)]).ToArray();

        // AC-01: malformed, unknown, revoked, expired, draft and void tokens are one 404 on every endpoint, whatever the other fields hold.
        var bodies = new List<string>();
        var unusable = new string?[] { null, string.Empty, "short", new string('!', 43), InvoiceApi.NewToken(), revoked, expired, draft.Token, voided.Token };

        foreach (var token in unusable)
        {
            foreach (var endpoint in Endpoints)
            {
                var response = await OnlineApi.PostAsync(host.Client, endpoint, BodyFor(endpoint, token));
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
                Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
                bodies.Add(await InvoiceApi.WithoutTraceAsync(response));
            }
        }

        Assert.Single(bodies.Distinct());
        Assert.Equal("invoice_link_unavailable", JsonNode.Parse(bodies[0])!["code"]!.GetValue<string>());

        // A valid token with the id of another invoice (same organization), of another organization or of nothing: the same 404.
        var foreignIds = new (string Endpoint, Guid Id)[]
        {
            ("payments/status", siblingAttempt),
            ("payments/status", foreignAttempt),
            ("receipt", siblingPayment),
            ("photos/content", foreignPhoto),
            ("photos/content", sibling.Job.BeforePhoto),
        };

        foreach (var (endpoint, id) in foreignIds)
        {
            var response = await OnlineApi.PostAsync(host.Client, endpoint, BodyFor(endpoint, invoice.Token, id));
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal(bodies[0], await InvoiceApi.WithoutTraceAsync(response));
        }

        Assert.Equal(before[0], await database.FlowStateAsync(world.Org, invoice.Id));
        Assert.Equal(before[1], await database.FlowStateAsync(other.Org, invoice.Id));
        Assert.Equal(before[2], await database.FlowStateAsync(world.Org, sibling.Id));
        Assert.Equal(before[3], await database.FlowStateAsync(other.Org, foreign.Id));

        // A paid invoice stays reachable with its token, and its own ids work.
        Assert.Equal(HttpStatusCode.OK, (await OnlineApi.ViewAsync(host.Client, sibling.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await OnlineApi.StatusAsync(host.Client, sibling.Token, siblingAttempt)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await OnlineApi.PostAsync(host.Client, "receipt", BodyFor("receipt", sibling.Token, siblingPayment))).StatusCode);

        // Unknown keys (a card number, an amount, an organization id) are rejected before anything runs on every payment endpoint.
        foreach (var endpoint in Endpoints.Where(endpoint => endpoint != "view"))
        {
            foreach (var field in new[] { "cardNumber", "amount", "organizationId" })
            {
                var body = BodyFor(endpoint, invoice.Token);
                body[field] = "4242424242424242";
                var rejected = await OnlineApi.PostAsync(host.Client, endpoint, body);
                Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
                Assert.DoesNotContain("4242424242424242", await rejected.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            }
        }

        Assert.Equal(before[0], await database.FlowStateAsync(world.Org, invoice.Id));
    }

    [Fact]
    public async Task PublicEndpoints_AreRateLimitedPerClientGroupAndInvoiceAndCapBodiesAt16Kb()
    {
        var world = await database.SeedWorldAsync();
        await database.SeedBankAsync(world.Org);
        await using var host = OnlineHost.Create(database);
        var (_, member) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var invoice = await database.InvoiceAsync(world, member.UserId);
        var nothing = InvoiceApi.NewToken();

        async Task<HttpStatusCode> CallAsync(string endpoint, string ip, string? token = null) =>
            (await OnlineApi.PostAsync(host.Client, endpoint, BodyFor(endpoint, token ?? nothing), ip)).StatusCode;

        // Per client and 5 minutes: card intent 5, status 60, the read group 60 (view, photos, photo content) and the PDFs 10.
        var ip = "10.250.0.1";
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.NotFound, await CallAsync("payments/card-intent", ip));
        }

        var limited = await OnlineApi.PostAsync(host.Client, "payments/card-intent", BodyFor("payments/card-intent", nothing), ip);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(int.Parse(limited.Headers.GetValues("Retry-After").Single(), System.Globalization.CultureInfo.InvariantCulture) >= 1);
        Assert.Equal("no-store", limited.Headers.CacheControl?.ToString());
        Assert.Equal("no-referrer", limited.Headers.GetValues("Referrer-Policy").Single());
        Assert.Equal("nosniff", limited.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal(HttpStatusCode.NotFound, await CallAsync("payments/card-intent", "10.250.0.2"));

        ip = "10.250.0.3";
        for (var i = 0; i < 60; i++)
        {
            Assert.Equal(HttpStatusCode.NotFound, await CallAsync("payments/status", ip));
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, await CallAsync("payments/status", ip));

        ip = "10.250.0.4";
        for (var i = 0; i < 20; i++)
        {
            Assert.Equal(HttpStatusCode.NotFound, await CallAsync("view", ip));
            Assert.Equal(HttpStatusCode.NotFound, await CallAsync("photos", ip));
            Assert.Equal(HttpStatusCode.NotFound, await CallAsync("photos/content", ip));
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, await CallAsync("photos/content", ip));

        ip = "10.250.0.5";
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.NotFound, await CallAsync("receipt", ip));
            Assert.Equal(HttpStatusCode.NotFound, await CallAsync("completion-report", ip));
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, await CallAsync("receipt", ip));

        // Per invoice and hour: bank notice 3, review 3 and card intent 10, whatever the client (each call comes from a new address).
        for (var i = 0; i < 3; i++)
        {
            Assert.Contains(await CallAsync("payments/bank-transfer-notice", SessionNewIp(), invoice.Token), new[] { HttpStatusCode.Created, HttpStatusCode.OK });
        }

        var noticeLimited = await OnlineApi.PostAsync(host.Client, "payments/bank-transfer-notice", BodyFor("payments/bank-transfer-notice", invoice.Token), SessionNewIp());
        Assert.Equal(HttpStatusCode.TooManyRequests, noticeLimited.StatusCode);
        Assert.NotEmpty(noticeLimited.Headers.GetValues("Retry-After"));

        var paid = await database.InvoiceAsync(world, member.UserId, new OnlineSpec { Status = "paid", Paid = 357.28m });
        var reviews = new List<HttpStatusCode>();

        for (var i = 0; i < 4; i++)
        {
            reviews.Add(await CallAsync("review", SessionNewIp(), paid.Token));
        }

        Assert.Equal([HttpStatusCode.Created, HttpStatusCode.Conflict, HttpStatusCode.Conflict, HttpStatusCode.TooManyRequests], reviews);

        var card = new List<HttpStatusCode>();

        for (var i = 0; i < 11; i++)
        {
            card.Add(await CallAsync("payments/card-intent", SessionNewIp(), invoice.Token));
        }

        Assert.Equal(HttpStatusCode.Created, card[0]);
        Assert.All(card.Skip(1).Take(9), status => Assert.Equal(HttpStatusCode.Conflict, status));
        Assert.Equal(HttpStatusCode.TooManyRequests, card[10]);

        var neighbour = await database.InvoiceAsync(world, member.UserId);
        Assert.Equal(HttpStatusCode.Created, await CallAsync("payments/card-intent", SessionNewIp(), neighbour.Token));

        // Bodies over 16 KB are a 413 on the new public endpoints (enforced by a real server, like the invoice link precedent).
        await using var factory = FieldOpsApiFactory.Create(connectionString: database.ConnectionString);
        factory.UseKestrel(0);
        factory.StartServer();
        using var client = factory.CreateClient();
        var padding = new string('s', PublicInvoiceLinksController.MaxRequestBodyBytes + 1);

        foreach (var endpoint in new[] { "payments/card-intent", "payments/status", "payments/bank-transfer-notice", "review", "photos", "receipt", "completion-report" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/public/invoice-links/{endpoint}")
            {
                Content = new StringContent($$"""{"token":"{{new string('A', 43)}}","padding":"{{padding}}"}""", System.Text.Encoding.UTF8, "application/json"),
            };

            var tooLarge = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, tooLarge.StatusCode);
            Assert.True(tooLarge.Headers.CacheControl is { NoStore: true });
            Assert.Equal("no-referrer", tooLarge.Headers.GetValues("Referrer-Policy").Single());
            Assert.Equal("nosniff", tooLarge.Headers.GetValues("X-Content-Type-Options").Single());
        }

        static string SessionNewIp() => FieldOps.IntegrationTests.Sessions.SessionApi.NewClientIp();
    }

    [Fact]
    public async Task AuditRowsAndLogs_NeverContainTokensSecretsProviderIdsCardDataBankDataCommentsEmailsOrNames()
    {
        var world = await database.SeedWorldAsync();
        await database.SeedBankAsync(world.Org);
        await using var host = OnlineHost.Create(database);
        var (owner, member) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId, "Olga", "Ledger");
        var invoice = await database.InvoiceAsync(world, member.UserId);
        var failed = await database.InvoiceAsync(world, member.UserId);

        // Every operation of the feature: intent, failure, success with receipt email, refund, notice, review, bank details.
        var (failedAttempt, failedIntent) = await OnlineApi.StartCardAsync(host.Client, failed);
        Assert.Equal(HttpStatusCode.OK, (await OnlineApi.DeliverAsync(host.Client, OnlineApi.Failed(NewEvent(), failedIntent, 357.28m))).StatusCode);
        var (attempt, intent) = await OnlineApi.StartCardAsync(host.Client, invoice);
        Assert.Equal(HttpStatusCode.OK, (await OnlineApi.DeliverAsync(host.Client, OnlineApi.Succeeded(NewEvent(), intent, 357.28m))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await OnlineApi.DeliverAsync(host.Client, OnlineApi.Refunded(NewEvent(), intent, 357.28m, 100m))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await OnlineApi.DeliverAsync(host.Client, OnlineApi.Succeeded(NewEvent(), "pi_nobody", 1m))).StatusCode);
        await BillingSeed.ReadAsync(await OnlineApi.PostAsync(host.Client, "payments/bank-transfer-notice", OnlineApi.KeyBody(failed.Token)), HttpStatusCode.Created);
        var paid = await database.InvoiceAsync(world, member.UserId, new OnlineSpec { Status = "paid", Paid = 357.28m });
        await BillingSeed.ReadAsync(
            await OnlineApi.PostAsync(host.Client, "review", new JsonObject { ["token"] = paid.Token, ["rating"] = 4, ["comment"] = "Marvelous work by the crew" }),
            HttpStatusCode.Created);
        Assert.Equal(
            HttpStatusCode.OK,
            (await host.SendAsync(HttpMethod.Put, "/organization-settings/bank-details", owner, new JsonObject
            {
                ["bankName"] = "Ledger Savings",
                ["accountNumber"] = "987654321098",
                ["routingNumber"] = "111000025",
                ["updatedAt"] = (await BillingSeed.ReadAsync(await host.SendAsync(HttpMethod.Get, "/organization-settings/bank-details", owner)))["updatedAt"]!.GetValue<string>(),
            })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await OnlineApi.PostAsync(host.Client, "payments/card-intent", new JsonObject { ["token"] = invoice.Token, ["cardNumber"] = "4242424242424242", ["cvv"] = "123" })).StatusCode);
        Assert.NotEmpty(host.Sender.Messages);

        var actions = await database.TextsAsync("SELECT DISTINCT action FROM audit_logs WHERE organization_id = @o AND (action LIKE 'invoice_payment_attempt.%' OR action LIKE 'payment.%' OR action LIKE 'invoice.%' OR action LIKE 'organization.%') ORDER BY action", ("o", world.Org));
        Assert.Contains("invoice_payment_attempt.created", actions);
        Assert.Contains("invoice_payment_attempt.failed", actions);
        Assert.Contains("payment.recorded", actions);
        Assert.Contains("payment.refunded", actions);
        Assert.Contains("invoice.refund_applied", actions);
        Assert.Contains("invoice.bank_transfer_reported", actions);
        Assert.Contains("invoice.review_submitted", actions);
        Assert.Contains("organization.bank_details_updated", actions);

        var audit = await database.OrgAuditTextAsync(world.Org);
        var logs = string.Join('\n', host.Logs.Entries.Select(entry => $"{entry.Message}\n{entry.Exception}\n{string.Join(' ', entry.Properties.Select(pair => $"{pair.Key}={pair.Value}"))}"));
        var secrets = new[]
        {
            invoice.Token, failed.Token, paid.Token, FakePaymentGateway.ClientSecret(intent), "_secret_", intent, failedIntent, "pi_", "evt_", "visa", "Visa", "4242", "4242424242424242",
            "First Bank", "Ledger Savings", OnlineSeed.BankAccount, "987654321098", OnlineSeed.Routing, "111000025", "Marvelous", "carla@example.com", "billing@acme.test",
            "Carla", "Olga", "Ledger",
        };

        foreach (var secret in secrets)
        {
            Assert.DoesNotContain(secret, audit, StringComparison.Ordinal);
            Assert.DoesNotContain(secret, logs, StringComparison.Ordinal);
        }

        Assert.Contains(attempt.ToString(), audit, StringComparison.Ordinal);
        Assert.Contains(failedAttempt.ToString(), audit, StringComparison.Ordinal);
    }
}
