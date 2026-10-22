using System.Text.Json;
using FieldOps.Application.Features.Access;
using FieldOps.Application.Features.AuditTrail;
using FieldOps.Application.Features.Customers;
using FieldOps.Application.Features.ServiceRequests;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Persistence;

/// <summary>
/// Read model of the Activity tab (customer-related-records BR-10 to BR-13). Events come from <c>audit_logs</c> of the
/// organization only, restricted to the whitelist of <see cref="CustomerActivityCatalog"/> and to entities that are visible
/// under <see cref="CustomerRecordScope"/>. Redaction is by projection: only id, action, entity, actor and time are
/// selected from the audit row (plus the new-status code of <c>work_order.status_changed</c>); summaries come from the
/// current records, never from the audit payload.
/// </summary>
internal sealed class CustomerActivityStore(FieldOpsDbContext dbContext) : ICustomerActivityStore
{
    public async Task<CustomerActivityBlock> ListAsync(
        Guid organizationId,
        BranchScope scope,
        Guid customerId,
        CustomerRecordAccess access,
        CustomerActivityQuery query,
        CancellationToken cancellationToken)
    {
        var raws = new List<RawEvent>();

        foreach (var type in query.Types.Where(type => CustomerActivityCatalog.IsAvailable(type, access)))
        {
            foreach (var entityType in CustomerActivityCatalog.EntityTypesOf(type))
            {
                var actions = CustomerActivityCatalog.Of(type, entityType).Select(entry => entry.Action).ToArray();
                var ids = EntityIds(organizationId, scope, customerId, entityType);

                var rows = dbContext.AuditLogs.AsNoTracking()
                    .Where(log => log.OrganizationId == organizationId
                        && log.EntityType == entityType
                        && actions.Contains(log.Action)
                        && ids.Contains(log.EntityId));

                if (query.Since is { } since)
                {
                    rows = rows.Where(log => log.OccurredAt >= since);
                }

                if (query.Before is { } before)
                {
                    var at = before.OccurredAt;
                    var id = before.Id;
                    rows = rows.Where(log => log.OccurredAt < at || (log.OccurredAt == at && log.Id < id));
                }

                var found = await rows
                    .OrderByDescending(log => log.OccurredAt)
                    .ThenByDescending(log => log.Id)
                    .Take(CustomerActivityRules.BlockSize + 1)
                    .Select(log => new { log.Id, log.Action, log.EntityId, log.ActorUserId, log.OccurredAt })
                    .ToListAsync(cancellationToken);

                raws.AddRange(found.Select(row => new RawEvent(
                    row.Id, type, row.Action, entityType, row.EntityId!.Value, row.ActorUserId, row.OccurredAt)));
            }
        }

        var ordered = raws
            .OrderByDescending(raw => raw.OccurredAt)
            .ThenByDescending(raw => raw.Id)
            .Take(CustomerActivityRules.BlockSize + 1)
            .ToList();
        var hasMore = ordered.Count > CustomerActivityRules.BlockSize;
        var block = ordered.Take(CustomerActivityRules.BlockSize).ToList();

        if (block.Count == 0)
        {
            return new CustomerActivityBlock([], null);
        }

        var details = await ReadDetailsAsync(organizationId, scope, customerId, block, cancellationToken);
        var items = block.Select(raw => details.ToEvent(raw)).ToList();
        var last = block[^1];

        return new CustomerActivityBlock(items, hasMore ? new CustomerActivityCursor(last.OccurredAt, last.Id) : null);
    }

    // The visible entities of the customer for one audit entity type (BR-10).
    private IQueryable<Guid?> EntityIds(Guid organizationId, BranchScope scope, Guid customerId, string entityType) =>
        entityType switch
        {
            CustomerAuditActions.EntityType => dbContext.Customers.AsNoTracking()
                .Where(customer => customer.OrganizationId == organizationId && customer.Id == customerId)
                .Select(customer => (Guid?)customer.Id),
            CustomerAuditActions.PropertyEntityType => dbContext.Properties.AsNoTracking()
                .Where(property => property.OrganizationId == organizationId && property.CustomerId == customerId)
                .Select(property => (Guid?)property.Id),
            CustomerAuditActions.NoteEntityType => dbContext.CustomerNotes.AsNoTracking()
                .Where(note => note.OrganizationId == organizationId && note.CustomerId == customerId)
                .Select(note => (Guid?)note.Id),
            CustomerActivityCatalog.ContactEntity => dbContext.CustomerContacts.AsNoTracking()
                .Where(contact => contact.OrganizationId == organizationId && contact.CustomerId == customerId)
                .Select(contact => (Guid?)contact.Id),
            CustomerActivityCatalog.ServiceRequestEntity => dbContext.VisibleRequests(organizationId, scope, customerId)
                .Select(request => (Guid?)request.Id),
            CustomerActivityCatalog.QuoteEntity => dbContext.VisibleQuotes(organizationId, scope, customerId)
                .Select(quote => (Guid?)quote.Id),
            CustomerActivityCatalog.WorkOrderEntity => dbContext.VisibleJobs(organizationId, scope, customerId)
                .Select(order => (Guid?)order.Id),
            CustomerActivityCatalog.VisitEntity => dbContext.VisibleVisits(organizationId, scope, customerId)
                .Select(visit => (Guid?)visit.Id),
            CustomerActivityCatalog.InvoiceEntity => dbContext.VisibleInvoices(organizationId, scope, customerId)
                .Select(invoice => (Guid?)invoice.Id),
            CustomerActivityCatalog.PaymentEntity => dbContext.VisiblePayments(organizationId, scope, customerId)
                .Select(payment => (Guid?)payment.Id),
            _ => throw new InvalidOperationException("Unexpected activity entity type."),
        };

    private async Task<Details> ReadDetailsAsync(
        Guid organizationId, BranchScope scope, Guid customerId, List<RawEvent> block, CancellationToken cancellationToken)
    {
        Guid[] IdsOf(string entityType) =>
            [.. block.Where(raw => raw.EntityType == entityType).Select(raw => raw.EntityId).Distinct()];

        var organization = await dbContext.Organizations.AsNoTracking()
            .Where(candidate => candidate.Id == organizationId)
            .Select(candidate => new
            {
                candidate.RequestPrefix,
                candidate.QuotePrefix,
                candidate.WorkOrderPrefix,
                candidate.InvoicePrefix,
            })
            .SingleAsync(cancellationToken);

        var details = new Details();

        var propertyIds = IdsOf(CustomerAuditActions.PropertyEntityType);

        if (propertyIds.Length > 0)
        {
            details.Properties = await dbContext.Properties.AsNoTracking()
                .Where(property => property.OrganizationId == organizationId
                    && property.CustomerId == customerId
                    && propertyIds.Contains(property.Id))
                .ToDictionaryAsync(property => property.Id, property => property.Name, cancellationToken);
        }

        var contactIds = IdsOf(CustomerActivityCatalog.ContactEntity);

        if (contactIds.Length > 0)
        {
            var contacts = await dbContext.CustomerContacts.AsNoTracking()
                .Where(contact => contact.OrganizationId == organizationId
                    && contact.CustomerId == customerId
                    && contactIds.Contains(contact.Id))
                .Select(contact => new { contact.Id, contact.FirstName, contact.LastName })
                .ToListAsync(cancellationToken);

            details.Contacts = contacts.ToDictionary(
                contact => contact.Id, contact => $"{contact.FirstName} {contact.LastName}".Trim());
        }

        var requestIds = IdsOf(CustomerActivityCatalog.ServiceRequestEntity);

        if (requestIds.Length > 0)
        {
            var requests = await (
                from request in dbContext.VisibleRequests(organizationId, scope, customerId)
                from item in dbContext.CatalogItems.AsNoTracking()
                    .Where(candidate => candidate.OrganizationId == organizationId && candidate.Id == request.CatalogItemId)
                    .DefaultIfEmpty()
                from category in dbContext.ServiceCategories.AsNoTracking()
                    .Where(candidate => candidate.OrganizationId == organizationId && candidate.Id == request.CategoryId)
                    .DefaultIfEmpty()
                where requestIds.Contains(request.Id)
                select new
                {
                    request.Id,
                    request.RequestNumber,
                    request.Description,
                    ServiceName = item == null ? null : item.Name,
                    CategoryName = category == null ? null : category.Name,
                })
                .ToListAsync(cancellationToken);

            details.Requests = requests.ToDictionary(
                request => request.Id,
                request => (
                    RequestCardRules.DisplayNumber(organization.RequestPrefix, request.RequestNumber),
                    RequestCardRules.Title(request.ServiceName, request.CategoryName, request.Description)));
        }

        var quoteIds = IdsOf(CustomerActivityCatalog.QuoteEntity);

        if (quoteIds.Length > 0)
        {
            var quotes = await dbContext.VisibleQuotes(organizationId, scope, customerId)
                .Where(quote => quoteIds.Contains(quote.Id))
                .Select(quote => new { quote.Id, quote.QuoteNumber, quote.CurrentVersionNo })
                .ToListAsync(cancellationToken);

            var versions = await dbContext.QuoteVersions.AsNoTracking()
                .Where(version => version.OrganizationId == organizationId && quoteIds.Contains(version.QuoteId))
                .Select(version => new { version.QuoteId, version.VersionNo, version.IsImmutable, version.Total })
                .ToListAsync(cancellationToken);

            details.Quotes = quotes.ToDictionary(
                quote => quote.Id,
                quote => (
                    RequestCardRules.DisplayNumber(organization.QuotePrefix, quote.QuoteNumber),
                    versions
                        .Where(version => version.QuoteId == quote.Id
                            && (quote.CurrentVersionNo == 0 ? !version.IsImmutable : version.VersionNo == quote.CurrentVersionNo))
                        .Select(version => (decimal?)version.Total)
                        .FirstOrDefault()));
        }

        var jobIds = IdsOf(CustomerActivityCatalog.WorkOrderEntity);
        var visitIds = IdsOf(CustomerActivityCatalog.VisitEntity);

        if (jobIds.Length > 0)
        {
            var jobs = await dbContext.VisibleJobs(organizationId, scope, customerId)
                .Where(order => jobIds.Contains(order.Id))
                .Select(order => new { order.Id, order.WorkOrderNumber, order.Title })
                .ToListAsync(cancellationToken);

            details.Jobs = jobs.ToDictionary(
                order => order.Id,
                order => (RequestCardRules.DisplayNumber(organization.WorkOrderPrefix, order.WorkOrderNumber), order.Title));
        }

        if (visitIds.Length > 0)
        {
            var visitRows = await (
                from visit in dbContext.VisibleVisits(organizationId, scope, customerId)
                join order in dbContext.WorkOrders.AsNoTracking() on visit.WorkOrderId equals order.Id
                where visitIds.Contains(visit.Id)
                select new { visit.Id, visit.WorkOrderId, visit.VisitNumber, visit.ScheduledStart, order.WorkOrderNumber })
                .ToListAsync(cancellationToken);

            details.Visits = visitRows.ToDictionary(
                visit => visit.Id,
                visit => (
                    visit.WorkOrderId,
                    RequestCardRules.DisplayNumber(organization.WorkOrderPrefix, visit.WorkOrderNumber),
                    visit.VisitNumber,
                    visit.ScheduledStart));
        }

        var invoiceIds = IdsOf(CustomerActivityCatalog.InvoiceEntity);

        if (invoiceIds.Length > 0)
        {
            var invoices = await dbContext.VisibleInvoices(organizationId, scope, customerId)
                .Where(invoice => invoiceIds.Contains(invoice.Id))
                .Select(invoice => new { invoice.Id, invoice.InvoiceNumber, invoice.Total })
                .ToListAsync(cancellationToken);

            details.Invoices = invoices.ToDictionary(
                invoice => invoice.Id,
                invoice => (RequestCardRules.DisplayNumber(organization.InvoicePrefix, invoice.InvoiceNumber), invoice.Total));
        }

        var paymentIds = IdsOf(CustomerActivityCatalog.PaymentEntity);

        if (paymentIds.Length > 0)
        {
            var payments = await dbContext.VisiblePayments(organizationId, scope, customerId)
                .Where(payment => paymentIds.Contains(payment.Id))
                .Select(payment => new { payment.Id, payment.Amount })
                .ToListAsync(cancellationToken);

            var visibleInvoices = dbContext.VisibleInvoices(organizationId, scope, customerId);
            var allocations = await (
                from allocation in dbContext.PaymentAllocations.AsNoTracking()
                join invoice in visibleInvoices on allocation.InvoiceId equals invoice.Id
                where paymentIds.Contains(allocation.PaymentId)
                select new { allocation.PaymentId, invoice.Id, invoice.InvoiceNumber })
                .ToListAsync(cancellationToken);

            details.Payments = payments.ToDictionary(
                payment => payment.Id,
                payment =>
                {
                    var linked = allocations
                        .Where(allocation => allocation.PaymentId == payment.Id)
                        .OrderBy(allocation => allocation.InvoiceNumber)
                        .ThenBy(allocation => allocation.Id)
                        .ToList();

                    return (
                        payment.Amount,
                        (IReadOnlyList<string>)[.. linked.Select(item => RequestCardRules.DisplayNumber(organization.InvoicePrefix, item.InvoiceNumber))],
                        linked.Count > 0 ? (Guid?)linked[0].Id : null);
                });
        }

        // Only the new-status code of a job status change is read from the audit payload, and only to pick the title.
        var statusEventIds = block
            .Where(raw => raw.Action == CustomerActivityCatalog.WorkOrderStatusChanged)
            .Select(raw => raw.Id)
            .ToArray();

        if (statusEventIds.Length > 0)
        {
            var payloads = await dbContext.AuditLogs.AsNoTracking()
                .Where(log => log.OrganizationId == organizationId && statusEventIds.Contains(log.Id))
                .Select(log => new { log.Id, log.AfterData })
                .ToListAsync(cancellationToken);

            foreach (var payload in payloads)
            {
                if (NewStatus(payload.AfterData) is { } status)
                {
                    details.NewStatuses[payload.Id] = status;
                }
            }
        }

        var actorIds = block.Where(raw => raw.ActorUserId != null).Select(raw => raw.ActorUserId!.Value).Distinct().ToArray();

        if (actorIds.Length > 0)
        {
            details.Members = (await dbContext.OrganizationUsers.AsNoTracking()
                .Where(member => member.OrganizationId == organizationId && actorIds.Contains(member.UserId))
                .Select(member => member.UserId)
                .ToListAsync(cancellationToken))
                .ToHashSet();

            var names = await dbContext.Users.AsNoTracking()
                .Where(user => actorIds.Contains(user.Id))
                .Select(user => new { user.Id, user.FirstName, user.LastName })
                .ToListAsync(cancellationToken);

            details.Actors = names.ToDictionary(user => user.Id, user => $"{user.FirstName} {user.LastName}".Trim());
        }

        return details;
    }

    private static string? NewStatus(string? afterData)
    {
        if (string.IsNullOrWhiteSpace(afterData))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(afterData);

            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("status", out var status)
                && status.ValueKind == JsonValueKind.String
                    ? status.GetString()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record RawEvent(
        long Id, string Type, string Action, string EntityType, Guid EntityId, Guid? ActorUserId, DateTimeOffset OccurredAt);

    private sealed class Details
    {
        public Dictionary<Guid, string> Properties { get; set; } = [];

        public Dictionary<Guid, string> Contacts { get; set; } = [];

        public Dictionary<Guid, (string Number, string Service)> Requests { get; set; } = [];

        public Dictionary<Guid, (string Number, decimal? Total)> Quotes { get; set; } = [];

        public Dictionary<Guid, (string Number, string Title)> Jobs { get; set; } = [];

        public Dictionary<Guid, (Guid JobId, string Number, int VisitNumber, DateTimeOffset? ScheduledStart)> Visits { get; set; } = [];

        public Dictionary<Guid, (string Number, decimal Total)> Invoices { get; set; } = [];

        public Dictionary<Guid, (decimal Amount, IReadOnlyList<string> InvoiceNumbers, Guid? FirstInvoiceId)> Payments { get; set; } = [];

        public Dictionary<long, string> NewStatuses { get; } = [];

        public HashSet<Guid> Members { get; set; } = [];

        public Dictionary<Guid, string> Actors { get; set; } = [];

        public CustomerActivityEvent ToEvent(RawEvent raw)
        {
            var subject = CustomerActivitySubject.Empty;
            CustomerActivityLink? link = null;
            var linkKind = CustomerActivityCatalog.TryGet(raw.Action, out var entry) ? entry.LinkKind : null;

            switch (raw.Type)
            {
                case CustomerActivityTypes.Customer when Properties.TryGetValue(raw.EntityId, out var propertyName)
                    && CustomerActivityCatalog.NamesProperty(raw.Action):
                    subject = subject with { PropertyName = propertyName };
                    break;
                case CustomerActivityTypes.Requests when Requests.TryGetValue(raw.EntityId, out var request):
                    subject = subject with { Number = request.Number, Service = request.Service };
                    link = Link(linkKind, raw.EntityId);
                    break;
                case CustomerActivityTypes.Messages:
                    subject = subject with { ContactName = Contacts.GetValueOrDefault(raw.EntityId) };
                    break;
                case CustomerActivityTypes.Quotes when Quotes.TryGetValue(raw.EntityId, out var quote):
                    subject = subject with { Number = quote.Number, Amount = quote.Total };
                    link = Link(linkKind, raw.EntityId);
                    break;
                case CustomerActivityTypes.Jobs when Jobs.TryGetValue(raw.EntityId, out var job):
                    subject = subject with
                    {
                        Number = job.Number,
                        Title = job.Title,
                        NewStatus = raw.Action == CustomerActivityCatalog.WorkOrderStatusChanged
                            ? NewStatuses.GetValueOrDefault(raw.Id)
                            : null,
                    };
                    link = Link(linkKind, raw.EntityId);
                    break;
                case CustomerActivityTypes.Visits when Visits.TryGetValue(raw.EntityId, out var visit):
                    subject = subject with
                    {
                        Number = visit.Number,
                        VisitNumber = visit.VisitNumber,
                        ScheduledAt = raw.Action == CustomerActivityCatalog.VisitDispatched ? visit.ScheduledStart : null,
                    };
                    link = Link(linkKind, visit.JobId);
                    break;
                case CustomerActivityTypes.Invoices when Invoices.TryGetValue(raw.EntityId, out var invoice):
                    subject = subject with { Number = invoice.Number, Amount = invoice.Total };
                    link = Link(linkKind, raw.EntityId);
                    break;
                case CustomerActivityTypes.Payments when Payments.TryGetValue(raw.EntityId, out var payment):
                    subject = subject with { Amount = payment.Amount, InvoiceNumbers = payment.InvoiceNumbers };
                    link = payment.FirstInvoiceId is { } invoiceId ? Link(linkKind, invoiceId) : null;
                    break;
            }

            return new CustomerActivityEvent(
                raw.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                raw.Type,
                raw.Action,
                raw.OccurredAt,
                Origin(raw),
                subject,
                link);
        }

        private static CustomerActivityLink? Link(string? kind, Guid id) => kind is null ? null : new CustomerActivityLink(kind, id);

        // BR-13: portal messages always come from the customer; otherwise the shared audit source decides.
        private CustomerActivityOrigin Origin(RawEvent raw)
        {
            if (raw.Type == CustomerActivityTypes.Messages)
            {
                return new CustomerActivityOrigin("customer_portal", null);
            }

            var source = AuditSourceResolver.Resolve(
                raw.ActorUserId, raw.ActorUserId is { } actor && Members.Contains(actor), raw.Action);

            return source switch
            {
                AuditSourceResolver.Customer => new CustomerActivityOrigin("customer_portal", null),
                AuditSourceResolver.System => new CustomerActivityOrigin("system", null),
                _ => new CustomerActivityOrigin(
                    "staff", raw.ActorUserId is { } staff ? Actors.GetValueOrDefault(staff) : null),
            };
        }
    }
}
