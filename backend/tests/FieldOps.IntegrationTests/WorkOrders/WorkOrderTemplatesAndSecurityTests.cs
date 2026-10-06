using System.Net;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.PublicRequests;
using FieldOps.IntegrationTests.Quotes;
using FieldOps.IntegrationTests.ServiceRequests;

namespace FieldOps.IntegrationTests.WorkOrders;

/// <summary>Checklist templates, roles, branch scope and tenant isolation of the work order creation (create-work-order AC-01, AC-16, AC-20 to AC-22).</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class WorkOrderTemplatesAndSecurityTests(CompanySettingsDatabaseFixture database)
{
    private static JsonObject Template(string name, Guid? category = null, params string[] labels) =>
        new()
        {
            ["name"] = name,
            ["serviceCategoryId"] = category,
            ["items"] = new JsonArray(labels.Select(label => (JsonNode?)new JsonObject { ["label"] = label }).ToArray()),
        };

    // AC-16: list, create, case-insensitive duplicates, item and category validation, audit without labels, tenant scoping.
    [Fact]
    public async Task Templates_ListCreateAndRejectDuplicatesIgnoringCase_WithAuditWithoutLabels()
    {
        var world = await database.SeedQuoteWorldAsync();
        var foreign = await database.SeedQuoteWorldAsync();
        await using var host = RequestsHost.Create(database);
        var (owner, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var (foreignOwner, _) = await host.SignInAsync(database, foreign.Org, CompanySettingsDatabaseFixture.OwnerRoleId);

        Assert.Empty((await WorkOrdersApi.ReadOkAsync(await host.SendAsync(HttpMethod.Get, "/checklist-templates", owner))).AsArray());

        var created = await WorkOrdersApi.ReadOkAsync(
            await host.SendAsync(HttpMethod.Post, "/checklist-templates", owner, Template("Drain checklist", world.Category, "Inspect line", "Test flow")),
            HttpStatusCode.Created);
        var plain = await WorkOrdersApi.ReadOkAsync(
            await host.SendAsync(HttpMethod.Post, "/checklist-templates", owner, Template("Arrival basics", null, "Greet customer")),
            HttpStatusCode.Created);
        Assert.Equal(world.Category, created["serviceCategoryId"]!.GetValue<Guid>());
        Assert.Null(plain["serviceCategoryId"]);
        Assert.Equal(["Inspect line", "Test flow"], created["items"]!.AsArray().Select(item => item!["label"]!.GetValue<string>()).ToArray());

        var listed = (await WorkOrdersApi.ReadOkAsync(await host.SendAsync(HttpMethod.Get, "/checklist-templates", owner))).AsArray();
        Assert.Equal(["Arrival basics", "Drain checklist"], listed.Select(item => item!["name"]!.GetValue<string>()).ToArray());

        // Another organization never sees them and may reuse the name.
        Assert.Empty((await WorkOrdersApi.ReadOkAsync(await host.SendAsync(HttpMethod.Get, "/checklist-templates", foreignOwner))).AsArray());
        Assert.Equal(
            HttpStatusCode.Created,
            (await host.SendAsync(HttpMethod.Post, "/checklist-templates", foreignOwner, Template("Drain checklist", null, "Other task"))).StatusCode);

        var inactive = await database.SeedCategoryAsync(world.Org, "Retired", active: false);
        var cases = new (string Key, JsonObject Body)[]
        {
            ("name", Template("  drain CHECKLIST ", null, "Task")),
            ("name", Template("", null, "Task")),
            ("name", Template(new string('n', 121), null, "Task")),
            ("items", Template("No items", null)),
            ("items", Template("Blank item", null, " ")),
            ("items", Template("Long item", null, new string('i', 241))),
            ("items", Template("Many items", null, Enumerable.Range(0, 51).Select(i => $"Task {i}").ToArray())),
            ("serviceCategoryId", Template("Foreign category", foreign.Category, "Task")),
            ("serviceCategoryId", Template("Inactive category", inactive, "Task")),
        };

        foreach (var (key, body) in cases)
        {
            await WorkOrdersApi.AssertInvalidAsync(await host.SendAsync(HttpMethod.Post, "/checklist-templates", owner, body), key);
        }

        Assert.Equal(2L, await database.ScalarAsync<long>("SELECT COUNT(*) FROM checklist_templates WHERE organization_id = @o", ("o", world.Org)));

        // BR-19: the audit row carries the name, the category and the item count, never the labels.
        var templateId = created["id"]!.GetValue<Guid>();
        Assert.Equal(1L, await database.ScalarAsync<long>("SELECT COUNT(*) FROM audit_logs WHERE entity_id = @t AND action = 'checklist_template.created'", ("t", templateId)));
        var audit = await database.ScalarAsync<string>(
            "SELECT entity_type || ' ' || COALESCE(branch_id::text, 'no-branch') || ' ' || after_data::text || ' ' || metadata::text FROM audit_logs WHERE entity_id = @t", ("t", templateId));
        Assert.StartsWith("checklist_template no-branch", audit, StringComparison.Ordinal);
        Assert.Contains("Drain checklist", audit, StringComparison.Ordinal);
        Assert.Contains("\"itemCount\":2", audit.Replace(" ", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.DoesNotContain("Inspect line", audit, StringComparison.Ordinal);
    }

    // AC-01, AC-20, AC-21, AC-22: roles, branch scope, tenant isolation, list scope and order, and the quote page flags.
    [Fact]
    public async Task Endpoints_EnforceRolesBranchScopeAndOrganization_AndExposeWorkOrderStateOnTheQuote()
    {
        var world = await database.SeedQuoteWorldAsync();
        var foreign = await database.SeedQuoteWorldAsync();
        await using var host = RequestsHost.Create(database);
        var (owner, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var (operations, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OperationsManagerRoleId);
        var (dispatcherA, _) = await host.SignInAsync(
            database, world.Org, CompanySettingsDatabaseFixture.DispatcherRoleId, "Dina", "Dispatcher", world.BranchA);
        var (dispatcherB, _) = await host.SignInAsync(
            database, world.Org, CompanySettingsDatabaseFixture.DispatcherRoleId, "Bea", "Bravo", world.BranchB);
        var (viewer, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.ViewerRoleId);
        var (accounting, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.AccountingRoleId);
        var (technician, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.TechnicianRoleId);
        var (foreignOwner, _) = await host.SignInAsync(database, foreign.Org, CompanySettingsDatabaseFixture.OwnerRoleId);

        var quoteA = await WorkOrdersApi.ApproveAsync(database, host, world, owner);
        var quoteB = await WorkOrdersApi.ApproveAsync(database, host, world, owner, branch: world.BranchB);
        var fresh = await WorkOrdersApi.ApproveAsync(database, host, world, owner);
        var bodyA = WorkOrdersApi.Body(world, quoteA);
        var bodyB = WorkOrdersApi.Body(world, quoteB, branch: world.BranchB);

        // Manage roles open the editor of the quote in scope; Viewer, Accounting and Technician are 403 on every Manage endpoint.
        foreach (var manager in new[] { owner, operations, dispatcherA })
        {
            Assert.Equal(HttpStatusCode.OK, (await WorkOrdersApi.EditorAsync(host, manager, quoteA.QuoteId)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(HttpMethod.Get, "/checklist-templates", manager)).StatusCode);
        }

        foreach (var denied in new[] { viewer, accounting, technician })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await WorkOrdersApi.EditorAsync(host, denied, quoteA.QuoteId)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await WorkOrdersApi.DraftAsync(host, denied, quoteA.QuoteId, bodyA)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await WorkOrdersApi.CreateAsync(host, denied, quoteA.QuoteId, bodyA)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(HttpMethod.Get, "/checklist-templates", denied)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(HttpMethod.Post, "/checklist-templates", denied, Template("Nope", null, "Task"))).StatusCode);
        }

        // A foreign organization and the Dispatcher of another branch get the same 404 as a missing quote, on every operation.
        foreach (var outsider in new[] { foreignOwner, dispatcherB })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await WorkOrdersApi.EditorAsync(host, outsider, quoteA.QuoteId)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await WorkOrdersApi.DraftAsync(host, outsider, quoteA.QuoteId, bodyA)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await WorkOrdersApi.CreateAsync(host, outsider, quoteA.QuoteId, bodyA)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.NotFound, (await WorkOrdersApi.EditorAsync(host, dispatcherA, quoteB.QuoteId)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await WorkOrdersApi.EditorAsync(host, owner, Guid.NewGuid())).StatusCode);
        Assert.Equal(0L, await database.OrderCountAsync(world.Org));

        // AC-01: the quote page flags, before any order, with a draft and once created.
        async Task<(string? Status, bool Manage)> Flags(string cookie, Guid quoteId)
        {
            var quote = await QuotesApi.GetAsync(host, cookie, quoteId);

            return (quote["workOrder"]?["status"]?.GetValue<string>(), quote["canManageWorkOrders"]!.GetValue<bool>());
        }

        Assert.Equal(((string?)null, true), await Flags(owner, fresh.QuoteId));
        Assert.Equal(((string?)null, true), await Flags(operations, fresh.QuoteId));
        Assert.Equal(((string?)null, true), await Flags(dispatcherA, fresh.QuoteId));
        Assert.Equal(((string?)null, false), await Flags(viewer, fresh.QuoteId));
        Assert.Equal(((string?)null, false), await Flags(accounting, fresh.QuoteId));
        await WorkOrdersApi.ReadOkAsync(await WorkOrdersApi.DraftAsync(host, owner, fresh.QuoteId, WorkOrdersApi.Body(world, fresh)), HttpStatusCode.Created);
        Assert.Equal(("draft", false), await Flags(viewer, fresh.QuoteId));

        // A Dispatcher creates the order of branch A; the Owner creates the later one of branch B.
        var orderA = (await WorkOrdersApi.ReadOkAsync(await WorkOrdersApi.CreateAsync(host, dispatcherA, quoteA.QuoteId, bodyA), HttpStatusCode.Created))["id"]!.GetValue<Guid>();
        var orderB = (await WorkOrdersApi.ReadOkAsync(await WorkOrdersApi.CreateAsync(host, owner, quoteB.QuoteId, bodyB), HttpStatusCode.Created))["id"]!.GetValue<Guid>();
        Assert.Equal(("ready_to_schedule", false), await Flags(viewer, quoteA.QuoteId));
        Assert.Equal(("ready_to_schedule", true), await Flags(dispatcherA, quoteA.QuoteId));

        // Jobs: Read roles see the detail (Viewer without manage); Accounting and Technician are 403; outsiders are 404.
        foreach (var (cookie, canManage) in new[] { (owner, true), (operations, true), (dispatcherA, true), (viewer, false) })
        {
            var detail = await WorkOrdersApi.ReadOkAsync(await host.SendAsync(HttpMethod.Get, $"/work-orders/{orderA}", cookie));
            Assert.Equal(canManage, detail["canManage"]!.GetValue<bool>());
            Assert.Equal(orderA, detail["id"]!.GetValue<Guid>());
        }

        foreach (var denied in new[] { accounting, technician })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(HttpMethod.Get, $"/work-orders/{orderA}", denied)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(HttpMethod.Get, "/work-orders", denied)).StatusCode);
        }

        foreach (var outsider in new[] { foreignOwner, dispatcherB })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Get, $"/work-orders/{orderA}", outsider)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Get, $"/work-orders/{orderB}", dispatcherA)).StatusCode);

        // The list: every branch for the Owner and Viewer, newest first; scoped for Dispatchers; empty for another organization.
        foreach (var cookie in new[] { owner, viewer })
        {
            var page = await WorkOrdersApi.ReadOkAsync(await host.SendAsync(HttpMethod.Get, "/work-orders", cookie));
            Assert.Equal(3, page["total"]!.GetValue<int>());
            Assert.Equal(
                [orderB.ToString(), orderA.ToString()],
                page["items"]!.AsArray().Select(item => item!["id"]!.GetValue<string>()).Where(id => id == orderA.ToString() || id == orderB.ToString()).ToArray());
            Assert.Equal("draft", page["items"]!.AsArray().Single(item => item!["status"]!.GetValue<string>() == "draft")!["status"]!.GetValue<string>());
        }

        var scoped = await WorkOrdersApi.ReadOkAsync(await host.SendAsync(HttpMethod.Get, "/work-orders", dispatcherB));
        Assert.Equal([orderB.ToString()], scoped["items"]!.AsArray().Select(item => item!["id"]!.GetValue<string>()).ToArray());
        var item = scoped["items"]![0]!;
        Assert.Equal(("Carla Customer", "Bravo Branch", "normal", "ready_to_schedule"), (item["customerName"]!.GetValue<string>(), item["branchName"]!.GetValue<string>(), item["priority"]!.GetValue<string>(), item["status"]!.GetValue<string>()));
        Assert.Equal(0, (await WorkOrdersApi.ReadOkAsync(await host.SendAsync(HttpMethod.Get, "/work-orders", foreignOwner)))["total"]!.GetValue<int>());
        Assert.Equal(
            [HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.OK],
            (await Task.WhenAll(new[] { "?page=0", "?pageSize=0", "?pageSize=101", "?page=1&pageSize=100" }
                .Select(query => host.SendAsync(HttpMethod.Get, $"/work-orders{query}", owner)))).Select(response => response.StatusCode).ToArray());
    }
}
