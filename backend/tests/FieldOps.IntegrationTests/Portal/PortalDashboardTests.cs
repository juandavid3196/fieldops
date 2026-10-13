using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.BillingReview;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.OnlinePayments;
using FieldOps.IntegrationTests.ServiceRequests;
using FieldOps.IntegrationTests.Team;

namespace FieldOps.IntegrationTests.Portal;

/// <summary>Dashboard aggregation, property scoping, lists and the derived updates feed (customer portal BR-17 … BR-29, AC-12 … AC-14, AC-19).</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class PortalDashboardTests(CompanySettingsDatabaseFixture database)
{
    [Fact]
    public async Task GetPortalDashboard_WithAllAndEachProperty_FollowsTheSelectionOrderingAndUpdatesRules()
    {
        await using var host = OnlineHost.Create(database);
        var world = await database.SeedWorldAsync();
        var staff = await database.SeedUserAsync();
        var user = await database.LinkPortalAsync(world.Org, world.Contact);
        var p1 = world.Property;
        var p2 = await database.SeedPropertyAsync(world, "Cabin");
        var p3 = await database.SeedPropertyAsync(world, "Empty");
        var cabin = world with { Property = p2 };
        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var tomorrow = new DateTimeOffset(now.UtcDateTime.Date.AddDays(1).AddHours(10), TimeSpan.Zero);

        // Quotes: two candidates on the primary property, one by-date expired and one question on the cabin, a draft and an old one.
        var q1 = await database.SeedQuoteAsync(world, staff, "sent", today.AddDays(10), now.AddDays(-2), now.AddDays(-5));
        var q2 = await database.SeedQuoteAsync(world, staff, "sent", today.AddDays(3), now.AddDays(-1), now.AddDays(-4));
        var q3 = await database.SeedQuoteAsync(cabin, staff, "sent", today.AddDays(-1), now.AddDays(-3), now.AddDays(-6));
        var q4 = await database.SeedQuoteAsync(cabin, staff, "clarification_requested", null, now.AddHours(-1), now.AddDays(-3));
        var draft = await database.SeedQuoteAsync(world, staff, "draft", null, null, now.AddDays(-7));
        await database.SeedQuoteAsync(world, staff, "rejected", null, now.AddDays(-100), now.AddDays(-120));
        await database.SeedRequestAsync(world, status: "converted", createdAt: now.AddDays(-1));

        // Invoices: draft and void never show; the overdue one is picked first; paid has a receipt.
        var i1 = await database.InvoiceAsync(world, staff, new OnlineSpec { Status = "sent", Total = 100m, DueDays = 5, FullJob = false });
        var i2 = await database.InvoiceAsync(world, staff, new OnlineSpec { Status = "overdue", Total = 50m, DueDays = -3, FullJob = false });
        await database.InvoiceAsync(world, staff, new OnlineSpec { Status = "draft", FullJob = false });
        await database.InvoiceAsync(cabin, staff, new OnlineSpec { Status = "void", FullJob = false });
        var i5 = await database.InvoiceAsync(cabin, staff, new OnlineSpec { Status = "paid", Total = 70m, Paid = 70m, FullJob = false });
        var i6 = await database.InvoiceAsync(cabin, staff, new OnlineSpec { Status = "sent", Total = 70m, DueDays = null, FullJob = false });
        var receipt = await database.SeedPaymentAsync(world.Org, world.Customer, i5.Id, staff, 70m, now.AddDays(-1));

        // Visits: one scheduled with a technician and an arrival window, one on the way, one later, one cancelled, one past.
        var tina = await database.SeedTechAsync(world.Org, world.BranchA, "Tina", "Tech");
        var tom = await database.SeedTechAsync(world.Org, world.BranchA, "Tom", "Tech");
        var v1 = await database.SeedVisitAsync(world, staff, "Water heater", "scheduled", tomorrow, arrivalStart: tomorrow.AddHours(-1), arrivalEnd: tomorrow.AddHours(1));
        await database.AssignAsync(v1.Visit, tina, staff, now.AddHours(-5));
        var v2 = await database.SeedVisitAsync(cabin, staff, "Roof leak", "on_the_way", now.AddMinutes(-30), orderStatus: "in_progress");
        await database.AssignAsync(v2.Visit, tom, staff, now.AddHours(-2));
        await database.SeedVisitHistoryAsync(v2.Visit, "on_the_way", now.AddMinutes(-20));
        var v3 = await database.SeedVisitAsync(world, staff, "Gutter", "scheduled", tomorrow.AddDays(2));
        await database.SeedVisitAsync(world, staff, "Cancelled job", "scheduled", tomorrow.AddHours(3), orderStatus: "cancelled");
        var past = await database.SeedVisitAsync(world, staff, "Past job", "completed", now.AddDays(-1), orderStatus: "completed", completedAt: now.AddDays(-1).AddHours(1));
        await database.SeedVisitHistoryAsync(past.Visit, "completed", now.AddHours(-20));

        // Another customer of the same organization only adds noise that must never appear.
        var sibling = await database.SeedSiblingAsync(world);
        await database.SeedQuoteAsync(sibling, staff, "sent", today.AddDays(1), now, now.AddDays(-1));
        await database.InvoiceAsync(sibling, staff, new OnlineSpec { Status = "sent", DueDays = 1, FullJob = false });
        await database.ExecuteAsync("UPDATE customer_contacts SET portal_updates_seen_at = @t WHERE id = @c", ("t", now.AddHours(-6)), ("c", world.Contact));

        var cookie = await PortalApi.CookieAsync(host.Client, user.Email);

        async Task<JsonNode> Dashboard(Guid? property = null) =>
            await PortalApi.ReadAsync(await PortalApi.GetAsync(host.Client, "/portal/dashboard" + (property is null ? string.Empty : $"?propertyId={property}"), cookie));

        static Guid[] Ids(JsonNode? array, string key = "id") =>
            [.. array!.AsArray().Select(item => item![key]!.GetValue<Guid>())];

        // All properties.
        var all = await Dashboard();
        Assert.Null(all["selectedPropertyId"]);
        Assert.Equal(await database.ScalarAsync<string>("SELECT name FROM organizations WHERE id = @o", ("o", world.Org)), all["organization"]!["name"]!.GetValue<string>());
        Assert.Equal([p1, p2, p3], Ids(all["properties"]));
        Assert.True(all["properties"]![0]!["isPrimary"]!.GetValue<bool>());
        Assert.NotNull(all["properties"]![0]!["lastServiceOn"]);
        Assert.Null(all["properties"]![2]!["lastServiceOn"]);
        Assert.Equal(q2.Quote, all["actionQuote"]!["id"]!.GetValue<Guid>());
        Assert.Equal(2, all["actionQuote"]!["moreCount"]!.GetValue<int>());
        Assert.Equal(today.AddDays(3).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), all["actionQuote"]!["validUntil"]!.GetValue<string>());
        Assert.Equal(i2.Id, all["paymentDue"]!["id"]!.GetValue<Guid>());
        Assert.True(all["paymentDue"]!["isOverdue"]!.GetValue<bool>());
        Assert.Equal(2, all["paymentDue"]!["moreCount"]!.GetValue<int>());
        Assert.Equal(50m, all["paymentDue"]!["balanceDue"]!.GetValue<decimal>());
        Assert.Equal(v2.Visit, all["upcomingAppointment"]!["visitId"]!.GetValue<Guid>());
        Assert.Equal("on_the_way", all["upcomingAppointment"]!["progressStep"]!.GetValue<string>());
        Assert.Equal("Tom Tech", all["upcomingAppointment"]!["technician"]!["fullName"]!.GetValue<string>());
        Assert.Equal([q4.Request, q2.Request, q1.Request], Ids(all["activeRequests"]));
        Assert.Equal(3, all["updates"]!["items"]!.AsArray().Count);
        Assert.Equal(4, all["updates"]!["unreadCount"]!.GetValue<int>());
        Assert.Equal(5, all["recentActivity"]!.AsArray().Count);

        // The primary property.
        var primary = await Dashboard(p1);
        Assert.Equal(p1, primary["selectedPropertyId"]!.GetValue<Guid>());
        Assert.Equal(q2.Quote, primary["actionQuote"]!["id"]!.GetValue<Guid>());
        Assert.Equal(1, primary["actionQuote"]!["moreCount"]!.GetValue<int>());
        Assert.Equal(i2.Id, primary["paymentDue"]!["id"]!.GetValue<Guid>());
        Assert.Equal(1, primary["paymentDue"]!["moreCount"]!.GetValue<int>());
        var appointment = primary["upcomingAppointment"]!;
        Assert.Equal(v1.Visit, appointment["visitId"]!.GetValue<Guid>());
        Assert.Equal("scheduled", appointment["progressStep"]!.GetValue<string>());
        Assert.Equal("Tina Tech", appointment["technician"]!["fullName"]!.GetValue<string>());
        Assert.Equal("Tina", appointment["technician"]!["firstName"]!.GetValue<string>());
        Assert.Equal("09:00", appointment["arrivalWindow"]!["start"]!.GetValue<string>());
        Assert.Equal("11:00", appointment["arrivalWindow"]!["end"]!.GetValue<string>());
        Assert.Equal("Primary property", appointment["property"]!["name"]!.GetValue<string>());
        Assert.True(appointment["canRequestReschedule"]!.GetValue<bool>());
        Assert.Null(appointment["rescheduleRequestedOn"]);
        Assert.Equal([q2.Request, q1.Request, draft.Request], Ids(primary["activeRequests"]));

        // The cabin: the question card, the invoice without a due date and the visit on the way.
        var cabinView = await Dashboard(p2);
        Assert.Equal(q4.Quote, cabinView["actionQuote"]!["id"]!.GetValue<Guid>());
        Assert.Equal("clarification_requested", cabinView["actionQuote"]!["status"]!.GetValue<string>());
        Assert.Null(cabinView["actionQuote"]!["validUntil"]);
        Assert.Equal(0, cabinView["actionQuote"]!["moreCount"]!.GetValue<int>());
        Assert.Equal(i6.Id, cabinView["paymentDue"]!["id"]!.GetValue<Guid>());
        Assert.False(cabinView["paymentDue"]!["isOverdue"]!.GetValue<bool>());
        Assert.Null(cabinView["paymentDue"]!["dueDate"]);
        Assert.Equal(v2.Visit, cabinView["upcomingAppointment"]!["visitId"]!.GetValue<Guid>());
        Assert.Equal([q4.Request, q3.Request], Ids(cabinView["activeRequests"]));

        // Updates ignore the selection; a property with nothing has null and empty cards.
        Assert.Equal(4, cabinView["updates"]!["unreadCount"]!.GetValue<int>());
        var empty = await Dashboard(p3);
        Assert.Null(empty["actionQuote"]);
        Assert.Null(empty["paymentDue"]);
        Assert.Null(empty["upcomingAppointment"]);
        Assert.Empty(empty["activeRequests"]!.AsArray());
        Assert.Empty(empty["recentActivity"]!.AsArray());
        Assert.Equal(3, empty["updates"]!["items"]!.AsArray().Count);

        // A value that is not a property of the customer is the identical 404.
        await PortalApi.AssertUnavailableAsync(await PortalApi.GetAsync(host.Client, "/portal/dashboard?propertyId=not-an-id", cookie));

        // Activity: statuses, references, receipts and paging.
        var primaryActivity = await PortalApi.ReadAsync(await PortalApi.GetAsync(host.Client, $"/portal/activity?propertyId={p1}", cookie));
        Assert.Equal(4, primaryActivity["total"]!.GetValue<int>());
        Assert.Equal(20, primaryActivity["pageSize"]!.GetValue<int>());
        var byOrder = primaryActivity["items"]!.AsArray().ToDictionary(row => row!["workOrderId"]!.GetValue<Guid>());
        Assert.Equal("invoice_due", byOrder[i1.Order]!["status"]!.GetValue<string>());
        Assert.Equal(i1.Display, byOrder[i1.Order]!["reference"]!.GetValue<string>());
        Assert.Equal(100m, byOrder[i1.Order]!["amount"]!.GetValue<decimal>());
        Assert.Equal("completed", byOrder[past.Order]!["status"]!.GetValue<string>());
        Assert.Null(byOrder[past.Order]!["amount"]);
        var cabinActivity = await PortalApi.ReadAsync(await PortalApi.GetAsync(host.Client, $"/portal/activity?propertyId={p2}", cookie));
        var paid = cabinActivity["items"]!.AsArray().Single(row => row!["workOrderId"]!.GetValue<Guid>() == i5.Order)!;
        Assert.Equal("paid", paid["status"]!.GetValue<string>());
        Assert.Equal(receipt, paid["receiptPaymentId"]!.GetValue<Guid>());
        await BillingSeed.ProblemAsync(await PortalApi.GetAsync(host.Client, "/portal/activity?page=0", cookie), HttpStatusCode.BadRequest, errorKey: "page");

        // Appointments: upcoming ascending, the cancelled work order never shows, past with the report flag.
        var upcoming = await PortalApi.ReadAsync(await PortalApi.GetAsync(host.Client, "/portal/appointments?scope=upcoming", cookie));
        Assert.Equal([v2.Visit, v1.Visit, v3.Visit], Ids(upcoming["items"], "visitId"));
        var primaryUpcoming = await PortalApi.ReadAsync(await PortalApi.GetAsync(host.Client, $"/portal/appointments?scope=upcoming&propertyId={p1}", cookie));
        Assert.Equal([v1.Visit, v3.Visit], Ids(primaryUpcoming["items"], "visitId"));
        var pastList = await PortalApi.ReadAsync(await PortalApi.GetAsync(host.Client, "/portal/appointments?scope=past", cookie));
        Assert.Equal([past.Visit], Ids(pastList["items"], "visitId"));
        Assert.Equal("completed", pastList["items"]![0]!["progressStep"]!.GetValue<string>());
        Assert.True(pastList["items"]![0]!["reportAvailable"]!.GetValue<bool>());
        await BillingSeed.ProblemAsync(await PortalApi.GetAsync(host.Client, "/portal/appointments?scope=later", cookie), HttpStatusCode.BadRequest, errorKey: "scope");

        // Lists limited to the customer: by-date expiry, hidden drafts and voids, request labels.
        var quotes = await PortalApi.ReadAsync(await PortalApi.GetAsync(host.Client, "/portal/quotes", cookie));
        Assert.Equal(5, quotes["total"]!.GetValue<int>());
        Assert.Equal("expired", quotes["items"]!.AsArray().Single(row => row!["id"]!.GetValue<Guid>() == q3.Quote)!["status"]!.GetValue<string>());
        Assert.DoesNotContain(draft.Quote, Ids(quotes["items"]));
        var invoices = await PortalApi.ReadAsync(await PortalApi.GetAsync(host.Client, "/portal/invoices", cookie));
        Assert.Equal(4, invoices["total"]!.GetValue<int>());
        var requests = await PortalApi.ReadAsync(await PortalApi.GetAsync(host.Client, "/portal/requests", cookie));
        Assert.Equal(7, requests["total"]!.GetValue<int>());
        Assert.Contains("Job created", requests["items"]!.AsArray().Select(row => row!["statusLabel"]!.GetValue<string>()));
        Assert.Equal("Quote ready", requests["items"]!.AsArray().First(row => row!["id"]!.GetValue<Guid>() == q1.Request)!["statusLabel"]!.GetValue<string>());
        var cabinRequests = await PortalApi.ReadAsync(await PortalApi.GetAsync(host.Client, $"/portal/requests?propertyId={p2}", cookie));
        Assert.Equal(2, cabinRequests["total"]!.GetValue<int>());

        // The updates feed: the customer's events of the last 90 days, newest first, unread after the seen time.
        var updates = await PortalApi.ReadAsync(await PortalApi.GetAsync(host.Client, "/portal/updates", cookie));
        var items = updates["items"]!.AsArray();
        Assert.Equal(13, items.Count);
        Assert.Equal(4, updates["unreadCount"]!.GetValue<int>());
        Assert.Equal(
            ["invoice_sent", "on_the_way", "quote_sent", "receipt_available", "technician_assigned", "visit_completed"],
            items.Select(item => item!["type"]!.GetValue<string>()).Distinct().Order().ToArray());
        Assert.Equal("on_the_way", items[0]!["type"]!.GetValue<string>());
        Assert.Equal("Tom is on the way", items[0]!["title"]!.GetValue<string>());
        Assert.Equal("appointment", items[0]!["target"]!["kind"]!.GetValue<string>());
        var times = items.Select(item => item!["occurredAt"]!.GetValue<DateTimeOffset>()).ToArray();
        Assert.Equal(times.OrderByDescending(time => time).ToArray(), times);
        Assert.Equal(4, items.Count(item => item!["isUnread"]!.GetValue<bool>()));

        var seen = await PortalApi.PostAsync(host.Client, "/portal/updates/seen", cookie);
        Assert.Equal(HttpStatusCode.NoContent, seen.StatusCode);
        var afterSeen = await PortalApi.ReadAsync(await PortalApi.GetAsync(host.Client, "/portal/updates", cookie));
        Assert.Equal(0, afterSeen["unreadCount"]!.GetValue<int>());
        Assert.DoesNotContain(afterSeen["items"]!.AsArray(), item => item!["isUnread"]!.GetValue<bool>());
        Assert.Equal(0, (await Dashboard())["updates"]!["unreadCount"]!.GetValue<int>());
    }
}
