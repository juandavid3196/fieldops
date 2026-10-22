using System.Net;
using FieldOps.Application.Features.Access;
using FieldOps.Application.Features.Customers;
using FieldOps.Domain.Customers;
using FieldOps.Domain.Invoices;
using FieldOps.Domain.Notifications;
using FieldOps.Domain.WorkOrders;
using FieldOps.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FieldOps.Infrastructure.Persistence;

internal sealed class CustomerDetailStore(FieldOpsDbContext dbContext) : ICustomerDetailStore
{
    private const int NotesPageSize = 10;

    private const int RecentWorkLimit = 5;

    public Task<bool> IsCustomerVisibleAsync(
        Guid organizationId, BranchScope scope, Guid customerId, CancellationToken cancellationToken) =>
        Visible(organizationId, scope).AnyAsync(customer => customer.Id == customerId, cancellationToken);

    public async Task<CustomerOverviewData?> GetOverviewAsync(
        Guid organizationId, BranchScope scope, Guid customerId, CustomerRecordAccess access, CancellationToken cancellationToken)
    {
        var customer = await Visible(organizationId, scope)
            .Where(candidate => candidate.Id == customerId)
            .Select(candidate => new { candidate.DisplayName, candidate.CreatedAt })
            .SingleOrDefaultAsync(cancellationToken);

        if (customer is null)
        {
            return null;
        }

        // BR-14: every value derives from the records visible to the caller; a module the role cannot read is omitted (null), not zero.
        decimal? balance = null;
        decimal? lifetimeValue = null;
        CustomerLastInvoice? lastInvoice = null;

        if (access.Invoices)
        {
            var invoices = dbContext.VisibleInvoices(organizationId, scope, customerId);

            balance = await invoices
                .Where(invoice => invoice.Status == InvoiceStatus.Sent
                    || invoice.Status == InvoiceStatus.PartiallyPaid
                    || invoice.Status == InvoiceStatus.Overdue)
                .SumAsync(invoice => invoice.BalanceDue, cancellationToken);

            lifetimeValue = await invoices.SumAsync(invoice => invoice.AmountPaid, cancellationToken);

            var last = await invoices
                .Where(invoice => invoice.Status != InvoiceStatus.Draft)
                .OrderBy(invoice => invoice.IssueDate == null ? 1 : 0)
                .ThenByDescending(invoice => invoice.IssueDate)
                .ThenByDescending(invoice => invoice.CreatedAt)
                .ThenBy(invoice => invoice.Id)
                .Select(invoice => new { invoice.InvoiceNumber, invoice.Status, invoice.IssueDate })
                .FirstOrDefaultAsync(cancellationToken);

            if (last is not null)
            {
                var prefix = await dbContext.Organizations.AsNoTracking()
                    .Where(organization => organization.Id == organizationId)
                    .Select(organization => organization.InvoicePrefix)
                    .SingleAsync(cancellationToken);

                lastInvoice = new CustomerLastInvoice(
                    $"{prefix}-{last.InvoiceNumber}", CustomerEnumText.Snake(last.Status), last.IssueDate);
            }
        }

        int? totalJobs = null;
        DateTimeOffset? lastServiceAt = null;

        if (access.Jobs)
        {
            // Unchanged rule: cancelled jobs are not counted.
            totalJobs = await dbContext.VisibleJobs(organizationId, scope, customerId)
                .CountAsync(order => order.Status != WorkOrderStatus.Cancelled, cancellationToken);

            lastServiceAt = await dbContext.VisibleVisits(organizationId, scope, customerId)
                .Where(visit => visit.Status == VisitStatus.Completed || visit.Status == VisitStatus.Approved)
                .Select(visit => visit.ActualCompletedAt ?? visit.ScheduledEnd)
                .MaxAsync(cancellationToken);
        }

        return new CustomerOverviewData(
            customer.DisplayName, balance, totalJobs, lifetimeValue, customer.CreatedAt, lastServiceAt, lastInvoice);
    }

    public async Task<IReadOnlyList<CustomerPropertyView>?> ListPropertiesAsync(
        Guid organizationId,
        BranchScope scope,
        Guid customerId,
        CustomerRecordAccess access,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!await IsCustomerVisibleAsync(organizationId, scope, customerId, cancellationToken))
        {
            return null;
        }

        var properties = await dbContext.Properties.AsNoTracking()
            .Where(property => property.OrganizationId == organizationId && property.CustomerId == customerId)
            .OrderByDescending(property => property.IsActive)
            .ThenByDescending(property => property.IsPrimary)
            .ThenBy(property => property.Name.ToLower())
            .ThenBy(property => property.Id)
            .ToListAsync(cancellationToken);

        return await ToViewsAsync(organizationId, scope, customerId, properties, access, now, cancellationToken);
    }

    public async Task<CustomerPropertyView?> GetPropertyAsync(
        Guid organizationId,
        BranchScope scope,
        Guid customerId,
        Guid propertyId,
        CustomerRecordAccess access,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var property = await LoadPropertyAsync(organizationId, scope, customerId, propertyId, track: false, cancellationToken);

        return property is null ? null : await ToViewAsync(property, scope, access, now, cancellationToken);
    }

    public async Task<CustomerPropertyState?> GetPropertyStateAsync(
        Guid organizationId, BranchScope scope, Guid customerId, Guid propertyId, CancellationToken cancellationToken)
    {
        var property = await LoadPropertyAsync(organizationId, scope, customerId, propertyId, track: false, cancellationToken);

        return property is null ? null : new CustomerPropertyState(property.BranchId, property.IsActive);
    }

    public async Task<CustomerPropertyView?> CreatePropertyAsync(
        Guid organizationId,
        BranchScope scope,
        Guid customerId,
        CustomerPropertyValues values,
        string countryCode,
        Guid actorUserId,
        IPAddress? clientIp,
        CustomerRecordAccess access,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!await IsCustomerVisibleAsync(organizationId, scope, customerId, cancellationToken))
        {
            return null;
        }

        for (var attempt = 0; ; attempt++)
        {
            var hasPrimary = await dbContext.Properties.AsNoTracking()
                .AnyAsync(
                    property => property.OrganizationId == organizationId
                        && property.CustomerId == customerId
                        && property.IsPrimary
                        && property.IsActive,
                    cancellationToken);

            var created = Property.Create(
                organizationId,
                customerId,
                values.Name,
                values.AddressLine1,
                values.City,
                countryCode,
                values.BranchId,
                values.StateRegion,
                values.PostalCode,
                values.ServiceInstructions,
                isPrimary: !hasPrimary,
                addressLine2: values.AddressLine2);

            dbContext.Properties.Add(created);
            dbContext.AuditLogs.Add(PropertyAudit.Created(created, actorUserId, clientIp));

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);

                return await ToViewAsync(created, scope, access, now, cancellationToken);
            }
            catch (DbUpdateException ex) when (attempt == 0 && IsPrimaryViolation(ex))
            {
                // A concurrent request made another property primary first: retry once as a non-primary one.
                dbContext.ChangeTracker.Clear();
            }
        }
    }

    public async Task<CustomerPropertyChange> UpdatePropertyAsync(
        Guid organizationId,
        BranchScope scope,
        Guid customerId,
        Guid propertyId,
        CustomerPropertyValues values,
        Guid actorUserId,
        IPAddress? clientIp,
        CustomerRecordAccess access,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var property = await LoadPropertyAsync(organizationId, scope, customerId, propertyId, track: true, cancellationToken);

        if (property is null)
        {
            return new CustomerPropertyChange(CustomerPropertyOutcome.NotFound);
        }

        if (!property.IsActive)
        {
            return new CustomerPropertyChange(CustomerPropertyOutcome.ArchivedCannotBeEdited);
        }

        var changed = property.Edit(
            values.Name,
            values.AddressLine1,
            values.AddressLine2,
            values.City,
            values.StateRegion,
            values.PostalCode,
            values.BranchId,
            values.ServiceInstructions,
            now);

        if (changed.Count > 0)
        {
            dbContext.AuditLogs.Add(PropertyAudit.Updated(property, changed, actorUserId, clientIp));

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return new CustomerPropertyChange(CustomerPropertyOutcome.Changed, await ToViewAsync(property, scope, access, now, cancellationToken));
    }

    public async Task<CustomerPropertyOutcome> ChangePropertyStateAsync(
        Guid organizationId,
        BranchScope scope,
        Guid customerId,
        Guid propertyId,
        CustomerPropertyAction action,
        Guid actorUserId,
        IPAddress? clientIp,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var property = await LoadPropertyAsync(organizationId, scope, customerId, propertyId, track: true, cancellationToken);

        if (property is null)
        {
            return CustomerPropertyOutcome.NotFound;
        }

        return action switch
        {
            CustomerPropertyAction.SetPrimary => await SetPrimaryAsync(property, actorUserId, clientIp, now, cancellationToken),
            CustomerPropertyAction.Archive => await ArchiveAsync(property, actorUserId, clientIp, now, cancellationToken),
            _ => await ReactivateAsync(property, actorUserId, clientIp, now, cancellationToken),
        };
    }

    public async Task<IReadOnlyList<CustomerRecentWorkItem>?> ListRecentWorkAsync(
        Guid organizationId, BranchScope scope, Guid customerId, CustomerRecordAccess access, CancellationToken cancellationToken)
    {
        if (!await IsCustomerVisibleAsync(organizationId, scope, customerId, cancellationToken))
        {
            return null;
        }

        var prefixes = await dbContext.Organizations.AsNoTracking()
            .Where(organization => organization.Id == organizationId)
            .Select(organization => new { organization.QuotePrefix, organization.WorkOrderPrefix })
            .SingleAsync(cancellationToken);

        var items = new List<CustomerRecentWorkItem>();

        // BR-14: rows come only from visible records, and a row kind the role cannot read is dropped.
        if (access.Requests)
        {
            var requests = await dbContext.VisibleRequests(organizationId, scope, customerId)
                .OrderByDescending(request => request.CreatedAt)
                .ThenBy(request => request.Id)
                .Take(RecentWorkLimit)
                .Select(request => new
                {
                    request.Id,
                    request.RequestNumber,
                    request.Description,
                    request.Status,
                    request.CreatedAt,
                })
                .ToListAsync(cancellationToken);

            var quotes = await (
                from quote in dbContext.VisibleQuotes(organizationId, scope, customerId)
                join version in dbContext.QuoteVersions.AsNoTracking()
                    on new { QuoteId = quote.Id, VersionNo = quote.CurrentVersionNo }
                    equals new { version.QuoteId, version.VersionNo } into versions
                from version in versions.DefaultIfEmpty()
                join request in dbContext.ServiceRequests.AsNoTracking() on quote.RequestId equals request.Id
                orderby (version.SentAt ?? quote.CreatedAt) descending, quote.Id
                select new
                {
                    quote.Id,
                    quote.QuoteNumber,
                    quote.Status,
                    Date = version.SentAt ?? quote.CreatedAt,
                    version.Scope,
                    Total = (decimal?)version.Total,
                    request.Description,
                })
                .Take(RecentWorkLimit)
                .ToListAsync(cancellationToken);

            items.AddRange(requests.Select(request => new CustomerRecentWorkItem(
                "request",
                request.Id,
                $"REQ-{request.RequestNumber}",
                CustomerEnumText.FirstLine(request.Description),
                CustomerEnumText.Snake(request.Status),
                request.CreatedAt,
                null,
                null)));

            items.AddRange(quotes.Select(quote => new CustomerRecentWorkItem(
                "quote",
                quote.Id,
                $"{prefixes.QuotePrefix}-{quote.QuoteNumber}",
                CustomerEnumText.FirstLine(quote.Scope ?? quote.Description),
                CustomerEnumText.Snake(quote.Status),
                quote.Date,
                null,
                quote.Total)));
        }

        if (access.Jobs)
        {
            var jobs = await (
                from order in dbContext.VisibleJobs(organizationId, scope, customerId)
                join source in dbContext.QuoteVersions.AsNoTracking() on order.QuoteVersionId equals source.Id into sources
                from source in sources.DefaultIfEmpty()
                let doneAt = (
                    from visit in dbContext.Visits
                    where visit.WorkOrderId == order.Id
                        && (visit.Status == VisitStatus.Completed || visit.Status == VisitStatus.Approved)
                    select visit.ActualCompletedAt ?? visit.ScheduledEnd).Max()
                orderby (doneAt ?? order.CreatedAt) descending, order.Id
                select new
                {
                    order.Id,
                    order.WorkOrderNumber,
                    order.Status,
                    order.ScopeSnapshot,
                    Date = doneAt ?? order.CreatedAt,
                    Total = (decimal?)source.Total,
                })
                .Take(RecentWorkLimit)
                .ToListAsync(cancellationToken);

            foreach (var job in jobs)
            {
                items.Add(new CustomerRecentWorkItem(
                    "job",
                    job.Id,
                    $"{prefixes.WorkOrderPrefix}-{job.WorkOrderNumber}",
                    CustomerEnumText.FirstLine(job.ScopeSnapshot),
                    CustomerEnumText.Snake(job.Status),
                    job.Date,
                    await TechnicianOfLatestVisitAsync(organizationId, job.Id, cancellationToken),
                    job.Total));
            }
        }

        // Ids compare as lower-case hex text, the same order as PostgreSQL's uuid comparison.
        return [.. items
            .OrderByDescending(item => item.Date)
            .ThenBy(item => item.Id.ToString("D"), StringComparer.Ordinal)
            .Take(RecentWorkLimit)];
    }

    public async Task<IReadOnlyList<CustomerUpcomingAppointment>?> ListUpcomingAppointmentsAsync(
        Guid organizationId,
        BranchScope scope,
        Guid customerId,
        CustomerRecordAccess access,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!await IsCustomerVisibleAsync(organizationId, scope, customerId, cancellationToken))
        {
            return null;
        }

        // BR-14: without Jobs read the card is empty.
        if (!access.Jobs)
        {
            return [];
        }

        var prefix = await dbContext.Organizations.AsNoTracking()
            .Where(organization => organization.Id == organizationId)
            .Select(organization => organization.WorkOrderPrefix)
            .SingleAsync(cancellationToken);

        var rows = await (
            from visit in dbContext.VisibleVisits(organizationId, scope, customerId)
            join order in dbContext.WorkOrders.AsNoTracking() on visit.WorkOrderId equals order.Id
            join property in dbContext.Properties.AsNoTracking() on order.PropertyId equals property.Id
            where visit.ScheduledStart != null
                && visit.ScheduledStart >= now
                && (visit.Status == VisitStatus.Scheduled
                    || visit.Status == VisitStatus.Assigned
                    || visit.Status == VisitStatus.OnTheWay)
            orderby visit.ScheduledStart, visit.Id
            select new
            {
                VisitId = visit.Id,
                visit.ScheduledStart,
                visit.ScheduledEnd,
                order.WorkOrderNumber,
                order.ScopeSnapshot,
                PropertyName = property.Name,
            })
            .Take(RecentWorkLimit)
            .ToListAsync(cancellationToken);

        var items = new List<CustomerUpcomingAppointment>(rows.Count);

        foreach (var row in rows)
        {
            items.Add(new CustomerUpcomingAppointment(
                row.VisitId,
                row.ScheduledStart!.Value,
                row.ScheduledEnd,
                $"{prefix}-{row.WorkOrderNumber}",
                CustomerEnumText.FirstLine(row.ScopeSnapshot),
                row.PropertyName,
                await PrimaryTechnicianAsync(organizationId, row.VisitId, cancellationToken)));
        }

        return items;
    }

    public async Task<CustomerNotesPage?> ListNotesAsync(
        Guid organizationId,
        BranchScope scope,
        Guid customerId,
        int page,
        string timezone,
        CancellationToken cancellationToken)
    {
        if (!await IsCustomerVisibleAsync(organizationId, scope, customerId, cancellationToken))
        {
            return null;
        }

        var notes = dbContext.CustomerNotes.AsNoTracking()
            .Where(note => note.OrganizationId == organizationId && note.CustomerId == customerId);

        var total = await notes.CountAsync(cancellationToken);

        var items = await (
            from note in notes
            join author in dbContext.Users.AsNoTracking() on note.AuthorUserId equals author.Id
            orderby note.CreatedAt descending, note.Id
            select new CustomerNoteView(note.Id, note.Note, author.FirstName + " " + author.LastName, note.CreatedAt))
            .Skip((page - 1) * NotesPageSize)
            .Take(NotesPageSize)
            .ToListAsync(cancellationToken);

        return new CustomerNotesPage(items, total, page, NotesPageSize, timezone);
    }

    public async Task<CustomerNoteView?> AddNoteAsync(
        Guid organizationId,
        BranchScope scope,
        Guid customerId,
        string note,
        Guid actorUserId,
        IPAddress? clientIp,
        CancellationToken cancellationToken)
    {
        var customerBranchId = await Visible(organizationId, scope)
            .Where(customer => customer.Id == customerId)
            .Select(customer => (Guid?)customer.BranchId)
            .SingleOrDefaultAsync(cancellationToken);

        if (customerBranchId is null)
        {
            return null;
        }

        var entry = CustomerNote.Create(organizationId, customerId, actorUserId, note);

        dbContext.CustomerNotes.Add(entry);
        dbContext.AuditLogs.Add(PropertyAudit.NoteCreated(entry, customerBranchId.Value, actorUserId, clientIp));

        await dbContext.SaveChangesAsync(cancellationToken);

        var author = await dbContext.Users.AsNoTracking()
            .Where(user => user.Id == actorUserId)
            .Select(user => user.FirstName + " " + user.LastName)
            .SingleAsync(cancellationToken);

        return new CustomerNoteView(entry.Id, entry.Note, author, entry.CreatedAt);
    }

    private static bool IsPrimaryViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres
        && postgres.ConstraintName == PropertyConfiguration.PrimaryIndexName;

    private static CustomerPropertyOutcome ToOutcome(PropertyTransition transition) =>
        transition switch
        {
            PropertyTransition.Applied => CustomerPropertyOutcome.Changed,
            PropertyTransition.AlreadyPrimary => CustomerPropertyOutcome.AlreadyPrimary,
            PropertyTransition.NotActive => CustomerPropertyOutcome.ArchivedCannotBePrimary,
            PropertyTransition.PrimaryCannotBeArchived => CustomerPropertyOutcome.PrimaryCannotBeArchived,
            PropertyTransition.AlreadyArchived => CustomerPropertyOutcome.AlreadyArchived,
            _ => CustomerPropertyOutcome.AlreadyActive,
        };

    // Every query filters the organization first and then the branch scope.
    private IQueryable<Customer> Visible(Guid organizationId, BranchScope scope)
    {
        var query = dbContext.Customers.AsNoTracking().Where(customer => customer.OrganizationId == organizationId);

        if (!scope.All)
        {
            var ids = scope.BranchIds.ToArray();
            query = query.Where(customer => ids.Contains(customer.BranchId));
        }

        return query;
    }

    private async Task<Property?> LoadPropertyAsync(
        Guid organizationId,
        BranchScope scope,
        Guid customerId,
        Guid propertyId,
        bool track,
        CancellationToken cancellationToken)
    {
        if (!await IsCustomerVisibleAsync(organizationId, scope, customerId, cancellationToken))
        {
            return null;
        }

        var query = dbContext.Properties.Where(property =>
            property.OrganizationId == organizationId
            && property.CustomerId == customerId
            && property.Id == propertyId);

        return await (track ? query : query.AsNoTracking()).SingleOrDefaultAsync(cancellationToken);
    }

    // Serializes the property state changes of one customer; a row lock on the active properties (the primary
    // is always one of them) makes a concurrent set-primary or archive wait and then see the new state.
    private async Task LockActivePropertiesAsync(Guid organizationId, Guid customerId, CancellationToken cancellationToken) =>
        _ = await dbContext.Database
            .SqlQuery<Guid>(
                $"""
                SELECT id FROM properties
                WHERE organization_id = {organizationId} AND customer_id = {customerId} AND is_active
                ORDER BY id
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);

    private async Task<CustomerPropertyOutcome> SetPrimaryAsync(
        Property property,
        Guid actorUserId,
        IPAddress? clientIp,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var organizationId = property.OrganizationId;
        var customerId = property.CustomerId;
        var targetId = property.Id;

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        await LockActivePropertiesAsync(organizationId, customerId, cancellationToken);
        await dbContext.Entry(property).ReloadAsync(cancellationToken);

        var transition = property.SetPrimary(now);

        if (transition != PropertyTransition.Applied)
        {
            await transaction.RollbackAsync(cancellationToken);

            return ToOutcome(transition);
        }

        var previousPrimaryId = await dbContext.Properties.AsNoTracking()
            .Where(other => other.OrganizationId == organizationId
                && other.CustomerId == customerId
                && other.IsPrimary
                && other.Id != targetId)
            .Select(other => (Guid?)other.Id)
            .SingleOrDefaultAsync(cancellationToken);

        // The previous primary is cleared first so the unique index never sees two primaries.
        await dbContext.Properties
            .Where(other => other.OrganizationId == organizationId
                && other.CustomerId == customerId
                && other.IsPrimary
                && other.Id != targetId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(other => other.IsPrimary, false)
                    .SetProperty(other => other.UpdatedAt, now),
                cancellationToken);

        var audit = PropertyAudit.PrimaryChanged(property, previousPrimaryId, actorUserId, clientIp);
        dbContext.AuditLogs.Add(audit);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return CustomerPropertyOutcome.Changed;
        }
        catch (DbUpdateException ex) when (IsPrimaryViolation(ex))
        {
            dbContext.AuditLogs.Remove(audit);
            await transaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();

            return CustomerPropertyOutcome.PrimaryChangedConcurrently;
        }
    }

    private async Task<CustomerPropertyOutcome> ArchiveAsync(
        Property property,
        Guid actorUserId,
        IPAddress? clientIp,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        await LockActivePropertiesAsync(property.OrganizationId, property.CustomerId, cancellationToken);
        await dbContext.Entry(property).ReloadAsync(cancellationToken);

        var transition = property.Archive(now);

        if (transition != PropertyTransition.Applied)
        {
            await transaction.RollbackAsync(cancellationToken);

            return ToOutcome(transition);
        }

        dbContext.AuditLogs.Add(PropertyAudit.StateChanged(property, isActive: false, actorUserId, clientIp));

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return CustomerPropertyOutcome.Changed;
    }

    private async Task<CustomerPropertyOutcome> ReactivateAsync(
        Property property,
        Guid actorUserId,
        IPAddress? clientIp,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var transition = property.Reactivate(now);

        if (transition != PropertyTransition.Applied)
        {
            return ToOutcome(transition);
        }

        dbContext.AuditLogs.Add(PropertyAudit.StateChanged(property, isActive: true, actorUserId, clientIp));

        await dbContext.SaveChangesAsync(cancellationToken);

        return CustomerPropertyOutcome.Changed;
    }

    private async Task<CustomerPropertyView> ToViewAsync(
        Property property, BranchScope scope, CustomerRecordAccess access, DateTimeOffset now, CancellationToken cancellationToken) =>
        (await ToViewsAsync(property.OrganizationId, scope, property.CustomerId, [property], access, now, cancellationToken))[0];

    private async Task<IReadOnlyList<CustomerPropertyView>> ToViewsAsync(
        Guid organizationId,
        BranchScope scope,
        Guid customerId,
        IReadOnlyList<Property> properties,
        CustomerRecordAccess access,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var branchIds = properties.Where(property => property.BranchId != null).Select(property => property.BranchId!.Value).Distinct().ToArray();

        var branches = branchIds.Length == 0
            ? []
            : await dbContext.Branches.AsNoTracking()
                .Where(branch => branch.OrganizationId == organizationId && branchIds.Contains(branch.Id))
                .ToDictionaryAsync(branch => branch.Id, branch => branch.Name, cancellationToken);

        var views = new List<CustomerPropertyView>(properties.Count);
        var visibleJobs = dbContext.VisibleJobs(organizationId, scope, customerId);

        foreach (var property in properties)
        {
            // BR-14: the service history and the next appointment come from visible jobs and are omitted without Jobs read.
            var propertyId = property.Id;
            var last = access.Jobs
                ? await (
                    from visit in dbContext.Visits.AsNoTracking()
                    join order in visibleJobs on visit.WorkOrderId equals order.Id
                    where order.PropertyId == propertyId
                        && (visit.Status == VisitStatus.Completed || visit.Status == VisitStatus.Approved)
                        && (visit.ActualCompletedAt ?? visit.ScheduledEnd) != null
                    orderby (visit.ActualCompletedAt ?? visit.ScheduledEnd) descending, visit.Id
                    select new { At = visit.ActualCompletedAt ?? visit.ScheduledEnd, order.ScopeSnapshot })
                    .FirstOrDefaultAsync(cancellationToken)
                : null;

            var next = access.Jobs
                ? await (
                    from visit in dbContext.Visits.AsNoTracking()
                    join order in visibleJobs on visit.WorkOrderId equals order.Id
                    where order.PropertyId == propertyId
                        && visit.ScheduledStart != null
                        && visit.ScheduledStart >= now
                        && (visit.Status == VisitStatus.Scheduled
                            || visit.Status == VisitStatus.Assigned
                            || visit.Status == VisitStatus.OnTheWay)
                    orderby visit.ScheduledStart, visit.Id
                    select visit.ScheduledStart)
                    .FirstOrDefaultAsync(cancellationToken)
                : null;

            views.Add(new CustomerPropertyView(
                property.Id,
                property.Name,
                property.IsPrimary,
                property.IsActive,
                property.AddressLine1,
                property.AddressLine2,
                property.City,
                property.StateRegion,
                property.PostalCode,
                property.BranchId is { } branchId && branches.TryGetValue(branchId, out var branchName)
                    ? new CustomerPropertyBranch(branchId, branchName)
                    : null,
                property.ServiceNotes,
                last is null ? null : new CustomerLastService(last.At!.Value, CustomerEnumText.FirstLine(last.ScopeSnapshot)),
                next is { } startsAt ? new CustomerNextService(startsAt) : null));
        }

        return views;
    }

    // The name of the active primary assignment of the latest visit of a job (BR-14).
    private async Task<string?> TechnicianOfLatestVisitAsync(
        Guid organizationId, Guid workOrderId, CancellationToken cancellationToken)
    {
        var visitId = await dbContext.Visits.AsNoTracking()
            .Where(visit => visit.OrganizationId == organizationId && visit.WorkOrderId == workOrderId)
            .OrderBy(visit => visit.ScheduledStart == null ? 1 : 0)
            .ThenByDescending(visit => visit.ScheduledStart)
            .ThenByDescending(visit => visit.VisitNumber)
            .Select(visit => (Guid?)visit.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return visitId is null ? null : await PrimaryTechnicianAsync(organizationId, visitId.Value, cancellationToken);
    }

    private async Task<string?> PrimaryTechnicianAsync(Guid organizationId, Guid visitId, CancellationToken cancellationToken) =>
        await (
            from assignment in dbContext.VisitAssignments.AsNoTracking()
            join technician in dbContext.TechnicianProfiles.AsNoTracking() on assignment.TechnicianId equals technician.Id
            where assignment.VisitId == visitId
                && assignment.IsPrimary
                && assignment.UnassignedAt == null
                && technician.OrganizationId == organizationId
            orderby assignment.AssignedAt, assignment.Id
            select technician.FirstName + " " + technician.LastName)
            .FirstOrDefaultAsync(cancellationToken);
}
