using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using FieldOps.Infrastructure.Persistence;
using FieldOps.IntegrationTests.Api;
using FieldOps.IntegrationTests.BillingReview;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.Dispatch;
using FieldOps.IntegrationTests.InvoiceDelivery;
using FieldOps.IntegrationTests.InvoicePayments;
using FieldOps.IntegrationTests.OnlinePayments;
using FieldOps.IntegrationTests.Portal;
using FieldOps.IntegrationTests.PublicRequests;
using FieldOps.IntegrationTests.QuoteLinks;
using FieldOps.IntegrationTests.Quotes;
using FieldOps.IntegrationTests.ServiceRequests;
using FieldOps.IntegrationTests.Sessions;
using FieldOps.IntegrationTests.TechnicianVisits;
using FieldOps.IntegrationTests.Users;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using static FieldOps.IntegrationTests.Notifications.NotificationSeed;

namespace FieldOps.IntegrationTests.Notifications;

/// <summary>
/// One representative producer per event family writes its notification set in the event transaction, with the BR-06 to BR-08
/// snapshot, never for replays or failures, and never with personal data (notifications-center AC-04 to AC-06).
/// </summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class NotificationProducerTests(CompanySettingsDatabaseFixture database)
{
    private sealed record Row(Guid User, string Subject, JsonObject Payload, string Raw);

    private async Task<Row[]> RowsAsync(Guid org, string code, Guid? entity = null)
    {
        var lines = await database.TextsAsync(
            """
            SELECT recipient_user_id::text || '|' || subject || '|' || payload::text
            FROM notifications
            WHERE organization_id = @o AND template_code = @c AND channel = 'in_app' AND status = 'sent'
                AND (CAST(@e AS text) IS NULL OR payload->>'entityId' = CAST(@e AS text))
            ORDER BY recipient_user_id
            """,
            ("o", org),
            ("c", code),
            ("e", entity?.ToString()));

        return [.. lines.Select(line =>
        {
            var parts = line.Split('|', 3);

            return new Row(Guid.Parse(parts[0]), parts[1], JsonNode.Parse(parts[2])!.AsObject(), line);
        })];
    }

    private static void AssertRows(
        Row[] rows, Guid[] recipients, string subject, string description, string kind, string label, string? secondary, Guid? branch, Guid entity)
    {
        Assert.Equal(recipients.Order(), rows.Select(row => row.User).Order());
        Assert.All(rows, row =>
        {
            Assert.Equal(subject, row.Subject);
            Assert.Equal(
                ["branchId", "description", "entityId", "entityKind", "entityLabel", "secondaryLabel", "type"],
                row.Payload.Select(pair => pair.Key).Order().ToArray());
            Assert.Equal(
                (description, kind, label, secondary, branch?.ToString(), entity.ToString()),
                (row.Payload["description"]!.GetValue<string>(),
                    row.Payload["entityKind"]!.GetValue<string>(),
                    row.Payload["entityLabel"]!.GetValue<string>(),
                    row.Payload["secondaryLabel"]?.GetValue<string>(),
                    row.Payload["branchId"]?.GetValue<string>(),
                    row.Payload["entityId"]!.GetValue<string>()));
        });
    }

    private Task<long> CountAsync(Guid org) =>
        database.ScalarAsync<long>("SELECT COUNT(*) FROM notifications WHERE organization_id = @o", ("o", org));

    // AC-04, AC-05, AC-06 (request and message families): public form, staff creation (the actor is excluded) and the portal message
    // (no message text and no contact data), plus the failed submissions that write nothing.
    [Fact]
    public async Task Producers_RequestsAndPortalMessage_WriteTheSnapshotWithoutPersonalDataAndNothingOnFailure()
    {
        // Public form: no actor, no branch; Owner and Dispatcher are notified, Operations Manager and Accounting are not.
        var org = await database.SeedPublicOrgAsync();
        var owner = await database.SeedMemberAsync(org.Id, Owner, "Olive", "Owner");
        var dispatcher = await database.SeedMemberAsync(org.Id, Dispatcher, "Dee", "Dispatch");
        await database.SeedMemberAsync(org.Id, Operations, "Opal", "Ops");
        await database.SeedMemberAsync(org.Id, Accounting, "Ann", "Books");
        await using var publicHost = PublicRequestHost.Create(database);

        var invalid = PublicRequestSeed.ValidBody(org);
        invalid["consent"] = false;
        Assert.Equal(HttpStatusCode.BadRequest, (await publicHost.PostAsync(org.Slug, invalid)).StatusCode);
        Assert.Equal(0L, await CountAsync(org.Id));

        Assert.Equal(HttpStatusCode.Created, (await publicHost.PostAsync(org.Slug, PublicRequestSeed.ValidBody(org))).StatusCode);
        var publicRequest = await database.ScalarAsync<Guid>("SELECT id FROM service_requests WHERE organization_id = @o", ("o", org.Id));
        var rows = await RowsAsync(org.Id, "request.created");
        AssertRows(rows, [owner.UserId, dispatcher.UserId], "New service request", "Ada Lovelace submitted REQ-1.", "request", "REQ-1", null, null, publicRequest);
        Assert.All(rows, row =>
        {
            foreach (var secret in new[] { "visitor@example.com", "555", "Analytical", "Kitchen drain", "Gate code", "Call first" })
            {
                Assert.DoesNotContain(secret, row.Raw, StringComparison.OrdinalIgnoreCase);
            }
        });

        // Staff creation by a Dispatcher: the actor is not notified; a Dispatcher of another branch is not either.
        var world = await database.SeedWorldAsync();
        await using var host = RequestsHost.Create(database);
        var (_, staffOwner) = await host.SignInAsync(database, world.Org, Owner);
        var (actorCookie, actor) = await host.SignInAsync(database, world.Org, Dispatcher, "Dina", "Dispatcher");
        await host.SignInAsync(database, world.Org, Dispatcher, "Bea", "Bravo", world.BranchB);
        var created = await host.SendAsync(
            HttpMethod.Post,
            "/service-requests",
            actorCookie,
            new JsonObject
            {
                ["customerId"] = world.Customer,
                ["contactId"] = world.Contact,
                ["propertyId"] = world.Property,
                ["categoryId"] = world.Category,
                ["serviceId"] = world.Service,
                ["notSure"] = false,
                ["description"] = "Water heater is leaking badly",
                ["urgency"] = "urgent",
                ["hasActiveDamage"] = true,
                ["availability"] = new JsonObject { ["dateMode"] = "asap", ["preferredDate"] = null, ["timeWindow"] = "any", ["schedulingNotes"] = null },
            });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var staffRequest = Guid.Parse((await RequestsHost.ReadAsync(created))["id"]!.GetValue<string>());
        var staffBranch = await database.ScalarAsync<Guid>("SELECT branch_id FROM service_requests WHERE id = @r", ("r", staffRequest));
        AssertRows(
            await RowsAsync(world.Org, "request.created"),
            [staffOwner.UserId],
            "New service request",
            "Carla Customer submitted REQ-1.",
            "request",
            "REQ-1",
            null,
            staffBranch,
            staffRequest);
        Assert.DoesNotContain(actor.UserId, (await RowsAsync(world.Org, "request.created")).Select(row => row.User));

        // Customer portal message: unavailable (409) writes nothing, then one set with names only.
        await using var portalHost = OnlineHost.Create(database);
        var portalWorld = await database.SeedWorldAsync();
        var portalOwner = await database.SeedMemberAsync(portalWorld.Org, Owner, "Pia", "Owner");
        var portalDispatcher = await database.SeedMemberAsync(portalWorld.Org, Dispatcher, "Pete", "Dispatch");
        var portalUser = await database.LinkPortalAsync(portalWorld.Org, portalWorld.Contact);
        var cookie = await PortalApi.CookieAsync(portalHost.Client, portalUser.Email);
        const string Text = "SECRET MESSAGE about the kitchen pipe";

        Assert.Equal(HttpStatusCode.Conflict, (await PortalApi.PostAsync(portalHost.Client, "/portal/messages", cookie, new JsonObject { ["message"] = Text })).StatusCode);
        Assert.Equal(0L, await CountAsync(portalWorld.Org));

        await database.SetOrganizationEmailAsync(portalWorld.Org, "office@acme.test");
        Assert.Equal(HttpStatusCode.BadRequest, (await PortalApi.PostAsync(portalHost.Client, "/portal/messages", cookie, new JsonObject { ["message"] = "  " })).StatusCode);
        Assert.Equal(0L, await CountAsync(portalWorld.Org));
        Assert.Equal(HttpStatusCode.Accepted, (await PortalApi.PostAsync(portalHost.Client, "/portal/messages", cookie, new JsonObject { ["message"] = Text })).StatusCode);

        var messages = await RowsAsync(portalWorld.Org, "customer.message_sent");
        AssertRows(
            messages,
            [portalOwner.UserId, portalDispatcher.UserId],
            "Customer sent a message",
            "Pat Contact from Carla Customer sent a message from the customer portal.",
            "customer",
            "Carla Customer",
            null,
            portalWorld.BranchA,
            portalWorld.Customer);
        Assert.All(messages, row =>
        {
            foreach (var secret in new[] { Text, "SECRET", "carla@example.com", "5551234567", "office@acme.test" })
            {
                Assert.DoesNotContain(secret, row.Raw, StringComparison.OrdinalIgnoreCase);
            }
        });
    }

    // AC-04, AC-05, AC-06 (quote family): approve, decline and question write once per response; a repeat, a refused answer and an
    // invalid answer write nothing; a failing notification write fails the response and leaves no trace (BR-09).
    [Fact]
    public async Task Producers_QuoteResponses_WriteOncePerResponseAndRollBackWithTheEvent()
    {
        var world = await database.SeedQuoteWorldAsync();
        await using var host = RequestsHost.Create(database);
        var (ownerCookie, owner) = await host.SignInAsync(database, world.Org, Owner);
        var (_, dispatcher) = await host.SignInAsync(database, world.Org, Dispatcher, "Dee", "Dispatch");
        await host.SignInAsync(database, world.Org, Operations, "Opal", "Ops");
        await host.SignInAsync(database, world.Org, Accounting, "Ann", "Books");
        var recipients = new[] { owner.UserId, dispatcher.UserId };

        var approve = await QuoteLinkApi.SendAsync(database, host, world, ownerCookie);
        var decline = await QuoteLinkApi.SendAsync(database, host, world, ownerCookie);
        var ask = await QuoteLinkApi.SendAsync(database, host, world, ownerCookie);
        var failing = await QuoteLinkApi.SendAsync(database, host, world, ownerCookie);
        var optional = QuoteLinkApi.OptionalIds(await QuoteLinkApi.ViewAsync(host, approve.Token));

        async Task<(string Quote, string Request, Guid? Branch)> LabelsAsync(SentQuote sent) =>
            (await database.ScalarAsync<string>(
                "SELECT o.quote_prefix || '-' || q.quote_number FROM quotes q JOIN organizations o ON o.id = q.organization_id WHERE q.id = @q", ("q", sent.QuoteId)),
                await database.ScalarAsync<string>(
                    "SELECT o.request_prefix || '-' || r.request_number FROM service_requests r JOIN organizations o ON o.id = r.organization_id WHERE r.id = @r", ("r", sent.RequestId)),
                await database.ScalarAsync<Guid?>("SELECT COALESCE(q.branch_id, r.branch_id) FROM quotes q JOIN service_requests r ON r.id = q.request_id WHERE q.id = @q", ("q", sent.QuoteId)));

        // Approval with one optional line: the approved total in the document currency.
        var approved = await QuoteLinkApi.PostAsync(host, "approve", QuoteLinkApi.Body(approve.Token, ("selectedOptionalLineIds", QuoteLinkApi.Ids(optional[0])), ("acceptTerms", true)));
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        var (quoteLabel, requestLabel, branch) = await LabelsAsync(approve);
        AssertRows(
            await RowsAsync(world.Org, "quote.approved", approve.QuoteId),
            recipients,
            $"Quote {quoteLabel} was approved",
            "Carla Customer approved the quote for 324.75 USD.",
            "quote",
            quoteLabel,
            requestLabel,
            branch,
            approve.QuoteId);

        // A repeat, a different answer after the final one and an invalid answer write nothing.
        var written = await CountAsync(world.Org);
        Assert.Equal(HttpStatusCode.OK, (await QuoteLinkApi.PostAsync(host, "approve", QuoteLinkApi.Body(approve.Token, ("selectedOptionalLineIds", QuoteLinkApi.Ids()), ("acceptTerms", true)))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await QuoteLinkApi.PostAsync(host, "decline", QuoteLinkApi.Body(approve.Token, ("reason", "Changed my mind")))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await QuoteLinkApi.PostAsync(host, "decline", QuoteLinkApi.Body(decline.Token, ("reason", "  ")))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await QuoteLinkApi.PostAsync(host, "clarification", QuoteLinkApi.Body(ask.Token, ("message", "  ")))).StatusCode);
        Assert.Equal(written, await CountAsync(world.Org));

        // Decline: the reason is never in the notification.
        Assert.Equal(HttpStatusCode.OK, (await QuoteLinkApi.PostAsync(host, "decline", QuoteLinkApi.Body(decline.Token, ("reason", "SECRET REASON too expensive")))).StatusCode);
        var (declinedLabel, declinedRequest, declinedBranch) = await LabelsAsync(decline);
        var declinedRows = await RowsAsync(world.Org, "quote.rejected", decline.QuoteId);
        AssertRows(declinedRows, recipients, $"Quote {declinedLabel} was declined", "Carla Customer declined the quote.", "quote", declinedLabel, declinedRequest, declinedBranch, decline.QuoteId);
        Assert.All(declinedRows, row => Assert.DoesNotContain("SECRET", row.Raw, StringComparison.Ordinal));

        // Clarification: one question per version, so a second one writes nothing.
        Assert.Equal(HttpStatusCode.OK, (await QuoteLinkApi.PostAsync(host, "clarification", QuoteLinkApi.Body(ask.Token, ("message", "SECRET QUESTION about parking")))).StatusCode);
        var (askedLabel, askedRequest, askedBranch) = await LabelsAsync(ask);
        var askedRows = await RowsAsync(world.Org, "quote.clarification_requested", ask.QuoteId);
        AssertRows(askedRows, recipients, $"Question about quote {askedLabel}", "Carla Customer requested a clarification.", "quote", askedLabel, askedRequest, askedBranch, ask.QuoteId);
        Assert.All(askedRows, row => Assert.DoesNotContain("SECRET", row.Raw, StringComparison.Ordinal));
        written = await CountAsync(world.Org);
        Assert.Equal(HttpStatusCode.OK, (await QuoteLinkApi.PostAsync(host, "clarification", QuoteLinkApi.Body(ask.Token, ("message", "Another question")))).StatusCode);
        Assert.Equal(written, await CountAsync(world.Org));

        // AC-06: a failing notification write fails the whole response: no response row, no status change, no notification.
        var interceptor = new FailOnNotificationInterceptor();
        await using var session = SessionTestHost.Create(database.ConnectionString);
        using var failingClient = SessionApi.CreateClient(session.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.ConfigureDbContext<FieldOpsDbContext>(options => options.AddInterceptors(interceptor)))));

        async Task<HttpResponseMessage> ApproveAsync()
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{QuoteLinkApi.Base}/approve")
            {
                Content = new StringContent(
                    QuoteLinkApi.Body(failing.Token, ("selectedOptionalLineIds", QuoteLinkApi.Ids()), ("acceptTerms", true)).ToJsonString(),
                    Encoding.UTF8,
                    new MediaTypeHeaderValue("application/json")),
            };
            request.Headers.Add(TestClientIpStartupFilter.HeaderName, SessionApi.NewClientIp());

            return await failingClient.SendAsync(request);
        }

        interceptor.Armed = true;
        Assert.Equal(HttpStatusCode.InternalServerError, (await ApproveAsync()).StatusCode);
        interceptor.Armed = false;
        Assert.Equal(0L, await database.ResponseCountAsync(failing.QuoteId));
        Assert.Equal("sent", await database.QuoteStatusAsync(failing.QuoteId));
        Assert.Empty(await RowsAsync(world.Org, "quote.approved", failing.QuoteId));
        Assert.Equal(HttpStatusCode.OK, (await ApproveAsync()).StatusCode);
        Assert.Equal(2, (await RowsAsync(world.Org, "quote.approved", failing.QuoteId)).Length);
    }

    // AC-04, AC-05, AC-06 (visit and job families): dispatch and reassignment by staff, then travel, start, pause and completion by the
    // technician; repeated and refused actions write nothing.
    [Fact]
    public async Task Producers_VisitAndJobEvents_NotifyTheRoleSetsAndSkipRepeats()
    {
        var world = await database.SeedWorldAsync();
        await using var host = RequestsHost.Create(database);
        var (ownerCookie, owner) = await host.SignInAsync(database, world.Org, Owner);
        var (_, ops) = await host.SignInAsync(database, world.Org, Operations, "Opal", "Ops");
        var (_, dispatcher) = await host.SignInAsync(database, world.Org, Dispatcher, "Dee", "Dispatch");
        var (_, accounting) = await host.SignInAsync(database, world.Org, Accounting, "Ann", "Books");
        await host.SignInAsync(database, world.Org, Viewer, "Vic", "Viewer");
        await host.SignInAsync(database, world.Org, Dispatcher, "Bea", "Bravo", world.BranchB);
        var prefix = await database.PrefixAsync(world.Org);

        // visit.assigned: the acting Owner is excluded; a reassignment writes again; an identical request and an unassignment do not.
        var ann = await database.SeedAvailableTechAsync(world.Org, world.BranchA, "Ann", "Alpha");
        var bob = await database.SeedAvailableTechAsync(world.Org, world.BranchA, "Bob", "Bravo");
        var order = await database.SeedOrderAsync(world, owner.UserId);
        var slot = DispatchSeed.Future(4, 9);
        var token = await DispatchSeed.TokenAsync(host, ownerCookie, order.Visit);

        var scheduled = await DispatchSeed.ReadAsync(await DispatchSeed.PutAsync(host, ownerCookie, order.Visit, DispatchSeed.Body(slot, token)));
        Assert.Equal(0L, await CountAsync(world.Org));

        var assigned = await DispatchSeed.ReadAsync(await DispatchSeed.PutAsync(
            host, ownerCookie, order.Visit, DispatchSeed.Body(slot, scheduled["visit"]!["updatedAt"]!.GetValue<string>(), technicians: [ann], primary: ann)));
        var label = $"{prefix}-{order.Number}";
        var when = slot.ToString("MMM d, h:mm tt", CultureInfo.InvariantCulture);
        AssertRows(
            await RowsAsync(world.Org, "visit.assigned", order.Visit),
            [ops.UserId, dispatcher.UserId],
            "Visit assigned",
            $"{label} was assigned to Ann Alpha for {when}.",
            "visit",
            label,
            "Visit 1",
            world.BranchA,
            order.Visit);

        var identical = await DispatchSeed.PutAsync(
            host, ownerCookie, order.Visit, DispatchSeed.Body(slot, assigned["visit"]!["updatedAt"]!.GetValue<string>(), technicians: [ann], primary: ann));
        Assert.Equal(HttpStatusCode.OK, identical.StatusCode);
        Assert.Equal(2L, await CountAsync(world.Org));

        var reassigned = await DispatchSeed.ReadAsync(await DispatchSeed.PutAsync(
            host, ownerCookie, order.Visit, DispatchSeed.Body(slot, assigned["visit"]!["updatedAt"]!.GetValue<string>(), technicians: [bob], primary: bob)));
        Assert.Equal("assigned", reassigned["visit"]!["status"]!.GetValue<string>());
        Assert.Equal(4L, await CountAsync(world.Org));
        Assert.Contains(
            (await RowsAsync(world.Org, "visit.assigned", order.Visit)).Select(row => row.Payload["description"]!.GetValue<string>()),
            description => description.Contains("Bob Bravo", StringComparison.Ordinal));

        // The technician side: travel, repeat, start, repeat, pause, repeat.
        await using var technicianHost = TechnicianHost.Create(database);
        var me = await database.SeedTechnicianAsync(technicianHost, world);
        var noon = TechnicianVisitSeed.Utc("2026-06-10T12:00:00Z");
        technicianHost.SetNow(noon);
        var job = await database.SeedJobAsync(world, me.Member.UserId, me.Profile, "assigned", noon.AddHours(1), 60, "Fix drain");
        var jobLabel = $"{prefix}-{job.Number}";
        var everyVisitRole = new[] { owner.UserId, ops.UserId, dispatcher.UserId };

        string Path(string suffix) => $"/technician/visits/{job.Visit}/{suffix}";

        await TechnicianVisitSeed.ReadAsync(await technicianHost.PostAsync(Path("start-travel"), me.Cookie));
        AssertRows(
            await RowsAsync(world.Org, "visit.on_the_way", job.Visit),
            everyVisitRole,
            "Technician on the way",
            $"Tess Tech is on the way to {jobLabel}.",
            "visit",
            jobLabel,
            "Visit 1",
            world.BranchA,
            job.Visit);
        await technicianHost.PostAsync(Path("start-travel"), me.Cookie);
        Assert.Equal(3, (await RowsAsync(world.Org, "visit.on_the_way", job.Visit)).Length);

        await TechnicianVisitSeed.ReadAsync(await technicianHost.PostAsync(Path("arrive"), me.Cookie));
        var beforeStart = await CountAsync(world.Org);
        await TechnicianVisitSeed.ReadAsync(await technicianHost.PostAsync(Path("start-job"), me.Cookie));
        AssertRows(await RowsAsync(world.Org, "visit.started", job.Visit), everyVisitRole, "Visit started", $"Tess Tech started {jobLabel}.", "visit", jobLabel, "Visit 1", world.BranchA, job.Visit);
        Assert.Equal(beforeStart + 3, await CountAsync(world.Org));
        await technicianHost.PostAsync(Path("start-job"), me.Cookie);
        Assert.Equal(beforeStart + 3, await CountAsync(world.Org));

        await TechnicianVisitSeed.ReadAsync(await technicianHost.PostAsync(Path("pause"), me.Cookie));
        AssertRows(await RowsAsync(world.Org, "visit.paused", job.Visit), everyVisitRole, "Visit paused", $"Tess Tech paused {jobLabel}.", "visit", jobLabel, "Visit 1", world.BranchA, job.Visit);
        await technicianHost.PostAsync(Path("pause"), me.Cookie);
        Assert.Equal(beforeStart + 6, await CountAsync(world.Org));

        // job.completed: a refused completion writes nothing; the completion that moves the work order to completed notifies accounting too.
        var done = await database.SeedOrderAsync(
            world, me.Member.UserId, status: "in_progress", title: "Fix drain", visitStatus: "in_progress", start: noon, end: noon.AddHours(1));
        await database.AssignAsync(done.Visit, me.Profile, me.Member.UserId);
        var form = () => new MultipartFormDataContent { { new StringContent("customer_absent"), "method" }, { new StringContent("Nobody home"), "comment" } };
        var doneLabel = $"{prefix}-{done.Number}";

        var refused = await technicianHost.PostFormAsync($"/technician/visits/{done.Visit}/complete", me.Cookie, form());
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Empty(await RowsAsync(world.Org, "job.completed"));

        await database.ExecuteAsync(
            "INSERT INTO visit_checklist_items (visit_id, label, is_required, is_completed, completed_by_user_id, completed_at, sort_order) VALUES (@v, 'Shut off water', true, true, @u, @c, 0)",
            ("v", done.Visit),
            ("u", me.Member.UserId),
            ("c", noon.AddMinutes(-15)));
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4 };
        await database.ExecuteAsync(
            "INSERT INTO visit_evidence (visit_id, file_name, content, mime_type, size_bytes, evidence_type, uploaded_by_user_id) VALUES (@v, 'b.png', @c, 'image/png', @n, 'before', @u), (@v, 'a.png', @c, 'image/png', @n, 'after', @u)",
            ("v", done.Visit),
            ("c", png),
            ("n", (long)png.Length),
            ("u", me.Member.UserId));
        await database.SeedEntryAsync(done.Visit, me.Profile, "work", noon.AddMinutes(-30));
        await database.ExecuteAsync("UPDATE visits SET actual_started_at = @a WHERE id = @v", ("a", noon.AddMinutes(-40)), ("v", done.Visit));

        await TechnicianVisitSeed.ReadAsync(await technicianHost.PostFormAsync($"/technician/visits/{done.Visit}/complete", me.Cookie, form()));
        AssertRows(
            await RowsAsync(world.Org, "job.completed", done.Order),
            [.. everyVisitRole, accounting.UserId],
            "Job completed",
            $"Tess Tech completed {doneLabel}.",
            "work_order",
            doneLabel,
            null,
            world.BranchA,
            done.Order);
    }

    // AC-04, AC-05, AC-06 (invoice and payment families): send (not resend), manual payment, online payment (first application only) and
    // the bank transfer report (not its repeat), with the acting staff member excluded.
    [Fact]
    public async Task Producers_InvoiceAndPaymentEvents_NotifyBillingRolesAndSkipReplays()
    {
        var world = await database.SeedWorldAsync();
        await using var host = OnlineHost.Create(database);
        var (ownerCookie, owner) = await host.SignInAsync(database, world.Org, Owner);
        var receiver = await database.SeedReceiverAsync(world.Org);
        await host.SignInAsync(database, world.Org, Dispatcher, "Dee", "Dispatch");
        await host.SignInAsync(database, world.Org, Operations, "Opal", "Ops");
        await database.SeedBankAsync(world.Org);
        var billing = new[] { owner.UserId, receiver.UserId };

        // invoice.sent: the acting Owner is excluded; the resend and a repeated send write nothing.
        var draft = await database.GenerateDraftAsync((method, path, body) => host.SendAsync(method, path, ownerCookie, body), world, owner.UserId);
        var detailBefore = await BillingSeed.ReadAsync(await host.SendAsync(HttpMethod.Get, InvoiceApi.Path(draft.Id), ownerCookie));
        var stamp = detailBefore["updatedAt"]!.GetValue<string>();
        var sent = await BillingSeed.ReadAsync(await host.SendAsync(
            HttpMethod.Post, InvoiceApi.Path(draft.Id, "/send"), ownerCookie, InvoiceApi.Body("net_30", "carla@example.com", "Please pay soon", stamp)));
        var total = sent["invoice"]!["totals"]!["total"]!.GetValue<decimal>().ToString("N2", CultureInfo.InvariantCulture);
        var invoiceLabel = await database.ScalarAsync<string>(
            "SELECT o.invoice_prefix || '-' || i.invoice_number FROM invoices i JOIN organizations o ON o.id = i.organization_id WHERE i.id = @i", ("i", draft.Id));
        AssertRows(
            await RowsAsync(world.Org, "invoice.sent", draft.Id),
            [receiver.UserId],
            $"Invoice {invoiceLabel} was sent",
            $"{invoiceLabel} was sent to Carla Customer for {total} USD.",
            "invoice",
            invoiceLabel,
            null,
            world.BranchA,
            draft.Id);

        var sentStamp = sent["invoice"]!["updatedAt"]!.GetValue<string>();
        await BillingSeed.ReadAsync(await host.SendAsync(HttpMethod.Post, InvoiceApi.Path(draft.Id, "/resend-email"), ownerCookie, InvoiceApi.Resend(sentStamp)));
        await host.SendAsync(HttpMethod.Post, InvoiceApi.Path(draft.Id, "/send"), ownerCookie, InvoiceApi.Body("net_30", "carla@example.com", "Please pay soon", sentStamp));
        Assert.Single(await RowsAsync(world.Org, "invoice.sent"));

        // payment.received (manual): the acting Owner is excluded; the same idempotency key writes nothing again.
        var manual = await database.SeedInvoiceAsync(world, owner.UserId, new InvoiceSpec { Total = 100m });
        var key = Guid.NewGuid();
        var stampOfManual = await database.StampAsync(manual.Id);
        var body = PaymentApi.Body(key, 40m, HubSeed.Day(0), "cash", "CHECK 9981 SECRET", receiver.UserId, false, "SECRET NOTE", stampOfManual);
        await PaymentApi.ReadAsync(await host.SendAsync(HttpMethod.Post, PaymentApi.Path(manual.Id), ownerCookie, body), HttpStatusCode.Created);
        var manualRows = await RowsAsync(world.Org, "payment.received", manual.Id);
        AssertRows(
            manualRows,
            [receiver.UserId],
            "Payment received",
            $"Payment of 40.00 USD was recorded for invoice {manual.Display}.",
            "invoice",
            manual.Display,
            null,
            world.BranchA,
            manual.Id);
        Assert.All(manualRows, row => Assert.DoesNotContain("SECRET", row.Raw, StringComparison.Ordinal));
        await host.SendAsync(HttpMethod.Post, PaymentApi.Path(manual.Id), ownerCookie, body);
        Assert.Single(await RowsAsync(world.Org, "payment.received", manual.Id));

        // payment.received (online): no actor, so every billing role; a replayed event and a second event of the same attempt write nothing.
        var online = await database.InvoiceAsync(world, owner.UserId);
        var (attempt, intent) = await OnlineApi.StartCardAsync(host.Client, online);
        var eventId = $"evt_{Guid.NewGuid():N}";
        var succeeded = OnlineApi.Succeeded(eventId, intent, 357.28m, OnlineApi.Metadata(attempt, world.Org, online.Id));
        Assert.Equal(HttpStatusCode.OK, (await OnlineApi.DeliverAsync(host.Client, succeeded)).StatusCode);
        AssertRows(
            await RowsAsync(world.Org, "payment.received", online.Id),
            billing,
            "Payment received",
            $"Payment of 357.28 USD was recorded for invoice {online.Display}.",
            "invoice",
            online.Display,
            null,
            world.BranchA,
            online.Id);
        Assert.Equal(HttpStatusCode.OK, (await OnlineApi.DeliverAsync(host.Client, succeeded)).StatusCode);
        var secondEvent = OnlineApi.Succeeded($"evt_{Guid.NewGuid():N}", intent, 357.28m, OnlineApi.Metadata(attempt, world.Org, online.Id));
        Assert.Equal(HttpStatusCode.OK, (await OnlineApi.DeliverAsync(host.Client, secondEvent)).StatusCode);
        Assert.Equal(2, (await RowsAsync(world.Org, "payment.received", online.Id)).Length);

        // invoice.bank_transfer_reported: a customer action; the repeated report writes nothing.
        var transfer = await database.InvoiceAsync(world, owner.UserId);
        var bodyOfTransfer = OnlineApi.KeyBody(transfer.Token);
        await BillingSeed.ReadAsync(await OnlineApi.PostAsync(host.Client, "payments/bank-transfer-notice", bodyOfTransfer), HttpStatusCode.Created);
        AssertRows(
            await RowsAsync(world.Org, "invoice.bank_transfer_reported", transfer.Id),
            billing,
            "Bank transfer reported",
            $"Carla Customer reported a bank transfer of 357.28 USD for {transfer.Display}.",
            "invoice",
            transfer.Display,
            null,
            world.BranchA,
            transfer.Id);
        await OnlineApi.PostAsync(host.Client, "payments/bank-transfer-notice", bodyOfTransfer);
        Assert.Equal(2, (await RowsAsync(world.Org, "invoice.bank_transfer_reported", transfer.Id)).Length);
    }

    private sealed class FailOnNotificationInterceptor : SaveChangesInterceptor
    {
        public volatile bool Armed;

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            Check(eventData);

            return result;
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Check(eventData);

            return ValueTask.FromResult(result);
        }

        private void Check(DbContextEventData eventData)
        {
            if (Armed && eventData.Context!.ChangeTracker.Entries<FieldOps.Domain.Notifications.Notification>().Any(entry => entry.State == EntityState.Added))
            {
                throw new InvalidOperationException("Injected failure while saving the notifications.");
            }
        }
    }
}
