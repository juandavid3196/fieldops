using System.Net;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.BillingReview;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.InvoiceDelivery;
using FieldOps.IntegrationTests.InvoicePayments;
using FieldOps.IntegrationTests.ServiceRequests;

namespace FieldOps.IntegrationTests.OnlinePayments;

/// <summary>Card intent lock and idempotency, provider failure and lazy expiry (customer-invoice-payments AC-03, AC-08, BR-31 f).</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class CardIntentTests(CompanySettingsDatabaseFixture database)
{
    [Fact]
    public async Task CardIntent_CreatesOneFullBalanceAttemptAndGuardsReplayConflictAndProviderFailure()
    {
        var world = await database.SeedWorldAsync();
        await using var host = OnlineHost.Create(database);
        var (_, member) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var invoice = await database.InvoiceAsync(world, member.UserId);
        var key = Guid.NewGuid();

        // AC-03: one pending attempt for the full balance with a 30-minute expiry; the intent is created for exactly that attempt.
        var created = await BillingSeed.ReadAsync(await OnlineApi.CardIntentAsync(host.Client, invoice.Token, key), HttpStatusCode.Created);
        var attempt = created["attemptId"]!.GetValue<Guid>();
        var secret = FakePaymentGateway.ClientSecret(FakePaymentGateway.IntentId(attempt));

        Assert.Equal((357.28m, "USD", "pending", secret), (created["amount"]!.GetValue<decimal>(), created["currency"]!.GetValue<string>(), created["status"]!.GetValue<string>(), created["clientSecret"]!.GetValue<string>()));
        var request = Assert.Single(host.Gateway.Requests);
        Assert.Equal((attempt, world.Org, invoice.Id, 357.28m, "USD"), (request.AttemptId, request.OrganizationId, request.InvoiceId, request.Amount, request.Currency));
        Assert.Equal(
            $"pending|card_online|357.28|true|true|{key}",
            await database.ScalarAsync<string>(
                """
                SELECT status::text || '|' || method::text || '|' || amount || '|' || (provider_payment_intent_id = @p)
                    || '|' || (expires_at - created_at = interval '30 minutes') || '|' || idempotency_key
                FROM invoice_payment_attempts WHERE id = @a
                """,
                ("p", FakePaymentGateway.IntentId(attempt)),
                ("a", attempt)));
        Assert.Equal(
            [$"{attempt}|card_online|357.28|{world.BranchA}"],
            await database.TextsAsync(
                "SELECT (metadata ->> 'attemptId') || '|' || (metadata ->> 'method') || '|' || (metadata ->> 'amount') || '|' || branch_id FROM audit_logs WHERE organization_id = @o AND action = 'invoice_payment_attempt.created'",
                ("o", world.Org)));
        Assert.Equal("sent|0.00|357.28", await database.InvoiceMoneyAsync(invoice.Id));

        // Same key: 200 with the same attempt and the secret read again from the provider; no second attempt or intent.
        var replay = await BillingSeed.ReadAsync(await OnlineApi.CardIntentAsync(host.Client, invoice.Token, key));
        Assert.Equal((attempt, secret, "pending"), (replay["attemptId"]!.GetValue<Guid>(), replay["clientSecret"]!.GetValue<string>(), replay["status"]!.GetValue<string>()));
        Assert.Single(host.Gateway.Requests);

        // A new key while one is pending: 409 payment_in_progress and nothing changes.
        var before = await database.FlowStateAsync(world.Org, invoice.Id);
        var busy = await BillingSeed.ProblemAsync(await OnlineApi.CardIntentAsync(host.Client, invoice.Token), HttpStatusCode.Conflict, "payment_in_progress");
        Assert.Equal("A payment is already in progress for this invoice.", busy["title"]!.GetValue<string>());
        Assert.Equal(before, await database.FlowStateAsync(world.Org, invoice.Id));

        // The key of another invoice of the organization is a conflict, not a replay.
        var other = await database.InvoiceAsync(world, member.UserId);
        await BillingSeed.ProblemAsync(await OnlineApi.CardIntentAsync(host.Client, other.Token, key), HttpStatusCode.Conflict, "idempotency_conflict");
        Assert.Equal(0, await database.CountAsync("invoice_payment_attempts", "invoice_id = @i", ("i", other.Id)));

        // Paid, zero balance, no card keys: nothing is written.
        var paid = await database.InvoiceAsync(world, member.UserId, new OnlineSpec { Status = "paid", Paid = 357.28m });
        var settled = await database.InvoiceAsync(world, member.UserId, new OnlineSpec { Paid = 357.28m });
        await BillingSeed.ProblemAsync(await OnlineApi.CardIntentAsync(host.Client, paid.Token), HttpStatusCode.Conflict, "invoice_not_payable");
        await BillingSeed.ProblemAsync(await OnlineApi.CardIntentAsync(host.Client, settled.Token), HttpStatusCode.Conflict, "invoice_not_payable");
        host.Gateway.CardAvailable = false;
        await BillingSeed.ProblemAsync(await OnlineApi.CardIntentAsync(host.Client, other.Token), HttpStatusCode.Conflict, "card_unavailable");
        host.Gateway.CardAvailable = true;
        Assert.Equal(0, await database.CountAsync("invoice_payment_attempts", "invoice_id IN (@a, @b, @c)", ("a", paid.Id), ("b", settled.Id), ("c", other.Id)));

        // The provider failing: 502, the attempt is failed (provider_unavailable) and the lock is released for a retry.
        host.Gateway.FailCreate = true;
        await BillingSeed.ProblemAsync(await OnlineApi.CardIntentAsync(host.Client, other.Token), HttpStatusCode.BadGateway, "payment_provider_unavailable");
        Assert.Equal(
            ["failed|provider_unavailable"],
            await database.TextsAsync("SELECT status::text || '|' || failure_category FROM invoice_payment_attempts WHERE invoice_id = @i", ("i", other.Id)));
        Assert.Equal(1, await database.AuditCountAsync(world.Org, "invoice_payment_attempt.failed"));
        host.Gateway.FailCreate = false;
        await BillingSeed.ReadAsync(await OnlineApi.CardIntentAsync(host.Client, other.Token), HttpStatusCode.Created);

        // Token and ownership (404) come before the fields (400); a card field or an amount is an unknown key and rejected.
        await BillingSeed.ProblemAsync(
            await OnlineApi.PostAsync(host.Client, "payments/card-intent", new JsonObject { ["token"] = InvoiceApi.NewToken(), ["idempotencyKey"] = "nope" }),
            HttpStatusCode.NotFound,
            "invoice_link_unavailable");
        await BillingSeed.ProblemAsync(
            await OnlineApi.PostAsync(host.Client, "payments/card-intent", new JsonObject { ["token"] = settled.Token, ["idempotencyKey"] = "nope" }),
            HttpStatusCode.BadRequest,
            errorKey: "idempotencyKey");

        foreach (var field in new[] { "amount", "cardNumber", "organizationId", "status" })
        {
            var body = OnlineApi.KeyBody(settled.Token);
            body[field] = "4242424242424242";
            Assert.Equal(HttpStatusCode.BadRequest, (await OnlineApi.PostAsync(host.Client, "payments/card-intent", body)).StatusCode);
        }

        // The client secret is returned only to the token holder: never stored, audited or logged.
        Assert.False(await database.ScalarAsync<bool>("SELECT EXISTS (SELECT 1 FROM invoice_payment_attempts WHERE to_jsonb(invoice_payment_attempts)::text LIKE '%secret%')"));
        Assert.DoesNotContain("_secret_", await database.OrgAuditTextAsync(world.Org), StringComparison.Ordinal);
        Assert.DoesNotContain("_secret_", string.Join('\n', host.Logs.Entries.Select(entry => entry.Message + entry.Exception)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task LazyExpiry_ReleasesTheInvoiceOnlyAfterTheProviderConfirmsTheCancellation()
    {
        var world = await database.SeedWorldAsync();
        await using var host = OnlineHost.Create(database);
        var (owner, member) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var receiver = await database.SeedReceiverAsync(world.Org);

        async Task<HttpResponseMessage> TouchAsync(string kind, OnlineInvoice invoice, Guid attempt) => kind switch
        {
            "card-intent" => await OnlineApi.CardIntentAsync(host.Client, invoice.Token),
            "external" => await host.SendAsync(
                HttpMethod.Post,
                PaymentApi.Path(invoice.Id),
                owner,
                PaymentApi.Body(amount: 10m, paidDate: BillingSeed.Today(OnlineSeed.Timezone), receivedBy: receiver.UserId, updatedAt: await database.StampAsync(invoice.Id))),
            "view" => await OnlineApi.ViewAsync(host.Client, invoice.Token),
            _ => await OnlineApi.StatusAsync(host.Client, invoice.Token, attempt),
        };

        // AC-08: a confirmed cancellation fails the attempt (expired) and the touching request proceeds.
        foreach (var kind in new[] { "card-intent", "external", "view", "status" })
        {
            var invoice = await database.InvoiceAsync(world, member.UserId);
            var (attempt, intent) = await database.PendingAttemptAsync(world.Org, invoice, expired: true);
            host.Gateway.CancelResult = FieldOps.Application.Features.OnlinePayments.GatewayCancelResult.Canceled;

            var response = await TouchAsync(kind, invoice, attempt);

            Assert.Equal(kind is "card-intent" or "external" ? HttpStatusCode.Created : HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("failed|expired|0.00|false", await database.AttemptAsync(attempt));
            Assert.Contains(intent, host.Gateway.Canceled);

            if (kind == "view")
            {
                Assert.Null((await BillingSeed.ReadAsync(await OnlineApi.ViewAsync(host.Client, invoice.Token)))["activeAttempt"]);
            }

            if (kind == "status")
            {
                var status = await BillingSeed.ReadAsync(response);
                Assert.Equal(("failed", "expired"), (status["status"]!.GetValue<string>(), status["failureCategory"]!.GetValue<string>()));
                await BillingSeed.ReadAsync(await OnlineApi.CardIntentAsync(host.Client, invoice.Token), HttpStatusCode.Created);
            }
        }

        // A refused or unavailable cancellation keeps the attempt pending and the invoice locked.
        foreach (var unavailable in new[] { false, true })
        {
            host.Gateway.CancelResult = FieldOps.Application.Features.OnlinePayments.GatewayCancelResult.NotCancelable;
            host.Gateway.ThrowOnCancel = unavailable;
            var invoice = await database.InvoiceAsync(world, member.UserId);
            var (attempt, _) = await database.PendingAttemptAsync(world.Org, invoice, expired: true);
            var before = await database.FlowStateAsync(world.Org, invoice.Id);

            await BillingSeed.ProblemAsync(await OnlineApi.CardIntentAsync(host.Client, invoice.Token), HttpStatusCode.Conflict, "payment_in_progress");
            var external = await BillingSeed.ProblemAsync(
                await host.SendAsync(
                    HttpMethod.Post,
                    PaymentApi.Path(invoice.Id),
                    owner,
                    PaymentApi.Body(amount: 10m, paidDate: BillingSeed.Today(OnlineSeed.Timezone), receivedBy: receiver.UserId, updatedAt: await database.StampAsync(invoice.Id))),
                HttpStatusCode.Conflict,
                "payment_in_progress");
            Assert.Equal("An online card payment is in progress for this invoice. Try again in a few minutes.", external["title"]!.GetValue<string>());

            var status = await BillingSeed.ReadAsync(await OnlineApi.StatusAsync(host.Client, invoice.Token, attempt));
            Assert.Equal("pending", status["status"]!.GetValue<string>());
            var view = await BillingSeed.ReadAsync(await OnlineApi.ViewAsync(host.Client, invoice.Token));
            Assert.Equal((attempt, "pending"), (view["activeAttempt"]!["attemptId"]!.GetValue<Guid>(), view["activeAttempt"]!["status"]!.GetValue<string>()));
            Assert.Equal("pending|-|0.00|false", await database.AttemptAsync(attempt));
            Assert.Equal(before, await database.FlowStateAsync(world.Org, invoice.Id));
        }

        host.Gateway.ThrowOnCancel = false;

        // An attempt that never got an intent id fails with provider_unavailable without asking the provider.
        var orphan = await database.InvoiceAsync(world, member.UserId);
        var (orphanAttempt, _) = await database.PendingAttemptAsync(world.Org, orphan, expired: true, withIntent: false);
        var canceledBefore = host.Gateway.Canceled.Count;
        var orphanStatus = await BillingSeed.ReadAsync(await OnlineApi.StatusAsync(host.Client, orphan.Token, orphanAttempt));

        Assert.Equal(("failed", "provider_unavailable"), (orphanStatus["status"]!.GetValue<string>(), orphanStatus["failureCategory"]!.GetValue<string>()));
        Assert.Equal(canceledBefore, host.Gateway.Canceled.Count);
        Assert.True(await database.AuditCountAsync(world.Org, "invoice_payment_attempt.failed") >= 5);
    }
}
