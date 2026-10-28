using System.Text.Json;
using FieldOps.Application.Authentication;
using FieldOps.Application.Features.InvoiceDelivery;
using FieldOps.Application.Features.Notifications;
using FieldOps.Application.Features.Quotes;
using FieldOps.Domain.Branches;
using FieldOps.Domain.Catalog;
using FieldOps.Domain.Customers;
using FieldOps.Domain.Invoices;
using FieldOps.Domain.Notifications;
using FieldOps.Domain.Organizations;
using FieldOps.Domain.Quotes;
using FieldOps.Domain.Requests;
using FieldOps.Domain.Technicians;
using FieldOps.Domain.Users;
using FieldOps.Domain.WorkOrders;

namespace FieldOps.DemoSeeder;

/// <summary>Everything the loader inserts, plus the numbering counters to advance after the insert.</summary>
public sealed class DemoDataSet
{
    public required Organization Organization { get; init; }

    public List<object> Entities { get; } = [];

    public List<(string Email, string Role)> Accounts { get; } = [];

    public long NextQuoteNumber { get; set; } = 1;

    public long NextWorkOrderNumber { get; set; } = 1;

    public long NextInvoiceNumber { get; set; } = 1;

    public long NextPaymentNumber { get; set; } = 1;

    public long NextRequestNumber { get; set; } = 1;
}

/// <summary>
/// Builds a fictitious, internally consistent tenant in memory using the domain factories and state transitions, so
/// the business invariants apply. It touches no database and sends nothing; <see cref="Program"/> persists the result.
/// </summary>
public sealed class DemoDataBuilder(IPasswordHasher hasher, IReadOnlyDictionary<string, short> roleIds)
{
    public const string PublicSlug = "fieldops-demo";

    private const string Currency = "USD";

    private const decimal TaxRate = 8.25m;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly List<(Guid UserId, string RoleCode)> staff = [];

    private DemoDataSet data = null!;

    private DateTimeOffset now;

    private string password = string.Empty;

    private string domain = string.Empty;

    private Guid ownerId;

    private Guid dispatcherId;

    private Guid accountingId;

    private Guid mainBranchId;

    private Guid westBranchId;

    public DemoDataSet Build(string emailDomain, string accountPassword, DateTimeOffset reference)
    {
        domain = emailDomain;
        password = accountPassword;
        now = reference;

        var organization = Organization.Create(
            "Summit Home Services (Demo)",
            "Summit Home Services LLC",
            null,
            $"hello@{domain}",
            "555-0100",
            "America/New_York",
            Currency,
            TaxRate,
            "Q",
            "WO",
            "INV",
            1000,
            PublicSlug);

        data = new DemoDataSet { Organization = organization };
        data.Entities.Add(organization);

        var hours = JsonSerializer.Serialize(
            new Dictionary<string, object>
            {
                ["monday"] = new { start = "08:00", end = "17:00" },
                ["tuesday"] = new { start = "08:00", end = "17:00" },
                ["wednesday"] = new { start = "08:00", end = "17:00" },
                ["thursday"] = new { start = "08:00", end = "17:00" },
                ["friday"] = new { start = "08:00", end = "17:00" },
            });

        var main = Branch.Create(organization.Id, "Downtown", "DT", $"downtown@{domain}", "555-0101", "100 Main St", null, "Springfield", "NY", "10001", "US", "America/New_York", hours, isMain: true);
        var west = Branch.Create(organization.Id, "Westside", "WS", $"westside@{domain}", "555-0102", "42 Orchard Ave", null, "Springfield", "NY", "10002", "US", "America/New_York", hours);
        mainBranchId = main.Id;
        westBranchId = west.Id;
        data.Entities.AddRange(main, west);

        CreateStaff(main, west);
        var technicians = CreateTechnicians(main, west);
        var (categories, items) = CreateCatalog();
        var customers = CreateCustomers(main, west);
        CreateWorkflow(customers, categories, items, technicians);
        CreateNotifications();

        return data;
    }

    private Guid AddUser(string local, string first, string last, string role, Branch[] branches, bool allBranches)
    {
        var email = EmailNormalizer.Normalize($"{local}@{domain}");
        var user = User.Create(email, hasher.Hash(password), first, last, "555-0110");
        user.Activate();
        user.MarkEmailVerified(now.AddDays(-60));

        var membership = OrganizationUser.Create(
            data.Organization.Id,
            user.Id,
            roleIds[role],
            allBranches,
            now.AddDays(-60),
            ownerId == Guid.Empty ? null : ownerId);

        data.Entities.Add(user);
        data.Entities.Add(membership);

        if (!allBranches)
        {
            data.Entities.AddRange(branches.Select(branch => OrganizationUserBranch.Create(membership.Id, branch.Id)));
        }

        data.Accounts.Add((email, role));
        staff.Add((user.Id, role));
        membershipByUser[user.Id] = membership.Id;

        return user.Id;
    }

    private readonly Dictionary<Guid, Guid> membershipByUser = [];

    private readonly List<(string Code, NotificationContent Content, Guid EntityId, int HoursAgo)> events = [];

    private void CreateStaff(Branch main, Branch west)
    {
        ownerId = AddUser("owner", "Olivia", "Owner", "owner", [main, west], true);
        AddUser("operations", "Omar", "Ortiz", "operations_manager", [main, west], true);
        dispatcherId = AddUser("dispatcher", "Dana", "Diaz", "dispatcher", [main], false);
        accountingId = AddUser("accounting", "Alex", "Abbott", "accounting", [main, west], true);
        AddUser("viewer", "Vera", "Vance", "viewer", [main, west], true);
    }

    private List<TechnicianProfile> CreateTechnicians(Branch main, Branch west)
    {
        var skills = new[] { "HVAC", "Plumbing", "Electrical" }
            .Select(name => Skill.Create(data.Organization.Id, name))
            .ToList();
        data.Entities.AddRange(skills);

        var people = new[]
        {
            ("tech.hvac", "Tomas", "Herrera", main, 0, 0),
            ("tech.plumbing", "Priya", "Patel", main, 1, 1),
            ("tech.electric", "Eli", "Evans", west, 2, 2),
        };

        var technicians = new List<TechnicianProfile>();

        foreach (var (local, first, last, branch, skillIndex, colorIndex) in people)
        {
            var userId = AddUser(local, first, last, "technician", [branch], false);
            var profile = TechnicianProfile.Create(
                data.Organization.Id, branch.Id, first, last, $"{local}@{domain}", "555-0120", $"T-{colorIndex + 1:00}");
            profile.LinkMembership(membershipByUser[userId], now.AddDays(-60));
            profile.UseCustomSchedule(now.AddDays(-60));

            data.Entities.Add(profile);
            data.Entities.Add(TechnicianSkill.Create(profile.Id, skills[skillIndex].Id, 5, 8m, true));
            data.Entities.Add(TechnicianSkill.Create(profile.Id, skills[(skillIndex + 1) % 3].Id, 3, 2m));

            for (short day = 1; day <= 5; day++)
            {
                data.Entities.Add(TechnicianWeeklyAvailability.Create(profile.Id, day, new TimeOnly(8, 0), new TimeOnly(17, 0)));
            }

            technicians.Add(profile);
        }

        return technicians;
    }

    private (List<ServiceCategory> Categories, List<CatalogItem> Items) CreateCatalog()
    {
        var categories = new[] { "HVAC", "Plumbing", "Electrical" }
            .Select(name => ServiceCategory.Create(data.Organization.Id, name))
            .ToList();
        data.Entities.AddRange(categories);

        var specs = new (string Name, CatalogItemType Type, decimal Cost, decimal Price, int Category)[]
        {
            ("AC diagnostic visit", CatalogItemType.Service, 40m, 95m, 0),
            ("AC tune-up", CatalogItemType.Service, 60m, 149m, 0),
            ("Air filter (pleated)", CatalogItemType.Product, 6m, 18m, 0),
            ("Drain cleaning", CatalogItemType.Service, 55m, 140m, 1),
            ("Water heater flush", CatalogItemType.Service, 45m, 120m, 1),
            ("Faucet cartridge", CatalogItemType.Product, 12m, 34m, 1),
            ("Outlet replacement", CatalogItemType.Service, 35m, 85m, 2),
            ("Ceiling fan install", CatalogItemType.Service, 70m, 180m, 2),
        };

        var items = specs
            .Select(spec => CatalogItem.Create(
                data.Organization.Id, spec.Type, spec.Name, "Demo catalog entry.", spec.Cost, spec.Price, true, true, categories[spec.Category].Id))
            .ToList();
        data.Entities.AddRange(items);

        return (categories, items);
    }

    private sealed record DemoCustomer(Customer Customer, CustomerContact Contact, Property Property, Branch Branch);

    private List<DemoCustomer> CreateCustomers(Branch main, Branch west)
    {
        var specs = new (string Name, CustomerType Type, string First, string Last, string Street, Branch Branch)[]
        {
            ("Grace Hopper", CustomerType.Person, "Grace", "Hopper", "12 Elm St", main),
            ("Marcus Lee", CustomerType.Person, "Marcus", "Lee", "88 Pine Rd", main),
            ("Riverside Dental", CustomerType.Company, "Nora", "Quinn", "301 River Rd", main),
            ("Sofia Alvarez", CustomerType.Person, "Sofia", "Alvarez", "9 Maple Ct", west),
            ("Brightway Cafe", CustomerType.Company, "Ben", "Carter", "55 Harbor Way", west),
        };

        var customers = new List<DemoCustomer>();

        for (var index = 0; index < specs.Length; index++)
        {
            var spec = specs[index];
            var email = $"customer{index + 1}@{domain}";
            var customer = Customer.Create(data.Organization.Id, spec.Branch.Id, spec.Type, spec.Name, email, "555-0130", "Demo customer.");
            var contact = CustomerContact.CreatePrimary(data.Organization.Id, customer.Id, spec.First, spec.Last, email, "555-0130", null, true, false);
            var property = Property.Create(
                data.Organization.Id, customer.Id, "Primary address", spec.Street, "Springfield", "US", spec.Branch.Id, "NY", "10001", isPrimary: true);

            data.Entities.AddRange(customer, contact, property);
            customers.Add(new DemoCustomer(customer, contact, property, spec.Branch));
        }

        // The first customer can sign in to the portal with the same demo password.
        var portalUser = User.Create(EmailNormalizer.Normalize($"customer1@{domain}"), hasher.Hash(password), "Grace", "Hopper", "555-0130");
        portalUser.Activate();
        portalUser.MarkEmailVerified(now.AddDays(-30));
        customers[0].Contact.LinkPortal(portalUser.Id, now.AddDays(-30));
        data.Entities.Add(portalUser);
        data.Accounts.Add((EmailNormalizer.Normalize($"customer1@{domain}"), "customer portal"));

        return customers;
    }

    private static string AddressJson(Property property) =>
        JsonSerializer.Serialize(
            new
            {
                line1 = property.AddressLine1,
                line2 = property.AddressLine2,
                city = property.City,
                state = property.StateRegion,
                postalCode = property.PostalCode,
                countryCode = property.CountryCode,
                propertyType = "home",
            },
            Json);

    private ServiceRequest NewRequest(DemoCustomer customer, ServiceCategory category, string description, RequestStatus status, int daysAgo, string urgency = "standard")
    {
        var request = ServiceRequest.Create(
            data.Organization.Id,
            data.NextRequestNumber++,
            description,
            now.AddDays(-daysAgo + 3),
            now.AddDays(-daysAgo + 3).AddHours(4),
            customer.Customer.Id,
            customer.Contact.Id,
            customer.Property.Id,
            category.Id,
            null,
            null,
            null,
            null,
            AddressJson(customer.Property),
            urgency,
            false,
            null,
            now.AddDays(-daysAgo),
            "public_form",
            customer.Branch.Id);

        if (status != RequestStatus.New)
        {
            request.ChangeStatus(status);
            data.Entities.Add(RequestStatusHistory.Create(data.Organization.Id, request.Id, RequestStatus.New, status, dispatcherId));
        }

        data.Entities.Add(request);

        if (status is RequestStatus.New or RequestStatus.NeedsReview)
        {
            events.Add((
                NotificationEventCodes.RequestCreated,
                NotificationCopy.RequestCreated($"REQ-{request.RequestNumber}", customer.Customer.DisplayName),
                request.Id,
                daysAgo * 12));
        }

        return request;
    }

    private sealed record Priced(string Name, CatalogItem Item, decimal Quantity);

    private (Quote Quote, QuoteVersion Version, decimal Subtotal, decimal Tax, decimal Total, List<QuoteLine> Lines) NewQuote(
        ServiceRequest request, DemoCustomer customer, Priced[] lines, QuoteStatus target, int daysAgo)
    {
        var inputs = lines.Select(line => new QuoteLineInput(true, false, line.Quantity, line.Item.UnitPrice, line.Item.UnitCost)).ToList();
        var calculation = QuoteCalculator.Calculate(inputs, 0m, TaxRate, Currency).Value
            ?? throw new InvalidOperationException("Demo quote calculation failed.");

        var quote = Quote.Create(data.Organization.Id, customer.Branch.Id, request.Id, customer.Customer.Id, customer.Property.Id, data.NextQuoteNumber++, dispatcherId);
        var version = QuoteVersion.Create(
            data.Organization.Id,
            quote.Id,
            1,
            string.Join(", ", lines.Select(line => line.Name)),
            calculation.Subtotal,
            calculation.TaxTotal,
            calculation.Total,
            Currency,
            dispatcherId,
            0m,
            "Payment due within 15 days of invoice.",
            DateOnly.FromDateTime(now.AddDays(30 - daysAgo).UtcDateTime),
            "Thank you for choosing Summit Home Services.");

        var quoteLines = new List<QuoteLine>();

        for (var index = 0; index < lines.Length; index++)
        {
            var calculated = calculation.Lines[index];
            var item = lines[index].Item;
            quoteLines.Add(QuoteLine.Create(
                data.Organization.Id,
                version.Id,
                item.Id,
                item.Type,
                item.Name,
                item.Description,
                lines[index].Quantity,
                item.Unit,
                item.UnitCost,
                item.UnitPrice,
                calculated.TaxRate,
                calculated.LineSubtotal,
                calculated.LineTax,
                calculated.LineTotal,
                index,
                false));
        }

        if (target != QuoteStatus.Draft)
        {
            version.Freeze(now.AddDays(-daysAgo));
            quote.MarkSent(1);

            switch (target)
            {
                case QuoteStatus.Approved:
                    quote.Approve(version.Id, now.AddDays(-daysAgo + 1));
                    break;
                case QuoteStatus.Rejected:
                    quote.Reject(now.AddDays(-daysAgo + 1));
                    break;
                case QuoteStatus.ClarificationRequested:
                    quote.RequestClarification(now.AddDays(-daysAgo + 1));
                    break;
            }
        }

        data.Entities.Add(quote);
        data.Entities.Add(version);
        data.Entities.AddRange(quoteLines);

        return (quote, version, calculation.Subtotal, calculation.TaxTotal, calculation.Total, quoteLines);
    }

    private void CreateWorkflow(List<DemoCustomer> customers, List<ServiceCategory> categories, List<CatalogItem> items, List<TechnicianProfile> technicians)
    {
        // Requests at the front of the funnel.
        NewRequest(customers[1], categories[0], "AC blows warm air since yesterday.", RequestStatus.New, 1, "urgent");
        NewRequest(customers[4], categories[2], "Two outlets in the kitchen stopped working.", RequestStatus.NeedsReview, 2);
        NewRequest(customers[3], categories[1], "Slow drain in the main bathroom.", RequestStatus.ReadyForQuote, 3);

        // Assessment scheduled.
        var assessed = NewRequest(customers[2], categories[0], "Annual HVAC inspection for the clinic.", RequestStatus.AssessmentScheduled, 4);
        data.Entities.Add(Assessment.Create(data.Organization.Id, assessed.Id, now.AddDays(2).AddHours(1), now.AddDays(2).AddHours(2), dispatcherId, technicians[0].Id, "On-site inspection"));

        // Quoted: one sent, one awaiting clarification, one rejected.
        var sent = NewRequest(customers[0], categories[1], "Water heater is making noise.", RequestStatus.Quoted, 5);
        NewQuote(sent, customers[0], [new Priced("Water heater flush", items[4], 1)], QuoteStatus.Sent, 5);

        var clarification = NewRequest(customers[4], categories[0], "Cafe AC tune-up before summer.", RequestStatus.Quoted, 6);
        var clarifying = NewQuote(clarification, customers[4], [new Priced("AC tune-up", items[1], 2), new Priced("Air filter", items[2], 4)], QuoteStatus.ClarificationRequested, 6);
        events.Add((
            NotificationEventCodes.QuoteClarificationRequested,
            NotificationCopy.QuoteClarification($"Q-{clarifying.Quote.QuoteNumber}", $"REQ-{clarification.RequestNumber}", customers[4].Customer.DisplayName),
            clarifying.Quote.Id,
            10));

        var rejected = NewRequest(customers[1], categories[2], "Ceiling fan installation in the bedroom.", RequestStatus.Quoted, 12);
        NewQuote(rejected, customers[1], [new Priced("Ceiling fan install", items[7], 1)], QuoteStatus.Rejected, 12);

        // Approved quotes that became work orders in every stage of the job lifecycle.
        CreateJob(customers[0], categories[0], technicians[0], items, JobStage.Scheduled, "AC tune-up before the season", 14, new Priced("AC tune-up", items[1], 1));
        CreateJob(customers[2], categories[1], technicians[1], items, JobStage.InProgress, "Clinic drain cleaning", 10, new Priced("Drain cleaning", items[3], 2));
        CreateJob(customers[3], categories[2], technicians[2], items, JobStage.AwaitingReview, "Replace kitchen outlets", 8, new Priced("Outlet replacement", items[6], 3));
        CreateJob(customers[1], categories[0], technicians[0], items, JobStage.InvoiceDraft, "AC diagnostic and filter swap", 20, new Priced("AC diagnostic visit", items[0], 1), new Priced("Air filter", items[2], 2));
        CreateJob(customers[0], categories[1], technicians[1], items, JobStage.InvoiceSent, "Faucet cartridge replacement", 25, new Priced("Faucet cartridge", items[5], 2));
        CreateJob(customers[4], categories[2], technicians[2], items, JobStage.InvoicePartlyPaid, "Install two ceiling fans", 32, new Priced("Ceiling fan install", items[7], 2));
        CreateJob(customers[2], categories[0], technicians[0], items, JobStage.InvoicePaid, "Quarterly HVAC service", 45, new Priced("AC tune-up", items[1], 3));
    }

    private enum JobStage
    {
        Scheduled,
        InProgress,
        AwaitingReview,
        InvoiceDraft,
        InvoiceSent,
        InvoicePartlyPaid,
        InvoicePaid,
    }

    private void CreateJob(
        DemoCustomer customer, ServiceCategory category, TechnicianProfile technician, List<CatalogItem> items, JobStage stage, string title, int daysAgo, params Priced[] lines)
    {
        var request = NewRequest(customer, category, title, RequestStatus.Converted, daysAgo);
        var (quote, version, subtotal, tax, total, quoteLines) = NewQuote(request, customer, lines, QuoteStatus.Approved, daysAgo);

        events.Add((
            NotificationEventCodes.QuoteApproved,
            NotificationCopy.QuoteApproved($"Q-{quote.QuoteNumber}", $"REQ-{request.RequestNumber}", customer.Customer.DisplayName, NotificationCopy.Money(total, Currency)),
            quote.Id,
            daysAgo * 24 - 24));

        var workOrder = WorkOrder.Create(
            data.Organization.Id,
            customer.Branch.Id,
            data.NextWorkOrderNumber++,
            version.Id,
            customer.Customer.Id,
            customer.Property.Id,
            version.Scope,
            dispatcherId,
            new WorkOrderFields(title, "one_time", category.Id, 3, 120, null, null, "Demo job instructions.", null, null, true, true, true));
        workOrder.MarkReady(now.AddDays(-daysAgo + 2));
        data.Entities.Add(workOrder);

        var visitDay = stage == JobStage.Scheduled ? now.AddDays(2) : now.AddDays(-daysAgo + 3);
        var start = new DateTimeOffset(visitDay.Year, visitDay.Month, visitDay.Day, 14, 0, 0, TimeSpan.Zero);
        var visit = Visit.Create(data.Organization.Id, workOrder.Id, 1, start, start.AddHours(2));
        visit.ApplyDispatch(start, start.AddHours(2), start.AddMinutes(-30), start.AddMinutes(30), "Call 15 minutes before arriving.", now.AddDays(-daysAgo + 2));
        visit.SetStatus(VisitStatus.Assigned);
        workOrder.MarkScheduled(now.AddDays(-daysAgo + 2));

        data.Entities.Add(visit);
        data.Entities.Add(VisitAssignment.Create(visit.Id, technician.Id, dispatcherId, true, now.AddDays(-daysAgo + 2)));
        data.Entities.Add(VisitStatusHistory.Create(visit.Id, null, VisitStatus.Scheduled, dispatcherId, now.AddDays(-daysAgo + 2)));
        data.Entities.Add(VisitStatusHistory.Create(visit.Id, VisitStatus.Scheduled, VisitStatus.Assigned, dispatcherId, now.AddDays(-daysAgo + 2)));

        if (stage == JobStage.Scheduled)
        {
            return;
        }

        visit.StartJob(start.AddMinutes(5));
        workOrder.MarkInProgress(start.AddMinutes(5));
        data.Entities.Add(VisitStatusHistory.Create(visit.Id, VisitStatus.Assigned, VisitStatus.InProgress, null, start.AddMinutes(5)));
        data.Entities.Add(VisitTimeEntry.Create(visit.Id, technician.Id, start.AddMinutes(5), VisitTimeEntryType.Work, stage == JobStage.InProgress ? null : start.AddMinutes(95)));
        data.Entities.Add(VisitChecklistItem.Create(visit.Id, "Confirm scope with the customer", null, 0, true));

        if (stage == JobStage.InProgress)
        {
            return;
        }

        var completedAt = start.AddMinutes(95);
        visit.SetTechnicianNotes("Work completed as quoted. Customer satisfied.", completedAt);
        visit.Complete(0, "Customer not available to sign.", completedAt);
        workOrder.MarkCompleted(completedAt);
        data.Entities.Add(VisitStatusHistory.Create(visit.Id, VisitStatus.InProgress, VisitStatus.Completed, null, completedAt));

        events.Add((
            NotificationEventCodes.JobCompleted,
            NotificationCopy.JobCompleted($"WO-{workOrder.WorkOrderNumber}", $"{technician.FirstName} {technician.LastName}"),
            workOrder.Id,
            Math.Max(1, (int)(now - completedAt).TotalHours)));

        if (stage == JobStage.AwaitingReview)
        {
            return;
        }

        visit.Approve(accountingId, completedAt.AddDays(1));
        workOrder.MarkApprovedForBilling("Approved for billing.", completedAt.AddDays(1));

        var invoice = Invoice.Create(
            data.Organization.Id,
            customer.Branch.Id,
            data.NextInvoiceNumber++,
            workOrder.Id,
            customer.Customer.Id,
            Currency,
            subtotal,
            tax,
            total,
            total,
            accountingId,
            DateOnly.FromDateTime(completedAt.AddDays(2).UtcDateTime),
            null,
            0m,
            "net_15");
        var invoiceLines = quoteLines
            .Select(line => InvoiceLine.Create(
                invoice.Id, line.Name, line.Quantity, line.Unit, line.UnitPrice, line.LineSubtotal, line.LineTax, line.LineTotal, line.Id, null, line.TaxRate, line.SortOrder))
            .ToList();

        data.Entities.Add(invoice);
        data.Entities.AddRange(invoiceLines);

        if (stage == JobStage.InvoiceDraft)
        {
            return;
        }

        var issued = completedAt.AddDays(2);
        var snapshot = JsonSerializer.Serialize(
            new InvoiceCustomerSnapshot(
                new InvoiceBillTo(customer.Customer.DisplayName, customer.Customer.PrimaryEmail, customer.Customer.PrimaryPhone, [customer.Property.AddressLine1, "Springfield, NY 10001"]),
                $"{customer.Property.AddressLine1}, Springfield, NY",
                "Work completed as quoted."),
            Json);

        events.Add((
            NotificationEventCodes.InvoiceSent,
            NotificationCopy.InvoiceSent($"INV-{invoice.InvoiceNumber}", customer.Customer.DisplayName, NotificationCopy.Money(total, Currency)),
            invoice.Id,
            Math.Max(1, (int)(now - issued).TotalHours)));

        invoice.MarkSent("net_15", DateOnly.FromDateTime(issued.AddDays(15).UtcDateTime), customer.Customer.PrimaryEmail!, "Thank you for your business.", snapshot, issued);

        if (stage == JobStage.InvoiceSent)
        {
            return;
        }

        var paidAmount = stage == JobStage.InvoicePaid ? total : Math.Round(total / 2m, 2, MidpointRounding.AwayFromZero);
        var payment = Payment.Create(
            data.Organization.Id,
            customer.Customer.Id,
            data.NextPaymentNumber++,
            PaymentMethod.Check,
            paidAmount,
            Currency,
            issued.AddDays(3),
            accountingId,
            Guid.NewGuid(),
            accountingId,
            "Check #1042",
            null,
            $"R-{data.NextPaymentNumber - 1:0000}");
        invoice.ApplyPayment(paidAmount, issued.AddDays(3));
        events.Add((
            NotificationEventCodes.PaymentReceived,
            NotificationCopy.PaymentReceived($"INV-{invoice.InvoiceNumber}", NotificationCopy.Money(paidAmount, Currency)),
            invoice.Id,
            Math.Max(1, (int)(now - issued.AddDays(3)).TotalHours)));

        data.Entities.Add(payment);
        data.Entities.Add(PaymentAllocation.Create(payment.Id, invoice.Id, paidAmount));
    }

    private void CreateNotifications()
    {
        // In-app notifications for the events above; recipients follow the catalog roles. No email is involved.
        foreach (var (code, content, entityId, hoursAgo) in events)
        {
            var catalog = NotificationCatalog.Get(code);
            var occurredAt = now.AddHours(-hoursAgo);
            var payload = JsonSerializer.Serialize(
                new
                {
                    type = catalog.Type,
                    description = content.Description,
                    entityKind = content.EntityKind,
                    entityId,
                    entityLabel = content.EntityLabel,
                    secondaryLabel = content.SecondaryLabel,
                    branchId = (Guid?)null,
                },
                Json);

            foreach (var userId in staff.Where(member => catalog.Roles.Contains(member.RoleCode)).Select(member => member.UserId).Distinct())
            {
                var notification = Notification.CreateInApp(
                    data.Organization.Id, userId, code, NotificationCopy.Truncate(content.Title), payload, occurredAt);

                // A mix of read and unread items makes the inbox representative.
                if (hoursAgo >= 72)
                {
                    notification.MarkRead();
                }

                data.Entities.Add(notification);
            }
        }
    }
}
