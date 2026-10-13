using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.BillingReview;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.OnlinePayments;
using FieldOps.IntegrationTests.PasswordResets;
using FieldOps.IntegrationTests.ServiceRequests;
using FieldOps.IntegrationTests.Users;

namespace FieldOps.IntegrationTests.Portal;

/// <summary>Portal service requests, reschedule requests, properties and messages (customer portal BR-27, BR-28, BR-32, BR-33, BR-35, AC-18, AC-19, AC-23 … AC-25, AC-28).</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class PortalRequestAndPropertyTests(CompanySettingsDatabaseFixture database)
{
    [Fact]
    public async Task PortalServiceRequestsAndReschedule_CreateScopedRequestsAndStoreTheRescheduleWithoutChangingTheVisit()
    {
        await using var host = OnlineHost.Create(database);
        var world = await database.SeedWorldAsync();
        var staff = await database.SeedUserAsync();
        var other = await database.SeedSiblingAsync(world);
        await database.SetOrganizationEmailAsync(world.Org, "office@acme.test");
        var user = await database.LinkPortalAsync(world.Org, world.Contact);
        var cookie = await PortalApi.CookieAsync(host.Client, user.Email);
        var contactEmail = await database.ScalarAsync<string>("SELECT email FROM customer_contacts WHERE id = @c", ("c", world.Contact));

        // The public form configuration of the session organization.
        var form = await PortalApi.ReadAsync(await PortalApi.GetAsync(host.Client, "/portal/service-request-form", cookie));
        Assert.Equal("Drain cleaning", form["categories"]![0]!["services"]![0]!["name"]!.GetValue<string>());

        // An existing property: the customer and contact come from the session, the contact is the guest snapshot.
        var created = await PortalApi.ReadAsync(
            await PortalApi.SubmitRequestAsync(host.Client, cookie, PortalApi.RequestBody(world, new JsonObject { ["propertyId"] = world.Property.ToString() })),
            HttpStatusCode.Created);
        var requestId = created["requestId"]!.GetValue<Guid>();
        Assert.False(string.IsNullOrEmpty(created["requestNumber"]!.GetValue<string>()));
        Assert.Equal(
            $"portal|{world.Customer}|{world.Contact}|{world.Property}|Pat Contact|{contactEmail}|new",
            await database.ScalarAsync<string>(
                "SELECT source || '|' || customer_id || '|' || contact_id || '|' || property_id || '|' || guest_name || '|' || guest_email || '|' || status::text FROM service_requests WHERE id = @r",
                ("r", requestId)));
        Assert.Equal(1, await database.CountAsync("request_status_history", "request_id = @r", ("r", requestId)));
        Assert.Equal(
            user.UserId,
            await database.ScalarAsync<Guid>("SELECT actor_user_id FROM audit_logs WHERE entity_id = @r AND action = 'service_request.created'", ("r", requestId)));
        await host.Sender.WaitForAttemptsAsync(1);
        Assert.Equal(contactEmail, host.Sender.Messages[0].To);

        // A new property is created for the customer, never primary while another one is.
        var newProperty = new JsonObject
        {
            ["propertyType"] = "home",
            ["addressLine1"] = "7 Oak Ave",
            ["addressLine2"] = null,
            ["city"] = "Austin",
            ["state"] = "TX",
            ["postalCode"] = "78702",
            ["accessInstructions"] = "Side gate",
        };
        var second = await PortalApi.ReadAsync(
            await PortalApi.SubmitRequestAsync(host.Client, cookie, PortalApi.RequestBody(world, new JsonObject { ["newProperty"] = newProperty })),
            HttpStatusCode.Created);
        Assert.Equal(
            "false|true",
            await database.ScalarAsync<string>(
                """
                SELECT p.is_primary::text || '|' || (r.property_id = p.id AND p.customer_id = r.customer_id)::text
                FROM service_requests r JOIN properties p ON p.address_line1 = '7 Oak Ave' AND p.customer_id = r.customer_id WHERE r.id = @r
                """,
                ("r", second["requestId"]!.GetValue<Guid>())));

        // A property of another customer is a field error and nothing is created.
        var before = await database.CountAsync("service_requests", "organization_id = @o", ("o", world.Org));
        var foreign = await BillingSeed.ProblemAsync(
            await PortalApi.SubmitRequestAsync(host.Client, cookie, PortalApi.RequestBody(world, new JsonObject { ["propertyId"] = other.Property.ToString() })),
            HttpStatusCode.BadRequest,
            errorKey: "property.propertyId");
        Assert.Equal("Select one of your properties.", foreign["errors"]!["property.propertyId"]![0]!.GetValue<string>());
        Assert.Equal(before, await database.CountAsync("service_requests", "organization_id = @o", ("o", world.Org)));

        // The detail carries no internal data.
        var detail = await PortalApi.ReadAsync(await PortalApi.GetAsync(host.Client, $"/portal/requests/{requestId}", cookie));
        Assert.Equal("Submitted", detail["statusLabel"]!.GetValue<string>());
        Assert.Equal("asap", detail["availability"]!["mode"]!.GetValue<string>());
        Assert.Empty(detail["availability"]!["dates"]!.AsArray());
        Assert.Equal(world.Property, detail["property"]!["id"]!.GetValue<Guid>());
        Assert.Null(detail["quote"]);
        Assert.Equal(
            ["availability", "categoryName", "description", "displayNumber", "hasActiveDamage", "id", "property", "propertyName", "quote", "serviceName", "status", "statusLabel", "submittedOn", "title", "urgency"],
            detail.AsObject().Select(member => member.Key).Order().ToArray());

        // Reschedule: stored once per scheduled start, the visit is never changed.
        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var scheduled = await database.SeedVisitAsync(world, staff, "Water heater", "scheduled", now.AddDays(3));
        var onTheWay = await database.SeedVisitAsync(world, staff, "Roof leak", "on_the_way", now.AddMinutes(30), orderStatus: "in_progress");
        var path = $"/portal/appointments/{scheduled.Visit}/reschedule-requests";
        var body = new JsonObject
        {
            ["preferredDate"] = today.AddDays(5).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["timeWindow"] = "morning",
            ["reason"] = "Out of town",
        };
        var visitState = await VisitStateAsync(scheduled.Visit);

        var requested = await PortalApi.ReadAsync(await PortalApi.PostAsync(host.Client, path, cookie, body), HttpStatusCode.Created);
        Assert.Equal(today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), requested["requestedOn"]!.GetValue<string>());
        Assert.Equal(visitState, await VisitStateAsync(scheduled.Visit));
        Assert.True(await database.ScalarAsync<bool>(
            "SELECT original_scheduled_start = (SELECT scheduled_start FROM visits WHERE id = @v) FROM visit_reschedule_requests WHERE visit_id = @v", ("v", scheduled.Visit)));
        Assert.Equal(1, await database.CountAuditLogsAsync(world.Org, "visit.reschedule_requested"));
        await host.Sender.WaitForAttemptsAsync(2);
        var mail = host.Sender.Messages[^1];
        Assert.Equal("office@acme.test", mail.To);
        Assert.StartsWith("Reschedule requested for ", mail.Subject, StringComparison.Ordinal);
        Assert.Contains("Out of town", mail.TextBody, StringComparison.Ordinal);
        var appointment = await PortalApi.ReadAsync(await PortalApi.GetAsync(host.Client, $"/portal/appointments/{scheduled.Visit}", cookie));
        Assert.False(appointment["canRequestReschedule"]!.GetValue<bool>());
        Assert.Equal(today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), appointment["rescheduleRequestedOn"]!.GetValue<string>());

        await BillingSeed.ProblemAsync(await PortalApi.PostAsync(host.Client, path, cookie, body), HttpStatusCode.Conflict, "reschedule_already_requested");
        var invalid = await BillingSeed.ProblemAsync(
            await PortalApi.PostAsync(host.Client, path, cookie, new JsonObject { ["preferredDate"] = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), ["timeWindow"] = "noon", ["reason"] = " " }),
            HttpStatusCode.BadRequest);
        Assert.Equal(["preferredDate", "reason", "timeWindow"], invalid["errors"]!.AsObject().Select(member => member.Key).Order().ToArray());

        // Staff move the visit: the old request no longer counts and a new one is allowed.
        await database.ExecuteAsync(
            "UPDATE visits SET scheduled_start = scheduled_start + interval '1 hour', scheduled_end = scheduled_end + interval '1 hour' WHERE id = @v", ("v", scheduled.Visit));
        var moved = await PortalApi.ReadAsync(await PortalApi.GetAsync(host.Client, $"/portal/appointments/{scheduled.Visit}", cookie));
        Assert.True(moved["canRequestReschedule"]!.GetValue<bool>());
        Assert.Null(moved["rescheduleRequestedOn"]);
        Assert.Equal(HttpStatusCode.Created, (await PortalApi.PostAsync(host.Client, path, cookie, body)).StatusCode);
        Assert.Equal(2, await database.CountAsync("visit_reschedule_requests", "visit_id = @v", ("v", scheduled.Visit)));

        // A visit on the way cannot be rescheduled online; the sixth write of the user in the window is a 429.
        await BillingSeed.ProblemAsync(
            await PortalApi.PostAsync(host.Client, $"/portal/appointments/{onTheWay.Visit}/reschedule-requests", cookie, body),
            HttpStatusCode.Conflict,
            "reschedule_not_available");
        var limited = await PortalApi.PostAsync(host.Client, path, cookie, body);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.NotNull(limited.Headers.RetryAfter);

        // No reason text, address or email in the audit trail or the logs.
        var trail = await database.OrgAuditTextAsync(world.Org) + new CapturingLogs(host.Logs).AllText();
        Assert.DoesNotContain("Out of town", trail, StringComparison.Ordinal);
        Assert.DoesNotContain("7 Oak Ave", trail, StringComparison.Ordinal);
        Assert.DoesNotContain(contactEmail, trail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PortalPropertiesAndMessages_CreateEditWithConcurrencyAndSendWithoutLeakingTheText()
    {
        await using var host = OnlineHost.Create(database);
        var world = await database.SeedWorldAsync();
        var user = await database.LinkPortalAsync(world.Org, world.Contact);
        var cookie = await PortalApi.CookieAsync(host.Client, user.Email);
        var contactEmail = await database.ScalarAsync<string>("SELECT email FROM customer_contacts WHERE id = @c", ("c", world.Contact));

        // Add a property: non-primary while another one is primary, no branch, US only, no address in the audit.
        var create = new JsonObject
        {
            ["name"] = "Cabin",
            ["addressLine1"] = "9 Pine Rd",
            ["addressLine2"] = null,
            ["city"] = "Austin",
            ["stateRegion"] = "TX",
            ["postalCode"] = "78701",
            ["countryCode"] = "US",
            ["accessInstructions"] = "Gate 1",
        };
        var invalid = await BillingSeed.ProblemAsync(
            await PortalApi.PostAsync(host.Client, "/portal/properties", cookie, new JsonObject { ["name"] = "Bad", ["addressLine1"] = "1 A St", ["city"] = "Austin", ["stateRegion"] = "ZZ", ["postalCode"] = "78701", ["countryCode"] = "CA" }),
            HttpStatusCode.BadRequest);
        Assert.Equal(["countryCode", "stateRegion"], invalid["errors"]!.AsObject().Select(member => member.Key).Order().ToArray());
        var added = await PortalApi.ReadAsync(await PortalApi.PostAsync(host.Client, "/portal/properties", cookie, create), HttpStatusCode.Created);
        var propertyId = added["id"]!.GetValue<Guid>();
        Assert.False(added["isPrimary"]!.GetValue<bool>());
        Assert.Equal("US", added["countryCode"]!.GetValue<string>());
        Assert.True(await database.ScalarAsync<bool>("SELECT branch_id IS NULL FROM properties WHERE id = @p", ("p", propertyId)));
        Assert.Equal(2, (await PortalApi.ReadAsync(await PortalApi.GetAsync(host.Client, "/portal/properties", cookie))).AsArray().Count);

        // Edit: name, instructions and the primary flag in one transaction; the previous primary is cleared.
        var token = added["updatedAt"]!.GetValue<string>();
        var edited = await PortalApi.ReadAsync(await PortalApi.SendAsync(
            host.Client,
            HttpMethod.Patch,
            $"/portal/properties/{propertyId}",
            cookie,
            new JsonObject { ["name"] = "Cabin 2", ["accessInstructions"] = "Back door", ["isPrimary"] = true, ["updatedAt"] = token }));
        Assert.Equal("Cabin 2", edited["name"]!.GetValue<string>());
        Assert.True(edited["isPrimary"]!.GetValue<bool>());
        Assert.Equal(
            $"{propertyId}|9 Pine Rd",
            await database.ScalarAsync<string>(
                "SELECT string_agg(id::text, ',') || '|' || MIN(address_line1) FILTER (WHERE id = @p) FROM properties WHERE customer_id = @c AND is_primary", ("p", propertyId), ("c", world.Customer)));
        Assert.False(await database.ScalarAsync<bool>("SELECT is_primary FROM properties WHERE id = @p", ("p", world.Property)));

        // A stale token is a 409; an address field, a missing token and unknown fields are 400s that change nothing.
        await BillingSeed.ProblemAsync(
            await PortalApi.SendAsync(host.Client, HttpMethod.Patch, $"/portal/properties/{propertyId}", cookie, new JsonObject { ["name"] = "Stale", ["updatedAt"] = token }),
            HttpStatusCode.Conflict,
            "property_changed");
        var current = edited["updatedAt"]!.GetValue<string>();
        var address = await BillingSeed.ProblemAsync(
            await PortalApi.SendAsync(host.Client, HttpMethod.Patch, $"/portal/properties/{propertyId}", cookie, new JsonObject { ["addressLine1"] = "1 Hacked St", ["updatedAt"] = current }),
            HttpStatusCode.BadRequest,
            errorKey: "addressLine1");
        Assert.Equal("The address can't be changed online.", address["errors"]!["addressLine1"]![0]!.GetValue<string>());
        await BillingSeed.ProblemAsync(
            await PortalApi.SendAsync(host.Client, HttpMethod.Patch, $"/portal/properties/{propertyId}", cookie, new JsonObject { ["name"] = "No token" }),
            HttpStatusCode.BadRequest,
            errorKey: "updatedAt");
        Assert.Equal("Cabin 2|9 Pine Rd", await database.ScalarAsync<string>("SELECT name || '|' || address_line1 FROM properties WHERE id = @p", ("p", propertyId)));

        // isPrimary false changes nothing.
        var unchanged = await PortalApi.ReadAsync(await PortalApi.SendAsync(
            host.Client, HttpMethod.Patch, $"/portal/properties/{propertyId}", cookie, new JsonObject { ["isPrimary"] = false, ["updatedAt"] = current }));
        Assert.True(unchanged["isPrimary"]!.GetValue<bool>());
        Assert.Equal(user.UserId, await database.ScalarAsync<Guid>("SELECT actor_user_id FROM audit_logs WHERE organization_id = @o AND action = 'property.created'", ("o", world.Org)));
        Assert.DoesNotContain("Pine Rd", await database.OrgAuditTextAsync(world.Org), StringComparison.Ordinal);

        // Messages: unavailable without an organization or main branch email, then validated, audited by length, mailed after the commit.
        var dashboard = await PortalApi.ReadAsync(await PortalApi.GetAsync(host.Client, "/portal/dashboard", cookie));
        Assert.False(dashboard["organization"]!["canReceiveMessages"]!.GetValue<bool>());
        await BillingSeed.ProblemAsync(
            await PortalApi.PostAsync(host.Client, "/portal/messages", cookie, new JsonObject { ["message"] = "Hello" }), HttpStatusCode.Conflict, "messaging_unavailable");

        await database.SetOrganizationEmailAsync(world.Org, "office@acme.test");
        await BillingSeed.ProblemAsync(
            await PortalApi.PostAsync(host.Client, "/portal/messages", cookie, new JsonObject { ["message"] = "  " }), HttpStatusCode.BadRequest, errorKey: "message");
        const string Text = "Pipe burst in the kitchen";
        var sent = await PortalApi.PostAsync(host.Client, "/portal/messages", cookie, new JsonObject { ["message"] = Text });
        Assert.Equal(HttpStatusCode.Accepted, sent.StatusCode);
        var mail = Assert.Single(host.Sender.Messages);
        Assert.Equal("office@acme.test", mail.To);
        Assert.Equal(contactEmail, mail.ReplyTo);
        Assert.Equal("Message from Carla Customer", mail.Subject);
        Assert.Contains(Text, mail.TextBody, StringComparison.Ordinal);
        Assert.Contains($"\"length\": {Text.Length}", await database.ScalarAsync<string>("SELECT metadata::text FROM audit_logs WHERE organization_id = @o AND action = 'portal.message_sent'", ("o", world.Org)), StringComparison.Ordinal);

        // A provider failure after the commit still answers 202; the limit is five messages per window.
        host.Sender.Fail = true;
        Assert.Equal(HttpStatusCode.Accepted, (await PortalApi.PostAsync(host.Client, "/portal/messages", cookie, new JsonObject { ["message"] = Text })).StatusCode);
        host.Sender.Fail = false;
        Assert.Equal(HttpStatusCode.Accepted, (await PortalApi.PostAsync(host.Client, "/portal/messages", cookie, new JsonObject { ["message"] = Text })).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await PortalApi.PostAsync(host.Client, "/portal/messages", cookie, new JsonObject { ["message"] = Text })).StatusCode);
        var limited = await PortalApi.PostAsync(host.Client, "/portal/messages", cookie, new JsonObject { ["message"] = Text });
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.NotNull(limited.Headers.RetryAfter);

        var trail = await database.OrgAuditTextAsync(world.Org) + new CapturingLogs(host.Logs).AllText();
        Assert.DoesNotContain(Text, trail, StringComparison.Ordinal);
        Assert.DoesNotContain(contactEmail, trail, StringComparison.OrdinalIgnoreCase);
    }

    private Task<string> VisitStateAsync(Guid visit) =>
        database.ScalarAsync<string>(
            "SELECT status::text || '|' || scheduled_start::text || '|' || scheduled_end::text || '|' || updated_at::text FROM visits WHERE id = @v", ("v", visit));
}
