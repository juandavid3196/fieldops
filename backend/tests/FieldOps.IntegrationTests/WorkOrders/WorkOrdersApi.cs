using System.Net;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.QuoteLinks;
using FieldOps.IntegrationTests.Quotes;
using FieldOps.IntegrationTests.ServiceRequests;

namespace FieldOps.IntegrationTests.WorkOrders;

/// <summary>An approved quote seeded through the real send and approval flows, with the ids the work order tests need.</summary>
internal sealed record ApprovedQuote(
    Guid QuoteId,
    Guid RequestId,
    Guid VersionId,
    Guid ServiceLine,
    Guid ProductLine,
    Guid SelectedOptionalService,
    Guid SelectedOptionalProduct,
    Guid UnselectedOptionalProduct,
    Guid UnselectedOptionalService);

/// <summary>Request builders and seeds of the create-work-order integration tests.</summary>
internal static class WorkOrdersApi
{
    public static JsonObject QuoteDraft() =>
        QuotesApi.Draft(
            [
                QuotesApi.Line(name: "Drain cleaning", unitPrice: 100m),
                QuotesApi.Line(type: "product", name: "PVC pipe", quantity: 3m, unit: "ft", unitPrice: 12m),
                QuotesApi.Line(name: "Camera inspection", unitPrice: 120m, optional: true),
                QuotesApi.Line(type: "product", name: "Extra trap", unitPrice: 40m, optional: true),
                QuotesApi.Line(type: "product", name: "Spare valve", unitPrice: 25m, optional: true),
                QuotesApi.Line(name: "Hydro jetting", unitPrice: 300m, optional: true),
            ],
            note: "SECRET INTERNAL NOTE");

    /// <summary>Sends the quote, approves it from the public link selecting the camera inspection and the extra trap.</summary>
    public static async Task<ApprovedQuote> ApproveAsync(
        CompanySettingsDatabaseFixture db, RequestsHost host, RequestWorld world, string ownerCookie, bool linkCustomer = true, Guid? branch = null)
    {
        var sent = await QuoteLinkApi.SendAsync(db, host, world, ownerCookie, QuoteDraft(), linkCustomer, branch);
        Guid Id(string name) =>
            db.ScalarAsync<Guid>("SELECT id FROM quote_lines WHERE quote_version_id = @v AND name = @n", ("v", sent.VersionId), ("n", name)).GetAwaiter().GetResult();


        var approved = await QuoteLinkApi.PostAsync(
            host,
            "approve",
            QuoteLinkApi.Body(
                sent.Token,
                ("selectedOptionalLineIds", QuoteLinkApi.Ids(Id("Camera inspection"), Id("Extra trap"))),
                ("acceptTerms", true)));
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);

        return new ApprovedQuote(
            sent.QuoteId,
            sent.RequestId,
            sent.VersionId,
            Id("Drain cleaning"),
            Id("PVC pipe"),
            Id("Camera inspection"),
            Id("Extra trap"),
            Id("Spare valve"),
            Id("Hydro jetting"));
    }

    public static JsonObject Body(RequestWorld world, ApprovedQuote quote, Guid? skill = null, Guid? branch = null) =>
        new()
        {
            ["title"] = "Replace drain line",
            ["jobType"] = "one_time",
            ["serviceCategoryId"] = world.Category,
            ["branchId"] = branch ?? world.BranchA,
            ["priority"] = "normal",
            ["estimatedDurationMinutes"] = 120,
            ["skillIds"] = new JsonArray(skill is null ? [] : [JsonValue.Create(skill.Value)]),
            ["tasks"] = new JsonArray(
                new JsonObject { ["label"] = "Inspect line" },
                new JsonObject { ["label"] = "Clear blockage" },
                new JsonObject { ["label"] = "Test flow" }),
            ["materials"] = new JsonArray(
                new JsonObject
                {
                    ["quoteLineId"] = quote.ProductLine,
                    ["description"] = "PVC pipe",
                    ["quantity"] = 3,
                    ["unit"] = "ft",
                    ["source"] = "truck_stock",
                }),
            ["instructions"] = "Call before arriving.",
            ["preferredDate"] = null,
            ["arrivalWindow"] = "any",
            ["recurrence"] = null,
            ["communication"] = new JsonObject
            {
                ["notifyCustomerWhenScheduled"] = true,
                ["sendTechnicianDetails"] = true,
                ["sendArrivalReminder"] = true,
            },
            ["updatedAt"] = null,
        };

    public static JsonObject Edit(this JsonObject body, Action<JsonObject> change)
    {
        var copy = (JsonObject)body.DeepClone();
        change(copy);

        return copy;
    }

    public static JsonObject Material(string description, decimal quantity = 1m, string unit = "ea", string source = "warehouse") =>
        new() { ["description"] = description, ["quantity"] = quantity, ["unit"] = unit, ["source"] = source };

    public static string EditorPath(Guid quoteId) => $"/quotes/{quoteId}/work-order";

    public static Task<HttpResponseMessage> EditorAsync(RequestsHost host, string? cookie, Guid quoteId) =>
        host.SendAsync(HttpMethod.Get, EditorPath(quoteId), cookie);

    public static Task<HttpResponseMessage> DraftAsync(RequestsHost host, string? cookie, Guid quoteId, JsonObject body) =>
        host.SendAsync(HttpMethod.Put, $"{EditorPath(quoteId)}/draft", cookie, body);

    public static Task<HttpResponseMessage> CreateAsync(RequestsHost host, string? cookie, Guid quoteId, JsonObject body) =>
        host.SendAsync(HttpMethod.Post, EditorPath(quoteId), cookie, body);

    public static async Task<JsonNode> ReadOkAsync(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        Assert.True(response.StatusCode == expected, $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri?.AbsolutePath}: expected {expected} but was {response.StatusCode} {await response.Content.ReadAsStringAsync()}");

        return await RequestsHost.ReadAsync(response);
    }

    public static async Task AssertConflictAsync(HttpResponseMessage response, string code)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(code, (await RequestsHost.ReadAsync(response))["code"]!.GetValue<string>());
    }

    public static async Task AssertInvalidAsync(HttpResponseMessage response, string key)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await RequestsHost.ReadAsync(response);
        Assert.True(problem["errors"]![key] is not null, $"Expected errors.{key} in {problem.ToJsonString()}");
    }

    public static Task<long> OrderCountAsync(this CompanySettingsDatabaseFixture db, Guid org) =>
        db.ScalarAsync<long>("SELECT COUNT(*) FROM work_orders WHERE organization_id = @o", ("o", org));

    public static Task<long> NextNumberAsync(this CompanySettingsDatabaseFixture db, Guid org) =>
        db.ScalarAsync<long>("SELECT next_work_order_number FROM organizations WHERE id = @o", ("o", org));

    /// <summary>Counts of every table this feature must not touch on a draft, as one comparable text.</summary>
    public static Task<string> SideEffectsAsync(this CompanySettingsDatabaseFixture db, Guid org, Guid request) =>
        db.ScalarAsync<string>(
            """
            SELECT (SELECT COUNT(*) FROM visits WHERE organization_id = @o) || '|'
                || (SELECT COUNT(*) FROM visit_status_history h JOIN visits v ON v.id = h.visit_id WHERE v.organization_id = @o) || '|'
                || (SELECT COUNT(*) FROM visit_checklist_items i JOIN visits v ON v.id = i.visit_id WHERE v.organization_id = @o) || '|'
                || (SELECT COUNT(*) FROM visit_materials m JOIN visits v ON v.id = m.visit_id WHERE v.organization_id = @o) || '|'
                || (SELECT COUNT(*) FROM visit_assignments a JOIN visits v ON v.id = a.visit_id WHERE v.organization_id = @o) || '|'
                || (SELECT COUNT(*) FROM notifications WHERE organization_id = @o) || '|'
                || (SELECT COUNT(*) FROM audit_logs WHERE organization_id = @o AND action = 'work_order.created') || '|'
                || (SELECT COUNT(*) FROM request_status_history WHERE request_id = @r) || '|'
                || (SELECT status::text FROM service_requests WHERE id = @r)
            """,
            ("o", org),
            ("r", request));

    /// <summary>A digest of every quote row of the organization: the feature never writes quotes (BR-17).</summary>
    public static Task<string> QuoteDigestAsync(this CompanySettingsDatabaseFixture db, Guid org) =>
        db.ScalarAsync<string>(
            """
            SELECT md5(COALESCE(string_agg(t, '|' ORDER BY t), '')) FROM (
                SELECT q::text AS t FROM quotes q WHERE organization_id = @o
                UNION ALL SELECT v::text FROM quote_versions v WHERE organization_id = @o
                UNION ALL SELECT l::text FROM quote_lines l WHERE organization_id = @o
                UNION ALL SELECT r::text FROM quote_responses r WHERE organization_id = @o
                UNION ALL SELECT s::text FROM quote_response_optional_lines s WHERE organization_id = @o) x
            """,
            ("o", org));

    public static async Task<Guid> OrderOfAsync(this CompanySettingsDatabaseFixture db, Guid quoteId) =>
        await db.ScalarAsync<Guid>(
            "SELECT w.id FROM work_orders w JOIN quote_versions v ON v.id = w.quote_version_id WHERE v.quote_id = @q", ("q", quoteId));
}
