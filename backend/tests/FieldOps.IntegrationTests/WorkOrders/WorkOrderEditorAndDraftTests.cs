using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.Catalog;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.PublicRequests;
using FieldOps.IntegrationTests.Quotes;
using FieldOps.IntegrationTests.ServiceRequests;
using FieldOps.IntegrationTests.Team;

namespace FieldOps.IntegrationTests.WorkOrders;

/// <summary>Editor load, draft lifecycle and validation of the work order creation (create-work-order AC-02 to AC-08, AC-11, AC-12, AC-18, AC-19).</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class WorkOrderEditorAndDraftTests(CompanySettingsDatabaseFixture database)
{
    // AC-02, AC-03, AC-11, AC-12: locked context and prefill without writes, and the approval guards.
    [Fact]
    public async Task Editor_LoadsLockedContextAndPrefillWithoutWriting_AndGuardsUnapprovedAndGuestQuotes()
    {
        var world = await database.SeedQuoteWorldAsync();
        await using var host = RequestsHost.Create(database);
        var (owner, ownerMember) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var approved = await WorkOrdersApi.ApproveAsync(database, host, world, owner);

        await database.ExecuteAsync("UPDATE service_requests SET urgency = 'urgent' WHERE id = @r", ("r", approved.RequestId));
        await database.ExecuteAsync("UPDATE properties SET access_instructions = 'Gate code 4321' WHERE id = @p", ("p", world.Property));
        var assessment = await database.SeedAssessmentAsync(
            world.Org, approved.RequestId, DateTimeOffset.UtcNow.AddHours(-3), DateTimeOffset.UtcNow.AddHours(-2), ownerMember.UserId, status: "completed");
        await database.ExecuteAsync("UPDATE assessments SET diagnosis = 'Worn valve', completed_at = now() WHERE id = @a", ("a", assessment));
        var jpeg = CatalogSeed.Jpeg(100);
        await database.ExecuteAsync(
            "INSERT INTO assessment_attachments (id, organization_id, assessment_id, file_name, content, mime_type, size_bytes) VALUES (@id, @o, @a, 'seed.jpg', @c, 'image/jpeg', @s)",
            ("id", Guid.NewGuid()),
            ("o", world.Org),
            ("a", assessment),
            ("c", jpeg),
            ("s", (long)jpeg.Length));
        var before = (await database.OrderCountAsync(world.Org), await database.NextNumberAsync(world.Org), await database.SideEffectsAsync(world.Org, approved.RequestId));

        var editor = await WorkOrdersApi.ReadOkAsync(await WorkOrdersApi.EditorAsync(host, owner, approved.QuoteId));

        Assert.StartsWith("Q-", editor["quote"]!["displayNumber"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal(
            await database.ScalarAsync<decimal>("SELECT total FROM quote_responses WHERE quote_version_id = @v", ("v", approved.VersionId)),
            editor["quote"]!["approvedTotal"]!.GetValue<decimal>());
        Assert.Equal("Carla Customer", editor["customer"]!["name"]!.GetValue<string>());
        Assert.False(string.IsNullOrWhiteSpace(editor["customer"]!["phone"]!.GetValue<string>()));
        Assert.Equal("carla@example.com", editor["customer"]!["email"]!.GetValue<string>());
        Assert.Equal("Gate code 4321", editor["accessNote"]!.GetValue<string>());
        Assert.Equal("Worn valve", editor["assessment"]!["diagnosis"]!.GetValue<string>());
        Assert.Single(editor["assessment"]!["photos"]!.AsArray());
        Assert.Null(editor["workOrder"]);

        var values = editor["values"]!;
        Assert.Equal(["Drain cleaning", "Camera inspection"], values["tasks"]!.AsArray().Select(task => task!["label"]!.GetValue<string>()).ToArray());
        Assert.Equal(["PVC pipe", "Extra trap"], values["materials"]!.AsArray().Select(material => material!["description"]!.GetValue<string>()).ToArray());
        Assert.All(values["materials"]!.AsArray(), material => Assert.Equal("truck_stock", material!["source"]!.GetValue<string>()));
        Assert.Equal(approved.ProductLine, values["materials"]![0]!["quoteLineId"]!.GetValue<Guid>());
        Assert.Equal("high", values["priority"]!.GetValue<string>());
        Assert.Equal("one_time", values["jobType"]!.GetValue<string>());
        Assert.Equal(world.Category, values["serviceCategoryId"]!.GetValue<Guid>());
        Assert.Equal(world.BranchA, values["branchId"]!.GetValue<Guid>());
        Assert.Equal("any", values["arrivalWindow"]!.GetValue<string>());
        Assert.True(values["communication"]!["sendArrivalReminder"]!.GetValue<bool>());
        Assert.Equal(2, editor["options"]!["branches"]!.AsArray().Count);
        Assert.Equal(
            before,
            (await database.OrderCountAsync(world.Org), await database.NextNumberAsync(world.Org), await database.SideEffectsAsync(world.Org, approved.RequestId)));

        // AC-03: no assessment and no access note.
        var bare = await WorkOrdersApi.ApproveAsync(database, host, world, owner);
        await database.ExecuteAsync("UPDATE properties SET access_instructions = NULL WHERE id = @p", ("p", world.Property));
        var bareEditor = await WorkOrdersApi.ReadOkAsync(await WorkOrdersApi.EditorAsync(host, owner, bare.QuoteId));
        Assert.Null(bareEditor["assessment"]);
        Assert.Null(bareEditor["accessNote"]);

        // AC-11, AC-12: an unapproved quote and a guest quote refuse the three operations and change nothing.
        var sentRequest = await database.SeedReadyRequestAsync(world);
        var sentQuote = await QuotesApi.CreateAsync(host, owner, sentRequest);
        var sent = await QuotesApi.SendAsync(host, owner, sentQuote, QuotesApi.Draft([QuotesApi.Line()]));
        var sentId = sent["quote"]!["id"]!.GetValue<Guid>();
        var guest = await WorkOrdersApi.ApproveAsync(database, host, world, owner, linkCustomer: false);
        var body = WorkOrdersApi.Body(world, approved);
        var counter = await database.NextNumberAsync(world.Org);

        foreach (var (quoteId, code) in new[] { (sentId, "quote_not_approved"), (guest.QuoteId, "customer_required") })
        {
            await WorkOrdersApi.AssertConflictAsync(await WorkOrdersApi.EditorAsync(host, owner, quoteId), code);
            await WorkOrdersApi.AssertConflictAsync(await WorkOrdersApi.DraftAsync(host, owner, quoteId, body), code);
            await WorkOrdersApi.AssertConflictAsync(await WorkOrdersApi.CreateAsync(host, owner, quoteId, body), code);
        }

        Assert.Equal((0L, counter), (await database.OrderCountAsync(world.Org), await database.NextNumberAsync(world.Org)));
        Assert.Equal("quoted", await database.StatusOfAsync(guest.RequestId));
    }

    // AC-06, AC-07, AC-08: insert with numbering, update with the token, stale and missing tokens, and the saved values on reload.
    [Fact]
    public async Task Draft_InsertsWithNumberThenReplacesWithToken_AndRefusesStaleAndMissingTokens()
    {
        var world = await database.SeedQuoteWorldAsync();
        await using var host = RequestsHost.Create(database);
        var (owner, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var approved = await WorkOrdersApi.ApproveAsync(database, host, world, owner);
        var skill = await database.SeedSkillAsync(world.Org, "Welding");
        var firstNumber = await database.NextNumberAsync(world.Org);
        var baseline = await database.SideEffectsAsync(world.Org, approved.RequestId);
        var body = WorkOrdersApi.Body(world, approved, skill);

        var inserted = await WorkOrdersApi.ReadOkAsync(await WorkOrdersApi.DraftAsync(host, owner, approved.QuoteId, body), HttpStatusCode.Created);
        var saved = inserted["workOrder"]!;
        Assert.Equal("draft", saved["status"]!.GetValue<string>());
        Assert.Equal($"WO-{firstNumber}", saved["displayNumber"]!.GetValue<string>());
        Assert.Equal(firstNumber + 1, await database.NextNumberAsync(world.Org));

        var orderId = saved["id"]!.GetValue<Guid>();
        Assert.Equal(
            $"{approved.VersionId}|{world.Customer}|{world.Property}|draft|Replace drain line|{world.Category}|{world.BranchA}",
            await database.ScalarAsync<string>(
                "SELECT quote_version_id || '|' || customer_id || '|' || property_id || '|' || status::text || '|' || title || '|' || service_category_id || '|' || branch_id FROM work_orders WHERE id = @w",
                ("w", orderId)));
        Assert.Equal(
            await database.ScalarAsync<string>("SELECT scope FROM quote_versions WHERE id = @v", ("v", approved.VersionId)),
            await database.ScalarAsync<string>("SELECT scope_snapshot FROM work_orders WHERE id = @w", ("w", orderId)));
        Assert.Equal(
            "1|3|1",
            await database.ScalarAsync<string>(
                "SELECT (SELECT COUNT(*) FROM work_order_required_skills WHERE work_order_id = @w) || '|' || (SELECT COUNT(*) FROM work_order_checklist_templates WHERE work_order_id = @w) || '|' || (SELECT COUNT(*) FROM work_order_planned_materials WHERE work_order_id = @w)",
                ("w", orderId)));
        Assert.Equal(baseline, await database.SideEffectsAsync(world.Org, approved.RequestId));

        // The token comes back as received; a changed draft replaces every child and returns a new token.
        var token = saved["updatedAt"]!.GetValue<string>();
        var changed = body.Edit(draft =>
        {
            draft["updatedAt"] = token;
            draft["title"] = "Replace main drain";
            draft["tasks"] = new JsonArray(new JsonObject { ["label"] = "Clear blockage" }, new JsonObject { ["label"] = "Test flow" });
            draft["materials"]!.AsArray().Add(WorkOrdersApi.Material("Pipe glue"));
        });
        var updated = await WorkOrdersApi.ReadOkAsync(await WorkOrdersApi.DraftAsync(host, owner, approved.QuoteId, changed));
        Assert.NotEqual(token, updated["workOrder"]!["updatedAt"]!.GetValue<string>());
        Assert.Equal(orderId, updated["workOrder"]!["id"]!.GetValue<Guid>());
        Assert.Equal(firstNumber + 1, await database.NextNumberAsync(world.Org));
        Assert.Equal(
            "2|2",
            await database.ScalarAsync<string>(
                "SELECT (SELECT COUNT(*) FROM work_order_checklist_templates WHERE work_order_id = @w) || '|' || (SELECT COUNT(*) FROM work_order_planned_materials WHERE work_order_id = @w)",
                ("w", orderId)));

        // AC-07: the old token and a missing token are conflicts that change nothing and consume no number.
        foreach (var stale in new string?[] { token, null })
        {
            await WorkOrdersApi.AssertConflictAsync(
                await WorkOrdersApi.DraftAsync(host, owner, approved.QuoteId, changed.Edit(draft => { draft["updatedAt"] = stale; draft["title"] = "Stale write"; })),
                "work_order_changed");
        }

        Assert.Equal("Replace main drain", await database.ScalarAsync<string>("SELECT title FROM work_orders WHERE id = @w", ("w", orderId)));
        Assert.Equal(firstNumber + 1, await database.NextNumberAsync(world.Org));

        // AC-08: returning to the editor shows the saved values, not the prefill.
        var reloaded = await WorkOrdersApi.ReadOkAsync(await WorkOrdersApi.EditorAsync(host, owner, approved.QuoteId));
        Assert.Equal("Replace main drain", reloaded["values"]!["title"]!.GetValue<string>());
        Assert.Equal(2, reloaded["values"]!["tasks"]!.AsArray().Count);
        Assert.Equal(skill, reloaded["values"]!["skillIds"]![0]!.GetValue<Guid>());
        Assert.Equal(updated["workOrder"]!["updatedAt"]!.GetValue<string>(), reloaded["workOrder"]!["updatedAt"]!.GetValue<string>());
    }

    // AC-04, AC-05, AC-18, AC-19: BR-08 and BR-09 cases through draft and create, plus the stored window and recurrence.
    [Fact]
    public async Task Validation_RejectsInvalidFieldsIdentifiersRecurrenceAndWindows_WithoutSavingAnything()
    {
        var world = await database.SeedQuoteWorldAsync();
        var foreign = await database.SeedQuoteWorldAsync();
        await using var host = RequestsHost.Create(database);
        var (owner, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var (dispatcher, _) = await host.SignInAsync(
            database, world.Org, CompanySettingsDatabaseFixture.DispatcherRoleId, "Dina", "Dispatcher", world.BranchA);
        var approved = await WorkOrdersApi.ApproveAsync(database, host, world, owner);
        var otherQuote = await WorkOrdersApi.ApproveAsync(database, host, world, owner);
        var body = WorkOrdersApi.Body(world, approved);

        var inactiveCategory = await database.SeedCategoryAsync(world.Org, "Retired", active: false);
        var inactiveSkill = await database.SeedSkillAsync(world.Org, "Retired skill", active: false);
        var foreignSkill = await database.SeedSkillAsync(foreign.Org, "Foreign skill");
        var foreignProduct = await database.SeedServiceAsync(foreign.Org, foreign.Category, "Foreign pipe", type: "product");
        var inactiveProduct = await database.SeedServiceAsync(world.Org, world.Category, "Old pipe", active: false, type: "product");
        var serviceItem = world.Service;
        var elevenSkills = new JsonArray(Enumerable.Range(0, 11).Select(_ => (JsonNode?)JsonValue.Create(Guid.NewGuid())).ToArray());
        var yesterday = QuotesApi.Today(-2);

        Action<JsonObject> Material(Action<JsonObject> change) => draft => change(draft["materials"]![0]!.AsObject());
        JsonObject Valid(string frequency, int count) => new() { ["frequency"] = frequency, ["count"] = count };

        var cases = new (string Key, Action<JsonObject> Change)[]
        {
            ("title", draft => draft["title"] = "   "),
            ("title", draft => draft["title"] = new string('x', 161)),
            ("jobType", draft => draft["jobType"] = "weekly"),
            ("serviceCategoryId", draft => draft["serviceCategoryId"] = null),
            ("serviceCategoryId", draft => draft["serviceCategoryId"] = foreign.Category),
            ("serviceCategoryId", draft => draft["serviceCategoryId"] = inactiveCategory),
            ("branchId", draft => draft["branchId"] = foreign.BranchA),
            ("priority", draft => draft["priority"] = "critical"),
            ("estimatedDurationMinutes", draft => draft["estimatedDurationMinutes"] = 45),
            ("estimatedDurationMinutes", draft => draft["estimatedDurationMinutes"] = 750),
            ("skillIds", draft => draft["skillIds"] = elevenSkills.DeepClone()),
            ("skillIds", draft => draft["skillIds"] = new JsonArray(JsonValue.Create(foreignSkill))),
            ("skillIds", draft => draft["skillIds"] = new JsonArray(JsonValue.Create(inactiveSkill))),
            ("tasks", draft => draft["tasks"] = new JsonArray()),
            ("tasks", draft => draft["tasks"] = new JsonArray(Enumerable.Range(0, 51).Select(i => (JsonNode?)new JsonObject { ["label"] = $"Task {i}" }).ToArray())),
            ("tasks[1].label", draft => draft["tasks"]![1]!["label"] = ""),
            ("tasks[0].label", draft => draft["tasks"]![0]!["label"] = new string('t', 241)),
            ("materials[0].description", Material(material => material["description"] = "")),
            ("materials[0].quantity", Material(material => material["quantity"] = 0)),
            ("materials[0].quantity", Material(material => material["quantity"] = 1.2345m)),
            ("materials[0].quantity", Material(material => material["quantity"] = 100000)),
            ("materials[0].unit", Material(material => material["unit"] = " ")),
            ("materials[0].source", Material(material => material["source"] = "garage")),
            ("materials[0].catalogItemId", Material(material => material["catalogItemId"] = foreignProduct)),
            ("materials[0].catalogItemId", Material(material => material["catalogItemId"] = inactiveProduct)),
            ("materials[0].catalogItemId", Material(material => material["catalogItemId"] = serviceItem)),
            ("materials[0].quoteLineId", Material(material => material["quoteLineId"] = otherQuote.ProductLine)),
            ("materials[0].quoteLineId", Material(material => material["quoteLineId"] = approved.UnselectedOptionalProduct)),
            ("materials[0].quoteLineId", Material(material => material["quoteLineId"] = approved.ServiceLine)),
            ("materials[1].quoteLineId", draft => draft["materials"]!.AsArray().Add(WorkOrdersApi.Material("Again").With("quoteLineId", approved.ProductLine))),
            ("instructions", draft => draft["instructions"] = new string('i', 2001)),
            ("preferredDate", draft => draft["preferredDate"] = yesterday),
            ("preferredDate", draft => draft["preferredDate"] = "tomorrow"),
            ("arrivalWindow", draft => draft["arrivalWindow"] = "09-12"),
            ("arrivalWindow", draft => { draft["preferredDate"] = QuotesApi.Today(3); draft["arrivalWindow"] = "10-13"; }),
            ("recurrence", draft => draft["recurrence"] = Valid("monthly", 6)),
            ("recurrence", draft => draft["jobType"] = "recurring"),
            ("recurrence", draft => { draft["jobType"] = "recurring"; draft["recurrence"] = Valid("monthly", 25); }),
            ("recurrence", draft => { draft["jobType"] = "recurring"; draft["recurrence"] = Valid("daily", 5); }),
            ("communication.sendArrivalReminder", draft => draft["communication"]!["sendArrivalReminder"] = null),
            ("updatedAt", draft => draft["updatedAt"] = "not a date"),
        };

        var counter = await database.NextNumberAsync(world.Org);
        var baseline = await database.SideEffectsAsync(world.Org, approved.RequestId);

        foreach (var (key, change) in cases)
        {
            var invalid = body.Edit(change);
            await WorkOrdersApi.AssertInvalidAsync(await WorkOrdersApi.DraftAsync(host, owner, approved.QuoteId, invalid), key);
            await WorkOrdersApi.AssertInvalidAsync(await WorkOrdersApi.CreateAsync(host, owner, approved.QuoteId, invalid), key);
        }

        // AC-05: a Dispatcher of branch A cannot target branch B.
        await WorkOrdersApi.AssertInvalidAsync(
            await WorkOrdersApi.DraftAsync(host, dispatcher, approved.QuoteId, body.Edit(draft => draft["branchId"] = world.BranchB)),
            "branchId");

        Assert.Equal((0L, counter, baseline), (await database.OrderCountAsync(world.Org), await database.NextNumberAsync(world.Org), await database.SideEffectsAsync(world.Org, approved.RequestId)));

        // AC-18, AC-19: a recurring order stores its recurrence and only visit #1 exists later; the window is local to the branch zone.
        const string zoneId = "America/New_York";
        var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        await database.ExecuteAsync("UPDATE branches SET timezone = @tz WHERE id = @b", ("tz", zoneId), ("b", world.BranchA));
        var tomorrow = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).DateTime).AddDays(1);
        var scheduled = body.Edit(draft =>
        {
            draft["jobType"] = "recurring";
            draft["recurrence"] = Valid("monthly", 6);
            draft["preferredDate"] = tomorrow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            draft["arrivalWindow"] = "09-12";
        });
        var editor = await WorkOrdersApi.ReadOkAsync(await WorkOrdersApi.DraftAsync(host, owner, approved.QuoteId, scheduled), HttpStatusCode.Created);
        var orderId = editor["workOrder"]!["id"]!.GetValue<Guid>();
        var expectedStart = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(tomorrow.ToDateTime(new TimeOnly(9, 0)), zone), TimeSpan.Zero);

        Assert.Equal(
            (expectedStart, expectedStart.AddHours(3), "monthly", (short)6),
            (
                await database.ScalarAsync<DateTimeOffset>("SELECT preferred_start FROM work_orders WHERE id = @w", ("w", orderId)),
                await database.ScalarAsync<DateTimeOffset>("SELECT preferred_end FROM work_orders WHERE id = @w", ("w", orderId)),
                await database.ScalarAsync<string>("SELECT recurrence_frequency FROM work_orders WHERE id = @w", ("w", orderId)),
                await database.ScalarAsync<short>("SELECT recurrence_count FROM work_orders WHERE id = @w", ("w", orderId))));
        Assert.Equal(
            (tomorrow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), "09-12", "monthly", 6),
            (
                editor["values"]!["preferredDate"]!.GetValue<string>(),
                editor["values"]!["arrivalWindow"]!.GetValue<string>(),
                editor["values"]!["recurrence"]!["frequency"]!.GetValue<string>(),
                editor["values"]!["recurrence"]!["count"]!.GetValue<int>()));
    }
}
