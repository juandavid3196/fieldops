using System.Net;
using System.Text.Json.Nodes;
using FieldOps.Application.Features.Customers;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.Users;

namespace FieldOps.IntegrationTests.Customers;

/// <summary>Derived data, notes and activity of the customer detail page (FR-02, FR-04, FR-09 to FR-13): AC-04, AC-14 to AC-18.</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class CustomerDetailReadTests(CompanySettingsDatabaseFixture database)
{
    // AC-04, AC-14, FR-02, FR-09: header, summary and billing values over seeded work orders, visits and invoices
    // (cancelled, draft and void excluded), the BR-04 property order and the BR-13 history of every property.
    [Fact]
    public async Task DetailAndProperties_ComputeSummaryBillingOrderAndHistory()
    {
        var org = await database.SeedOrganizationAsync();
        var branchX = await database.SeedBranchAsync(org, "Xray Branch", isMain: true);
        var branchY = await database.SeedBranchAsync(org, "Yankee Branch");
        var owner = await database.SeedMemberAsync(org, CompanySettingsDatabaseFixture.OwnerRoleId, "Ola", "Owner");
        var customer = await database.SeedCustomerAsync(org, branchX.Id, "Overview Customer", email: "pat@example.com", phone: "5125550101", createdAt: new DateTimeOffset(2025, 3, 10, 12, 0, 0, TimeSpan.Zero));
        await database.ExecuteAsync("UPDATE customers SET notes = 'Pinned text' WHERE id = @c", ("c", customer));
        await database.AssignTagAsync(org, customer, await database.SeedTagAsync(org, "Gold"));
        var primary = await database.ScalarAsync<Guid>("SELECT id FROM properties WHERE customer_id = @c", ("c", customer));
        var alpha = await database.SeedPropertyAsync(org, customer, branchY.Id, "alpha site");
        var beta = await database.SeedPropertyAsync(org, customer, null, "Beta Site");
        var archived = await database.SeedPropertyAsync(org, customer, branchX.Id, "Aardvark Archived", isActive: false);
        var now = DateTimeOffset.UtcNow;

        await database.SeedInvoiceAsync(org, owner.UserId, branchX.Id, customer, 1, "sent", 300m, 100m, new DateOnly(2026, 5, 1), now.AddDays(-90));
        await database.SeedInvoiceAsync(org, owner.UserId, branchX.Id, customer, 2, "overdue", 500m, 0m, new DateOnly(2026, 4, 1), now.AddDays(-80));
        await database.SeedInvoiceAsync(org, owner.UserId, branchX.Id, customer, 3, "paid", 400m, 400m, new DateOnly(2026, 6, 1), now.AddDays(-70));
        await database.SeedInvoiceAsync(org, owner.UserId, branchX.Id, customer, 4, "draft", 900m, 0m, new DateOnly(2026, 7, 1), now.AddDays(-60));
        await database.SeedInvoiceAsync(org, owner.UserId, branchX.Id, customer, 5, "void", 800m, 50m, new DateOnly(2026, 8, 1), now.AddDays(-50));

        var completed = await database.SeedJobAsync(org, owner.UserId, branchX.Id, customer, primary, Guid.NewGuid(), 1, "completed", "Fix pump\nSecond line", now.AddDays(-40));
        await database.SeedVisitAsync(org, completed, 1, "completed", now.AddDays(-30), now.AddDays(-30).AddHours(2), now.AddDays(-30).AddHours(1));
        var approved = await database.SeedJobAsync(org, owner.UserId, branchX.Id, customer, primary, Guid.NewGuid(), 2, "approved_for_billing", "Replace valve", now.AddDays(-35));
        await database.SeedVisitAsync(org, approved, 1, "approved", now.AddDays(-20).AddHours(-3), now.AddDays(-20), null);
        var cancelled = await database.SeedJobAsync(org, owner.UserId, branchY.Id, customer, alpha, Guid.NewGuid(), 3, "cancelled", "Cancelled work", now.AddDays(-34));
        await database.SeedVisitAsync(org, cancelled, 1, "cancelled", now.AddDays(1), now.AddDays(1).AddHours(1));
        var scheduled = await database.SeedJobAsync(org, owner.UserId, branchY.Id, customer, alpha, Guid.NewGuid(), 4, "scheduled", "Inspect roof", now.AddDays(-5));
        var upcoming = now.AddDays(3);
        await database.SeedVisitAsync(org, scheduled, 1, "scheduled", upcoming, upcoming.AddHours(2));
        await database.SeedVisitAsync(org, scheduled, 2, "scheduled", now.AddDays(-2), now.AddDays(-2).AddHours(1));

        await using var host = CustomerHost.Create(database);
        var cookie = await host.CookieAsync(database, org, CompanySettingsDatabaseFixture.ViewerRoleId);

        var detail = await CustomerHost.ReadAsync(await host.SendAsync(HttpMethod.Get, $"/customers/{customer}/detail", cookie));
        Assert.Equal(("residential", "Overview Customer", "overdue", true, "USD", "Pinned text"), (
            detail["type"]!.GetValue<string>(),
            detail["displayName"]!.GetValue<string>(),
            detail["displayStatus"]!.GetValue<string>(),
            detail["isActive"]!.GetValue<bool>(),
            detail["currency"]!.GetValue<string>(),
            detail["pinnedNote"]!.GetValue<string>()));
        Assert.Equal(700m, detail["outstandingBalance"]!.GetValue<decimal>());
        Assert.Equal(("Pat", "5125550101", "pat@example.com"), (
            detail["contact"]!["firstName"]!.GetValue<string>(),
            detail["contact"]!["phone"]!.GetValue<string>(),
            detail["contact"]!["email"]!.GetValue<string>()));
        Assert.Equal(["Gold"], detail["tags"]!.AsArray().Select(tag => tag!["name"]!.GetValue<string>()).ToArray());
        Assert.Equal((3, 500m), (detail["summary"]!["totalJobs"]!.GetValue<int>(), detail["summary"]!["lifetimeValue"]!.GetValue<decimal>()));
        Assert.Equal(new DateTimeOffset(2025, 3, 10, 12, 0, 0, TimeSpan.Zero), detail["summary"]!["customerSince"]!.GetValue<DateTimeOffset>());
        Assert.Equal(now.AddDays(-20), detail["summary"]!["lastServiceAt"]!.GetValue<DateTimeOffset>(), TimeSpan.FromMilliseconds(5));
        Assert.Equal(("INV-3", "paid", "2026-06-01"), (
            detail["lastInvoice"]!["number"]!.GetValue<string>(),
            detail["lastInvoice"]!["status"]!.GetValue<string>(),
            detail["lastInvoice"]!["issueDate"]!.GetValue<string>()));

        // BR-04, BR-13: primary first, then name A-Z ignoring case, archived last; history per property.
        var list = await CustomerHost.ReadAsync(await host.SendAsync(HttpMethod.Get, $"/customers/{customer}/properties", cookie));
        var items = list["items"]!.AsArray();
        Assert.Equal([primary, alpha, beta, archived], items.Select(item => item!["id"]!.GetValue<Guid>()).ToArray());
        Assert.Equal((true, "Xray Branch", "Replace valve"), (
            items[0]!["isPrimary"]!.GetValue<bool>(),
            items[0]!["branch"]!["name"]!.GetValue<string>(),
            items[0]!["lastService"]!["summary"]!.GetValue<string>()));
        Assert.Equal(now.AddDays(-20), items[0]!["lastService"]!["completedAt"]!.GetValue<DateTimeOffset>(), TimeSpan.FromMilliseconds(5));
        Assert.Null(items[0]!["nextAppointment"]);
        Assert.Equal(upcoming, items[1]!["nextAppointment"]!["startsAt"]!.GetValue<DateTimeOffset>(), TimeSpan.FromMilliseconds(5));
        Assert.Null(items[1]!["lastService"]);
        Assert.Null(items[2]!["branch"]);
        Assert.False(items[3]!["isActive"]!.GetValue<bool>());

        // "No completed service" and "None": a customer without work or invoices.
        var empty = await database.SeedCustomerAsync(org, branchX.Id, "Empty Customer");
        var emptyDetail = await CustomerHost.ReadAsync(await host.SendAsync(HttpMethod.Get, $"/customers/{empty}/detail", cookie));
        Assert.Equal((0, 0m, 0m, "lead"), (
            emptyDetail["summary"]!["totalJobs"]!.GetValue<int>(),
            emptyDetail["summary"]!["lifetimeValue"]!.GetValue<decimal>(),
            emptyDetail["outstandingBalance"]!.GetValue<decimal>(),
            emptyDetail["displayStatus"]!.GetValue<string>()));
        Assert.Null(emptyDetail["summary"]!["lastServiceAt"]);
        Assert.Null(emptyDetail["lastInvoice"]);
    }

    // AC-15, AC-16: recent work merges requests, quotes and jobs (max 5, newest first, BR-14 formats) and upcoming
    // appointments list the earliest 5 eligible future visits (BR-15), both empty for a customer without work.
    [Fact]
    public async Task RecentWorkAndUpcomingAppointments_FollowOrderLimitsAndFormats()
    {
        var org = await database.SeedOrganizationAsync();
        var branch = await database.SeedBranchAsync(org, isMain: true);
        var owner = await database.SeedMemberAsync(org, CompanySettingsDatabaseFixture.OwnerRoleId, "Ola", "Owner");
        var customer = await database.SeedCustomerAsync(org, branch.Id, "Work Customer");
        var property = await database.SeedPropertyAsync(org, customer, branch.Id, "Warehouse");
        var technician = await database.SeedTechnicianProfileAsync(org, branch.Id, "Tina", "Tech");
        var former = await database.SeedTechnicianProfileAsync(org, branch.Id, "Fred", "Former");
        var now = DateTimeOffset.UtcNow;

        var request1 = await database.SeedRequestAsync(org, customer, 101, "First line\nmore", "new", now.AddDays(-10));
        var request2 = await database.SeedRequestAsync(org, customer, 102, "Second request", "quoted", now.AddDays(-1));
        await database.SeedRequestAsync(org, customer, 103, "Old request", "cancelled", now.AddDays(-50));
        await database.SeedRequestAsync(org, customer, 104, "Older request", "new", now.AddDays(-60));
        await database.SeedRequestAsync(org, customer, 105, "Oldest request", "new", now.AddDays(-70));
        var (_, version) = await database.SeedQuoteAsync(org, owner.UserId, customer, request2, 7, "sent", "Quote scope line\nsecond", 1250.50m, now.AddDays(-5), now.AddDays(-3));
        await database.SeedQuoteAsync(org, owner.UserId, customer, request1, 8, "draft", "unused", 0m, now.AddDays(-20), null, withVersion: false);

        var done = await database.SeedJobAsync(org, owner.UserId, branch.Id, customer, property, version, 55, "completed", "Job scope\nline two", now.AddDays(-30));
        var doneVisit = await database.SeedVisitAsync(org, done, 1, "completed", now.AddDays(-2).AddHours(-2), now.AddDays(-2), now.AddDays(-2).AddHours(-1));
        await database.SeedAssignmentAsync(doneVisit, former, owner.UserId, unassignedAt: now.AddDays(-3));
        await database.SeedAssignmentAsync(doneVisit, technician, owner.UserId);
        await database.SeedJobAsync(org, owner.UserId, branch.Id, customer, property, Guid.NewGuid(), 56, "draft", "Draft job", now.AddDays(-40));

        await using var host = CustomerHost.Create(database);
        var cookie = await host.CookieAsync(database, org, CompanySettingsDatabaseFixture.OperationsManagerRoleId);

        var recent = await CustomerHost.ReadAsync(await host.SendAsync(HttpMethod.Get, $"/customers/{customer}/recent-work", cookie));
        var items = recent["items"]!.AsArray();
        Assert.Equal(["request", "job", "quote", "request", "quote"], items.Select(item => item!["type"]!.GetValue<string>()).ToArray());
        Assert.Equal(["REQ-102", "WO-55", "Q-7", "REQ-101", "Q-8"], items.Select(item => item!["number"]!.GetValue<string>()).ToArray());
        Assert.Equal(["Second request", "Job scope", "Quote scope line", "First line", "First line"], items.Select(item => item!["title"]!.GetValue<string>()).ToArray());
        Assert.Equal(["quoted", "completed", "sent", "new", "draft"], items.Select(item => item!["status"]!.GetValue<string>()).ToArray());
        Assert.Equal("Tina Tech", items[1]!["technicianName"]!.GetValue<string>());
        Assert.Equal(1250.50m, items[1]!["amount"]!.GetValue<decimal>());
        Assert.Equal(1250.50m, items[2]!["amount"]!.GetValue<decimal>());
        Assert.Null(items[0]!["amount"]);
        Assert.Null(items[4]!["amount"]);
        Assert.Equal(("USD", 5), (recent["currency"]!.GetValue<string>(), items.Count));

        var future = new[] { 1, 2, 3, 4, 5, 6, 7 };
        var job = await database.SeedJobAsync(org, owner.UserId, branch.Id, customer, property, Guid.NewGuid(), 57, "scheduled", "Roof inspection\nlevel 2", now.AddDays(-1));
        var number = 1;

        foreach (var day in future)
        {
            var visit = await database.SeedVisitAsync(org, job, number++, day == 2 ? "on_the_way" : "scheduled", now.AddDays(day), now.AddDays(day).AddHours(2));

            if (day == 3)
            {
                await database.SeedAssignmentAsync(visit, technician, owner.UserId);
            }
        }

        await database.SeedVisitAsync(org, job, number++, "scheduled", now.AddDays(-1), now.AddDays(-1).AddHours(1));
        await database.SeedVisitAsync(org, job, number++, "in_progress", now.AddHours(1), now.AddHours(2));
        await database.SeedVisitAsync(org, job, number, "cancelled", now.AddHours(2), now.AddHours(3));

        var appointments = (await CustomerHost.ReadAsync(await host.SendAsync(HttpMethod.Get, $"/customers/{customer}/upcoming-appointments", cookie)))["items"]!.AsArray();
        Assert.Equal(5, appointments.Count);
        Assert.Equal(
            Enumerable.Range(1, 5).Select(day => now.AddDays(day)).ToArray(),
            appointments.Select(item => item!["startsAt"]!.GetValue<DateTimeOffset>()).ToArray(),
            new ApproximateComparer());
        Assert.Equal(appointments[0]!["startsAt"]!.GetValue<DateTimeOffset>().AddHours(2), appointments[0]!["endsAt"]!.GetValue<DateTimeOffset>(), new ApproximateComparer());
        Assert.Equal(("WO-57", "Roof inspection", "Warehouse"), (
            appointments[0]!["jobNumber"]!.GetValue<string>(),
            appointments[0]!["jobTitle"]!.GetValue<string>(),
            appointments[0]!["propertyName"]!.GetValue<string>()));
        Assert.Equal(["Tina Tech"], appointments.Select(item => item!["technicianName"]?.GetValue<string>()).OfType<string>().ToArray());

        var empty = await database.SeedCustomerAsync(org, branch.Id, "Nothing Customer");
        Assert.Empty((await CustomerHost.ReadAsync(await host.SendAsync(HttpMethod.Get, $"/customers/{empty}/recent-work", cookie)))["items"]!.AsArray());
        Assert.Empty((await CustomerHost.ReadAsync(await host.SendAsync(HttpMethod.Get, $"/customers/{empty}/upcoming-appointments", cookie)))["items"]!.AsArray());
    }

    // AC-17, AC-18, FR-12, FR-13, FR-17: notes paging and creation with a text-free audit row, and the activity history
    // restricted to the whitelist and the customer's own rows, without any before/after data, metadata, IP or note text.
    [Fact]
    public async Task NotesAndActivity_PageValidateAndNeverExposeSensitiveValues()
    {
        var org = await database.SeedOrganizationAsync();
        var other = await database.SeedOrganizationAsync();
        var branch = await database.SeedBranchAsync(org, isMain: true);
        var otherBranch = await database.SeedBranchAsync(other, isMain: true);
        var author = await database.SeedMemberAsync(org, CompanySettingsDatabaseFixture.OwnerRoleId, "Nora", "Notes");
        var notesCustomer = await database.SeedCustomerAsync(org, branch.Id, "Notes Customer");
        var now = DateTimeOffset.UtcNow;

        for (var i = 0; i < 12; i++)
        {
            await database.ExecuteAsync(
                "INSERT INTO customer_notes (organization_id, customer_id, author_user_id, note, created_at) VALUES (@o, @c, @a, @n, @t)",
                ("o", org), ("c", notesCustomer), ("a", author.UserId), ("n", $"Seeded note {i:00}"), ("t", now.AddMinutes(-60 + i)));
        }

        await using var host = CustomerHost.Create(database);
        var owner = await host.CookieAsync(database, org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var viewer = await host.CookieAsync(database, org, CompanySettingsDatabaseFixture.ViewerRoleId);
        var notesPath = $"/customers/{notesCustomer}/notes";

        var page1 = await CustomerHost.ReadAsync(await host.SendAsync(HttpMethod.Get, notesPath, viewer));
        Assert.Equal((12, 1, 10, 10), (
            page1["totalCount"]!.GetValue<int>(), page1["page"]!.GetValue<int>(), page1["pageSize"]!.GetValue<int>(), page1["items"]!.AsArray().Count));
        Assert.Equal(("Seeded note 11", "Nora Notes"), (page1["items"]![0]!["note"]!.GetValue<string>(), page1["items"]![0]!["authorName"]!.GetValue<string>()));
        var page2 = await CustomerHost.ReadAsync(await host.SendAsync(HttpMethod.Get, $"{notesPath}?page=2", viewer));
        Assert.Equal(["Seeded note 01", "Seeded note 00"], page2["items"]!.AsArray().Select(item => item!["note"]!.GetValue<string>()).ToArray());
        var beyond = await CustomerHost.ReadAsync(await host.SendAsync(HttpMethod.Get, $"{notesPath}?page=9", viewer));
        Assert.Equal((0, 12), (beyond["items"]!.AsArray().Count, beyond["totalCount"]!.GetValue<int>()));

        foreach (var page in new[] { "0", "-1", "abc" })
        {
            var bad = await host.SendAsync(HttpMethod.Get, $"{notesPath}?page={page}", viewer);
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
            Assert.Equal("Page must be 1 or more.", CustomerSeed.Error(await CustomerHost.ReadAsync(bad), "page"));
        }

        var created = await host.SendAsync(HttpMethod.Post, notesPath, owner, new JsonObject { ["note"] = "  Call before arriving  " });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var createdBody = await CustomerHost.ReadAsync(created);
        Assert.Equal(("Call before arriving", "Cus Tomer"), (createdBody["note"]!.GetValue<string>(), createdBody["authorName"]!.GetValue<string>()));
        Assert.Equal(createdBody["id"]!.GetValue<Guid>(), (await CustomerHost.ReadAsync(await host.SendAsync(HttpMethod.Get, notesPath, viewer)))["items"]![0]!["id"]!.GetValue<Guid>());
        var (noteBefore, noteAfter, noteMetadata) = await database.GetLatestAuditAsync(org, "customer_note.created");
        Assert.Null(noteBefore);
        Assert.Null(noteAfter);
        Assert.Equal("{}", noteMetadata);
        Assert.Equal(("customer_note", branch.Id), (
            await database.ScalarAsync<string>("SELECT entity_type FROM audit_logs WHERE organization_id = @o AND action = 'customer_note.created'", ("o", org)),
            await database.ScalarAsync<Guid>("SELECT branch_id FROM audit_logs WHERE organization_id = @o AND action = 'customer_note.created'", ("o", org))));

        Assert.Equal(HttpStatusCode.Created, (await host.SendAsync(HttpMethod.Post, notesPath, owner, new JsonObject { ["note"] = new string('n', 2000) })).StatusCode);
        var notes = await database.CountRowsAsync("customer_notes", org);

        foreach (var (text, message) in new[] { ("   ", "Enter a note."), (new string('n', 2001), "Use 2,000 characters or fewer.") })
        {
            var rejected = await host.SendAsync(HttpMethod.Post, notesPath, owner, new JsonObject { ["note"] = text });
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            Assert.Equal(message, CustomerSeed.Error(await CustomerHost.ReadAsync(rejected), "note"));
        }

        Assert.Equal(notes, await database.CountRowsAsync("customer_notes", org));

        // Activity: 25 whitelisted rows of the customer plus rows that must never appear.
        var customer = await database.SeedCustomerAsync(org, branch.Id, "Activity Customer");
        var rivalCustomer = await database.SeedCustomerAsync(org, branch.Id, "Rival Customer");
        var foreignCustomer = await database.SeedCustomerAsync(other, otherBranch.Id, "Foreign Customer");
        var property = await database.ScalarAsync<Guid>("SELECT id FROM properties WHERE customer_id = @c", ("c", customer));
        var rivalProperty = await database.ScalarAsync<Guid>("SELECT id FROM properties WHERE customer_id = @c", ("c", rivalCustomer));
        var noteIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var rivalNote = Guid.NewGuid();

        foreach (var id in noteIds.Append(rivalNote))
        {
            await database.ExecuteAsync(
                "INSERT INTO customer_notes (id, organization_id, customer_id, author_user_id, note) VALUES (@id, @o, @c, @a, 'Secret note text')",
                ("id", id), ("o", org), ("c", id == rivalNote ? rivalCustomer : customer), ("a", author.UserId));
        }

        const string secret = "{\"email\":\"leak@example.com\",\"note\":\"SECRET-NOTE\"}";
        var minute = 0;
        var seeded = new List<(string Action, string Type, Guid Id)>
        {
            ("customer.created", "customer", customer),
            ("customer.archived", "customer", customer),
            ("customer.reactivated", "customer", customer),
            ("property.created", "property", property),
            ("property.updated", "property", property),
            ("property.primary_changed", "property", property),
            ("property.archived", "property", property),
            ("property.reactivated", "property", property),
            ("customer_note.created", "customer_note", noteIds[0]),
            ("customer_note.created", "customer_note", noteIds[1]),
        };

        seeded.AddRange(Enumerable.Repeat(("customer.updated", "customer", customer), 15));
        Assert.Equal(25, seeded.Count);

        foreach (var (action, type, id) in seeded)
        {
            await database.SeedAuditAsync(org, minute == 3 ? null : author.UserId, action, type, id, now.AddMinutes(-100 + minute++), secret, secret, secret);
        }

        foreach (var (action, type, id, orgId) in new (string, string, Guid, Guid)[]
        {
            ("customer.imported", "customer", customer, org),
            ("customer_tag.created", "customer_tag", customer, org),
            ("property.deleted", "property", property, org),
            ("customer.updated", "customer", rivalCustomer, org),
            ("property.updated", "property", rivalProperty, org),
            ("customer_note.created", "customer_note", rivalNote, org),
            ("customer.updated", "customer", customer, other),
            ("customer.updated", "customer", foreignCustomer, other),
        })
        {
            await database.SeedAuditAsync(orgId, author.UserId, action, type, id, now.AddMinutes(-100 + minute++), secret, secret, secret);
        }

        await database.ExecuteAsync("UPDATE properties SET name = 'Renamed Site' WHERE id = @p", ("p", property));
        var activityPath = $"/customers/{customer}/activity";

        var first = await host.SendAsync(HttpMethod.Get, activityPath, viewer);
        var firstText = await first.Content.ReadAsStringAsync();
        var firstPage = JsonNode.Parse(firstText)!;
        var second = JsonNode.Parse(await (await host.SendAsync(HttpMethod.Get, $"{activityPath}?page=2", viewer)).Content.ReadAsStringAsync())!;
        Assert.Equal((25, 20, 20, 5), (
            firstPage["totalCount"]!.GetValue<int>(), firstPage["pageSize"]!.GetValue<int>(), firstPage["items"]!.AsArray().Count, second["items"]!.AsArray().Count));
        var all = firstPage["items"]!.AsArray().Concat(second["items"]!.AsArray()).ToArray();
        Assert.Equal(25, all.Select(item => item!["id"]!.GetValue<long>()).Distinct().Count());
        Assert.Equal(all.Select(item => item!["occurredAt"]!.GetValue<DateTimeOffset>()).OrderByDescending(at => at).ToArray(), all.Select(item => item!["occurredAt"]!.GetValue<DateTimeOffset>()).ToArray());
        Assert.All(all, item => Assert.Contains(item!["action"]!.GetValue<string>(), CustomerActivityActions.Customer
            .Concat(CustomerActivityActions.Property)
            .Concat(CustomerActivityActions.Note)));
        Assert.All(
            all.Where(item => item!["action"]!.GetValue<string>().StartsWith("property.", StringComparison.Ordinal)),
            item => Assert.Equal("Renamed Site", item!["subjectName"]!.GetValue<string>()));
        Assert.All(
            all.Where(item => !item!["action"]!.GetValue<string>().StartsWith("property.", StringComparison.Ordinal)),
            item => Assert.Null(item!["subjectName"]));
        Assert.Single(all, item => item!["actorName"] is null);
        Assert.Contains(all, item => item!["actorName"]?.GetValue<string>() == "Nora Notes");
        Assert.Empty((await CustomerHost.ReadAsync(await host.SendAsync(HttpMethod.Get, $"{activityPath}?page=3", viewer)))["items"]!.AsArray());
        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(HttpMethod.Get, $"{activityPath}?page=0", viewer)).StatusCode);

        foreach (var forbidden in new[] { "SECRET-NOTE", "leak@example.com", "10.1.2.3", "beforeData", "afterData", "metadata", "ipAddress", "Secret note text" })
        {
            Assert.DoesNotContain(forbidden, firstText, StringComparison.Ordinal);
        }
    }

    private sealed class ApproximateComparer : IEqualityComparer<DateTimeOffset>
    {
        public bool Equals(DateTimeOffset x, DateTimeOffset y) => (x - y).Duration() < TimeSpan.FromSeconds(1);

        public int GetHashCode(DateTimeOffset obj) => 0;
    }
}
