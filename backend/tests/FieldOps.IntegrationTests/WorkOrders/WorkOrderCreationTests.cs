using System.Net;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.Quotes;
using FieldOps.IntegrationTests.ServiceRequests;
using FieldOps.IntegrationTests.Team;

namespace FieldOps.IntegrationTests.WorkOrders;

/// <summary>Transactional create, idempotency, concurrency and rollback of the work order creation (create-work-order AC-09 to AC-11, AC-13, AC-14).</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class WorkOrderCreationTests(CompanySettingsDatabaseFixture database)
{
    // AC-09, AC-14: one transaction writes the order, the request transition, visit #1, the checklist copy, the materials and the audit.
    [Fact]
    public async Task Create_CommitsOrderRequestVisitChecklistMaterialsAndAuditTogether_WithoutTouchingQuotesOrNotifications()
    {
        var world = await database.SeedQuoteWorldAsync();
        await using var host = RequestsHost.Create(database);
        var (owner, ownerMember) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var fromDraft = await WorkOrdersApi.ApproveAsync(database, host, world, owner);
        var direct = await WorkOrdersApi.ApproveAsync(database, host, world, owner);
        var skill = await database.SeedSkillAsync(world.Org, "Welding");
        var quoteDigest = await database.QuoteDigestAsync(world.Org);
        var firstNumber = await database.NextNumberAsync(world.Org);
        var body = WorkOrdersApi.Body(world, fromDraft, skill);

        var draft = await WorkOrdersApi.ReadOkAsync(await WorkOrdersApi.DraftAsync(host, owner, fromDraft.QuoteId, body), HttpStatusCode.Created);
        var created = await WorkOrdersApi.ReadOkAsync(
            await WorkOrdersApi.CreateAsync(
                host,
                owner,
                fromDraft.QuoteId,
                body.Edit(change =>
                {
                    change["updatedAt"] = draft["workOrder"]!["updatedAt"]!.GetValue<string>();
                    change["title"] = "Final title";
                    change["materials"]![0]!.AsObject()["unitPrice"] = 999;
                    change["unitPrice"] = 999;
                    change["organizationId"] = Guid.NewGuid();
                })),
            HttpStatusCode.Created);
        var orderId = created["id"]!.GetValue<Guid>();

        Assert.Equal(draft["workOrder"]!["id"]!.GetValue<Guid>(), orderId);
        Assert.Equal($"WO-{firstNumber}", created["displayNumber"]!.GetValue<string>());
        Assert.Equal(
            "ready_to_schedule|Final title",
            await database.ScalarAsync<string>("SELECT status::text || '|' || title FROM work_orders WHERE id = @w", ("w", orderId)));
        Assert.Equal("converted", await database.StatusOfAsync(fromDraft.RequestId));
        Assert.Equal(
            1L,
            await database.ScalarAsync<long>(
                "SELECT COUNT(*) FROM request_status_history WHERE request_id = @r AND from_status = 'quoted' AND to_status = 'converted' AND changed_by_user_id = @u AND reason IS NULL",
                ("r", fromDraft.RequestId),
                ("u", ownerMember.UserId)));

        var visit = await database.ScalarAsync<Guid>("SELECT id FROM visits WHERE work_order_id = @w", ("w", orderId));
        Assert.Equal(
            "1|unscheduled|true",
            await database.ScalarAsync<string>(
                "SELECT visit_number || '|' || status::text || '|' || (scheduled_start IS NULL AND scheduled_end IS NULL) FROM visits WHERE id = @v", ("v", visit)));
        Assert.Equal(
            1L,
            await database.ScalarAsync<long>(
                "SELECT COUNT(*) FROM visit_status_history WHERE visit_id = @v AND from_status IS NULL AND to_status = 'unscheduled'", ("v", visit)));
        Assert.Equal(
            "Inspect line|Clear blockage|Test flow",
            await database.ScalarAsync<string>("SELECT string_agg(label, '|' ORDER BY sort_order) FROM visit_checklist_items WHERE visit_id = @v", ("v", visit)));
        Assert.Equal(
            await database.QueryGuidsAsync("SELECT id FROM work_order_checklist_templates WHERE work_order_id = @w ORDER BY sort_order", ("w", orderId)),
            await database.QueryGuidsAsync("SELECT template_item_id FROM visit_checklist_items WHERE visit_id = @v ORDER BY sort_order", ("v", visit)));
        Assert.Equal(
            1L,
            await database.ScalarAsync<long>("SELECT COUNT(*) FROM work_order_planned_materials WHERE work_order_id = @w AND quote_line_id = @l", ("w", orderId), ("l", fromDraft.ProductLine)));
        Assert.Equal(
            "0|0|0",
            await database.ScalarAsync<string>(
                "SELECT (SELECT COUNT(*) FROM visit_materials WHERE visit_id = @v) || '|' || (SELECT COUNT(*) FROM visit_assignments WHERE visit_id = @v) || '|' || (SELECT COUNT(*) FROM notifications WHERE organization_id = @o)",
                ("v", visit),
                ("o", world.Org)));

        // BR-19: one audit row with ids and counts only.
        Assert.Equal(1L, await database.ScalarAsync<long>("SELECT COUNT(*) FROM audit_logs WHERE entity_id = @w AND action = 'work_order.created'", ("w", orderId)));
        var audit = await database.ScalarAsync<string>(
            "SELECT entity_type || ' ' || actor_user_id || ' ' || branch_id || ' ' || after_data::text || ' ' || metadata::text FROM audit_logs WHERE entity_id = @w AND action = 'work_order.created'", ("w", orderId));
        Assert.Contains($"work_order {ownerMember.UserId} {world.BranchA}", audit, StringComparison.Ordinal);
        Assert.Contains("ready_to_schedule", audit, StringComparison.Ordinal);
        Assert.Contains($"\"visitId\":\"{visit}\"", audit.Replace(" ", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("\"taskCount\":3", audit.Replace(" ", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("\"materialCount\":1", audit.Replace(" ", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        foreach (var secret in new[] { "Carla", "carla@example.com", "5551234567", "Call before arriving", "PVC pipe", "Gate code", "Inspect line", "Final title" })
        {
            Assert.DoesNotContain(secret, audit, StringComparison.OrdinalIgnoreCase);
        }

        // The same transaction without a saved draft; prices in the body are ignored and the quotes stay byte-identical.
        var second = await WorkOrdersApi.ReadOkAsync(await WorkOrdersApi.CreateAsync(host, owner, direct.QuoteId, WorkOrdersApi.Body(world, direct)), HttpStatusCode.Created);
        Assert.Equal($"WO-{firstNumber + 1}", second["displayNumber"]!.GetValue<string>());
        Assert.Equal("converted", await database.StatusOfAsync(direct.RequestId));
        Assert.Equal(quoteDigest, await database.QuoteDigestAsync(world.Org));
        Assert.Equal(firstNumber + 2, await database.NextNumberAsync(world.Org));

        // The job detail and the quote page read the frozen approval total and the order.
        var detail = await WorkOrdersApi.ReadOkAsync(await host.SendAsync(HttpMethod.Get, $"/work-orders/{orderId}", owner));

        Assert.Equal("ready_to_schedule", detail["status"]!.GetValue<string>());
        Assert.Equal(
            await database.ScalarAsync<decimal>("SELECT total FROM quote_responses WHERE quote_version_id = @v", ("v", fromDraft.VersionId)),
            detail["quote"]!["approvedTotal"]!.GetValue<decimal>());
        Assert.Equal(
            [(1, "unscheduled")],
            detail["visits"]!.AsArray().Select(item => (item!["visitNumber"]!.GetValue<int>(), item["status"]!.GetValue<string>())).ToArray());
        var quote = await host.SendAsync(HttpMethod.Get, $"/quotes/{fromDraft.QuoteId}", owner);
        var quoteBody = await WorkOrdersApi.ReadOkAsync(quote);
        Assert.Equal(orderId, quoteBody["workOrder"]!["id"]!.GetValue<Guid>());
        Assert.Equal("ready_to_schedule", quoteBody["workOrder"]!["status"]!.GetValue<string>());
        Assert.True(quoteBody["canManageWorkOrders"]!.GetValue<bool>());
    }

    // AC-10: a repeat is a 200 with the same id and no change, three concurrent creates yield one order, and a draft save after creation conflicts.
    [Fact]
    public async Task Create_IsIdempotentUnderRepeatsAndConcurrency_AndConcurrentFirstDraftsConflict()
    {
        var world = await database.SeedQuoteWorldAsync();
        await using var host = RequestsHost.Create(database);
        var (owner, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var racing = await WorkOrdersApi.ApproveAsync(database, host, world, owner);
        var sequential = await WorkOrdersApi.ApproveAsync(database, host, world, owner);
        var drafts = await WorkOrdersApi.ApproveAsync(database, host, world, owner);

        // Three concurrent creates on a fresh quote serialize on the quote lock.
        var firstNumber = await database.NextNumberAsync(world.Org);
        var body = WorkOrdersApi.Body(world, racing);
        var responses = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => WorkOrdersApi.CreateAsync(host, owner, racing.QuoteId, body)));
        Assert.Equal(
            [HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.Created],
            responses.Select(response => response.StatusCode).Order().ToArray());
        var ids = (await Task.WhenAll(responses.Select(RequestsHost.ReadAsync))).Select(item => item["id"]!.GetValue<Guid>()).Distinct().ToArray();
        Assert.Single(ids);
        Assert.Equal(
            "1|1|1|converted",
            await database.ScalarAsync<string>(
                "SELECT (SELECT COUNT(*) FROM work_orders WHERE organization_id = @o) || '|' || (SELECT COUNT(*) FROM visits WHERE organization_id = @o) || '|' || (SELECT COUNT(*) FROM audit_logs WHERE organization_id = @o AND action = 'work_order.created') || '|' || (SELECT status::text FROM service_requests WHERE id = @r)",
                ("o", world.Org),
                ("r", racing.RequestId)));
        Assert.Equal(firstNumber + 1, await database.NextNumberAsync(world.Org));

        // A sequential repeat with another body changes nothing and answers the same order.
        var first = await WorkOrdersApi.ReadOkAsync(await WorkOrdersApi.CreateAsync(host, owner, sequential.QuoteId, WorkOrdersApi.Body(world, sequential)), HttpStatusCode.Created);
        var snapshot = await database.SideEffectsAsync(world.Org, sequential.RequestId);
        var repeat = await WorkOrdersApi.ReadOkAsync(
            await WorkOrdersApi.CreateAsync(host, owner, sequential.QuoteId, WorkOrdersApi.Body(world, sequential).Edit(change => change["title"] = "Another body")));
        Assert.Equal(first.ToJsonString(), repeat.ToJsonString());
        Assert.Equal(snapshot, await database.SideEffectsAsync(world.Org, sequential.RequestId));
        Assert.Equal(
            "Replace drain line",
            await database.ScalarAsync<string>("SELECT title FROM work_orders WHERE id = @w", ("w", first["id"]!.GetValue<Guid>())));

        // An invalid body does not matter once the order exists, and a draft save after creation is refused.
        Assert.Equal(
            HttpStatusCode.OK,
            (await WorkOrdersApi.CreateAsync(host, owner, sequential.QuoteId, new JsonObject { ["title"] = "" })).StatusCode);
        await WorkOrdersApi.AssertConflictAsync(
            await WorkOrdersApi.DraftAsync(host, owner, sequential.QuoteId, WorkOrdersApi.Body(world, sequential)), "work_order_created");

        // Two first drafts race: one inserts, the other is a changed conflict, and one number is consumed.
        var draftNumber = await database.NextNumberAsync(world.Org);
        var draftBody = WorkOrdersApi.Body(world, drafts);
        var attempts = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => WorkOrdersApi.DraftAsync(host, owner, drafts.QuoteId, draftBody)));
        Assert.Equal([HttpStatusCode.Created, HttpStatusCode.Conflict], attempts.Select(response => response.StatusCode).Order().ToArray());
        await WorkOrdersApi.AssertConflictAsync(attempts.Single(response => response.StatusCode == HttpStatusCode.Conflict), "work_order_changed");
        Assert.Equal(draftNumber + 1, await database.NextNumberAsync(world.Org));
        Assert.Equal(
            1L,
            await database.ScalarAsync<long>(
                "SELECT COUNT(*) FROM work_orders w JOIN quote_versions v ON v.id = w.quote_version_id WHERE v.quote_id = @q", ("q", drafts.QuoteId)));
    }

    // AC-11, AC-13: a version mismatch and a request that left quoted refuse create and roll every write back.
    [Fact]
    public async Task Create_RefusesVersionMismatchAndChangedRequest_WithFullRollback()
    {
        var world = await database.SeedQuoteWorldAsync();
        await using var host = RequestsHost.Create(database);
        var (owner, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var changed = await WorkOrdersApi.ApproveAsync(database, host, world, owner);
        var mismatch = await WorkOrdersApi.ApproveAsync(database, host, world, owner);

        // AC-13: the draft stays untouched when the request is no longer quoted at create.
        var draft = await WorkOrdersApi.ReadOkAsync(
            await WorkOrdersApi.DraftAsync(host, owner, changed.QuoteId, WorkOrdersApi.Body(world, changed)), HttpStatusCode.Created);
        var orderId = draft["workOrder"]!["id"]!.GetValue<Guid>();
        await database.ExecuteAsync("UPDATE service_requests SET status = 'needs_review' WHERE id = @r", ("r", changed.RequestId));
        var baseline = await database.SideEffectsAsync(world.Org, changed.RequestId);
        var storedToken = await database.ScalarAsync<DateTimeOffset>("SELECT updated_at FROM work_orders WHERE id = @w", ("w", orderId));

        await WorkOrdersApi.AssertConflictAsync(
            await WorkOrdersApi.CreateAsync(
                host,
                owner,
                changed.QuoteId,
                WorkOrdersApi.Body(world, changed).Edit(change =>
                {
                    change["updatedAt"] = draft["workOrder"]!["updatedAt"]!.GetValue<string>();
                    change["title"] = "Never saved";
                })),
            "request_changed");

        Assert.Equal(baseline, await database.SideEffectsAsync(world.Org, changed.RequestId));
        Assert.Equal(
            ("draft", "Replace drain line", storedToken),
            (
                await database.ScalarAsync<string>("SELECT status::text FROM work_orders WHERE id = @w", ("w", orderId)),
                await database.ScalarAsync<string>("SELECT title FROM work_orders WHERE id = @w", ("w", orderId)),
                await database.ScalarAsync<DateTimeOffset>("SELECT updated_at FROM work_orders WHERE id = @w", ("w", orderId))));

        // AC-11: a draft pointing at another version of the quote than the approved one.
        var mismatchDraft = await WorkOrdersApi.ReadOkAsync(
            await WorkOrdersApi.DraftAsync(host, owner, mismatch.QuoteId, WorkOrdersApi.Body(world, mismatch)), HttpStatusCode.Created);
        var otherVersion = Guid.NewGuid();
        await database.ExecuteAsync(
            """
            INSERT INTO quote_versions (id, organization_id, quote_id, version_no, scope, subtotal, tax_total, total, currency, sent_at, is_immutable, created_by_user_id)
            SELECT @n, organization_id, quote_id, 2, scope, subtotal, tax_total, total, currency, now(), true, created_by_user_id FROM quote_versions WHERE id = @v
            """,
            ("n", otherVersion),
            ("v", mismatch.VersionId));
        await database.ExecuteAsync(
            "UPDATE work_orders SET quote_version_id = @n WHERE id = @w", ("n", otherVersion), ("w", mismatchDraft["workOrder"]!["id"]!.GetValue<Guid>()));
        var mismatchBaseline = await database.SideEffectsAsync(world.Org, mismatch.RequestId);
        var counter = await database.NextNumberAsync(world.Org);

        await WorkOrdersApi.AssertConflictAsync(await WorkOrdersApi.EditorAsync(host, owner, mismatch.QuoteId), "quote_not_approved");
        await WorkOrdersApi.AssertConflictAsync(
            await WorkOrdersApi.DraftAsync(host, owner, mismatch.QuoteId, WorkOrdersApi.Body(world, mismatch)), "quote_not_approved");
        await WorkOrdersApi.AssertConflictAsync(
            await WorkOrdersApi.CreateAsync(host, owner, mismatch.QuoteId, WorkOrdersApi.Body(world, mismatch)), "quote_not_approved");

        Assert.Equal(mismatchBaseline, await database.SideEffectsAsync(world.Org, mismatch.RequestId));
        Assert.Equal(counter, await database.NextNumberAsync(world.Org));
        Assert.Equal("quoted", await database.StatusOfAsync(mismatch.RequestId));
    }
}
