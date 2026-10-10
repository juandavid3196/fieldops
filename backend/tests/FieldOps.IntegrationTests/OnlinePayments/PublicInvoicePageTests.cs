using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FieldOps.IntegrationTests.BillingReview;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.InvoiceDelivery;
using FieldOps.IntegrationTests.InvoicePayments;
using FieldOps.IntegrationTests.ServiceRequests;
using FieldOps.IntegrationTests.Team;
using FieldOps.IntegrationTests.Users;

namespace FieldOps.IntegrationTests.OnlinePayments;

/// <summary>The public invoice: view, bank transfer notice, documents and photos, and the review (customer-invoice-payments AC-02, AC-12 to AC-14).</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class PublicInvoicePageTests(CompanySettingsDatabaseFixture database)
{
    private static string NewEvent() => $"evt_{Guid.NewGuid():N}";

    private static string Local(DateTimeOffset instant) =>
        instant.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static readonly Regex Guids = new("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-", RegexOptions.CultureInvariant);

    [Fact]
    public async Task PublicView_ReturnsPaymentStateServiceOptionsAndPaymentsWithoutInternalData()
    {
        var world = await database.SeedWorldAsync();
        await database.SeedBankAsync(world.Org);
        await using var host = OnlineHost.Create(database);
        var (_, member) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var tech = await database.SeedTechAsync(world.Org, world.BranchA, "Tina", "Tech");
        var invoice = await database.InvoiceAsync(world, member.UserId, new OnlineSpec { Technician = tech });
        var completedOn = Local(await database.ScalarAsync<DateTimeOffset>("SELECT actual_completed_at FROM visits WHERE work_order_id = @w", ("w", invoice.Order)));

        // AC-02: a payable sent invoice with every option, the service, the technician and two before/after photos.
        var view = await BillingSeed.ReadAsync(await OnlineApi.ViewAsync(host.Client, invoice.Token));
        var text = view.ToJsonString();

        Assert.Equal(("sent", false, 0m, 357.28m, "Carla", "carla@example.com"), (view["status"]!.GetValue<string>(), view["overdue"]!.GetValue<bool>(), view["amountPaid"]!.GetValue<decimal>(), view["balanceDue"]!.GetValue<decimal>(), view["customerFirstName"]!.GetValue<string>(), view["receiptEmail"]!.GetValue<string>()));
        Assert.Null(view["daysOverdue"]);
        Assert.Equal(
            (completedOn, Local(DateTimeOffset.UtcNow.AddDays(-10)), (string?)null, (string?)null),
            (view["timeline"]!["serviceCompletedOn"]!.GetValue<string>(), view["timeline"]!["sentOn"]!.GetValue<string>(), view["timeline"]!["paidOn"]?.GetValue<string>(), view["timeline"]!["receiptOn"]?.GetValue<string>()));
        var service = view["service"]!;
        Assert.Equal(
            ("Fix drain", invoice.Job.Number is var number ? $"WO-{number}" : string.Empty, "All done", "Tina Tech", completedOn, true, 2),
            (service["title"]!.GetValue<string>(), service["workOrderNumber"]!.GetValue<string>(), service["completionNote"]!.GetValue<string>(), service["technicianName"]!.GetValue<string>(), service["serviceDate"]!.GetValue<string>(), service["hasCompletionReport"]!.GetValue<bool>(), service["photoCount"]!.GetValue<int>()));

        var options = view["paymentOptions"]!;
        Assert.Equal((true, FakePaymentGateway.Publishable), (options["card"]!["available"]!.GetValue<bool>(), options["card"]!["publishableKey"]!.GetValue<string>()));
        Assert.Equal(
            (true, "First Bank", OnlineSeed.BankAccount, OnlineSeed.Routing, invoice.Display, 357.28m),
            (options["bankTransfer"]!["available"]!.GetValue<bool>(), options["bankTransfer"]!["bankName"]!.GetValue<string>(), options["bankTransfer"]!["accountNumber"]!.GetValue<string>(), options["bankTransfer"]!["routingNumber"]!.GetValue<string>(), options["bankTransfer"]!["reference"]!.GetValue<string>(), options["bankTransfer"]!["amount"]!.GetValue<decimal>()));
        Assert.Equal(("+1 555 010 0100", "billing@acme.test"), (options["cash"]!["phone"]!.GetValue<string>(), options["cash"]!["email"]!.GetValue<string>()));
        Assert.Null(view["activeAttempt"]);
        Assert.Null(view["transferReportedOn"]);
        Assert.Empty(view["payments"]!.AsArray());
        Assert.Equal((false, false), (view["review"]!["available"]!.GetValue<bool>(), view["review"]!["submitted"]!.GetValue<bool>()));

        // No internal id (attempt and payment ids are the only ones allowed, and there is none yet) and no internal data.
        Assert.False(Guids.IsMatch(text), "The public invoice must not contain any id here.");
        Assert.DoesNotContain("Sam Staff", text, StringComparison.Ordinal);
        Assert.DoesNotContain(world.Org.ToString(), text, StringComparison.OrdinalIgnoreCase);

        // The bank details are never stored in plaintext.
        Assert.False(await database.ScalarAsync<bool>("SELECT bank_account_number_ciphertext = convert_to(@a, 'UTF8') FROM organizations WHERE id = @o", ("a", OnlineSeed.BankAccount), ("o", world.Org)));

        // An active card attempt and a reported transfer are shown.
        var (attempt, _) = await OnlineApi.StartCardAsync(host.Client, invoice);
        await BillingSeed.ReadAsync(await OnlineApi.PostAsync(host.Client, "payments/bank-transfer-notice", OnlineApi.KeyBody(invoice.Token)), HttpStatusCode.Created);
        var active = await BillingSeed.ReadAsync(await OnlineApi.ViewAsync(host.Client, invoice.Token));
        Assert.Equal((attempt, "pending", Local(DateTimeOffset.UtcNow)), (active["activeAttempt"]!["attemptId"]!.GetValue<Guid>(), active["activeAttempt"]!["status"]!.GetValue<string>(), active["transferReportedOn"]!.GetValue<string>()));

        // A partially paid overdue invoice lists its external payment by label, without reference, note or receiver.
        var overdue = await database.InvoiceAsync(world, member.UserId, new OnlineSpec { Status = "partially_paid", Paid = 100m, DueDays = -3 });
        var seeded = await database.SeedPaymentAsync(world.Org, new HubInvoice(overdue.Id, overdue.Number, overdue.OrderNumber, overdue.Order, overdue.Customer, overdue.Branch), 100m, -1, "bank_transfer", "WIRE-SECRET-1", member.UserId, member.UserId, "INTERNAL NOTE", OnlineSeed.Timezone);
        var partial = await BillingSeed.ReadAsync(await OnlineApi.ViewAsync(host.Client, overdue.Token));
        var item = Assert.Single(partial["payments"]!.AsArray())!;

        Assert.Equal(("partially_paid", true, 3, 100m, 257.28m), (partial["status"]!.GetValue<string>(), partial["overdue"]!.GetValue<bool>(), partial["daysOverdue"]!.GetValue<int>(), partial["amountPaid"]!.GetValue<decimal>(), partial["balanceDue"]!.GetValue<decimal>()));
        Assert.Equal(
            (seeded.Id, seeded.Display, (string?)null, Local(DateTimeOffset.UtcNow.AddDays(-1)), "bank_transfer", "Bank transfer", 100m, 0m, "succeeded"),
            (item["paymentId"]!.GetValue<Guid>(), item["number"]!.GetValue<string>(), item["receiptNumber"]?.GetValue<string>(), item["paidOn"]!.GetValue<string>(), item["method"]!.GetValue<string>(), item["methodLabel"]!.GetValue<string>(), item["amount"]!.GetValue<decimal>(), item["refundedAmount"]!.GetValue<decimal>(), item["status"]!.GetValue<string>()));
        Assert.DoesNotContain("WIRE-SECRET-1", partial.ToJsonString(), StringComparison.Ordinal);
        Assert.DoesNotContain("INTERNAL NOTE", partial.ToJsonString(), StringComparison.Ordinal);
        Assert.NotNull(partial["paymentOptions"]);

        // A card payment of a paid invoice: the options and the bank number disappear, the timeline and review follow.
        var paid = await database.InvoiceAsync(world, member.UserId);
        var (_, paidIntent) = await OnlineApi.StartCardAsync(host.Client, paid);
        Assert.Equal(HttpStatusCode.OK, (await OnlineApi.DeliverAsync(host.Client, OnlineApi.Succeeded(NewEvent(), paidIntent, 357.28m))).StatusCode);
        var settled = await BillingSeed.ReadAsync(await OnlineApi.ViewAsync(host.Client, paid.Token));
        var settledItem = Assert.Single(settled["payments"]!.AsArray())!;

        Assert.Equal(("paid", false), (settled["status"]!.GetValue<string>(), settled["overdue"]!.GetValue<bool>()));
        Assert.Null(settled["paymentOptions"]);
        Assert.DoesNotContain(OnlineSeed.BankAccount, settled.ToJsonString(), StringComparison.Ordinal);
        Assert.Equal((Local(DateTimeOffset.UtcNow), Local(DateTimeOffset.UtcNow)), (settled["timeline"]!["paidOn"]!.GetValue<string>(), settled["timeline"]!["receiptOn"]!.GetValue<string>()));
        Assert.Equal(("Visa ending in 4242", $"RCT-{paid.Number}-01"), (settledItem["methodLabel"]!.GetValue<string>(), settledItem["receiptNumber"]!.GetValue<string>()));
        Assert.Equal((true, false), (settled["review"]!["available"]!.GetValue<bool>(), settled["review"]!["submitted"]!.GetValue<bool>()));
        Assert.DoesNotContain("4242424242", settled.ToJsonString(), StringComparison.Ordinal);

        // Card keys missing and bank details unconfigured: the options say so and the bank part carries only the flag.
        var bare = await database.SeedWorldAsync();
        var bareInvoice = await database.InvoiceAsync(bare, (await database.SeedMemberAsync(bare.Org, CompanySettingsDatabaseFixture.OwnerRoleId, "Bea", "Bare")).UserId);
        host.Gateway.CardAvailable = false;
        var unavailable = (await BillingSeed.ReadAsync(await OnlineApi.ViewAsync(host.Client, bareInvoice.Token)))["paymentOptions"]!;
        host.Gateway.CardAvailable = true;

        Assert.False(unavailable["card"]!["available"]!.GetValue<bool>());
        Assert.Null(unavailable["card"]!["publishableKey"]);
        Assert.Equal(["available"], unavailable["bankTransfer"]!.AsObject().Select(property => property.Key).ToArray());
        Assert.False(unavailable["bankTransfer"]!["available"]!.GetValue<bool>());

        // Without the encryption key the stored details are not offered either.
        await using var keyless = OnlineHost.Create(database, bankKey: false);
        var withoutKey = (await BillingSeed.ReadAsync(await OnlineApi.ViewAsync(keyless.Client, invoice.Token)))["paymentOptions"]!;
        Assert.Equal(["available"], withoutKey["bankTransfer"]!.AsObject().Select(property => property.Key).ToArray());
    }

    [Fact]
    public async Task BankTransferNotice_CreatesOnePendingNoticeEmailsTheOrganizationAndNeverBlocksOtherPayments()
    {
        var world = await database.SeedWorldAsync();
        await database.SeedBankAsync(world.Org);
        await using var host = OnlineHost.Create(database);
        var (_, member) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var invoice = await database.InvoiceAsync(world, member.UserId);
        var before = await database.InvoiceMoneyAsync(invoice.Id);

        // AC-12: a pending bank transfer attempt, the audit row and the organization email after the commit.
        var created = await BillingSeed.ReadAsync(await OnlineApi.PostAsync(host.Client, "payments/bank-transfer-notice", OnlineApi.KeyBody(invoice.Token)), HttpStatusCode.Created);
        var today = Local(DateTimeOffset.UtcNow);

        Assert.Equal((true, today), (created["changed"]!.GetValue<bool>(), created["reportedOn"]!.GetValue<string>()));
        Assert.Equal(
            "pending|bank_transfer|357.28|true",
            await database.ScalarAsync<string>("SELECT status::text || '|' || method::text || '|' || amount || '|' || (expires_at IS NULL) FROM invoice_payment_attempts WHERE invoice_id = @i", ("i", invoice.Id)));
        Assert.Equal(
            ["357.28|-"],
            await database.TextsAsync(
                "SELECT (metadata ->> 'amount') || '|' || COALESCE(actor_user_id::text, '-') FROM audit_logs WHERE organization_id = @o AND action = 'invoice.bank_transfer_reported'",
                ("o", world.Org)));
        var email = Assert.Single(host.Sender.Messages);
        Assert.Equal(("billing@acme.test", $"Bank transfer reported for {invoice.Display}"), (email.To, email.Subject));
        Assert.Contains(
            $"A customer reported a bank transfer of 357.28 USD for invoice {invoice.Display} on {DateTimeOffset.UtcNow.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)}. Confirm it in your bank before recording the payment.",
            email.TextBody,
            StringComparison.Ordinal);
        Assert.Equal(before, await database.InvoiceMoneyAsync(invoice.Id));

        // Repeating it changes nothing (200, changed false); card payments remain possible.
        var repeat = await BillingSeed.ReadAsync(await OnlineApi.PostAsync(host.Client, "payments/bank-transfer-notice", OnlineApi.KeyBody(invoice.Token)));
        Assert.Equal((false, today), (repeat["changed"]!.GetValue<bool>(), repeat["reportedOn"]!.GetValue<string>()));
        Assert.Equal(1, await database.CountAsync("invoice_payment_attempts", "invoice_id = @i", ("i", invoice.Id)));
        Assert.Equal(1, await database.AuditCountAsync(world.Org, "invoice.bank_transfer_reported"));
        Assert.Single(host.Sender.Messages);
        await BillingSeed.ReadAsync(await OnlineApi.CardIntentAsync(host.Client, invoice.Token), HttpStatusCode.Created);
        Assert.Equal(before, await database.InvoiceMoneyAsync(invoice.Id));

        // The key of another invoice is a conflict; a malformed key is a field error; a paid invoice or missing details are 409.
        var other = await database.InvoiceAsync(world, member.UserId);
        var key = Guid.NewGuid();
        await BillingSeed.ReadAsync(await OnlineApi.PostAsync(host.Client, "payments/bank-transfer-notice", OnlineApi.KeyBody(other.Token, key)), HttpStatusCode.Created);
        var third = await database.InvoiceAsync(world, member.UserId);
        await BillingSeed.ProblemAsync(await OnlineApi.PostAsync(host.Client, "payments/bank-transfer-notice", OnlineApi.KeyBody(third.Token, key)), HttpStatusCode.Conflict, "idempotency_conflict");
        await BillingSeed.ProblemAsync(
            await OnlineApi.PostAsync(host.Client, "payments/bank-transfer-notice", new JsonObject { ["token"] = third.Token, ["idempotencyKey"] = "nope" }),
            HttpStatusCode.BadRequest,
            errorKey: "idempotencyKey");

        var paid = await database.InvoiceAsync(world, member.UserId, new OnlineSpec { Status = "paid", Paid = 357.28m });
        await BillingSeed.ProblemAsync(await OnlineApi.PostAsync(host.Client, "payments/bank-transfer-notice", OnlineApi.KeyBody(paid.Token)), HttpStatusCode.Conflict, "invoice_not_payable");

        var bare = await database.SeedWorldAsync();
        var bareInvoice = await database.InvoiceAsync(bare, (await database.SeedMemberAsync(bare.Org, CompanySettingsDatabaseFixture.OwnerRoleId, "Bea", "Bare")).UserId);
        await BillingSeed.ProblemAsync(await OnlineApi.PostAsync(host.Client, "payments/bank-transfer-notice", OnlineApi.KeyBody(bareInvoice.Token)), HttpStatusCode.Conflict, "bank_transfer_unavailable");
        await using var keyless = OnlineHost.Create(database, bankKey: false);
        await BillingSeed.ProblemAsync(await OnlineApi.PostAsync(keyless.Client, "payments/bank-transfer-notice", OnlineApi.KeyBody(third.Token)), HttpStatusCode.Conflict, "bank_transfer_unavailable");
        Assert.Equal(0, await database.CountAsync("invoice_payment_attempts", "invoice_id IN (@a, @b, @c)", ("a", paid.Id), ("b", bareInvoice.Id), ("c", third.Id)));

        // An organization without an email still records the notice; the email is skipped.
        var silent = await database.SeedWorldAsync();
        await database.SeedBankAsync(silent.Org);
        await database.ExecuteAsync("UPDATE organizations SET email = NULL WHERE id = @o", ("o", silent.Org));
        var silentInvoice = await database.InvoiceAsync(silent, (await database.SeedMemberAsync(silent.Org, CompanySettingsDatabaseFixture.OwnerRoleId, "Sid", "Silent")).UserId);
        var sent = host.Sender.Messages.Count;
        await BillingSeed.ReadAsync(await OnlineApi.PostAsync(host.Client, "payments/bank-transfer-notice", OnlineApi.KeyBody(silentInvoice.Token)), HttpStatusCode.Created);
        Assert.Equal(sent, host.Sender.Messages.Count);
        Assert.Equal(1, await database.CountAsync("invoice_payment_attempts", "invoice_id = @i", ("i", silentInvoice.Id)));
    }

    [Fact]
    public async Task Documents_ServeReceiptCompletionReportAndOnlyBeforeAfterPhotosScopedToTheTokenInvoice()
    {
        var world = await database.SeedWorldAsync();
        await using var host = OnlineHost.Create(database);
        var (_, member) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var tech = await database.SeedTechAsync(world.Org, world.BranchA, "Tina", "Tech");
        var invoice = await database.InvoiceAsync(world, member.UserId, new OnlineSpec { Technician = tech });
        var visit = invoice.Job.Visit;

        // Evidence of every type: only before and after are public, before first and then by creation.
        var earlier = Guid.NewGuid();
        var excluded = new List<Guid>();
        await database.SeedEvidenceAsync(member.UserId, visit, earlier, "before", "Earlier");
        await database.ExecuteAsync("UPDATE visit_evidence SET created_at = now() - interval '1 day' WHERE id = @id", ("id", earlier));

        foreach (var type in new[] { "during", "incident", "other" })
        {
            var id = Guid.NewGuid();
            excluded.Add(id);
            await database.SeedEvidenceAsync(member.UserId, visit, id, type, $"{type} secret");
        }

        var photos = await BillingSeed.ReadAsync(await OnlineApi.PostAsync(host.Client, "photos", new JsonObject { ["token"] = invoice.Token }));
        var list = photos.AsArray();

        Assert.Equal(
            [("before", "Earlier"), ("before", "Before photo"), ("after", "After photo")],
            list.Select(photo => (photo!["type"]!.GetValue<string>(), photo["caption"]!.GetValue<string>())).ToArray());
        Assert.Equal(earlier, list[0]!["photoId"]!.GetValue<Guid>());
        Assert.Equal(Local(DateTimeOffset.UtcNow.AddDays(-1)), list[0]!["takenOn"]!.GetValue<string>());
        Assert.DoesNotContain("secret", photos.ToJsonString(), StringComparison.Ordinal);
        Assert.Equal(3, (await BillingSeed.ReadAsync(await OnlineApi.ViewAsync(host.Client, invoice.Token)))["service"]!["photoCount"]!.GetValue<int>());

        var content = await OnlineApi.PostAsync(host.Client, "photos/content", new JsonObject { ["token"] = invoice.Token, ["photoId"] = earlier.ToString() });
        Assert.Equal(HttpStatusCode.OK, content.StatusCode);
        Assert.Equal("image/png", content.Content.Headers.ContentType?.MediaType);
        Assert.Equal(BillingSeed.Png, await content.Content.ReadAsByteArrayAsync());
        Assert.True(content.Headers.CacheControl?.NoStore);
        Assert.Equal("nosniff", content.Headers.GetValues("X-Content-Type-Options").Single());

        // Excluded types, a photo of another invoice and a malformed id: the same 404 (or a field error) and no content.
        var foreignInvoice = await database.InvoiceAsync(world, member.UserId);
        foreach (var photo in excluded.Append(foreignInvoice.Job.BeforePhoto).Append(Guid.NewGuid()))
        {
            await BillingSeed.ProblemAsync(
                await OnlineApi.PostAsync(host.Client, "photos/content", new JsonObject { ["token"] = invoice.Token, ["photoId"] = photo.ToString() }),
                HttpStatusCode.NotFound,
                "invoice_link_unavailable");
        }

        await BillingSeed.ProblemAsync(
            await OnlineApi.PostAsync(host.Client, "photos/content", new JsonObject { ["token"] = invoice.Token, ["photoId"] = "x" }),
            HttpStatusCode.BadRequest,
            errorKey: "photoId");

        // The completion report: a PDF named after the work order, only while a visit was completed.
        var report = await OnlineApi.PostAsync(host.Client, "completion-report", new JsonObject { ["token"] = invoice.Token });
        var reportBytes = await report.Content.ReadAsByteArrayAsync();
        Assert.Equal(HttpStatusCode.OK, report.StatusCode);
        Assert.Equal("application/pdf", report.Content.Headers.ContentType?.MediaType);
        Assert.Equal($"attachment; filename=\"WO-{invoice.OrderNumber}-completion-report.pdf\"", report.Content.Headers.ContentDisposition?.ToString());
        Assert.True(report.Headers.CacheControl?.NoStore);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(reportBytes, 0, 4));

        var open = await database.InvoiceAsync(world, member.UserId, new OnlineSpec { OpenJob = true });
        await BillingSeed.ProblemAsync(await OnlineApi.PostAsync(host.Client, "completion-report", new JsonObject { ["token"] = open.Token }), HttpStatusCode.NotFound, "invoice_link_unavailable");
        var openView = await BillingSeed.ReadAsync(await OnlineApi.ViewAsync(host.Client, open.Token));
        Assert.Equal((false, 0), (openView["service"]!["hasCompletionReport"]!.GetValue<bool>(), openView["service"]!["photoCount"]!.GetValue<int>()));
        Assert.Null(openView["timeline"]!["serviceCompletedOn"]);
        Assert.Null(openView["service"]!["technicianName"]);
        Assert.Empty((await BillingSeed.ReadAsync(await OnlineApi.PostAsync(host.Client, "photos", new JsonObject { ["token"] = open.Token }))).AsArray());

        // The receipt: only a payment with a receipt number of this invoice, named after it, still available after a refund.
        var (attempt, intent) = await OnlineApi.StartCardAsync(host.Client, invoice);
        Assert.Equal(HttpStatusCode.OK, (await OnlineApi.DeliverAsync(host.Client, OnlineApi.Succeeded(NewEvent(), intent, 357.28m))).StatusCode);
        var payment = (await BillingSeed.ReadAsync(await OnlineApi.StatusAsync(host.Client, invoice.Token, attempt)))["payment"]!["paymentId"]!.GetValue<Guid>();
        var receipt = await OnlineApi.PostAsync(host.Client, "receipt", new JsonObject { ["token"] = invoice.Token, ["paymentId"] = payment.ToString() });
        var receiptBytes = await receipt.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, receipt.StatusCode);
        Assert.Equal("application/pdf", receipt.Content.Headers.ContentType?.MediaType);
        Assert.Equal($"attachment; filename=\"RCT-{invoice.Number}-01.pdf\"", receipt.Content.Headers.ContentDisposition?.ToString());
        Assert.True(receipt.Headers.CacheControl?.NoStore);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(receiptBytes, 0, 4));

        Assert.Equal(HttpStatusCode.OK, (await OnlineApi.DeliverAsync(host.Client, OnlineApi.Refunded(NewEvent(), intent, 357.28m, 357.28m))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await OnlineApi.PostAsync(host.Client, "receipt", new JsonObject { ["token"] = invoice.Token, ["paymentId"] = payment.ToString() })).StatusCode);

        var legacy = await database.SeedPaymentAsync(
            world.Org, new HubInvoice(invoice.Id, invoice.Number, invoice.OrderNumber, invoice.Order, invoice.Customer, invoice.Branch), 5m, -1, "cash", null, member.UserId, member.UserId, null, OnlineSeed.Timezone);
        var foreignPayment = await database.SeedPaymentAsync(
            world.Org, new HubInvoice(foreignInvoice.Id, foreignInvoice.Number, foreignInvoice.OrderNumber, foreignInvoice.Order, foreignInvoice.Customer, foreignInvoice.Branch), 5m, -1, "cash", null, member.UserId, member.UserId, null, OnlineSeed.Timezone);

        foreach (var denied in new[] { legacy.Id, foreignPayment.Id, Guid.NewGuid() })
        {
            await BillingSeed.ProblemAsync(
                await OnlineApi.PostAsync(host.Client, "receipt", new JsonObject { ["token"] = invoice.Token, ["paymentId"] = denied.ToString() }),
                HttpStatusCode.NotFound,
                "invoice_link_unavailable");
        }

        await BillingSeed.ProblemAsync(
            await OnlineApi.PostAsync(host.Client, "receipt", new JsonObject { ["token"] = invoice.Token, ["paymentId"] = "x" }),
            HttpStatusCode.BadRequest,
            errorKey: "paymentId");
    }

    [Fact]
    public async Task Review_IsAcceptedOnceForAPaidInvoiceWithTheTechnicianAndRejectsInvalidAndConcurrentSubmissions()
    {
        var world = await database.SeedWorldAsync();
        await using var host = OnlineHost.Create(database);
        var (_, member) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var tech = await database.SeedTechAsync(world.Org, world.BranchA, "Tina", "Tech");

        JsonObject Body(string token, object? rating, string? comment = null) => new() { ["token"] = token, ["rating"] = JsonValue.Create(rating), ["comment"] = comment };

        // AC-14: not before payment, field errors, then one immutable review with the primary technician.
        var sent = await database.InvoiceAsync(world, member.UserId, new OnlineSpec { Technician = tech });
        await BillingSeed.ProblemAsync(await OnlineApi.PostAsync(host.Client, "review", Body(sent.Token, 5)), HttpStatusCode.Conflict, "review_not_available");

        var paid = await database.InvoiceAsync(world, member.UserId, new OnlineSpec { Status = "paid", Paid = 357.28m, Technician = tech });

        foreach (var (rating, comment, field) in new (object?, string?, string)[]
        {
            (null, null, "rating"), (0, null, "rating"), (6, null, "rating"), (3.5, null, "rating"), (3, new string('c', 501), "comment"),
        })
        {
            await BillingSeed.ProblemAsync(await OnlineApi.PostAsync(host.Client, "review", Body(paid.Token, rating, comment)), HttpStatusCode.BadRequest, errorKey: field);
        }

        Assert.Equal(0, await database.CountAsync("invoice_reviews", "invoice_id = @i", ("i", paid.Id)));

        var created = await BillingSeed.ReadAsync(await OnlineApi.PostAsync(host.Client, "review", Body(paid.Token, 5, "  Great and tidy  ")), HttpStatusCode.Created);
        Assert.True(created["submitted"]!.GetValue<bool>());
        Assert.Equal(
            $"5|Great and tidy|{tech}|{paid.Order}",
            await database.ScalarAsync<string>("SELECT rating || '|' || comment || '|' || technician_id || '|' || work_order_id FROM invoice_reviews WHERE invoice_id = @i", ("i", paid.Id)));
        Assert.Equal(
            ["5|-"],
            await database.TextsAsync(
                "SELECT (metadata ->> 'rating') || '|' || COALESCE(actor_user_id::text, '-') FROM audit_logs WHERE entity_id = @i AND action = 'invoice.review_submitted'",
                ("i", paid.Id)));
        Assert.DoesNotContain("Great", await database.OrgAuditTextAsync(world.Org), StringComparison.Ordinal);
        await BillingSeed.ProblemAsync(await OnlineApi.PostAsync(host.Client, "review", Body(paid.Token, 1)), HttpStatusCode.Conflict, "review_exists");
        var view = await BillingSeed.ReadAsync(await OnlineApi.ViewAsync(host.Client, paid.Token));
        Assert.Equal((false, true), (view["review"]!["available"]!.GetValue<bool>(), view["review"]!["submitted"]!.GetValue<bool>()));

        // A paid invoice without a completed visit stores no technician.
        var open = await database.InvoiceAsync(world, member.UserId, new OnlineSpec { Status = "paid", Paid = 357.28m, OpenJob = true });
        await BillingSeed.ReadAsync(await OnlineApi.PostAsync(host.Client, "review", Body(open.Token, 3)), HttpStatusCode.Created);
        Assert.True(await database.ScalarAsync<bool>("SELECT technician_id IS NULL AND comment IS NULL FROM invoice_reviews WHERE invoice_id = @i", ("i", open.Id)));

        // Concurrent submissions create exactly one review: one 201 and one 409 review_exists.
        var racing = await database.InvoiceAsync(world, member.UserId, new OnlineSpec { Status = "paid", Paid = 357.28m });
        var responses = await Task.WhenAll(
            OnlineApi.PostAsync(host.Client, "review", Body(racing.Token, 4)),
            OnlineApi.PostAsync(host.Client, "review", Body(racing.Token, 5)));

        Assert.Equal([HttpStatusCode.Created, HttpStatusCode.Conflict], responses.Select(response => response.StatusCode).Order().ToArray());
        Assert.Equal(1, await database.CountAsync("invoice_reviews", "invoice_id = @i", ("i", racing.Id)));
        Assert.Equal("review_exists", (await BillingSeed.ReadAsync(responses.Single(response => response.StatusCode == HttpStatusCode.Conflict), HttpStatusCode.Conflict))["code"]!.GetValue<string>());
    }
}
