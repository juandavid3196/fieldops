using System.Net;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.BillingReview;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.ServiceRequests;

namespace FieldOps.IntegrationTests.InvoiceDelivery;

/// <summary>Role matrix, branch scope and tenant isolation of the internal invoice endpoints (invoice-draft-delivery AC-01, AC-02).</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class InvoiceDeliveryAccessTests(CompanySettingsDatabaseFixture database)
{
    [Fact]
    public async Task Access_RolesBranchScopeAndTenantIsolation_Hold()
    {
        var world = await database.SeedWorldAsync();
        var foreign = await database.SeedWorldAsync();
        await using var host = RequestsHost.Create(database);
        var (owner, ownerMember) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var (operations, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OperationsManagerRoleId);
        var (viewer, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.ViewerRoleId);
        var (technician, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.TechnicianRoleId);
        var (accountingAll, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.AccountingRoleId);
        var (accountingA, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.AccountingRoleId, "Ann", "Acct", world.BranchA);
        var (dispatcherA, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.DispatcherRoleId, "Dee", "Spatch", world.BranchA);
        var (foreignOwner, _) = await host.SignInAsync(database, foreign.Org, CompanySettingsDatabaseFixture.OwnerRoleId);

        var invoiceA = await database.GenerateDraftAsync(host, owner, world, ownerMember.UserId, new JobSpec { Title = "Alpha job" });
        var invoiceB = await database.GenerateDraftAsync(host, owner, world, ownerMember.UserId, new JobSpec { Title = "Bravo job", Branch = world.BranchB });
        var voided = await database.GenerateDraftAsync(host, owner, world, ownerMember.UserId, new JobSpec { Title = "Void job" });
        await database.ExecuteAsync("UPDATE invoices SET status = 'void' WHERE id = @i", ("i", voided.Id));

        var stateA = await database.StateAsync(invoiceA.Id);
        var stateB = await database.StateAsync(invoiceB.Id);
        var stateVoid = await database.StateAsync(voided.Id);

        // Every action of the feature, for one invoice, with a body that would succeed for an authorized caller.
        async Task<HttpResponseMessage[]> Mutations(Guid id, string cookie) =>
        [
            await host.SendAsync(HttpMethod.Put, InvoiceApi.Path(id, "/draft"), cookie, InvoiceApi.Body(updatedAt: InvoiceApi.Stamp())),
            await host.SendAsync(HttpMethod.Post, InvoiceApi.Path(id, "/send"), cookie, InvoiceApi.Body(updatedAt: InvoiceApi.Stamp())),
            await host.SendAsync(HttpMethod.Post, InvoiceApi.Path(id, "/resend-email"), cookie, InvoiceApi.Resend(InvoiceApi.Stamp())),
        ];

        // Read roles read the detail and the PDF within no-store; branch-limited members see their branch.
        foreach (var cookie in new[] { owner, operations, viewer, dispatcherA, accountingA, accountingAll })
        {
            var detail = await host.SendAsync(HttpMethod.Get, InvoiceApi.Path(invoiceA.Id), cookie);
            var pdf = await host.SendAsync(HttpMethod.Get, InvoiceApi.Path(invoiceA.Id, "/pdf"), cookie);

            Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
            Assert.Equal("no-store", detail.Headers.CacheControl?.ToString());
            Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
            Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
            Assert.Equal("no-store", pdf.Headers.CacheControl?.ToString());
            Assert.Equal($"attachment; filename=\"{invoiceA.Number}.pdf\"", pdf.Content.Headers.ContentDisposition?.ToString());
            Assert.Equal("nosniff", pdf.Headers.GetValues("X-Content-Type-Options").Single());
        }

        // The technician and anonymous callers are refused before anything is read or written.
        foreach (var cookie in new string?[] { technician, null })
        {
            var expected = cookie is null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden;

            Assert.Equal(expected, (await host.SendAsync(HttpMethod.Get, InvoiceApi.Path(invoiceA.Id), cookie)).StatusCode);
            Assert.Equal(expected, (await host.SendAsync(HttpMethod.Get, InvoiceApi.Path(invoiceA.Id, "/pdf"), cookie)).StatusCode);

            if (cookie is not null)
            {
                Assert.All(await Mutations(invoiceA.Id, cookie), response => Assert.Equal(expected, response.StatusCode));
            }
        }

        // Only owner and accounting act; operations manager, viewer and dispatcher get 403 and nothing is written.
        foreach (var cookie in new[] { operations, viewer, dispatcherA })
        {
            Assert.All(await Mutations(invoiceA.Id, cookie), response => Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode));
        }

        Assert.Equal(stateA, await database.StateAsync(invoiceA.Id));

        foreach (var (cookie, expected) in new[] { (owner, true), (accountingA, true), (viewer, false), (dispatcherA, false) })
        {
            Assert.Equal(expected, (await InvoiceApi.DetailAsync(host, cookie, invoiceA.Id))["canAct"]!.GetValue<bool>());
        }

        // Branch scope: all-branch members read branch B; scoped accounting and dispatcher get the identical 404, also on actions.
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(HttpMethod.Get, InvoiceApi.Path(invoiceB.Id), accountingAll)).StatusCode);

        foreach (var cookie in new[] { accountingA, dispatcherA })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Get, InvoiceApi.Path(invoiceB.Id), cookie)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Get, InvoiceApi.Path(invoiceB.Id, "/pdf"), cookie)).StatusCode);
        }

        Assert.All(await Mutations(invoiceB.Id, accountingA), response => Assert.Equal(HttpStatusCode.NotFound, response.StatusCode));
        Assert.Equal(stateB, await database.StateAsync(invoiceB.Id));

        // Another organization, a random id and a void invoice: the same 404, no foreign data, no write.
        var missing = await host.SendAsync(HttpMethod.Get, InvoiceApi.Path(Guid.NewGuid()), owner);
        var cross = await host.SendAsync(HttpMethod.Get, InvoiceApi.Path(invoiceA.Id), foreignOwner);
        var voidRead = await host.SendAsync(HttpMethod.Get, InvoiceApi.Path(voided.Id), owner);

        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, cross.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, voidRead.StatusCode);
        Assert.Equal(await InvoiceApi.WithoutTraceAsync(missing), await InvoiceApi.WithoutTraceAsync(cross));
        Assert.Equal(await InvoiceApi.WithoutTraceAsync(missing), await InvoiceApi.WithoutTraceAsync(voidRead));
        Assert.DoesNotContain("Alpha job", await cross.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Get, InvoiceApi.Path(invoiceA.Id, "/pdf"), foreignOwner)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Get, InvoiceApi.Path(voided.Id, "/pdf"), owner)).StatusCode);
        Assert.All(await Mutations(invoiceA.Id, foreignOwner), response => Assert.Equal(HttpStatusCode.NotFound, response.StatusCode));
        Assert.All(await Mutations(voided.Id, owner), response => Assert.Equal(HttpStatusCode.NotFound, response.StatusCode));
        Assert.All(await Mutations(Guid.NewGuid(), owner), response => Assert.Equal(HttpStatusCode.NotFound, response.StatusCode));

        Assert.Equal(stateA, await database.StateAsync(invoiceA.Id));
        Assert.Equal(stateVoid, await database.StateAsync(voided.Id));
        Assert.Empty(await database.AuditActionsAsync(invoiceA.Id));
        Assert.Equal(0, await database.CountAsync("invoice_access_tokens", "organization_id IN (@a, @b)", ("a", world.Org), ("b", foreign.Org)));
    }
}
