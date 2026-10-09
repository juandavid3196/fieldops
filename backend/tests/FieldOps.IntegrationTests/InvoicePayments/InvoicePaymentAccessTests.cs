using System.Net;
using FieldOps.IntegrationTests.BillingReview;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.ServiceRequests;

namespace FieldOps.IntegrationTests.InvoicePayments;

/// <summary>Role matrix, branch scope and tenant isolation of the hub and the record endpoint (invoices-payments-management AC-01, AC-02).</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class InvoicePaymentAccessTests(CompanySettingsDatabaseFixture database)
{
    [Fact]
    public async Task Access_RolesBranchScopeAndTenantIsolation_Hold()
    {
        var world = await database.SeedWorldAsync(HubSeed.Timezone);
        var foreign = await database.SeedWorldAsync(HubSeed.Timezone);
        await using var host = RequestsHost.Create(database);
        var (owner, ownerMember) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var (operations, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OperationsManagerRoleId);
        var (viewer, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.ViewerRoleId);
        var (technician, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.TechnicianRoleId);
        var (dispatcherAll, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.DispatcherRoleId);
        var (dispatcherA, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.DispatcherRoleId, "Dee", "Spatch", world.BranchA);
        var (accountingAll, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.AccountingRoleId);
        var (accountingA, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.AccountingRoleId, "Ann", "Acct", world.BranchA);
        var (foreignOwner, foreignMember) = await host.SignInAsync(database, foreign.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var by = ownerMember.UserId;

        var inA = await database.SeedInvoiceAsync(world, by, new InvoiceSpec { Total = 100m });
        var inB = await database.SeedInvoiceAsync(world, by, new InvoiceSpec { Total = 100m, Branch = world.BranchB });
        var voided = await database.SeedInvoiceAsync(world, by, new InvoiceSpec { Status = "void", DueDays = null });
        var foreignInvoice = await database.SeedInvoiceAsync(foreign, foreignMember.UserId, new InvoiceSpec { Total = 100m });
        await database.SeedPaymentAsync(world.Org, inA, 5m, -1, "cash", null, by, by);
        await database.SeedPaymentAsync(world.Org, inB, 6m, -1, "cash", null, by, by);

        var reads = new[]
        {
            "/invoices/options", "/invoices/customers", "/invoices/overview", "/invoices", "/invoices/export", "/invoices/payments", "/invoices/payments/export",
        };

        // AC-01: every read role reads every endpoint, no-store; technician and anonymous callers are refused before any read.
        foreach (var cookie in new[] { owner, operations, viewer, dispatcherAll, dispatcherA, accountingAll, accountingA })
        {
            foreach (var path in reads)
            {
                var read = await host.SendAsync(HttpMethod.Get, path, cookie);

                Assert.Equal(HttpStatusCode.OK, read.StatusCode);
                Assert.Equal("no-store", read.Headers.CacheControl?.ToString());
            }
        }

        foreach (var path in reads)
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(HttpMethod.Get, path, technician)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await host.SendAsync(HttpMethod.Get, path, null)).StatusCode);
        }

        // Only owner and accounting record payments; the others get 403 before the invoice is looked up, with no write.
        var untouched = await database.PaymentStateAsync(world.Org);

        foreach (var cookie in new[] { operations, viewer, dispatcherAll, dispatcherA, technician })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(HttpMethod.Post, PaymentApi.Path(inA.Id), cookie, Valid(by, await database.StampAsync(inA.Id)))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(HttpMethod.Post, PaymentApi.Path(Guid.NewGuid()), cookie, Valid(by, null))).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await host.SendAsync(HttpMethod.Post, PaymentApi.Path(inA.Id), null, Valid(by, null))).StatusCode);
        Assert.Equal(untouched, await database.PaymentStateAsync(world.Org));
        Assert.Empty(host.Sender.Messages);

        foreach (var (cookie, expected) in new[] { (owner, true), (accountingA, true), (viewer, false), (dispatcherA, false), (operations, false) })
        {
            var options = await PaymentApi.ReadAsync(await host.SendAsync(HttpMethod.Get, "/invoices/options", cookie), HttpStatusCode.OK);
            var rows = PaymentApi.Items(await PaymentApi.ReadAsync(await host.SendAsync(HttpMethod.Get, "/invoices", cookie), HttpStatusCode.OK));

            Assert.Equal(expected, options["canAct"]!.GetValue<bool>());
            Assert.Equal(expected, options["members"]!.AsArray().Count > 0);
            Assert.Contains(rows, row => row!["storedStatus"]!.GetValue<string>() == "sent");
            Assert.All(rows, row => Assert.Equal(expected, row!["canRecordPayment"]!.GetValue<bool>()));
        }

        // Branch scope: scoped dispatcher and accounting see branch A only; all-branch members see both branches.
        var both = new[] { inA.Id.ToString(), inB.Id.ToString() }.Order().ToArray();

        foreach (var cookie in new[] { owner, operations, viewer, dispatcherAll, accountingAll })
        {
            Assert.Equal(both, PaymentApi.Texts(await PaymentApi.ReadAsync(await host.SendAsync(HttpMethod.Get, "/invoices", cookie), HttpStatusCode.OK), "id").Order().ToArray());
        }

        foreach (var cookie in new[] { dispatcherA, accountingA })
        {
            var listed = await PaymentApi.ReadAsync(await host.SendAsync(HttpMethod.Get, "/invoices", cookie), HttpStatusCode.OK);
            var payments = await PaymentApi.ReadAsync(await host.SendAsync(HttpMethod.Get, "/invoices/payments", cookie), HttpStatusCode.OK);
            var options = await PaymentApi.ReadAsync(await host.SendAsync(HttpMethod.Get, "/invoices/options", cookie), HttpStatusCode.OK);

            Assert.Equal([inA.Id.ToString()], PaymentApi.Texts(listed, "id"));
            Assert.Equal([inA.Display], PaymentApi.Texts(payments, "invoiceNumber"));
            Assert.Equal([world.BranchA], options["branches"]!.AsArray().Select(branch => branch!["id"]!.GetValue<Guid>()).ToArray());
            Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Get, $"/invoices?branchId={world.BranchB}", cookie)).StatusCode);
        }

        // Accounting records within its scope and across branches when it has them all.
        Assert.Equal(HttpStatusCode.Created, (await host.SendAsync(HttpMethod.Post, PaymentApi.Path(inA.Id), accountingA, Valid(by, await database.StampAsync(inA.Id)))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await host.SendAsync(HttpMethod.Post, PaymentApi.Path(inB.Id), accountingAll, Valid(by, await database.StampAsync(inB.Id)))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await host.SendAsync(HttpMethod.Post, PaymentApi.Path(inA.Id), owner, Valid(by, await database.StampAsync(inA.Id)))).StatusCode);

        // AC-02, payment path: out-of-scope, void, another organization and random invoices are one identical 404 with no write.
        var before = await database.PaymentStateAsync(world.Org);
        var foreignBefore = await database.PaymentStateAsync(foreign.Org);
        var attempts = new[]
        {
            await host.SendAsync(HttpMethod.Post, PaymentApi.Path(inB.Id), accountingA, Valid(by, await database.StampAsync(inB.Id))),
            await host.SendAsync(HttpMethod.Post, PaymentApi.Path(voided.Id), owner, Valid(by, await database.StampAsync(voided.Id))),
            await host.SendAsync(HttpMethod.Post, PaymentApi.Path(foreignInvoice.Id), owner, Valid(by, await database.StampAsync(foreignInvoice.Id))),
            await host.SendAsync(HttpMethod.Post, PaymentApi.Path(inA.Id), foreignOwner, Valid(foreignMember.UserId, await database.StampAsync(inA.Id))),
            await host.SendAsync(HttpMethod.Post, PaymentApi.Path(Guid.NewGuid()), owner, Valid(by, null)),
        };
        var bodies = new List<string>();

        foreach (var attempt in attempts)
        {
            Assert.Equal(HttpStatusCode.NotFound, attempt.StatusCode);
            bodies.Add(await PaymentApi.WithoutTraceAsync(attempt));
        }

        Assert.Single(bodies.Distinct());
        Assert.Equal(before, await database.PaymentStateAsync(world.Org));
        Assert.Equal(foreignBefore, await database.PaymentStateAsync(foreign.Org));
        Assert.Empty(host.Sender.Messages);

        // AC-02, filters: foreign, out-of-scope and unknown branch or customer ids are the same 404 on every read.
        var foreignCustomer = foreign.Customer;
        var filters = new[]
        {
            $"branchId={foreign.BranchA}",
            $"branchId={Guid.NewGuid()}",
        };
        var invoiceOnly = new[] { $"customerId={foreignCustomer}", $"customerId={Guid.NewGuid()}" };
        var paths = new[] { "/invoices", "/invoices/export", "/invoices/payments", "/invoices/payments/export", "/invoices/overview" };
        var notFound = new List<string>();

        foreach (var path in paths)
        {
            foreach (var filter in filters)
            {
                var response = await host.SendAsync(HttpMethod.Get, $"{path}?{filter}", owner);

                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
                notFound.Add(await PaymentApi.WithoutTraceAsync(response));
            }
        }

        foreach (var path in new[] { "/invoices", "/invoices/export" })
        {
            foreach (var filter in invoiceOnly)
            {
                var response = await host.SendAsync(HttpMethod.Get, $"{path}?{filter}", owner);

                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
                notFound.Add(await PaymentApi.WithoutTraceAsync(response));
            }
        }

        Assert.Single(notFound.Distinct());
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Get, $"/invoices/payments?branchId={world.BranchB}", dispatcherA)).StatusCode);

        // Foreign data never shows up for the owner of this organization.
        var everything = await (await host.SendAsync(HttpMethod.Get, "/invoices?page=1", owner)).Content.ReadAsStringAsync();
        Assert.DoesNotContain(foreignInvoice.Id.ToString(), everything, StringComparison.Ordinal);
        Assert.DoesNotContain(voided.Id.ToString(), everything, StringComparison.Ordinal);
    }

    private static System.Text.Json.Nodes.JsonObject Valid(Guid receivedBy, string? updatedAt) =>
        PaymentApi.Body(amount: 5m, receivedBy: receivedBy, updatedAt: updatedAt);
}
