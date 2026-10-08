using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.Dispatch;
using FieldOps.IntegrationTests.ServiceRequests;

namespace FieldOps.IntegrationTests.TechnicianVisits;

/// <summary>Job completion: acknowledgment, closing of time entries and the work order aggregate (mobile-job-completion AC-01 to AC-14).</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class TechnicianCompletionTests(CompanySettingsDatabaseFixture database)
{
    private const string NotAvailable = "This job isn't available.";

    private const string StatusInvalid = "This job can't be completed in its current state.";

    private const string RequirementsUnmet = "Complete required tasks and add before and after photos before completing this job.";

    private const string NameRequired = "Enter the signer's name.";

    private const string NameTooLong = "Use 180 characters or fewer.";

    private const string RelationshipInvalid = "Select the relationship.";

    private const string SignatureMissing = "Add the customer's signature.";

    private const string SignatureInvalid = "Capture the signature again.";

    private const string ReasonRequired = "Enter the reason.";

    private const string ConfirmationRequired = "Enter how the customer confirmed.";

    private const string CommentTooLong = "Use 1000 characters or fewer.";

    private const string ReviewRequired = "Confirm that the customer reviewed the work.";

    private const string MethodRequired = "Choose how the customer acknowledged the service.";

    private const string NotAllowed = "This field doesn't apply to the selected method.";

    private static readonly DateTimeOffset Noon = TechnicianVisitSeed.Utc("2026-06-10T12:00:00Z");

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];

    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 9, 8, 7];

    private static string Path(Guid visit, string suffix = "") => $"/technician/visits/{visit}{suffix}";

    /// <summary>A PNG (valid file signature) of exactly <paramref name="length"/> bytes.</summary>
    private static byte[] PngOf(int length)
    {
        var bytes = new byte[length];
        Png.CopyTo(bytes, 0);

        return bytes;
    }

    private static MultipartFormDataContent Form(
        string? method,
        (string Key, string Value)[]? fields = null,
        byte[]? signature = null,
        string signatureType = "image/png",
        string signatureName = "signature.png")
    {
        var form = new MultipartFormDataContent();

        if (method is not null)
        {
            form.Add(new StringContent(method), "method");
        }

        foreach (var (key, value) in fields ?? [])
        {
            form.Add(new StringContent(value), key);
        }

        if (signature is not null)
        {
            var part = new ByteArrayContent(signature);
            part.Headers.ContentType = new MediaTypeHeaderValue(signatureType);
            form.Add(part, "signature", signatureName);
        }

        return form;
    }

    // A body without the method (an entirely empty multipart body is not matched by the endpoint at all).
    private static MultipartFormDataContent NoMethod() => Form(null, [("comment", "Anything")]);

    private static MultipartFormDataContent Signed(string comment = "Looks good ZQCOMMENT") =>
        Form(
            "signed",
            [("signerName", "Pat Quill"), ("relationship", "family_member"), ("reviewConfirmed", "true"), ("comment", comment)],
            Png);

    private static MultipartFormDataContent Absent(string reason = "Nobody home") => Form("customer_absent", [("comment", reason)]);

    private Task<HttpResponseMessage> CompleteAsync(TechnicianHost host, Guid visit, string cookie, MultipartFormDataContent form) =>
        host.PostFormAsync(Path(visit, "/complete"), cookie, form);

    /// <summary>A work order and its first visit, optionally assigned (primary) to a technician.</summary>
    private async Task<SeededOrder> SeedAsync(
        RequestWorld world,
        Guid user,
        Guid? technician,
        string visitStatus,
        string orderStatus = "in_progress",
        string jobType = "one_time",
        short? count = null)
    {
        var order = await database.SeedOrderAsync(
            world,
            user,
            status: orderStatus,
            title: "Fix drain",
            jobType: jobType,
            frequency: jobType == "recurring" ? "weekly" : null,
            count: count,
            visitStatus: visitStatus,
            start: Noon,
            end: Noon.AddHours(1));

        if (technician is { } assignee)
        {
            await database.AssignAsync(order.Visit, assignee, user);
        }

        return order;
    }

    /// <summary>One completed required task, one optional open task, a before and an after photo and the time entries of the status.</summary>
    private async Task MakeReadyAsync(Guid visit, Guid technician, Guid user, string entries = "work")
    {
        await database.ExecuteAsync(
            "INSERT INTO visit_checklist_items (visit_id, label, is_required, is_completed, completed_by_user_id, completed_at, sort_order) VALUES (@v, 'Shut off water', true, true, @u, @c, 0), (@v, 'Photograph', false, false, NULL, NULL, 1)",
            ("v", visit),
            ("u", user),
            ("c", Noon.AddMinutes(-15)));
        await database.ExecuteAsync(
            "INSERT INTO visit_evidence (visit_id, file_name, content, mime_type, size_bytes, evidence_type, uploaded_by_user_id) VALUES (@v, 'b.png', @c, 'image/png', @n, 'before', @u), (@v, 'a.png', @c, 'image/png', @n, 'after', @u)",
            ("v", visit),
            ("c", Png),
            ("n", (long)Png.Length),
            ("u", user));

        switch (entries)
        {
            case "work":
                await database.SeedEntryAsync(visit, technician, "work", Noon.AddMinutes(-30));
                break;
            case "pause":
                await database.SeedEntryAsync(visit, technician, "work", Noon.AddMinutes(-40), Noon.AddMinutes(-20));
                await database.SeedEntryAsync(visit, technician, "pause", Noon.AddMinutes(-20));
                break;
        }

        await database.ExecuteAsync("UPDATE visits SET actual_started_at = @a WHERE id = @v", ("a", Noon.AddMinutes(-40)), ("v", visit));
    }

    private async Task<SeededOrder> SeedReadyAsync(
        RequestWorld world, SeededTechnician me, string visitStatus = "in_progress", string orderStatus = "in_progress", string entries = "work")
    {
        var job = await SeedAsync(world, me.Member.UserId, me.Profile, visitStatus, orderStatus);
        await MakeReadyAsync(job.Visit, me.Profile, me.Member.UserId, entries);

        return job;
    }

    /// <summary>Everything a completion may write: the visit, its children, the signoff and the work order.</summary>
    private async Task<string> StateAsync(SeededOrder job) =>
        await database.ProgressStateAsync(job.Visit)
        + "|s=" + await database.ScalarAsync<long>("SELECT COUNT(*) FROM customer_signoffs WHERE visit_id = @v", ("v", job.Visit))
        + "|wa=" + await database.ScalarAsync<long>("SELECT COUNT(*) FROM audit_logs WHERE entity_id = @w", ("w", job.Order))
        + "|wu=" + await database.ScalarAsync<string>("SELECT updated_at::text FROM work_orders WHERE id = @w", ("w", job.Order));

    private static async Task<JsonNode> ReadAsync(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK) =>
        await TechnicianVisitSeed.ReadAsync(response, expected);

    private async Task<string> OrderStatusAsync(Guid order) =>
        await database.ScalarAsync<string>("SELECT status::text FROM work_orders WHERE id = @w", ("w", order));

    [Fact]
    public async Task CompleteAndDetail_DenyUnavailableVisitsNonPrimaryAndUnusableProfilesBeforeReadingTheBodyWithoutWriting()
    {
        var world = await database.SeedWorldAsync();
        var foreign = await database.SeedWorldAsync();
        await using var host = TechnicianHost.Create(database);
        var me = await database.SeedTechnicianAsync(host, world);
        var other = await database.SeedTechnicianAsync(host, world, first: "Otto");
        var walker = await database.SeedTechnicianAsync(host, world, first: "Walt");
        var inactive = await database.SeedTechnicianAsync(host, world, "inactive", "Ina");
        var foreignTech = await database.SeedTechnicianAsync(host, foreign, first: "Fay");
        var (unlinked, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.TechnicianRoleId, "Una", "Linked");
        host.SetNow(Noon);
        var user = me.Member.UserId;

        var mine = await SeedReadyAsync(world, me);
        await database.AssignAsync(mine.Visit, walker.Profile, user, primary: false);

        // AC-01: other technicians', released, unscheduled, cancelled, other-organization and random visits.
        var hidden = new List<Guid>();
        var released = false;

        foreach (var (technician, status) in new[]
        {
            (other.Profile, "in_progress"),
            (me.Profile, "in_progress"),
            (me.Profile, "unscheduled"),
            (me.Profile, "cancelled"),
        })
        {
            var seeded = await SeedAsync(world, user, technician, status);
            await MakeReadyAsync(seeded.Visit, technician, user);

            if (technician == me.Profile && status == "in_progress" && !released)
            {
                await database.UnassignAsync(seeded.Visit);
                released = true;
            }

            hidden.Add(seeded.Visit);
        }

        var foreignJob = await SeedAsync(foreign, foreignTech.Member.UserId, foreignTech.Profile, "in_progress");
        await MakeReadyAsync(foreignJob.Visit, foreignTech.Profile, foreignTech.Member.UserId);
        hidden.Add(foreignJob.Visit);
        hidden.Add(Guid.NewGuid());

        var states = new Dictionary<Guid, string>();

        foreach (var visit in hidden)
        {
            states[visit] = await database.ProgressStateAsync(visit);
        }

        var bodies = new HashSet<string>();

        foreach (var visit in hidden)
        {
            var detail = await host.GetAsync(Path(visit), me.Cookie);
            var valid = await CompleteAsync(host, visit, me.Cookie, Signed());
            var invalid = await CompleteAsync(host, visit, me.Cookie, NoMethod());

            foreach (var response in new[] { detail, valid, invalid })
            {
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
                bodies.Add(await TechnicianVisitSeed.WithoutTraceAsync(response));
            }
        }

        var notFound = Assert.Single(bodies);
        Assert.DoesNotContain("\"code\"", notFound);
        Assert.Equal(NotAvailable, JsonNode.Parse(notFound)!["title"]!.GetValue<string>());

        foreach (var (visit, state) in states)
        {
            Assert.Equal(state, await database.ProgressStateAsync(visit));
        }

        // AC-02: an actively assigned non-primary technician is refused before the body is validated, but reads the detail.
        var before = await StateAsync(mine);

        foreach (var body in new[] { Signed(), NoMethod() })
        {
            var forbidden = await ReadAsync(await CompleteAsync(host, mine.Visit, walker.Cookie, body), HttpStatusCode.Forbidden);

            Assert.Equal("not_primary_technician", forbidden["code"]!.GetValue<string>());
            Assert.Equal("The primary technician manages this job.", forbidden["title"]!.GetValue<string>());
        }

        var read = await ReadAsync(await host.GetAsync(Path(mine.Visit), walker.Cookie));

        Assert.True(read["completion"]!["ready"]!.GetValue<bool>());
        Assert.Equal("Tess Tech", read["primaryTechnicianName"]!.GetValue<string>());

        // AC-02: no linked profile is a 404 with a code; an inactive profile is a 403 with a code.
        foreach (var body in new[] { Signed(), NoMethod() })
        {
            var unlinkedProblem = await ReadAsync(await CompleteAsync(host, mine.Visit, unlinked, body), HttpStatusCode.NotFound);

            Assert.Equal("technician_profile_not_linked", unlinkedProblem["code"]!.GetValue<string>());
        }

        var inactiveProblem = await ReadAsync(await CompleteAsync(host, mine.Visit, inactive.Cookie, Signed()), HttpStatusCode.Forbidden);
        var inactiveRead = await ReadAsync(await host.GetAsync(Path(mine.Visit), inactive.Cookie), HttpStatusCode.Forbidden);

        Assert.Equal("technician_inactive", inactiveProblem["code"]!.GetValue<string>());
        Assert.Equal("technician_inactive", inactiveRead["code"]!.GetValue<string>());
        Assert.Equal(before, await StateAsync(mine));
    }

    [Fact]
    public async Task Complete_FromInProgressAndPaused_ClosesEntriesStoresTheAcknowledgmentAndRepeatsWithoutWriting()
    {
        var world = await database.SeedWorldAsync();
        await using var host = TechnicianHost.Create(database);
        var me = await database.SeedTechnicianAsync(host, world);
        host.SetNow(Noon);
        var user = me.Member.UserId;
        var job = await SeedReadyAsync(world, me);

        // AC-03: the response is the detail without any acknowledgment data and is never cached.
        var response = await CompleteAsync(host, job.Visit, me.Cookie, Signed());
        var text = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);

        var result = await ReadAsync(response);

        Assert.True(result["changed"]!.GetValue<bool>());
        Assert.Equal("completed", result["visit"]!["status"]!.GetValue<string>());
        Assert.Null(result["visit"]!["time"]!["activeEntry"]);
        Assert.Equal(1800, result["visit"]!["time"]!["workSeconds"]!.GetValue<int>());
        Assert.DoesNotContain("Pat Quill", text);
        Assert.DoesNotContain("ZQCOMMENT", text);
        Assert.DoesNotContain("family_member", text);
        Assert.DoesNotContain("acknowledg", text, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(
            0,
            await database.ScalarAsync<long>(
                "SELECT COUNT(*) FROM visit_time_entries WHERE visit_id = @v AND ended_at IS NULL", ("v", job.Visit)));
        Assert.Equal(
            1,
            await database.ScalarAsync<long>(
                "SELECT COUNT(*) FROM visit_time_entries WHERE visit_id = @v AND entry_type = 'work' AND ended_at = @n", ("v", job.Visit), ("n", Noon)));
        Assert.Equal(
            "completed|true|0|true",
            await database.ScalarAsync<string>(
                "SELECT status::text || '|' || (actual_completed_at = @n)::text || '|' || pause_seconds || '|' || (completion_without_signature_reason IS NULL)::text FROM visits WHERE id = @v",
                ("n", Noon),
                ("v", job.Visit)));
        Assert.Equal(
            1,
            await database.ScalarAsync<long>(
                "SELECT COUNT(*) FROM visit_status_history WHERE visit_id = @v AND from_status = 'in_progress' AND to_status = 'completed' AND reason IS NULL AND changed_by_user_id = @u AND changed_at = @n",
                ("v", job.Visit),
                ("u", user),
                ("n", Noon)));
        Assert.Equal(1, await database.ScalarAsync<long>("SELECT COUNT(*) FROM visit_status_history WHERE visit_id = @v", ("v", job.Visit)));
        Assert.Equal(
            "signed|Pat Quill|family_member|image/png|true|true|Looks good ZQCOMMENT|-|true|true|true",
            await database.ScalarAsync<string>(
                """
                SELECT acknowledgement_method || '|' || signer_name || '|' || signer_relationship || '|' || signature_mime_type || '|' || review_confirmed::text
                    || '|' || accepted::text || '|' || COALESCE(comments, '-') || '|' || COALESCE(absence_reason, '-') || '|' || (signer_contact_id IS NULL)::text
                    || '|' || (recorded_by_user_id = @u)::text || '|' || (signed_at = @n)::text
                FROM customer_signoffs WHERE visit_id = @v
                """,
                ("u", user),
                ("n", Noon),
                ("v", job.Visit)));
        Assert.Equal(Png, await database.ScalarAsync<byte[]>("SELECT signature_content FROM customer_signoffs WHERE visit_id = @v", ("v", job.Visit)));

        // AC-03: one visit.completed audit row (and the work order change of the only visit) with ids and flags only.
        Assert.Equal(
            "visit|true|true|in_progress|completed|" + job.Order + "|1|signed|true|1800|0|true",
            await database.ScalarAsync<string>(
                """
                SELECT entity_type || '|' || (organization_id = @o)::text || '|' || (branch_id = @b AND actor_user_id = @u)::text || '|' || (before_data->>'status')
                    || '|' || (after_data->>'status') || '|' || (metadata->>'workOrderId') || '|' || (metadata->>'visitNumber') || '|' || (metadata->>'acknowledgementMethod')
                    || '|' || (metadata->>'hasSignature') || '|' || (metadata->>'workSeconds') || '|' || (metadata->>'pauseSeconds') || '|' || (metadata->>'workOrderStatusChanged')
                FROM audit_logs WHERE entity_id = @v AND action = 'visit.completed'
                """,
                ("o", world.Org),
                ("b", world.BranchA),
                ("u", user),
                ("v", job.Visit)));
        Assert.Equal(
            "in_progress|completed|" + job.Visit,
            await database.ScalarAsync<string>(
                "SELECT (before_data->>'status') || '|' || (after_data->>'status') || '|' || (metadata->>'visitId') FROM audit_logs WHERE entity_id = @w AND action = 'work_order.status_changed'",
                ("w", job.Order)));

        foreach (var entity in new[] { job.Visit, job.Order })
        {
            var audit = await DispatchSeed.AuditTextAsync(database, entity);

            Assert.DoesNotContain("Pat Quill", audit);
            Assert.DoesNotContain("family_member", audit);
            Assert.DoesNotContain("ZQCOMMENT", audit);
        }

        Assert.DoesNotContain(
            host.Logs.Entries,
            entry => entry.Message.Contains("Pat Quill", StringComparison.Ordinal) || entry.Message.Contains("ZQCOMMENT", StringComparison.Ordinal));
        Assert.Equal(
            0,
            await database.ScalarAsync<long>(
                "SELECT (SELECT COUNT(*) FROM invoices WHERE organization_id = @o) + (SELECT COUNT(*) FROM payments WHERE organization_id = @o) + (SELECT COUNT(*) FROM notifications WHERE organization_id = @o)",
                ("o", world.Org)));

        // AC-05: repeating with the same and with a different valid body writes nothing; edits are refused.
        var completedState = await StateAsync(job);

        foreach (var body in new[] { Signed(), Absent() })
        {
            var repeated = await ReadAsync(await CompleteAsync(host, job.Visit, me.Cookie, body));

            Assert.False(repeated["changed"]!.GetValue<bool>());
            Assert.Equal("completed", repeated["visit"]!["status"]!.GetValue<string>());
        }

        var taskId = await database.ScalarAsync<Guid>("SELECT id FROM visit_checklist_items WHERE visit_id = @v AND is_required", ("v", job.Visit));
        var refusals = new[]
        {
            await host.PostAsync(Path(job.Visit, "/start-job"), me.Cookie),
            await host.PostAsync(Path(job.Visit, "/pause"), me.Cookie),
            await host.SendAsync(HttpMethod.Patch, Path(job.Visit, $"/tasks/{taskId}"), me.Cookie, new JsonObject { ["isCompleted"] = false }),
            await host.UploadAsync(Path(job.Visit, "/evidence"), me.Cookie, Png, "image/png", "after"),
        };

        foreach (var refusal in refusals)
        {
            var problem = await ReadAsync(refusal, HttpStatusCode.Conflict);

            Assert.Equal("visit_status_invalid", problem["code"]!.GetValue<string>());
        }

        Assert.Equal(completedState, await StateAsync(job));

        // AC-04: a paused visit closes its pause entry, adds its whole seconds and ends the history at completed.
        var paused = await SeedReadyAsync(world, me, "paused", entries: "pause");
        var pausedResult = await ReadAsync(await CompleteAsync(host, paused.Visit, me.Cookie, Absent()));

        Assert.True(pausedResult["changed"]!.GetValue<bool>());
        Assert.Equal(1200, pausedResult["visit"]!["time"]!["pauseSeconds"]!.GetValue<int>());
        Assert.Equal(1200, pausedResult["visit"]!["time"]!["workSeconds"]!.GetValue<int>());
        Assert.Equal(1200, await database.ScalarAsync<int>("SELECT pause_seconds FROM visits WHERE id = @v", ("v", paused.Visit)));
        Assert.Equal(
            0,
            await database.ScalarAsync<long>("SELECT COUNT(*) FROM visit_time_entries WHERE visit_id = @v AND ended_at IS NULL", ("v", paused.Visit)));
        Assert.Equal(
            1,
            await database.ScalarAsync<long>(
                "SELECT COUNT(*) FROM visit_time_entries WHERE visit_id = @v AND entry_type = 'pause' AND ended_at = @n", ("v", paused.Visit), ("n", Noon)));
        Assert.Equal(
            1,
            await database.ScalarAsync<long>(
                "SELECT COUNT(*) FROM visit_status_history WHERE visit_id = @v AND from_status = 'paused' AND to_status = 'completed'", ("v", paused.Visit)));
        Assert.Equal(
            "Nobody home",
            await database.ScalarAsync<string>("SELECT completion_without_signature_reason FROM visits WHERE id = @v", ("v", paused.Visit)));
    }

    [Fact]
    public async Task Complete_RefusesEveryOtherStatusAndUnmetRequirementsWithoutWritingAndIgnoresOptionalTasks()
    {
        var world = await database.SeedWorldAsync();
        await using var host = TechnicianHost.Create(database);
        var me = await database.SeedTechnicianAsync(host, world);
        host.SetNow(Noon);
        var user = me.Member.UserId;

        // AC-06: the status guard runs after the body validation and before anything is written.
        foreach (var status in new[] { "assigned", "on_the_way", "needs_correction", "approved" })
        {
            var refused = await SeedReadyAsync(world, me, status, "scheduled", entries: "none");
            var before = await StateAsync(refused);
            var problem = await ReadAsync(await CompleteAsync(host, refused.Visit, me.Cookie, Signed()), HttpStatusCode.Conflict);

            Assert.Equal("visit_status_invalid", problem["code"]!.GetValue<string>());
            Assert.Equal(StatusInvalid, problem["title"]!.GetValue<string>());

            var invalid = await ReadAsync(await CompleteAsync(host, refused.Visit, me.Cookie, NoMethod()), HttpStatusCode.BadRequest);

            Assert.NotNull(invalid["errors"]!["method"]);
            Assert.Equal(before, await StateAsync(refused));
        }

        // AC-07: one required task open, no before photo, no after photo; the detail flags say which one.
        foreach (var (_, sql, flags) in new[]
        {
            ("required-task", "UPDATE visit_checklist_items SET is_completed = false, completed_at = NULL, completed_by_user_id = NULL WHERE visit_id = @v AND is_required", "False|True|True|False"),
            ("no-before", "DELETE FROM visit_evidence WHERE visit_id = @v AND evidence_type = 'before'", "True|False|True|False"),
            ("no-after", "DELETE FROM visit_evidence WHERE visit_id = @v AND evidence_type = 'after'", "True|True|False|False"),
        })
        {
            var job = await SeedReadyAsync(world, me);
            await database.ExecuteAsync(sql, ("v", job.Visit));

            var completion = (await ReadAsync(await host.GetAsync(Path(job.Visit), me.Cookie)))["completion"]!;
            var before = await StateAsync(job);
            var problem = await ReadAsync(await CompleteAsync(host, job.Visit, me.Cookie, Signed()), HttpStatusCode.Conflict);

            Assert.Equal(
                flags,
                string.Join(
                    '|',
                    new[] { "requiredTasksComplete", "hasBeforePhoto", "hasAfterPhoto", "ready" }
                        .Select(key => completion[key]!.GetValue<bool>())));
            Assert.Equal("completion_requirements_unmet", problem["code"]!.GetValue<string>());
            Assert.Equal(RequirementsUnmet, problem["title"]!.GetValue<string>());
            Assert.Equal(before, await StateAsync(job));
        }

        // AC-07: an incomplete optional task and no materials never block the completion.
        var ready = await SeedReadyAsync(world, me);
        var optional = await database.ScalarAsync<long>(
            "SELECT COUNT(*) FROM visit_checklist_items WHERE visit_id = @v AND NOT is_required AND NOT is_completed", ("v", ready.Visit));
        var completed = await ReadAsync(await CompleteAsync(host, ready.Visit, me.Cookie, Signed()));

        Assert.Equal(1, optional);
        Assert.True(completed["changed"]!.GetValue<bool>());
        Assert.Equal(user, await database.ScalarAsync<Guid>("SELECT recorded_by_user_id FROM customer_signoffs WHERE visit_id = @v", ("v", ready.Visit)));
    }

    [Fact]
    public async Task Complete_ValidatesTheAcknowledgmentMatrixAndSignatureFileAndMapsEachMethodToTheSignoff()
    {
        var world = await database.SeedWorldAsync();
        await using var host = TechnicianHost.Create(database);
        var me = await database.SeedTechnicianAsync(host, world);
        host.SetNow(Noon);
        var shared = await SeedReadyAsync(world, me);
        var name181 = new string('n', 181);
        var comment1001 = new string('c', 1001);
        (string Key, string Value)[] signedFields = [("signerName", "Pat Quill"), ("relationship", "customer"), ("reviewConfirmed", "true")];

        // AC-08, AC-10: each invalid body is a 400 with exactly the listed field messages.
        var invalid = new (Func<MultipartFormDataContent> Body, (string Field, string Message)[] Expected)[]
        {
            (() => NoMethod(), [("method", MethodRequired)]),
            (() => Form("handwritten"), [("method", MethodRequired)]),
            (() => Form("signed"), [("signerName", NameRequired), ("relationship", RelationshipInvalid), ("signature", SignatureMissing), ("reviewConfirmed", ReviewRequired)]),
            (() => Form("signed", [("signerName", "   "), ("relationship", "customer"), ("reviewConfirmed", "true")], Png), [("signerName", NameRequired)]),
            (() => Form("signed", [("signerName", name181), ("relationship", "customer"), ("reviewConfirmed", "true")], Png), [("signerName", NameTooLong)]),
            (() => Form("signed", [("signerName", "Pat"), ("relationship", "boss"), ("reviewConfirmed", "true")], Png), [("relationship", RelationshipInvalid)]),
            (() => Form("signed", [("signerName", "Pat"), ("relationship", "customer"), ("reviewConfirmed", "false")], Png), [("reviewConfirmed", ReviewRequired)]),
            (() => Form("signed", [.. signedFields, ("comment", comment1001)], Png), [("comment", CommentTooLong)]),
            (() => Form("customer_absent"), [("comment", ReasonRequired)]),
            (() => Form("customer_absent", [("signerName", "Pat"), ("relationship", "customer"), ("reviewConfirmed", "true"), ("comment", "Away")], Png), [("signerName", NotAllowed), ("relationship", NotAllowed), ("reviewConfirmed", NotAllowed), ("signature", NotAllowed)]),
            (() => Form("customer_refused", [("signerName", name181), ("comment", " ")]), [("signerName", NameTooLong), ("comment", ReasonRequired)]),
            (() => Form("customer_refused", [("relationship", "customer"), ("reviewConfirmed", "false"), ("comment", "No")]), [("relationship", NotAllowed), ("reviewConfirmed", NotAllowed)]),
            (() => Form("remote_confirmation"), [("signerName", NameRequired), ("relationship", RelationshipInvalid), ("comment", ConfirmationRequired), ("reviewConfirmed", ReviewRequired)]),
            (() => Form("remote_confirmation", [.. signedFields, ("comment", comment1001)]), [("comment", CommentTooLong)]),
            (() => Form("remote_confirmation", [.. signedFields, ("comment", "Phone")], Png), [("signature", NotAllowed)]),
            (() => Form("signed", signedFields, Jpeg, "image/jpeg", "signature.jpg"), [("signature", SignatureInvalid)]),
            (() => Form("signed", signedFields, Jpeg), [("signature", SignatureInvalid)]),
            (() => Form("signed", signedFields, "not an image"u8.ToArray()), [("signature", SignatureInvalid)]),
            (() => Form("signed", signedFields, []), [("signature", SignatureInvalid)]),
            (() => Form("signed", signedFields, PngOf((512 * 1024) + 1)), [("signature", SignatureInvalid)]),
            (() => Form("signed", signedFields, Png, "image/jpeg"), [("signature", SignatureInvalid)]),
        };
        var before = await StateAsync(shared);

        foreach (var (body, expected) in invalid)
        {
            var problem = await ReadAsync(await CompleteAsync(host, shared.Visit, me.Cookie, body()), HttpStatusCode.BadRequest);
            var errors = problem["errors"]!.AsObject();

            Assert.Equal(expected.Length, errors.Count);

            foreach (var (field, message) in expected)
            {
                Assert.Equal(message, errors[field]!.AsArray().Single()!.GetValue<string>());
            }
        }

        Assert.Equal(before, await StateAsync(shared));

        // AC-09, AC-10: every valid method stores its mapping on a fresh ready visit.
        var valid = new (string Name, Func<MultipartFormDataContent> Body, string Expected)[]
        {
            ("signed-512KiB", () => Form("signed", [("signerName", "Pat Quill"), ("relationship", "customer"), ("reviewConfirmed", "true"), ("comment", "  Great  ")], PngOf(512 * 1024)), "signed|Pat Quill|customer|true|true|true|Great|-|-"),
            ("signed-no-comment", () => Form("signed", [("signerName", "  Pat  "), ("relationship", "employee"), ("reviewConfirmed", "true")], Png), "signed|Pat|employee|true|true|true|-|-|-"),
            ("absent", () => Absent(" Nobody home "), "customer_absent|-|-|false|false|false|-|Nobody home|Nobody home"),
            ("refused-named", () => Form("customer_refused", [("signerName", "Rita Refuse"), ("comment", "Declined")]), "customer_refused|Rita Refuse|-|false|false|false|-|Declined|Declined"),
            ("refused-unnamed", () => Form("customer_refused", [("signerName", ""), ("comment", "Declined")]), "customer_refused|-|-|false|false|false|-|Declined|Declined"),
            ("remote", () => Form("remote_confirmation", [("signerName", "Rey Remote"), ("relationship", "tenant"), ("reviewConfirmed", "true"), ("comment", "Phone call")]), "remote_confirmation|Rey Remote|tenant|false|true|true|Phone call|-|Phone call"),
        };

        foreach (var (name, body, expected) in valid)
        {
            var job = await SeedReadyAsync(world, me);
            var result = await ReadAsync(await CompleteAsync(host, job.Visit, me.Cookie, body()));

            Assert.True(result["changed"]!.GetValue<bool>(), name);
            Assert.Equal(
                expected,
                await database.ScalarAsync<string>(
                    """
                    SELECT s.acknowledgement_method || '|' || COALESCE(s.signer_name, '-') || '|' || COALESCE(s.signer_relationship, '-') || '|' || (s.signature_content IS NOT NULL)::text
                        || '|' || s.review_confirmed::text || '|' || s.accepted::text || '|' || COALESCE(s.comments, '-') || '|' || COALESCE(s.absence_reason, '-')
                        || '|' || COALESCE(v.completion_without_signature_reason, '-')
                    FROM customer_signoffs s JOIN visits v ON v.id = s.visit_id WHERE s.visit_id = @v
                    """,
                    ("v", job.Visit)));
        }

        Assert.Equal(
            524288,
            await database.ScalarAsync<int>(
                "SELECT octet_length(s.signature_content) FROM customer_signoffs s JOIN visits v ON v.id = s.visit_id JOIN work_orders w ON w.id = v.work_order_id WHERE w.organization_id = @o AND s.signer_name = 'Pat Quill' AND s.comments = 'Great'",
                ("o", world.Org)));
    }

    [Fact]
    public async Task Complete_SetsTheWorkOrderStatusForOneTimeAndRecurringOrdersAndTheDetailPredictsIt()
    {
        var world = await database.SeedWorldAsync();
        await using var host = TechnicianHost.Create(database);
        var me = await database.SeedTechnicianAsync(host, world);
        host.SetNow(Noon);
        var user = me.Member.UserId;

        // AC-14: readiness flags and the primary technician's name; no acknowledgment data.
        var notReady = await SeedAsync(world, user, me.Profile, "in_progress");
        await MakeReadyAsync(notReady.Visit, me.Profile, user);
        await database.ExecuteAsync("DELETE FROM visit_evidence WHERE visit_id = @v AND evidence_type = 'after'", ("v", notReady.Visit));
        await database.ExecuteAsync(
            "UPDATE visit_checklist_items SET is_completed = false WHERE visit_id = @v AND is_required", ("v", notReady.Visit));

        var flags = await ReadAsync(await host.GetAsync(Path(notReady.Visit), me.Cookie));

        Assert.Equal("Tess Tech", flags["primaryTechnicianName"]!.GetValue<string>());
        Assert.Equal(
            "False|True|False|False|True",
            string.Join(
                '|',
                new[] { "requiredTasksComplete", "hasBeforePhoto", "hasAfterPhoto", "ready", "completesWorkOrder" }
                    .Select(key => flags["completion"]![key]!.GetValue<bool>())));

        // AC-11: a one-time order completes with its only visit.
        var single = await SeedReadyAsync(world, me, orderStatus: "scheduled");
        var orderUpdated = await database.ScalarAsync<DateTimeOffset>("SELECT updated_at FROM work_orders WHERE id = @w", ("w", single.Order));

        Assert.True((await ReadAsync(await host.GetAsync(Path(single.Visit), me.Cookie)))["completion"]!["completesWorkOrder"]!.GetValue<bool>());
        await ReadAsync(await CompleteAsync(host, single.Visit, me.Cookie, Signed()));

        Assert.Equal("completed", await OrderStatusAsync(single.Order));
        Assert.True(await database.ScalarAsync<DateTimeOffset>("SELECT updated_at FROM work_orders WHERE id = @w", ("w", single.Order)) > orderUpdated);
        Assert.Equal(
            1,
            await database.ScalarAsync<long>(
                "SELECT COUNT(*) FROM audit_logs WHERE entity_id = @w AND action = 'work_order.status_changed' AND entity_type = 'work_order' AND before_data->>'status' = 'scheduled' AND after_data->>'status' = 'completed' AND branch_id = @b",
                ("w", single.Order),
                ("b", world.BranchA)));
        Assert.Equal(
            1,
            await database.ScalarAsync<long>(
                "SELECT COUNT(*) FROM audit_logs WHERE entity_id = @v AND action = 'visit.completed' AND metadata->>'workOrderStatusChanged' = 'true'", ("v", single.Visit)));

        // AC-12: recurring orders; the detail predicts each outcome before the visit is completed.
        var cases = new (string Name, string OrderStatus, short Count, (int Number, string Status)[] Others, (int Number, bool Predicted, string After)[] Steps)[]
        {
            ("1-of-3-with-unscheduled", "in_progress", 3, [(2, "unscheduled")], [(1, false, "in_progress")]),
            ("3-of-3-then-2", "in_progress", 3, [(2, "in_progress"), (3, "in_progress")], [(3, false, "in_progress"), (2, true, "completed")]),
            ("2-of-2-with-cancelled", "in_progress", 2, [(2, "in_progress")], [(2, true, "completed")]),
            ("approved-for-billing", "approved_for_billing", 2, [(2, "completed")], [(1, false, "approved_for_billing")]),
        };

        foreach (var (name, orderStatus, count, others, steps) in cases)
        {
            // Visit 1 is the cancelled one only in the third case; elsewhere it is the open or completed first occurrence.
            var firstStatus = name == "2-of-2-with-cancelled" ? "cancelled" : name == "3-of-3-then-2" ? "completed" : "in_progress";
            var order = await SeedAsync(world, user, me.Profile, firstStatus, orderStatus, "recurring", count);
            var visits = new Dictionary<int, Guid> { [1] = order.Visit };

            if (firstStatus == "in_progress")
            {
                await MakeReadyAsync(order.Visit, me.Profile, user);
            }

            foreach (var (number, status) in others)
            {
                var open = status == "in_progress";
                var visit = await database.SeedVisitAsync(
                    world.Org,
                    order.Order,
                    number,
                    status,
                    open ? Noon.AddHours(number) : null,
                    open ? Noon.AddHours(number + 1) : null,
                    open ? me.Profile : null,
                    user);

                if (open)
                {
                    await MakeReadyAsync(visit, me.Profile, user);
                }

                visits[number] = visit;
            }

            foreach (var (number, predicted, after) in steps)
            {
                var visit = visits[number];
                var stamp = await database.ScalarAsync<string>("SELECT updated_at::text FROM work_orders WHERE id = @w", ("w", order.Order));
                var detail = await ReadAsync(await host.GetAsync(Path(visit), me.Cookie));

                Assert.True(detail["completion"]!["ready"]!.GetValue<bool>(), name);
                Assert.Equal(predicted, detail["completion"]!["completesWorkOrder"]!.GetValue<bool>());

                var result = await ReadAsync(await CompleteAsync(host, visit, me.Cookie, Absent()));

                Assert.True(result["changed"]!.GetValue<bool>(), name);
                Assert.Equal(after, await OrderStatusAsync(order.Order));
                Assert.Equal(
                    predicted ? 1 : 0,
                    await database.ScalarAsync<long>(
                        "SELECT COUNT(*) FROM audit_logs WHERE entity_id = @w AND action = 'work_order.status_changed'", ("w", order.Order)));
                Assert.Equal(
                    predicted,
                    await database.ScalarAsync<bool>(
                        "SELECT (metadata->>'workOrderStatusChanged')::boolean FROM audit_logs WHERE entity_id = @v AND action = 'visit.completed'", ("v", visit)));

                if (!predicted)
                {
                    Assert.Equal(stamp, await database.ScalarAsync<string>("SELECT updated_at::text FROM work_orders WHERE id = @w", ("w", order.Order)));
                }
            }
        }
    }

    [Fact]
    public async Task Complete_ConcurrentRequestsProduceOneCompletionPerVisitAndOneWorkOrderChange()
    {
        var world = await database.SeedWorldAsync();
        await using var host = TechnicianHost.Create(database);
        var me = await database.SeedTechnicianAsync(host, world);
        var other = await database.SeedTechnicianAsync(host, world, first: "Otto");
        host.SetNow(Noon);
        var user = me.Member.UserId;

        // AC-13: duplicate completions of one visit.
        var job = await SeedReadyAsync(world, me);
        var responses = await Task.WhenAll(
            Enumerable.Range(0, 4).Select(_ => CompleteAsync(host, job.Visit, me.Cookie, Signed())));
        var changes = new List<bool>();

        foreach (var response in responses)
        {
            changes.Add((await ReadAsync(response))["changed"]!.GetValue<bool>());
        }

        Assert.True(changes.Count(changed => changed) == 1, string.Join(',', changes));
        Assert.Equal(1, await database.ScalarAsync<long>("SELECT COUNT(*) FROM visit_status_history WHERE visit_id = @v", ("v", job.Visit)));
        Assert.Equal(1, await database.ScalarAsync<long>("SELECT COUNT(*) FROM customer_signoffs WHERE visit_id = @v", ("v", job.Visit)));
        Assert.Equal(1, await database.AuditCountAsync(job.Visit, "visit.completed"));
        Assert.Equal(1, await database.AuditCountAsync(job.Order, "work_order.status_changed"));

        // AC-13: the last two open visits of one recurring order, completed by two technicians at the same time.
        for (var round = 0; round < 3; round++)
        {
            var order = await SeedAsync(world, user, me.Profile, "in_progress", "in_progress", "recurring", 2);
            var second = await database.SeedVisitAsync(world.Org, order.Order, 2, "in_progress", Noon.AddHours(2), Noon.AddHours(3), other.Profile, user);
            await MakeReadyAsync(order.Visit, me.Profile, user);
            await MakeReadyAsync(second, other.Profile, user);

            var pair = await Task.WhenAll(
                CompleteAsync(host, order.Visit, me.Cookie, Signed()),
                CompleteAsync(host, second, other.Cookie, Signed()));

            foreach (var response in pair)
            {
                Assert.True((await ReadAsync(response))["changed"]!.GetValue<bool>());
            }

            Assert.Equal("completed", await OrderStatusAsync(order.Order));
            Assert.Equal(1, await database.AuditCountAsync(order.Order, "work_order.status_changed"));
            Assert.Equal(
                1,
                await database.ScalarAsync<long>(
                    "SELECT COUNT(*) FROM audit_logs WHERE entity_id = ANY(@v) AND action = 'visit.completed' AND metadata->>'workOrderStatusChanged' = 'true'",
                    ("v", new[] { order.Visit, second })));
            Assert.Equal(
                2,
                await database.ScalarAsync<long>("SELECT COUNT(*) FROM visits WHERE work_order_id = @w AND status = 'completed'", ("w", order.Order)));
        }
    }
}
