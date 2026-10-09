using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.BillingReview;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.Customers;
using FieldOps.IntegrationTests.ServiceRequests;
using FieldOps.IntegrationTests.Users;

namespace FieldOps.IntegrationTests.InvoicePayments;

/// <summary>Overview, invoice list, payment list and options (invoices-payments-management AC-03, AC-04, AC-05).</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class InvoiceHubReadTests(CompanySettingsDatabaseFixture database)
{
    private const string Tz = HubSeed.Timezone;

    private sealed record Hub(
        RequestWorld World,
        string Owner,
        SeededMember OwnerMember,
        SeededMember Receiver,
        Guid Dana,
        Dictionary<string, HubInvoice> Inv,
        Dictionary<string, HubPayment> Pay);

    // One data set for the read tests: every stored status across two branches, due dates around today in a non-UTC
    // timezone, payments in and out of the month and the 90-day window, a void invoice with a payment and a foreign organization.
    private async Task<Hub> SeedHubAsync(RequestsHost host)
    {
        var world = await database.SeedWorldAsync(Tz);
        var foreign = await database.SeedWorldAsync(Tz);
        var (owner, ownerMember) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId, "Olive", "Owner");
        var receiver = await database.SeedReceiverAsync(world.Org);
        await database.SeedReceiverAsync(world.Org, "Ina", "Inactive", status: "suspended");
        await database.ExecuteAsync("UPDATE organizations SET next_invoice_number = 100 WHERE id IN (@a, @b)", ("a", world.Org), ("b", foreign.Org));

        var dana = await database.SeedCustomerAsync(world.Org, world.BranchA, "Dana Dunn");
        var voidOnly = await database.SeedCustomerAsync(world.Org, world.BranchA, "Void Only Co");
        var user = ownerMember.UserId;
        var inv = new Dictionary<string, HubInvoice>
        {
            ["I1"] = await database.SeedInvoiceAsync(world, user, new InvoiceSpec { IssueDays = -40, DueDays = -1, Total = 100m }),
            ["I2"] = await database.SeedInvoiceAsync(world, user, new InvoiceSpec { Status = "partially_paid", IssueDays = -50, DueDays = -30, Total = 200m, Paid = 50m }),
            ["I3"] = await database.SeedInvoiceAsync(world, user, new InvoiceSpec { IssueDays = -60, DueDays = -31, Total = 300m, Branch = world.BranchB, Customer = dana }),
            ["I4"] = await database.SeedInvoiceAsync(world, user, new InvoiceSpec { IssueDays = -70, DueDays = -61, Total = 400m, Branch = world.BranchB, Customer = dana }),
            ["I5"] = await database.SeedInvoiceAsync(world, user, new InvoiceSpec { IssueDays = -3, DueDays = 0, Total = 50m }),
            ["I6"] = await database.SeedInvoiceAsync(world, user, new InvoiceSpec { IssueDays = -3, DueDays = 10, Total = 25m, Customer = dana }),
            ["I7"] = await database.SeedInvoiceAsync(world, user, new InvoiceSpec { Status = "draft", IssueDays = -1, DueDays = 13, Total = 70m, Recipient = null }),
            ["I8"] = await database.SeedInvoiceAsync(world, user, new InvoiceSpec { Status = "paid", IssueDays = -20, DueDays = -6, Total = 80m, Paid = 80m }),
            ["I9"] = await database.SeedInvoiceAsync(world, user, new InvoiceSpec { Status = "paid", IssueDays = -100, DueDays = -85, Total = 60m, Paid = 60m, Branch = world.BranchB, Customer = dana }),
            ["IV"] = await database.SeedInvoiceAsync(world, user, new InvoiceSpec { Status = "void", IssueDays = -2, DueDays = null, Total = 999m, Customer = voidOnly }),
        };
        var foreignUser = (await database.SeedMemberAsync(foreign.Org, CompanySettingsDatabaseFixture.OwnerRoleId, "Fay", "Foreign")).UserId;
        var foreignInvoice = await database.SeedInvoiceAsync(foreign, foreignUser, new InvoiceSpec { IssueDays = -5, DueDays = -2, Total = 5000m });
        await database.SeedPaymentAsync(foreign.Org, foreignInvoice, 5000m, -1, "cash", "FOREIGN-REF", foreignUser, foreignUser);

        var by = receiver.UserId;
        var pay = new Dictionary<string, HubPayment>
        {
            ["I9a"] = await database.SeedPaymentAsync(world.Org, inv["I9"], 20m, -95, "card_external", "CARD-1", by, user),
            ["I9b"] = await database.SeedPaymentAsync(world.Org, inv["I9"], 20m, -96, "cash", null, by, user),
            ["I9c"] = await database.SeedPaymentAsync(world.Org, inv["I9"], 20m, -97, "other", null, by, user),
            ["A"] = await database.SeedPaymentAsync(world.Org, inv["I8"], 30m, -7, "cash", null, by, user, "SECRET-NOTE"),
            ["B"] = await database.SeedPaymentAsync(world.Org, inv["I8"], 30m, -5, "check", "CHK-9001", by, user),
            ["C"] = await database.SeedPaymentAsync(world.Org, inv["I8"], 20m, -5, "bank_transfer", "WIRE-77", by, user),
            ["I2"] = await database.SeedPaymentAsync(world.Org, inv["I2"], 50m, -3, "check", "CHK-I2", by, user, "SECRET-NOTE"),
            ["Void"] = await database.SeedPaymentAsync(world.Org, inv["IV"], 999m, -1, "cash", "VOID-REF", by, user),
        };

        return new Hub(world, owner, ownerMember, receiver, dana, inv, pay);
    }

    private static decimal Money(JsonNode node) => node.GetValue<decimal>();

    private static bool InThisMonth(int daysFromToday)
    {
        var today = HubSeed.Today();
        var date = today.AddDays(daysFromToday);

        return date.Year == today.Year && date.Month == today.Month;
    }

    private static string[] Of(JsonNode page, string property = "id") => PaymentApi.Texts(page, property);

    [Fact]
    public async Task Overview_ComputesMetricsAgingAndRecentPaymentsForVisibleData()
    {
        await using var host = RequestsHost.Create(database);
        var hub = await SeedHubAsync(host);
        var (scoped, _) = await host.SignInAsync(
            database, hub.World.Org, CompanySettingsDatabaseFixture.DispatcherRoleId, "Dee", "Spatch", hub.World.BranchA);
        var paidThisMonth = new Dictionary<string, decimal>
        {
            ["A"] = 30m,
            ["B"] = 30m,
            ["C"] = 20m,
            ["I2"] = 50m,
            ["I9a"] = 20m,
            ["I9b"] = 20m,
            ["I9c"] = 20m,
        };
        var days = new Dictionary<string, int> { ["A"] = -7, ["B"] = -5, ["C"] = -5, ["I2"] = -3, ["I9a"] = -95, ["I9b"] = -96, ["I9c"] = -97 };
        decimal Month(params string[] keys) => keys.Where(key => InThisMonth(days[key])).Sum(key => paidThisMonth[key]);

        // AC-03: metrics, aging buckets (1, 30, 31, 61 days and not yet due), average time to pay and the five latest payments.
        var overview = await PaymentApi.ReadAsync(await host.SendAsync(HttpMethod.Get, "/invoices/overview", hub.Owner), HttpStatusCode.OK);
        var metrics = overview["metrics"]!;

        Assert.Equal(1025m, Money(metrics["outstanding"]!));
        Assert.Equal(950m, Money(metrics["overdue"]!));
        Assert.Equal(70m, Money(metrics["draft"]!));
        Assert.Equal(Month("A", "B", "C", "I2", "I9a", "I9b", "I9c"), Money(metrics["paidThisMonth"]!));
        Assert.Equal(15.0m, Money(metrics["averageDaysToPay"]!));
        Assert.Equal("USD", metrics["currency"]!.GetValue<string>());
        Assert.Equal(
            (75m, 250m, 300m, 400m),
            (Money(overview["aging"]!["current"]!), Money(overview["aging"]!["days1To30"]!), Money(overview["aging"]!["days31To60"]!), Money(overview["aging"]!["days60Plus"]!)));
        Assert.Equal(
            [hub.Pay["I2"].Id, hub.Pay["C"].Id, hub.Pay["B"].Id, hub.Pay["A"].Id, hub.Pay["I9a"].Id],
            overview["recentPayments"]!.AsArray().Select(row => row!["paymentId"]!.GetValue<Guid>()).ToArray());
        var first = overview["recentPayments"]![0]!;
        Assert.Equal(
            (HubSeed.Day(-3), "Carla Customer", hub.Inv["I2"].Id, hub.Inv["I2"].Display, 50m, "check"),
            (first["paidDate"]!.GetValue<string>(), first["customerName"]!.GetValue<string>(), first["invoiceId"]!.GetValue<Guid>(),
                first["invoiceNumber"]!.GetValue<string>(), Money(first["amount"]!), first["method"]!.GetValue<string>()));

        // The Branch filter drives metrics, aging and recent payments; no paid invoice in the window means null.
        var branchB = await PaymentApi.ReadAsync(
            await host.SendAsync(HttpMethod.Get, $"/invoices/overview?branchId={hub.World.BranchB}", hub.Owner), HttpStatusCode.OK);
        Assert.Equal((700m, 700m, 0m), (Money(branchB["metrics"]!["outstanding"]!), Money(branchB["metrics"]!["overdue"]!), Money(branchB["metrics"]!["draft"]!)));
        Assert.Equal(Month("I9a", "I9b", "I9c"), Money(branchB["metrics"]!["paidThisMonth"]!));
        Assert.Null(branchB["metrics"]!["averageDaysToPay"]);
        Assert.Equal((0m, 0m, 300m, 400m), (Money(branchB["aging"]!["current"]!), Money(branchB["aging"]!["days1To30"]!), Money(branchB["aging"]!["days31To60"]!), Money(branchB["aging"]!["days60Plus"]!)));
        Assert.Equal(
            [hub.Pay["I9a"].Id, hub.Pay["I9b"].Id, hub.Pay["I9c"].Id],
            branchB["recentPayments"]!.AsArray().Select(row => row!["paymentId"]!.GetValue<Guid>()).ToArray());

        // Branch scope limits the same numbers to branch A; void and foreign data never count.
        var scopedOverview = await PaymentApi.ReadAsync(await host.SendAsync(HttpMethod.Get, "/invoices/overview", scoped), HttpStatusCode.OK);
        Assert.Equal((325m, 250m, 70m), (Money(scopedOverview["metrics"]!["outstanding"]!), Money(scopedOverview["metrics"]!["overdue"]!), Money(scopedOverview["metrics"]!["draft"]!)));
        Assert.Equal(15.0m, Money(scopedOverview["metrics"]!["averageDaysToPay"]!));
        Assert.Equal(Month("A", "B", "C", "I2"), Money(scopedOverview["metrics"]!["paidThisMonth"]!));
        Assert.Equal(
            [hub.Pay["I2"].Id, hub.Pay["C"].Id, hub.Pay["B"].Id, hub.Pay["A"].Id],
            scopedOverview["recentPayments"]!.AsArray().Select(row => row!["paymentId"]!.GetValue<Guid>()).ToArray());
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Get, $"/invoices/overview?branchId={hub.World.BranchB}", scoped)).StatusCode);
        await BillingSeed.ProblemAsync(
            await host.SendAsync(HttpMethod.Get, "/invoices/overview?branchId=nope", hub.Owner), HttpStatusCode.BadRequest, errorKey: "branchId");

        // Options (BR-19): members only for action roles, active members of the organization only.
        var options = await PaymentApi.ReadAsync(await host.SendAsync(HttpMethod.Get, "/invoices/options", hub.Owner), HttpStatusCode.OK);
        Assert.Equal((Tz, "USD", "PAY", true), (options["timezone"]!.GetValue<string>(), options["currency"]!.GetValue<string>(), options["paymentPrefix"]!.GetValue<string>(), options["canAct"]!.GetValue<bool>()));
        Assert.Equal(["Alpha Branch", "Bravo Branch"], options["branches"]!.AsArray().Select(branch => branch!["name"]!.GetValue<string>()).ToArray());
        Assert.Equal(["Dee Spatch", "Olive Owner", "Rita Receiver"], options["members"]!.AsArray().Select(member => member!["name"]!.GetValue<string>()).Order().ToArray());
        Assert.Contains(hub.Receiver.UserId, options["members"]!.AsArray().Select(member => member!["userId"]!.GetValue<Guid>()));
        var scopedOptions = await PaymentApi.ReadAsync(await host.SendAsync(HttpMethod.Get, "/invoices/options", scoped), HttpStatusCode.OK);
        Assert.False(scopedOptions["canAct"]!.GetValue<bool>());
        Assert.Empty(scopedOptions["members"]!.AsArray());
        Assert.Equal(["Alpha Branch"], scopedOptions["branches"]!.AsArray().Select(branch => branch!["name"]!.GetValue<string>()).ToArray());

        // Customer filter: customers with at least one visible non-void invoice, by name, at most 20.
        var customers = await PaymentApi.ReadAsync(await host.SendAsync(HttpMethod.Get, "/invoices/customers", hub.Owner), HttpStatusCode.OK);
        Assert.Equal(["Carla Customer", "Dana Dunn"], customers.AsArray().Select(customer => customer!["name"]!.GetValue<string>()).ToArray());
        var searched = await PaymentApi.ReadAsync(await host.SendAsync(HttpMethod.Get, "/invoices/customers?search=DAN", hub.Owner), HttpStatusCode.OK);
        Assert.Equal([hub.Dana], searched.AsArray().Select(customer => customer!["id"]!.GetValue<Guid>()).ToArray());
        var scopedCustomers = await PaymentApi.ReadAsync(await host.SendAsync(HttpMethod.Get, "/invoices/customers", scoped), HttpStatusCode.OK);
        Assert.Equal(["Carla Customer", "Dana Dunn"], scopedCustomers.AsArray().Select(customer => customer!["name"]!.GetValue<string>()).ToArray());
        await BillingSeed.ProblemAsync(
            await host.SendAsync(HttpMethod.Get, $"/invoices/customers?search={new string('x', 101)}", hub.Owner), HttpStatusCode.BadRequest, errorKey: "search");
    }

    [Fact]
    public async Task Lists_FilterSearchSortAndPageVisibleInvoicesAndPayments()
    {
        await using var host = RequestsHost.Create(database);
        var hub = await SeedHubAsync(host);
        var inv = hub.Inv;

        async Task<JsonNode> InvoicesAsync(string query = "", HttpStatusCode status = HttpStatusCode.OK) =>
            await PaymentApi.ReadAsync(await host.SendAsync(HttpMethod.Get, "/invoices" + query, hub.Owner), status);

        async Task<JsonNode> PaymentsAsync(string query = "", HttpStatusCode status = HttpStatusCode.OK) =>
            await PaymentApi.ReadAsync(await host.SendAsync(HttpMethod.Get, "/invoices/payments" + query, hub.Owner), status);

        string[] InvoiceIds(JsonNode page) => Of(page);
        string[] Expect(params HubInvoice[] invoices) => [.. invoices.Select(invoice => invoice.Id.ToString())];
        string[] ExpectPayments(params string[] keys) => [.. keys.Select(key => hub.Pay[key].Id.ToString())];

        // AC-04: sorted by invoice date then number (descending), void invoices never listed, page size 20.
        var all = await InvoicesAsync();
        Assert.Equal(Expect(inv["I7"], inv["I6"], inv["I5"], inv["I8"], inv["I1"], inv["I2"], inv["I3"], inv["I4"], inv["I9"]), InvoiceIds(all));
        Assert.Equal((1, 20, 9), (all["page"]!.GetValue<int>(), all["pageSize"]!.GetValue<int>(), all["total"]!.GetValue<int>()));
        Assert.Empty((await InvoicesAsync("?page=2"))["items"]!.AsArray());
        Assert.Equal(9, (await InvoicesAsync("?page=2"))["total"]!.GetValue<int>());

        var rows = PaymentApi.Items(all).ToDictionary(row => row!["id"]!.GetValue<string>());
        JsonNode Row(string key) => rows[inv[key].Id.ToString()]!;

        var overdue = Row("I1");
        Assert.Equal(
            (inv["I1"].Display, "Carla Customer", inv["I1"].OrderDisplay, "Alpha Branch", HubSeed.Day(-40), HubSeed.Day(-1), "overdue", "sent", 1, true, true),
            (overdue["number"]!.GetValue<string>(), overdue["customerName"]!.GetValue<string>(), overdue["workOrderNumber"]!.GetValue<string>(),
                overdue["branchName"]!.GetValue<string>(), overdue["issueDate"]!.GetValue<string>(), overdue["dueDate"]!.GetValue<string>(),
                overdue["status"]!.GetValue<string>(), overdue["storedStatus"]!.GetValue<string>(), overdue["daysOverdue"]!.GetValue<int>(),
                overdue["hasRecipient"]!.GetValue<bool>(), overdue["canRecordPayment"]!.GetValue<bool>()));
        Assert.Equal((100m, 100m, "USD"), (Money(overdue["total"]!), Money(overdue["balanceDue"]!), overdue["currency"]!.GetValue<string>()));
        Assert.Equal(("sent", HubSeed.Day(-40)), (overdue["lastActivity"]!["kind"]!.GetValue<string>(), overdue["lastActivity"]!["date"]!.GetValue<string>()));
        Assert.Equal(inv["I1"].Customer, overdue["customerId"]!.GetValue<Guid>());
        Assert.Equal(inv["I1"].Order, overdue["workOrderId"]!.GetValue<Guid>());
        Assert.NotNull(overdue["updatedAt"]);

        // Overdue stays a presentation of the stored status, and the due date today is not overdue yet.
        Assert.Equal(("overdue", "partially_paid", 30, 150m), (Row("I2")["status"]!.GetValue<string>(), Row("I2")["storedStatus"]!.GetValue<string>(), Row("I2")["daysOverdue"]!.GetValue<int>(), Money(Row("I2")["balanceDue"]!)));
        Assert.Equal(("payment", HubSeed.Day(-3)), (Row("I2")["lastActivity"]!["kind"]!.GetValue<string>(), Row("I2")["lastActivity"]!["date"]!.GetValue<string>()));
        Assert.Equal(("sent", null), (Row("I5")["status"]!.GetValue<string>(), (int?)Row("I5")["daysOverdue"]?.GetValue<int>()));
        Assert.Equal(("paid", HubSeed.Day(-5)), (Row("I8")["lastActivity"]!["kind"]!.GetValue<string>(), Row("I8")["lastActivity"]!["date"]!.GetValue<string>()));
        Assert.Null(Row("I7")["lastActivity"]);
        Assert.False(Row("I7")["canRecordPayment"]!.GetValue<bool>());
        Assert.False(Row("I7")["hasRecipient"]!.GetValue<bool>());
        Assert.False(Row("I8")["canRecordPayment"]!.GetValue<bool>());

        // Search: customer name (case-insensitive), invoice number as digits or with the prefix, work order number.
        Assert.Equal(Expect(inv["I7"], inv["I5"], inv["I8"], inv["I1"], inv["I2"]), InvoiceIds(await InvoicesAsync("?search=CARLA")));
        Assert.Equal(Expect(inv["I3"]), InvoiceIds(await InvoicesAsync($"?search={inv["I3"].Display.ToLowerInvariant()}")));
        Assert.Equal(Expect(inv["I3"]), InvoiceIds(await InvoicesAsync($"?search={inv["I3"].Number}")));
        Assert.Equal(Expect(inv["I4"]), InvoiceIds(await InvoicesAsync($"?search={inv["I4"].OrderDisplay}")));
        Assert.Empty((await InvoicesAsync($"?search={inv["IV"].Display}"))["items"]!.AsArray());
        Assert.Equal(Expect(inv["I6"], inv["I3"], inv["I4"], inv["I9"]).Order().ToArray(), InvoiceIds(await InvoicesAsync("?search=dana")).Order().ToArray());

        // Date range on the invoice date (inclusive), every status including overdue, customer and branch.
        Assert.Equal(Expect(inv["I1"], inv["I2"], inv["I3"]), InvoiceIds(await InvoicesAsync($"?from={HubSeed.Day(-60)}&to={HubSeed.Day(-40)}")));
        var overdueList = await InvoicesAsync("?status=overdue");
        Assert.Equal(Expect(inv["I1"], inv["I2"], inv["I3"], inv["I4"]), InvoiceIds(overdueList));
        Assert.Equal([1, 30, 31, 61], PaymentApi.Items(overdueList).Select(row => row!["daysOverdue"]!.GetValue<int>()).ToArray());
        Assert.Equal(Expect(inv["I6"], inv["I5"]), InvoiceIds(await InvoicesAsync("?status=sent")));
        Assert.Empty((await InvoicesAsync("?status=partially_paid"))["items"]!.AsArray());
        Assert.Equal(Expect(inv["I8"], inv["I9"]), InvoiceIds(await InvoicesAsync("?status=paid")));
        Assert.Equal(Expect(inv["I7"]), InvoiceIds(await InvoicesAsync("?status=draft")));
        Assert.Equal(Expect(inv["I6"], inv["I3"], inv["I4"], inv["I9"]), InvoiceIds(await InvoicesAsync($"?customerId={hub.Dana}")));
        Assert.Equal(Expect(inv["I3"], inv["I4"], inv["I9"]), InvoiceIds(await InvoicesAsync($"?branchId={hub.World.BranchB}")));
        Assert.Equal(Expect(inv["I3"], inv["I4"]), InvoiceIds(await InvoicesAsync($"?branchId={hub.World.BranchB}&status=overdue&search=dana&to={HubSeed.Day(-60)}")));

        foreach (var invalid in new[]
        {
            ("from=2026-02-01&to=2026-01-01", "to"),
            ("from=yesterday", "from"),
            ("status=void", "status"),
            ("status=late", "status"),
            ("page=0", "page"),
            ("page=x", "page"),
            ("customerId=nope", "customerId"),
            ($"search={new string('s', 101)}", "search"),
        })
        {
            await BillingSeed.ProblemAsync(
                await host.SendAsync(HttpMethod.Get, "/invoices?" + invalid.Item1, hub.Owner), HttpStatusCode.BadRequest, errorKey: invalid.Item2);
        }

        // AC-05: payments sorted by date then number, only on visible invoices, notes never returned.
        var payments = await PaymentsAsync();
        Assert.Equal(ExpectPayments("I2", "C", "B", "A", "I9a", "I9b", "I9c"), Of(payments));
        Assert.Equal((1, 20, 7), (payments["page"]!.GetValue<int>(), payments["pageSize"]!.GetValue<int>(), payments["total"]!.GetValue<int>()));
        var text = payments.ToJsonString();
        Assert.DoesNotContain("SECRET-NOTE", text, StringComparison.Ordinal);
        Assert.DoesNotContain("VOID-REF", text, StringComparison.Ordinal);
        Assert.DoesNotContain("FOREIGN-REF", text, StringComparison.Ordinal);
        Assert.All(PaymentApi.Items(payments), row => Assert.False(row!.AsObject().ContainsKey("notes")));

        var bank = PaymentApi.Items(payments).Single(row => row!["id"]!.GetValue<Guid>() == hub.Pay["C"].Id)!;
        Assert.Equal(
            (hub.Pay["C"].Display, HubSeed.Day(-5), "Carla Customer", inv["I8"].Id, inv["I8"].Display, "bank_transfer", "WIRE-77", 20m, "USD", "Rita Receiver"),
            (bank["number"]!.GetValue<string>(), bank["paidDate"]!.GetValue<string>(), bank["customerName"]!.GetValue<string>(), bank["invoiceId"]!.GetValue<Guid>(),
                bank["invoiceNumber"]!.GetValue<string>(), bank["method"]!.GetValue<string>(), bank["reference"]!.GetValue<string>(), Money(bank["amount"]!),
                bank["currency"]!.GetValue<string>(), bank["receivedByName"]!.GetValue<string>()));
        Assert.Null(PaymentApi.Items(payments).Single(row => row!["id"]!.GetValue<Guid>() == hub.Pay["A"].Id)!["reference"]);

        Assert.Equal(ExpectPayments("B"), Of(await PaymentsAsync("?search=chk-9001")));
        Assert.Equal(ExpectPayments("I2", "B"), Of(await PaymentsAsync("?search=CHK")));
        Assert.Equal(ExpectPayments("C"), Of(await PaymentsAsync($"?search={hub.Pay["C"].Display}")));
        Assert.Equal(ExpectPayments("C"), Of(await PaymentsAsync($"?search={hub.Pay["C"].Number}")));
        Assert.Equal(ExpectPayments("C", "B", "A"), Of(await PaymentsAsync($"?search={inv["I8"].Display}")));
        Assert.Equal(ExpectPayments("I9a", "I9b", "I9c"), Of(await PaymentsAsync("?search=dana")));
        Assert.Equal(ExpectPayments("I2", "B"), Of(await PaymentsAsync("?method=check")));
        Assert.Equal(ExpectPayments("C", "B", "A"), Of(await PaymentsAsync($"?from={HubSeed.Day(-7)}&to={HubSeed.Day(-5)}")));
        Assert.Equal(ExpectPayments("I9a", "I9b", "I9c"), Of(await PaymentsAsync($"?branchId={hub.World.BranchB}")));
        Assert.Equal(ExpectPayments("I2", "C", "B", "A"), Of(await PaymentsAsync($"?branchId={hub.World.BranchA}")));
        Assert.Empty((await PaymentsAsync("?page=2"))["items"]!.AsArray());

        foreach (var invalid in new[] { ("method=wire", "method"), ("from=2026-02-01&to=2026-01-01", "to"), ("page=0", "page"), ($"search={new string('s', 101)}", "search") })
        {
            await BillingSeed.ProblemAsync(
                await host.SendAsync(HttpMethod.Get, "/invoices/payments?" + invalid.Item1, hub.Owner), HttpStatusCode.BadRequest, errorKey: invalid.Item2);
        }
    }

    [Fact]
    public async Task Exports_WriteTheApprovedColumnsEscapeCellsAndRefuseMoreThanFiveThousandRows()
    {
        await VerifyExportContentAsync();
        await VerifyPagingAndExportLimitAsync();
    }

    // AC-06: columns, filenames, filters, sort and escaping of both CSV files; notes, emails and addresses never leave.
    private async Task VerifyExportContentAsync()
    {
        var world = await database.SeedWorldAsync(Tz);
        await using var host = RequestsHost.Create(database);
        var (owner, member) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var receiver = await database.SeedReceiverAsync(world.Org, "Rita", "Receiver");
        var formula = await database.SeedCustomerAsync(world.Org, world.BranchA, "=Danger Co");
        var comma = await database.SeedCustomerAsync(world.Org, world.BranchB, "Smith, John");
        var user = member.UserId;

        var overdue = await database.SeedInvoiceAsync(world, user, new InvoiceSpec { IssueDays = -20, DueDays = -3, Total = 100m, Customer = formula });
        var partial = await database.SeedInvoiceAsync(world, user, new InvoiceSpec { Status = "partially_paid", IssueDays = -5, DueDays = 3, Total = 200m, Paid = 50m, Customer = comma, Branch = world.BranchB });
        var draft = await database.SeedInvoiceAsync(world, user, new InvoiceSpec { Status = "draft", IssueDays = -1, DueDays = 13, Total = 70m });
        var paid = await database.SeedInvoiceAsync(world, user, new InvoiceSpec { Status = "paid", IssueDays = -30, DueDays = -10, Total = 25m, Paid = 25m });
        await database.SeedInvoiceAsync(world, user, new InvoiceSpec { Status = "void", IssueDays = -2, DueDays = null, Total = 999m });
        var older = await database.SeedPaymentAsync(world.Org, paid, 25m, -12, "cash", null, receiver.UserId, user, "SECRET-NOTE");
        var newer = await database.SeedPaymentAsync(world.Org, partial, 50m, -2, "check", "=1+1", receiver.UserId, user, "SECRET-NOTE");
        var comma2 = await database.SeedPaymentAsync(world.Org, overdue, 10m, -2, "bank_transfer", "A,B \"x\"", receiver.UserId, user);

        var export = await host.SendAsync(HttpMethod.Get, "/invoices/export", owner);
        var csv = await export.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.Equal("text/csv; charset=utf-8", export.Content.Headers.ContentType?.ToString());
        Assert.Equal($"invoices-{HubSeed.Day(0)}.csv", export.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        Assert.Equal("no-store", export.Headers.CacheControl?.ToString());
        Assert.Equal(
            [
                "Invoice,Customer,Work order,Branch,Invoice date,Due date,Status,Total,Amount paid,Balance,Currency",
                $"{draft.Display},Carla Customer,{draft.OrderDisplay},Alpha Branch,{HubSeed.Day(-1)},{HubSeed.Day(13)},Draft,70.00,0.00,70.00,USD",
                $"{partial.Display},\"Smith, John\",{partial.OrderDisplay},Bravo Branch,{HubSeed.Day(-5)},{HubSeed.Day(3)},Partially paid,200.00,50.00,150.00,USD",
                $"{overdue.Display},'=Danger Co,{overdue.OrderDisplay},Alpha Branch,{HubSeed.Day(-20)},{HubSeed.Day(-3)},Overdue 3 days,100.00,0.00,100.00,USD",
                $"{paid.Display},Carla Customer,{paid.OrderDisplay},Alpha Branch,{HubSeed.Day(-30)},{HubSeed.Day(-10)},Paid,25.00,25.00,0.00,USD",
            ],
            csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries));

        var filtered = await (await host.SendAsync(HttpMethod.Get, "/invoices/export?status=overdue", owner)).Content.ReadAsStringAsync();
        Assert.Equal(2, filtered.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Contains(overdue.Display, filtered, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(HttpMethod.Get, "/invoices/export?status=late", owner)).StatusCode);

        var payments = await host.SendAsync(HttpMethod.Get, "/invoices/payments/export", owner);
        var paymentsCsv = await payments.Content.ReadAsStringAsync();

        Assert.Equal("text/csv; charset=utf-8", payments.Content.Headers.ContentType?.ToString());
        Assert.Equal($"payments-{HubSeed.Day(0)}.csv", payments.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        Assert.Equal(
            [
                "Payment,Date,Customer,Invoice,Method,Reference,Amount,Currency,Received by",
                $"{comma2.Display},{HubSeed.Day(-2)},'=Danger Co,{overdue.Display},Bank transfer,\"A,B \"\"x\"\"\",10.00,USD,Rita Receiver",
                $"{newer.Display},{HubSeed.Day(-2)},\"Smith, John\",{partial.Display},Check,'=1+1,50.00,USD,Rita Receiver",
                $"{older.Display},{HubSeed.Day(-12)},Carla Customer,{paid.Display},Cash,,25.00,USD,Rita Receiver",
            ],
            paymentsCsv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries));
        var onlyChecks = await (await host.SendAsync(HttpMethod.Get, "/invoices/payments/export?method=check", owner)).Content.ReadAsStringAsync();
        Assert.Equal(2, onlyChecks.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length);

        foreach (var secret in new[] { "SECRET-NOTE", "carla@example.com", "1 Seed St", "Pat Contact", "5551234567" })
        {
            Assert.DoesNotContain(secret, csv + paymentsCsv, StringComparison.Ordinal);
        }
    }

    // AC-04, AC-05, AC-06: page 20, an empty page beyond the last, and 409 export_too_large over 5,000 rows until the filters narrow it.
    private async Task VerifyPagingAndExportLimitAsync()
    {
        var world = await database.SeedWorldAsync();
        await using var host = RequestsHost.Create(database);
        var (owner, member) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var salt = world.Org.ToString("N");

        await database.ExecuteAsync(
            """
            SET session_replication_role = replica;
            INSERT INTO invoices (id, organization_id, branch_id, invoice_number, work_order_id, customer_id, status, issue_date, due_date, currency,
                subtotal, tax_total, total, amount_paid, balance_due, created_by_user_id, customer_snapshot)
            SELECT md5(@salt || 'i' || g)::uuid, @org, @branch, g, gen_random_uuid(), @customer, CAST('paid' AS invoice_status),
                DATE '2020-01-01' + g, DATE '2020-01-15' + g, 'USD', 10, 0, 10, 10, 0, @user, CAST(@snap AS jsonb)
            FROM generate_series(1, 5001) g;
            INSERT INTO payments (id, organization_id, customer_id, payment_number, method, amount, currency, paid_at, recorded_by_user_id, received_by_user_id, idempotency_key)
            SELECT md5(@salt || 'p' || g)::uuid, @org, @customer, g, CAST('cash' AS payment_method), 10, 'USD',
                CAST(DATE '2020-01-02' + g AS timestamp) AT TIME ZONE 'UTC', @user, @user, gen_random_uuid()
            FROM generate_series(1, 5001) g;
            INSERT INTO payment_allocations (payment_id, invoice_id, amount)
            SELECT md5(@salt || 'p' || g)::uuid, md5(@salt || 'i' || g)::uuid, 10 FROM generate_series(1, 5001) g;
            SET session_replication_role = DEFAULT;
            """,
            ("salt", salt),
            ("org", world.Org),
            ("branch", world.BranchA),
            ("customer", world.Customer),
            ("user", member.UserId),
            ("snap", SeedInvoiceSnapshot.Json));

        var firstPage = await PaymentApi.ReadAsync(await host.SendAsync(HttpMethod.Get, "/invoices", owner), HttpStatusCode.OK);
        Assert.Equal((20, 5001), (PaymentApi.Items(firstPage).Count, firstPage["total"]!.GetValue<int>()));
        Assert.Equal("INV-5001", PaymentApi.Items(firstPage)[0]!["number"]!.GetValue<string>());
        var lastPage = await PaymentApi.ReadAsync(await host.SendAsync(HttpMethod.Get, "/invoices?page=251", owner), HttpStatusCode.OK);
        Assert.Equal((1, 5001), (PaymentApi.Items(lastPage).Count, lastPage["total"]!.GetValue<int>()));
        var beyond = await PaymentApi.ReadAsync(await host.SendAsync(HttpMethod.Get, "/invoices?page=252", owner), HttpStatusCode.OK);
        Assert.Equal((0, 5001), (PaymentApi.Items(beyond).Count, beyond["total"]!.GetValue<int>()));
        var paymentsBeyond = await PaymentApi.ReadAsync(await host.SendAsync(HttpMethod.Get, "/invoices/payments?page=252", owner), HttpStatusCode.OK);
        Assert.Equal((0, 5001), (PaymentApi.Items(paymentsBeyond).Count, paymentsBeyond["total"]!.GetValue<int>()));
        Assert.Equal(20, PaymentApi.Items(await PaymentApi.ReadAsync(await host.SendAsync(HttpMethod.Get, "/invoices/payments", owner), HttpStatusCode.OK)).Count);

        foreach (var path in new[] { "/invoices/export", "/invoices/payments/export" })
        {
            var tooLarge = await BillingSeed.ProblemAsync(await host.SendAsync(HttpMethod.Get, path, owner), HttpStatusCode.Conflict, "export_too_large");
            Assert.Equal("Narrow the filters to export 5,000 rows or fewer.", tooLarge["title"]!.GetValue<string>());
        }

        var invoiceDay = new DateOnly(2020, 1, 1).AddDays(1);
        var paymentDay = new DateOnly(2020, 1, 2).AddDays(1);
        var narrowedInvoices = await host.SendAsync(HttpMethod.Get, $"/invoices/export?from={HubSeed.Iso(invoiceDay)}&to={HubSeed.Iso(invoiceDay)}", owner);
        var narrowedPayments = await host.SendAsync(HttpMethod.Get, $"/invoices/payments/export?from={HubSeed.Iso(paymentDay)}&to={HubSeed.Iso(paymentDay)}", owner);

        Assert.Equal(HttpStatusCode.OK, narrowedInvoices.StatusCode);
        Assert.Equal(HttpStatusCode.OK, narrowedPayments.StatusCode);
        Assert.Equal(2, (await narrowedInvoices.Content.ReadAsStringAsync()).Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Equal(2, (await narrowedPayments.Content.ReadAsStringAsync()).Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length);
    }
}
