using FieldOps.Application.Features.InvoicePayments;
using FieldOps.Application.Features.PortalAccess;
using FieldOps.Application.Features.PortalDashboard;
using FieldOps.Application.Features.QuoteLinks;
using FieldOps.Application.Features.Quotes;
using FieldOps.Application.Features.ServiceRequests;
using FieldOps.Domain.Invoices;
using FieldOps.Domain.Quotes;
using FieldOps.Domain.WorkOrders;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Persistence.Portal;

/// <summary>
/// The dashboard, the updates feed and the recent activity of the portal (customer portal BR-17 … BR-26). Every query starts
/// from the customer of the session and is top-N or per-customer bounded (one quote, one invoice, three requests, five
/// activity rows, updates from six sources of at most 50 events each); nothing scans the organization. The queries run
/// sequentially on the scoped context and are no-tracking.
/// </summary>
internal sealed class PortalDashboardStore(FieldOpsDbContext dbContext) : IPortalDashboardStore
{
    private const int UpdateWindowDays = 90;

    private const int UpdateSourceTake = 50;

    private const int DashboardUpdates = 3;

    private const int DashboardActivity = 5;

    private static readonly InvoiceStatus[] DueStatuses = [InvoiceStatus.Sent, InvoiceStatus.PartiallyPaid, InvoiceStatus.Overdue];

    public async Task<PortalDashboard?> GetDashboardAsync(
        PortalScope scope, Guid? propertyId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var org = await PortalOrg.LoadAsync(dbContext, scope, cancellationToken);
        var properties = await ListPropertySummariesAsync(scope, org, cancellationToken);

        if (propertyId is { } selected && properties.All(property => property.Id != selected))
        {
            return null;
        }

        var today = org.Today(now);
        var phone = await BranchPhoneAsync(scope, org, cancellationToken);
        var canMessage = await CanReceiveMessagesAsync(scope, org, cancellationToken);

        var actionQuote = await ActionQuoteAsync(scope, org, propertyId, today, cancellationToken);
        var paymentDue = await PaymentDueAsync(scope, org, propertyId, today, cancellationToken);
        var appointment = await UpcomingAsync(scope, org, propertyId, now, cancellationToken);

        var activeRequests = await PortalReaders.RequestRowsAsync(
            dbContext,
            scope,
            org,
            ActiveRequests(scope, propertyId).OrderByDescending(request => request.CreatedAt).ThenBy(request => request.Id).Take(3),
            cancellationToken);
        activeRequests = activeRequests.OrderByDescending(row => row.SubmittedOn).ToList();

        var updates = await ListUpdatesAsync(scope, now, cancellationToken);
        var activity = await ActivityPageAsync(scope, org, propertyId, 1, DashboardActivity, cancellationToken);

        return new PortalDashboard(
            new PortalOrganization(org.Name, phone, canMessage),
            properties,
            propertyId,
            actionQuote,
            paymentDue,
            appointment,
            activeRequests,
            new PortalUpdates(updates.Items.Take(DashboardUpdates).ToList(), updates.UnreadCount),
            activity.Items);
    }

    public async Task<PortalUpdates> ListUpdatesAsync(PortalScope scope, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var org = await PortalOrg.LoadAsync(dbContext, scope, cancellationToken);
        var cutoff = now.AddDays(-UpdateWindowDays);
        var events = new List<(string Type, string Title, string Subtitle, DateTimeOffset At, string Kind, Guid Id)>();

        // (a) quote sent: the current version of a non-draft, non-cancelled quote.
        var quotes = await (
            from quote in dbContext.Quotes.AsNoTracking().ForPortal(dbContext, scope)
            where quote.Status != QuoteStatus.Draft && quote.Status != QuoteStatus.Cancelled
            join version in dbContext.QuoteVersions.AsNoTracking()
                on new { quote.OrganizationId, QuoteId = quote.Id, VersionNo = quote.CurrentVersionNo }
                equals new { version.OrganizationId, version.QuoteId, version.VersionNo }
            where version.SentAt != null && version.SentAt >= cutoff
            orderby version.SentAt descending
            select new { quote.Id, quote.QuoteNumber, version.Scope, SentAt = version.SentAt!.Value })
            .Take(UpdateSourceTake)
            .ToListAsync(cancellationToken);

        events.AddRange(quotes.Select(row => (
            "quote_sent",
            $"Quote {RequestCardRules.DisplayNumber(org.QuotePrefix, row.QuoteNumber)} ready",
            row.Scope,
            row.SentAt,
            "quote",
            row.Id)));

        // (b) technician assigned: the active primary assignment.
        var assigned = await (
            from visit in dbContext.Visits.AsNoTracking().ForPortal(dbContext, scope)
            join assignment in dbContext.VisitAssignments.AsNoTracking() on visit.Id equals assignment.VisitId
            where assignment.IsPrimary && assignment.UnassignedAt == null && assignment.AssignedAt >= cutoff
            join profile in dbContext.TechnicianProfiles.AsNoTracking() on assignment.TechnicianId equals profile.Id
            join order in dbContext.WorkOrders.AsNoTracking()
                on new { visit.OrganizationId, Id = visit.WorkOrderId } equals new { order.OrganizationId, order.Id }
            orderby assignment.AssignedAt descending
            select new { VisitId = visit.Id, assignment.AssignedAt, profile.FirstName, profile.LastName, order.WorkOrderNumber, order.Title })
            .Take(UpdateSourceTake)
            .ToListAsync(cancellationToken);

        events.AddRange(assigned.Select(row => (
            "technician_assigned",
            $"{PortalReaders.FullName(row.FirstName, row.LastName)} assigned to {RequestCardRules.DisplayNumber(org.WorkOrderPrefix, row.WorkOrderNumber)}",
            row.Title,
            row.AssignedAt,
            "appointment",
            row.VisitId)));

        // (c) on the way and (d) completed: the visit status history.
        var history = await (
            from visit in dbContext.Visits.AsNoTracking().ForPortal(dbContext, scope)
            join change in dbContext.VisitStatusHistories.AsNoTracking() on visit.Id equals change.VisitId
            where (change.ToStatus == VisitStatus.OnTheWay || change.ToStatus == VisitStatus.Completed) && change.ChangedAt >= cutoff
            join order in dbContext.WorkOrders.AsNoTracking()
                on new { visit.OrganizationId, Id = visit.WorkOrderId } equals new { order.OrganizationId, order.Id }
            orderby change.ChangedAt descending
            select new
            {
                VisitId = visit.Id,
                change.ToStatus,
                change.ChangedAt,
                order.WorkOrderNumber,
                order.Title,
                TechnicianFirstName = dbContext.VisitAssignments
                    .Where(assignment => assignment.VisitId == visit.Id && assignment.IsPrimary && assignment.UnassignedAt == null)
                    .Join(dbContext.TechnicianProfiles, assignment => assignment.TechnicianId, profile => profile.Id, (assignment, profile) => profile.FirstName)
                    .FirstOrDefault(),
            })
            .Take(UpdateSourceTake * 2)
            .ToListAsync(cancellationToken);

        events.AddRange(history
            .Where(row => row.ToStatus == VisitStatus.OnTheWay)
            .Take(UpdateSourceTake)
            .Select(row => (
                "on_the_way",
                $"{(string.IsNullOrWhiteSpace(row.TechnicianFirstName) ? "Your technician" : row.TechnicianFirstName.Trim())} is on the way",
                row.Title,
                row.ChangedAt,
                "appointment",
                row.VisitId)));
        events.AddRange(history
            .Where(row => row.ToStatus == VisitStatus.Completed)
            .Take(UpdateSourceTake)
            .Select(row => (
                "visit_completed",
                $"{row.Title} completed",
                RequestCardRules.DisplayNumber(org.WorkOrderPrefix, row.WorkOrderNumber),
                row.ChangedAt,
                "appointment",
                row.VisitId)));

        // (e) invoice sent: non-void invoices.
        var invoices = await (
            from invoice in dbContext.Invoices.AsNoTracking().ForPortal(scope)
            where invoice.Status != InvoiceStatus.Void && invoice.Status != InvoiceStatus.Draft && invoice.SentAt != null && invoice.SentAt >= cutoff
            join order in dbContext.WorkOrders.AsNoTracking()
                on new { invoice.OrganizationId, Id = invoice.WorkOrderId } equals new { order.OrganizationId, order.Id }
            orderby invoice.SentAt descending
            select new { invoice.Id, invoice.InvoiceNumber, SentAt = invoice.SentAt!.Value, order.Title })
            .Take(UpdateSourceTake)
            .ToListAsync(cancellationToken);

        events.AddRange(invoices.Select(row => (
            "invoice_sent",
            $"Invoice {RequestCardRules.DisplayNumber(org.InvoicePrefix, row.InvoiceNumber)} sent",
            row.Title,
            row.SentAt,
            "invoice",
            row.Id)));

        // (f) receipt available: payments with a receipt number that are not refunded.
        var receipts = await (
            from payment in dbContext.Payments.AsNoTracking().ForPortal(scope)
            where payment.ReceiptNumber != null && payment.Status != PaymentStatus.Refunded && payment.CreatedAt >= cutoff
            orderby payment.CreatedAt descending
            select new { payment.Id, payment.ReceiptNumber, payment.CreatedAt })
            .Take(UpdateSourceTake)
            .ToListAsync(cancellationToken);

        if (receipts.Count > 0)
        {
            var paymentIds = receipts.Select(receipt => receipt.Id).ToList();
            var allocated = await (
                from allocation in dbContext.PaymentAllocations.AsNoTracking()
                where paymentIds.Contains(allocation.PaymentId)
                join invoice in dbContext.Invoices.AsNoTracking().ForPortal(scope) on allocation.InvoiceId equals invoice.Id
                join order in dbContext.WorkOrders.AsNoTracking()
                    on new { invoice.OrganizationId, Id = invoice.WorkOrderId } equals new { order.OrganizationId, order.Id }
                orderby allocation.CreatedAt, allocation.Id
                select new { allocation.PaymentId, InvoiceId = invoice.Id, order.Title })
                .ToListAsync(cancellationToken);

            foreach (var receipt in receipts)
            {
                var first = allocated.FirstOrDefault(candidate => candidate.PaymentId == receipt.Id);

                if (first is null)
                {
                    continue;
                }

                events.Add((
                    "receipt_available",
                    $"Receipt {receipt.ReceiptNumber} available",
                    first.Title,
                    receipt.CreatedAt,
                    "invoice",
                    first.InvoiceId));
            }
        }

        var seenAt = await dbContext.CustomerContacts.AsNoTracking().ForPortal(scope)
            .Where(contact => contact.Id == scope.ContactId)
            .Select(contact => contact.PortalUpdatesSeenAt)
            .SingleOrDefaultAsync(cancellationToken);

        var merged = events
            .OrderByDescending(evt => evt.At)
            .ThenBy(evt => evt.Id)
            .Select(evt => new PortalUpdate(
                evt.Type, evt.Title, evt.Subtitle, evt.At, seenAt is null || evt.At > seenAt, new PortalUpdateTarget(evt.Kind, evt.Id)))
            .ToList();

        return new PortalUpdates(merged.Take(50).ToList(), merged.Count(update => update.IsUnread));
    }

    public async Task MarkUpdatesSeenAsync(PortalScope scope, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var contactId = scope.ContactId;

        await dbContext.CustomerContacts.ForPortal(scope)
            .Where(contact => contact.Id == contactId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(contact => contact.PortalUpdatesSeenAt, now), cancellationToken);
    }

    public async Task<PortalPage<PortalActivityRow>?> ListActivityAsync(
        PortalScope scope, Guid? propertyId, int page, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (propertyId is { } selected && !await IsActivePropertyAsync(scope, selected, cancellationToken))
        {
            return null;
        }

        return await ActivityPageAsync(scope, await PortalOrg.LoadAsync(dbContext, scope, cancellationToken), propertyId, page, PortalPaging.PageSize, cancellationToken);
    }

    private Task<bool> IsActivePropertyAsync(PortalScope scope, Guid propertyId, CancellationToken cancellationToken) =>
        dbContext.Properties.AsNoTracking().ForPortal(scope).AnyAsync(property => property.Id == propertyId && property.IsActive, cancellationToken);

    private IQueryable<FieldOps.Domain.Requests.ServiceRequest> ActiveRequests(PortalScope scope, Guid? propertyId)
    {
        var active = PortalRequestRules.ActiveStatuses;

        return dbContext.ServiceRequests.AsNoTracking().ForPortal(scope)
            .Where(request => active.Contains(request.Status) && (propertyId == null || request.PropertyId == propertyId));
    }

    private async Task<List<PortalPropertySummary>> ListPropertySummariesAsync(PortalScope scope, PortalOrg org, CancellationToken cancellationToken)
    {
        var properties = await dbContext.Properties.AsNoTracking().ForPortal(scope)
            .Where(property => property.IsActive)
            .OrderByDescending(property => property.IsPrimary)
            .ThenBy(property => property.Name)
            .ThenBy(property => property.Id)
            .ToListAsync(cancellationToken);

        var lastService = await (
            from order in dbContext.WorkOrders.AsNoTracking().ForPortal(scope)
            join visit in dbContext.Visits.AsNoTracking()
                on new { order.OrganizationId, WorkOrderId = order.Id } equals new { visit.OrganizationId, visit.WorkOrderId }
            where visit.ActualCompletedAt != null
                && (visit.Status == VisitStatus.Completed || visit.Status == VisitStatus.NeedsCorrection || visit.Status == VisitStatus.Approved)
            group visit by order.PropertyId into byProperty
            select new { PropertyId = byProperty.Key, At = byProperty.Max(visit => visit.ActualCompletedAt) })
            .ToListAsync(cancellationToken);

        return properties
            .Select(property => new PortalPropertySummary(
                property.Id,
                property.Name,
                property.AddressLine1,
                property.AddressLine2,
                property.City,
                property.StateRegion,
                property.PostalCode,
                property.IsPrimary,
                lastService.FirstOrDefault(row => row.PropertyId == property.Id)?.At is { } at ? OrganizationTime.LocalDate(at, org.Zone) : null))
            .ToList();
    }

    // The phone of the customer's branch, else the organization (customer portal BR-35).
    private async Task<string?> BranchPhoneAsync(PortalScope scope, PortalOrg org, CancellationToken cancellationToken)
    {
        var organizationId = scope.OrganizationId;
        var customerId = scope.CustomerId;
        var branchPhone = await (
            from customer in dbContext.Customers.AsNoTracking()
            where customer.OrganizationId == organizationId && customer.Id == customerId
            join branch in dbContext.Branches.AsNoTracking()
                on new { customer.OrganizationId, Id = customer.BranchId } equals new { branch.OrganizationId, branch.Id }
            select branch.Phone)
            .SingleOrDefaultAsync(cancellationToken);

        var phone = !string.IsNullOrWhiteSpace(branchPhone) ? branchPhone : org.Phone;

        return string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
    }

    private async Task<bool> CanReceiveMessagesAsync(PortalScope scope, PortalOrg org, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(org.Email))
        {
            return true;
        }

        var organizationId = scope.OrganizationId;

        return await dbContext.Branches.AsNoTracking().AnyAsync(
            branch => branch.OrganizationId == organizationId && branch.IsMain && branch.Email != null && branch.Email != string.Empty,
            cancellationToken);
    }

    private async Task<PortalActionQuote?> ActionQuoteAsync(
        PortalScope scope, PortalOrg org, Guid? propertyId, DateOnly today, CancellationToken cancellationToken)
    {
        var rows = await (
            from quote in dbContext.Quotes.AsNoTracking().ForPortal(dbContext, scope)
            where (quote.Status == QuoteStatus.Sent || quote.Status == QuoteStatus.ClarificationRequested)
                && (propertyId == null || quote.PropertyId == propertyId)
            join version in dbContext.QuoteVersions.AsNoTracking()
                on new { quote.OrganizationId, QuoteId = quote.Id, VersionNo = quote.CurrentVersionNo }
                equals new { version.OrganizationId, version.QuoteId, version.VersionNo }
            orderby version.SentAt descending
            select new
            {
                quote.Id,
                quote.QuoteNumber,
                quote.Status,
                version.Scope,
                version.Total,
                version.Currency,
                version.ValidUntil,
                SentAt = version.SentAt ?? version.CreatedAt,
            })
            .Take(PortalReaders.Candidates)
            .ToListAsync(cancellationToken);

        var (pick, more) = PortalSelectionRules.PickActionQuote(
            rows.Select(row => new QuoteCandidate(row.Id, QuoteStatusCodes.Code(row.Status), row.ValidUntil, row.SentAt)),
            today);

        if (pick is null)
        {
            return null;
        }

        var chosen = rows.Single(row => row.Id == pick.Id);

        return new PortalActionQuote(
            chosen.Id,
            RequestCardRules.DisplayNumber(org.QuotePrefix, chosen.QuoteNumber),
            chosen.Scope,
            chosen.Total,
            chosen.Currency,
            chosen.ValidUntil,
            QuoteStatusCodes.Code(chosen.Status),
            more);
    }

    private async Task<PortalPaymentDue?> PaymentDueAsync(
        PortalScope scope, PortalOrg org, Guid? propertyId, DateOnly today, CancellationToken cancellationToken)
    {
        var rows = await (
            from invoice in dbContext.Invoices.AsNoTracking().ForPortal(scope)
            where DueStatuses.Contains(invoice.Status) && invoice.BalanceDue > 0m
            join order in dbContext.WorkOrders.AsNoTracking()
                on new { invoice.OrganizationId, Id = invoice.WorkOrderId } equals new { order.OrganizationId, order.Id }
            where propertyId == null || order.PropertyId == propertyId
            orderby invoice.DueDate, invoice.InvoiceNumber
            select new
            {
                invoice.Id,
                invoice.InvoiceNumber,
                invoice.Status,
                invoice.BalanceDue,
                invoice.DueDate,
                invoice.Currency,
                order.Title,
            })
            .Take(PortalReaders.Candidates)
            .ToListAsync(cancellationToken);

        var (pick, more) = PortalSelectionRules.PickPaymentDue(
            rows.Select(row => new InvoiceCandidate(row.Id, InvoiceHubRules.StoredStatus(row.Status), row.BalanceDue, row.DueDate, row.InvoiceNumber)));

        if (pick is null)
        {
            return null;
        }

        var chosen = rows.Single(row => row.Id == pick.Id);

        return new PortalPaymentDue(
            chosen.Id,
            RequestCardRules.DisplayNumber(org.InvoicePrefix, chosen.InvoiceNumber),
            chosen.Title,
            chosen.BalanceDue,
            chosen.Currency,
            chosen.DueDate,
            PortalSelectionRules.IsOverdue(pick.Status, chosen.DueDate, today),
            more);
    }

    private async Task<PortalAppointment?> UpcomingAsync(
        PortalScope scope, PortalOrg org, Guid? propertyId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var candidates = await (
            from visit in dbContext.Visits.AsNoTracking().ForPortal(dbContext, scope)
            where visit.ScheduledStart != null
                && (visit.Status == VisitStatus.Scheduled
                    || visit.Status == VisitStatus.Assigned
                    || visit.Status == VisitStatus.OnTheWay
                    || visit.Status == VisitStatus.InProgress
                    || visit.Status == VisitStatus.Paused)
            join order in dbContext.WorkOrders.AsNoTracking()
                on new { visit.OrganizationId, Id = visit.WorkOrderId } equals new { order.OrganizationId, order.Id }
            where order.Status != WorkOrderStatus.Cancelled && (propertyId == null || order.PropertyId == propertyId)
            orderby visit.ScheduledStart
            select new { visit.Id, visit.Status, visit.ScheduledStart })
            .Take(PortalReaders.Candidates)
            .ToListAsync(cancellationToken);

        var startOfToday = OrganizationTime.StartOfDayUtc(org.Today(now), org.Zone);
        var pick = PortalSelectionRules.PickUpcoming(
            candidates.Select(row => new VisitCandidate(row.Id, row.Status, row.ScheduledStart)), startOfToday);

        if (pick is null)
        {
            return null;
        }

        return (await PortalReaders.AppointmentsAsync(dbContext, scope, org, [pick.VisitId], now, cancellationToken)).FirstOrDefault();
    }

    private async Task<PortalPage<PortalActivityRow>> ActivityPageAsync(
        PortalScope scope, PortalOrg org, Guid? propertyId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var orders =
            from order in dbContext.WorkOrders.AsNoTracking().ForPortal(scope)
            where (order.Status == WorkOrderStatus.Completed || order.Status == WorkOrderStatus.ApprovedForBilling)
                && (propertyId == null || order.PropertyId == propertyId)
            let completedAt = dbContext.Visits
                .Where(visit => visit.OrganizationId == order.OrganizationId && visit.WorkOrderId == order.Id && visit.ActualCompletedAt != null)
                .Max(visit => visit.ActualCompletedAt)
            where completedAt != null
            select new { order.Id, order.OrganizationId, order.Title, order.WorkOrderNumber, CompletedAt = completedAt!.Value };

        var total = await orders.CountAsync(cancellationToken);
        var rows = await orders
            .OrderByDescending(row => row.CompletedAt)
            .ThenBy(row => row.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var orderIds = rows.Select(row => row.Id).ToList();
        var invoices = orderIds.Count == 0
            ? []
            : await dbContext.Invoices.AsNoTracking().ForPortal(scope)
                .Where(invoice => orderIds.Contains(invoice.WorkOrderId) && invoice.Status != InvoiceStatus.Void && invoice.Status != InvoiceStatus.Draft)
                .Select(invoice => new { invoice.Id, invoice.WorkOrderId, invoice.Status, invoice.InvoiceNumber, invoice.Total, invoice.Currency })
                .ToListAsync(cancellationToken);

        var paidIds = invoices.Where(invoice => invoice.Status == InvoiceStatus.Paid).Select(invoice => invoice.Id).ToList();
        var receipts = paidIds.Count == 0
            ? []
            : await (
                from allocation in dbContext.PaymentAllocations.AsNoTracking()
                where paidIds.Contains(allocation.InvoiceId)
                join payment in dbContext.Payments.AsNoTracking().ForPortal(scope) on allocation.PaymentId equals payment.Id
                where payment.ReceiptNumber != null
                orderby payment.CreatedAt descending
                select new { allocation.InvoiceId, PaymentId = payment.Id })
                .ToListAsync(cancellationToken);

        var items = rows
            .Select(row =>
            {
                var invoice = invoices.FirstOrDefault(candidate => candidate.WorkOrderId == row.Id);

                return PortalSelectionRules.ToActivityRow(
                    new ActivityFacts(
                        row.Id,
                        row.Title,
                        OrganizationTime.LocalDate(row.CompletedAt, org.Zone),
                        RequestCardRules.DisplayNumber(org.WorkOrderPrefix, row.WorkOrderNumber),
                        invoice?.Id,
                        invoice is null ? null : InvoiceHubRules.StoredStatus(invoice.Status),
                        invoice is null ? null : RequestCardRules.DisplayNumber(org.InvoicePrefix, invoice.InvoiceNumber),
                        invoice?.Total,
                        invoice?.Currency,
                        invoice is null ? null : receipts.FirstOrDefault(receipt => receipt.InvoiceId == invoice.Id)?.PaymentId),
                    org.Currency);
            })
            .ToList();

        return new PortalPage<PortalActivityRow>(items, page, pageSize, total);
    }
}
