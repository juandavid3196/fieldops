using FieldOps.Application.Features.Access;
using FieldOps.Application.Features.BillingReview;
using FieldOps.Application.Features.InvoicePayments;
using FieldOps.Application.Features.OnlinePayments;
using FieldOps.Application.Features.Quotes;
using FieldOps.Application.Features.ServiceRequests;
using FieldOps.Domain.Invoices;
using FieldOps.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Persistence;

/// <summary>
/// Read side of the invoices and payments hub (invoices-payments-management). Every query starts from the session
/// organization and the branch scope of <c>invoices.branch_id</c> (BR-02); void invoices and their payments never appear,
/// and a foreign, out-of-scope or unknown branch or customer filter is one identical "not found". Reads are no-tracking.
/// </summary>
internal sealed class InvoiceHubStore(FieldOpsDbContext dbContext) : IInvoiceHubStore
{
    private sealed record OrgInfo(string Timezone, string Currency, string InvoicePrefix, string WorkOrderPrefix, string PaymentPrefix);

    private sealed class InvoiceFact
    {
        public Guid Id { get; init; }

        public long InvoiceNumber { get; init; }

        public Guid CustomerId { get; init; }

        public string? CustomerName { get; init; }

        public Guid WorkOrderId { get; init; }

        public long? WorkOrderNumber { get; init; }

        public string? BranchName { get; init; }

        public DateOnly? IssueDate { get; init; }

        public DateOnly? DueDate { get; init; }

        public decimal Total { get; init; }

        public decimal AmountPaid { get; init; }

        public decimal BalanceDue { get; init; }

        public string Currency { get; init; } = string.Empty;

        public InvoiceStatus Status { get; init; }

        public DateTimeOffset UpdatedAt { get; init; }

        public DateTimeOffset? SentAt { get; init; }

        public bool HasRecipient { get; init; }
    }

    private sealed class PaymentFact
    {
        public Guid PaymentId { get; init; }

        public long PaymentNumber { get; init; }

        public DateTimeOffset PaidAt { get; init; }

        public PaymentMethod Method { get; init; }

        public string? Reference { get; init; }

        public decimal Amount { get; init; }

        public decimal RefundedAmount { get; init; }

        public PaymentStatus Status { get; init; }

        public string Currency { get; init; } = string.Empty;

        public Guid InvoiceId { get; init; }

        public long InvoiceNumber { get; init; }

        public string CustomerName { get; init; } = string.Empty;

        public string? ReceivedByName { get; init; }
    }

    public async Task<HubOptions> GetOptionsAsync(BillingActor actor, bool canAct, CancellationToken cancellationToken)
    {
        var organizationId = actor.OrganizationId;
        var org = await ReadOrgAsync(organizationId, cancellationToken);
        var branches = dbContext.Branches.AsNoTracking().Where(branch => branch.OrganizationId == organizationId);

        if (!actor.Scope.All)
        {
            var ids = actor.Scope.BranchIds.ToArray();
            branches = branches.Where(branch => ids.Contains(branch.Id));
        }

        var branchOptions = await branches
            .OrderBy(branch => branch.Name)
            .ThenBy(branch => branch.Id)
            .Select(branch => new NamedOption(branch.Id, branch.Name))
            .ToListAsync(cancellationToken);

        var members = new List<HubMember>();

        if (canAct)
        {
            var rows = await (
                from membership in dbContext.OrganizationUsers.AsNoTracking()
                join user in dbContext.Users.AsNoTracking() on membership.UserId equals user.Id
                where membership.OrganizationId == organizationId && membership.Status == UserStatus.Active
                orderby user.FirstName, user.LastName, user.Id
                select new { user.Id, user.FirstName, user.LastName })
                .ToListAsync(cancellationToken);

            members = rows.Select(row => new HubMember(row.Id, FullName(row.FirstName, row.LastName))).ToList();
        }

        return new HubOptions(org.Timezone, org.Currency, org.PaymentPrefix, branchOptions, members, canAct);
    }

    public async Task<IReadOnlyList<NamedOption>> GetCustomersAsync(BillingActor actor, string? search, CancellationToken cancellationToken) =>
        await CustomerOptions(actor, search).ToListAsync(cancellationToken);

    private IQueryable<NamedOption> CustomerOptions(BillingActor actor, string? search)
    {
        var organizationId = actor.OrganizationId;
        var visible = VisibleInvoices(organizationId, actor.Scope);
        var customers = dbContext.Customers.AsNoTracking()
            .Where(customer => customer.OrganizationId == organizationId
                && visible.Any(invoice => invoice.CustomerId == customer.Id));

        if (search is not null)
        {
            var pattern = RequestSearch.ContainsPattern(search);
            customers = customers.Where(customer => EF.Functions.ILike(customer.DisplayName, pattern));
        }

        return customers
            .OrderBy(customer => customer.DisplayName)
            .ThenBy(customer => customer.Id)
            .Take(InvoicePaymentMessages.MaxCustomers)
            .Select(customer => new NamedOption(customer.Id, customer.DisplayName));
    }

    public async Task<BillingOutcome<HubOverview>> GetOverviewAsync(
        BillingActor actor, Guid? branchId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var organizationId = actor.OrganizationId;

        if (branchId is { } filter && !await BranchVisibleAsync(actor, filter, cancellationToken))
        {
            return new BillingOutcome<HubOverview>.NotFound();
        }

        var org = await ReadOrgAsync(organizationId, cancellationToken);
        var zone = OrganizationTime.FindZone(org.Timezone);
        var today = OrganizationTime.LocalDate(now, zone);
        var invoices = VisibleInvoices(organizationId, actor.Scope);

        if (branchId is { } branch)
        {
            invoices = invoices.Where(invoice => invoice.BranchId == branch);
        }

        // Outstanding, overdue and aging come from the balances grouped by due date, bucketed in organization-local days.
        var outstandingRows = await OutstandingByDueDate(invoices).ToListAsync(cancellationToken);

        var buckets = new decimal[4];

        foreach (var row in outstandingRows)
        {
            buckets[(int)InvoiceHubRules.Bucket(row.Due, today)] += row.Balance;
        }

        var outstanding = outstandingRows.Sum(row => row.Balance);
        var overdue = buckets[1] + buckets[2] + buckets[3];
        var draft = await invoices
            .Where(invoice => invoice.Status == InvoiceStatus.Draft)
            .SumAsync(invoice => invoice.Total, cancellationToken);

        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var paidThisMonth = await PaidBetween(
            organizationId,
            invoices,
            InvoiceHubRules.DayStartUtc(monthStart, zone),
            InvoiceHubRules.DayStartUtc(monthStart.AddMonths(1), zone))
            .SumAsync(cancellationToken);

        var windowFrom = InvoiceHubRules.DayStartUtc(today.AddDays(-(InvoicePaymentMessages.AverageWindowDays - 1)), zone);
        var windowTo = InvoiceHubRules.DayStartUtc(today.AddDays(1), zone);
        var paidInvoices = await LatestPaymentOfPaidInvoices(organizationId, invoices, windowFrom, windowTo).ToListAsync(cancellationToken);

        var average = InvoiceHubRules.AverageDaysToPay(
            paidInvoices.Select(row => (row.IssueDate!.Value, row.Latest)), zone, today);

        var recent = (await PaymentFacts(organizationId, actor.Scope, branchId)
            .OrderByDescending(fact => fact.PaidAt)
            .ThenByDescending(fact => fact.PaymentNumber)
            .Take(InvoicePaymentMessages.RecentPayments)
            .ToListAsync(cancellationToken))
            .Select(fact => new RecentPayment(
                fact.PaymentId,
                OrganizationTime.LocalDate(fact.PaidAt, zone),
                fact.CustomerName,
                fact.InvoiceId,
                InvoiceHubRules.DisplayNumber(org.InvoicePrefix, fact.InvoiceNumber),
                QuoteCalculator.Money(fact.Amount - fact.RefundedAmount),
                PaymentMethodCodes.Code(fact.Method)))
            .ToList();

        return new BillingOutcome<HubOverview>.Succeeded(new HubOverview(
            new HubMetrics(
                QuoteCalculator.Money(outstanding),
                QuoteCalculator.Money(overdue),
                QuoteCalculator.Money(draft),
                QuoteCalculator.Money(paidThisMonth),
                average,
                org.Currency),
            new HubAging(
                QuoteCalculator.Money(buckets[0]),
                QuoteCalculator.Money(buckets[1]),
                QuoteCalculator.Money(buckets[2]),
                QuoteCalculator.Money(buckets[3])),
            recent));
    }

    private sealed record DueBalance(DateOnly? Due, decimal Balance);

    private sealed record PaidInvoice(DateOnly? IssueDate, DateTimeOffset Latest);

    private static IQueryable<DueBalance> OutstandingByDueDate(IQueryable<Invoice> invoices) =>
        invoices
            .Where(invoice => invoice.Status == InvoiceStatus.Sent || invoice.Status == InvoiceStatus.PartiallyPaid)
            .GroupBy(invoice => invoice.DueDate)
            .Select(group => new DueBalance(group.Key, group.Sum(invoice => invoice.BalanceDue)));

    // Net allocation amounts (allocation - refunded amount of the payment, customer-invoice-payments BR-31 a) of payments whose
    // paid_at falls in [from, to) on visible invoices (BR-04 paid this month). A payment has one allocation of its full amount.
    private IQueryable<decimal> PaidBetween(Guid organizationId, IQueryable<Invoice> invoices, DateTimeOffset start, DateTimeOffset end) =>
        from allocation in dbContext.PaymentAllocations.AsNoTracking()
        join payment in dbContext.Payments.AsNoTracking() on allocation.PaymentId equals payment.Id
        join invoice in invoices on allocation.InvoiceId equals invoice.Id
        where payment.OrganizationId == organizationId && payment.PaidAt >= start && payment.PaidAt < end
        select allocation.Amount - payment.RefundedAmount;

    // The latest payment instant of every paid invoice whose latest payment lies in [from, to) (BR-04 average time to pay).
    private IQueryable<PaidInvoice> LatestPaymentOfPaidInvoices(
        Guid organizationId, IQueryable<Invoice> invoices, DateTimeOffset start, DateTimeOffset end) =>
        (from allocation in dbContext.PaymentAllocations.AsNoTracking()
         join payment in dbContext.Payments.AsNoTracking() on allocation.PaymentId equals payment.Id
         join invoice in invoices.Where(candidate => candidate.Status == InvoiceStatus.Paid && candidate.IssueDate != null)
             on allocation.InvoiceId equals invoice.Id
         where payment.OrganizationId == organizationId && payment.Status != PaymentStatus.Refunded
         group payment.PaidAt by new { invoice.Id, invoice.IssueDate })
        .Where(paid => paid.Max() >= start && paid.Max() < end)
        .Select(paid => new PaidInvoice(paid.Key.IssueDate, paid.Max()));

    public async Task<BillingOutcome<InvoicePage>> GetInvoicesAsync(
        BillingActor actor, InvoiceQuery query, bool canAct, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (await ReadInvoiceContextAsync(actor, query, now, cancellationToken) is not { } context)
        {
            return new BillingOutcome<InvoicePage>.NotFound();
        }

        var total = await context.Filtered.CountAsync(cancellationToken);
        var facts = await ToFacts(
            Sorted(context.Filtered).Skip((query.Page - 1) * InvoicePaymentMessages.PageSize).Take(InvoicePaymentMessages.PageSize))
            .ToListAsync(cancellationToken);
        var latest = await LatestPaymentsAsync(
            facts.Where(fact => fact.Status is InvoiceStatus.PartiallyPaid or InvoiceStatus.Paid).Select(fact => fact.Id).ToArray(),
            cancellationToken);

        var items = facts
            .Select(fact =>
            {
                var (status, days) = InvoiceHubRules.Display(fact.Status, fact.DueDate, context.Today);

                return new InvoiceRow(
                    fact.Id,
                    InvoiceHubRules.DisplayNumber(context.Org.InvoicePrefix, fact.InvoiceNumber),
                    fact.CustomerId,
                    fact.CustomerName ?? string.Empty,
                    fact.WorkOrderId,
                    InvoiceHubRules.DisplayNumber(context.Org.WorkOrderPrefix, fact.WorkOrderNumber ?? 0),
                    fact.BranchName ?? string.Empty,
                    fact.IssueDate,
                    fact.DueDate,
                    QuoteCalculator.Money(fact.Total),
                    QuoteCalculator.Money(fact.BalanceDue),
                    fact.Currency,
                    status,
                    InvoiceHubRules.StoredStatus(fact.Status),
                    days,
                    Activity(fact, latest, context.Zone),
                    fact.HasRecipient,
                    fact.UpdatedAt,
                    canAct && InvoiceHubRules.IsOutstanding(fact.Status));
            })
            .ToList();

        return new BillingOutcome<InvoicePage>.Succeeded(new InvoicePage(items, query.Page, InvoicePaymentMessages.PageSize, total));
    }

    public async Task<BillingOutcome<InvoiceExport>> ExportInvoicesAsync(
        BillingActor actor, InvoiceQuery query, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (await ReadInvoiceContextAsync(actor, query, now, cancellationToken) is not { } context)
        {
            return new BillingOutcome<InvoiceExport>.NotFound();
        }

        if (await context.Filtered.CountAsync(cancellationToken) > InvoicePaymentMessages.MaxExportRows)
        {
            return new BillingOutcome<InvoiceExport>.Conflict(BillingCodes.ExportTooLarge, InvoicePaymentMessages.ExportTooLargeTitle);
        }

        var facts = await ToFacts(Sorted(context.Filtered)).ToListAsync(cancellationToken);
        var rows = facts
            .Select(fact =>
            {
                var (status, days) = InvoiceHubRules.Display(fact.Status, fact.DueDate, context.Today);

                return new InvoiceExportRow(
                    InvoiceHubRules.DisplayNumber(context.Org.InvoicePrefix, fact.InvoiceNumber),
                    fact.CustomerName ?? string.Empty,
                    InvoiceHubRules.DisplayNumber(context.Org.WorkOrderPrefix, fact.WorkOrderNumber ?? 0),
                    fact.BranchName ?? string.Empty,
                    fact.IssueDate,
                    fact.DueDate,
                    status,
                    days,
                    fact.Total,
                    fact.AmountPaid,
                    fact.BalanceDue,
                    fact.Currency);
            })
            .ToList();

        return new BillingOutcome<InvoiceExport>.Succeeded(new InvoiceExport(context.Today, rows));
    }

    public async Task<BillingOutcome<PaymentPage>> GetPaymentsAsync(
        BillingActor actor, PaymentQuery query, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (await ReadPaymentContextAsync(actor, query, now, cancellationToken) is not { } context)
        {
            return new BillingOutcome<PaymentPage>.NotFound();
        }

        var total = await context.Filtered.CountAsync(cancellationToken);
        var facts = await Sorted(context.Filtered)
            .Skip((query.Page - 1) * InvoicePaymentMessages.PageSize)
            .Take(InvoicePaymentMessages.PageSize)
            .ToListAsync(cancellationToken);

        return new BillingOutcome<PaymentPage>.Succeeded(new PaymentPage(
            facts.Select(fact => ToRow(fact, context.Org, context.Zone)).ToList(), query.Page, InvoicePaymentMessages.PageSize, total));
    }

    public async Task<BillingOutcome<PaymentExport>> ExportPaymentsAsync(
        BillingActor actor, PaymentQuery query, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (await ReadPaymentContextAsync(actor, query, now, cancellationToken) is not { } context)
        {
            return new BillingOutcome<PaymentExport>.NotFound();
        }

        if (await context.Filtered.CountAsync(cancellationToken) > InvoicePaymentMessages.MaxExportRows)
        {
            return new BillingOutcome<PaymentExport>.Conflict(BillingCodes.ExportTooLarge, InvoicePaymentMessages.ExportTooLargeTitle);
        }

        var facts = await Sorted(context.Filtered).ToListAsync(cancellationToken);

        return new BillingOutcome<PaymentExport>.Succeeded(new PaymentExport(
            context.Today, facts.Select(fact => ToRow(fact, context.Org, context.Zone)).ToList()));
    }

    private sealed record InvoiceContext(OrgInfo Org, TimeZoneInfo Zone, DateOnly Today, IQueryable<Invoice> Filtered);

    private sealed record PaymentContext(OrgInfo Org, TimeZoneInfo Zone, DateOnly Today, IQueryable<PaymentFact> Filtered);

    // Null for a foreign, out-of-scope or unknown branch or customer filter (BR-02).
    private async Task<InvoiceContext?> ReadInvoiceContextAsync(
        BillingActor actor, InvoiceQuery query, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var organizationId = actor.OrganizationId;

        if (query.BranchId is { } branchId && !await BranchVisibleAsync(actor, branchId, cancellationToken))
        {
            return null;
        }

        if (query.CustomerId is { } customerId
            && !await dbContext.Customers.AsNoTracking().AnyAsync(
                customer => customer.OrganizationId == organizationId && customer.Id == customerId, cancellationToken))
        {
            return null;
        }

        var org = await ReadOrgAsync(organizationId, cancellationToken);
        var zone = OrganizationTime.FindZone(org.Timezone);
        var today = OrganizationTime.LocalDate(now, zone);

        return new InvoiceContext(org, zone, today, FilterInvoices(actor, query, org, today));
    }

    private async Task<PaymentContext?> ReadPaymentContextAsync(
        BillingActor actor, PaymentQuery query, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (query.BranchId is { } branchId && !await BranchVisibleAsync(actor, branchId, cancellationToken))
        {
            return null;
        }

        var org = await ReadOrgAsync(actor.OrganizationId, cancellationToken);
        var zone = OrganizationTime.FindZone(org.Timezone);

        return new PaymentContext(org, zone, OrganizationTime.LocalDate(now, zone), FilterPayments(actor, query, org, zone));
    }

    /// <summary>The visible invoices that match the list filters (BR-07); status filters use the BR-03 display status at <paramref name="today"/>.</summary>
    private IQueryable<Invoice> FilterInvoices(BillingActor actor, InvoiceQuery query, OrgInfo org, DateOnly today)
    {
        var invoices = VisibleInvoices(actor.OrganizationId, actor.Scope);

        if (query.BranchId is { } branch)
        {
            invoices = invoices.Where(invoice => invoice.BranchId == branch);
        }

        if (query.CustomerId is { } customer)
        {
            invoices = invoices.Where(invoice => invoice.CustomerId == customer);
        }

        if (query.From is { } from)
        {
            invoices = invoices.Where(invoice => invoice.IssueDate >= from);
        }

        if (query.To is { } to)
        {
            invoices = invoices.Where(invoice => invoice.IssueDate <= to);
        }

        invoices = query.Status switch
        {
            InvoiceStatusFilters.Draft => invoices.Where(invoice => invoice.Status == InvoiceStatus.Draft),
            InvoiceStatusFilters.Paid => invoices.Where(invoice => invoice.Status == InvoiceStatus.Paid),
            InvoiceStatusFilters.Sent => invoices.Where(invoice =>
                invoice.Status == InvoiceStatus.Sent && (invoice.DueDate == null || invoice.DueDate >= today)),
            InvoiceStatusFilters.PartiallyPaid => invoices.Where(invoice =>
                invoice.Status == InvoiceStatus.PartiallyPaid && (invoice.DueDate == null || invoice.DueDate >= today)),
            InvoiceStatusFilters.Overdue => invoices.Where(invoice =>
                (invoice.Status == InvoiceStatus.Sent || invoice.Status == InvoiceStatus.PartiallyPaid) && invoice.DueDate < today),
            _ => invoices,
        };

        if (query.Search is { } search)
        {
            var pattern = RequestSearch.ContainsPattern(search);
            var hasInvoiceNumber = InvoiceHubRules.TryParseNumber(search, org.InvoicePrefix, out var invoiceNumber);
            var hasWorkOrderNumber = InvoiceHubRules.TryParseNumber(search, org.WorkOrderPrefix, out var workOrderNumber);

            invoices = invoices.Where(invoice =>
                dbContext.Customers.Any(candidate =>
                    candidate.OrganizationId == invoice.OrganizationId
                    && candidate.Id == invoice.CustomerId
                    && EF.Functions.ILike(candidate.DisplayName, pattern))
                || (hasInvoiceNumber && invoice.InvoiceNumber == invoiceNumber)
                || (hasWorkOrderNumber && dbContext.WorkOrders.Any(order =>
                    order.OrganizationId == invoice.OrganizationId
                    && order.Id == invoice.WorkOrderId
                    && order.WorkOrderNumber == workOrderNumber)));
        }

        return invoices;
    }

    /// <summary>The visible payments that match the list filters (BR-09); the dates are organization-local days.</summary>
    private IQueryable<PaymentFact> FilterPayments(BillingActor actor, PaymentQuery query, OrgInfo org, TimeZoneInfo zone)
    {
        var payments = PaymentFacts(actor.OrganizationId, actor.Scope, query.BranchId);

        if (query.From is { } from)
        {
            var start = InvoiceHubRules.DayStartUtc(from, zone);
            payments = payments.Where(fact => fact.PaidAt >= start);
        }

        if (query.To is { } to)
        {
            var end = InvoiceHubRules.DayStartUtc(to.AddDays(1), zone);
            payments = payments.Where(fact => fact.PaidAt < end);
        }

        if (query.Method is { } method)
        {
            payments = payments.Where(fact => fact.Method == method);
        }

        if (query.Search is { } search)
        {
            var pattern = RequestSearch.ContainsPattern(search);
            var hasPaymentNumber = InvoiceHubRules.TryParseNumber(search, org.PaymentPrefix, out var paymentNumber);
            var hasInvoiceNumber = InvoiceHubRules.TryParseNumber(search, org.InvoicePrefix, out var invoiceNumber);

            payments = payments.Where(fact =>
                EF.Functions.ILike(fact.CustomerName, pattern)
                || (fact.Reference != null && EF.Functions.ILike(fact.Reference, pattern))
                || (hasPaymentNumber && fact.PaymentNumber == paymentNumber)
                || (hasInvoiceNumber && fact.InvoiceNumber == invoiceNumber));
        }

        return payments;
    }

    /// <summary>Payments whose allocation points to a visible, non-void invoice (BR-02), with the customer and receiver names.</summary>
    private IQueryable<PaymentFact> PaymentFacts(Guid organizationId, BranchScope scope, Guid? branchId)
    {
        var invoices = VisibleInvoices(organizationId, scope);

        if (branchId is { } branch)
        {
            invoices = invoices.Where(invoice => invoice.BranchId == branch);
        }

        return
            from allocation in dbContext.PaymentAllocations.AsNoTracking()
            join payment in dbContext.Payments.AsNoTracking() on allocation.PaymentId equals payment.Id
            join invoice in invoices on allocation.InvoiceId equals invoice.Id
            join customer in dbContext.Customers.AsNoTracking()
                on new { invoice.OrganizationId, Id = invoice.CustomerId } equals new { customer.OrganizationId, customer.Id }
            where payment.OrganizationId == organizationId
            select new PaymentFact
            {
                PaymentId = payment.Id,
                PaymentNumber = payment.PaymentNumber,
                PaidAt = payment.PaidAt,
                Method = payment.Method,
                Reference = payment.ExternalReference,
                Amount = payment.Amount,
                RefundedAmount = payment.RefundedAmount,
                Status = payment.Status,
                Currency = payment.Currency,
                InvoiceId = invoice.Id,
                InvoiceNumber = invoice.InvoiceNumber,
                CustomerName = customer.DisplayName,
                ReceivedByName = dbContext.Users
                    .Where(user => payment.ReceivedByUserId != null && user.Id == payment.ReceivedByUserId)
                    .Select(user => user.FirstName + " " + user.LastName)
                    .FirstOrDefault(),
            };
    }

    private static IOrderedQueryable<PaymentFact> Sorted(IQueryable<PaymentFact> facts) =>
        facts.OrderByDescending(fact => fact.PaidAt).ThenByDescending(fact => fact.PaymentNumber);

    private static IOrderedQueryable<Invoice> Sorted(IQueryable<Invoice> invoices) =>
        invoices.OrderByDescending(invoice => invoice.IssueDate).ThenByDescending(invoice => invoice.InvoiceNumber);

    private IQueryable<InvoiceFact> ToFacts(IQueryable<Invoice> invoices) =>
        invoices.Select(invoice => new InvoiceFact
        {
            Id = invoice.Id,
            InvoiceNumber = invoice.InvoiceNumber,
            CustomerId = invoice.CustomerId,
            CustomerName = dbContext.Customers
                .Where(customer => customer.OrganizationId == invoice.OrganizationId && customer.Id == invoice.CustomerId)
                .Select(customer => customer.DisplayName)
                .FirstOrDefault(),
            WorkOrderId = invoice.WorkOrderId,
            WorkOrderNumber = dbContext.WorkOrders
                .Where(order => order.OrganizationId == invoice.OrganizationId && order.Id == invoice.WorkOrderId)
                .Select(order => (long?)order.WorkOrderNumber)
                .FirstOrDefault(),
            BranchName = dbContext.Branches
                .Where(branch => branch.OrganizationId == invoice.OrganizationId && branch.Id == invoice.BranchId)
                .Select(branch => branch.Name)
                .FirstOrDefault(),
            IssueDate = invoice.IssueDate,
            DueDate = invoice.DueDate,
            Total = invoice.Total,
            AmountPaid = invoice.AmountPaid,
            BalanceDue = invoice.BalanceDue,
            Currency = invoice.Currency,
            Status = invoice.Status,
            UpdatedAt = invoice.UpdatedAt,
            SentAt = invoice.SentAt,
            HasRecipient = invoice.RecipientEmail != null,
        });

    private async Task<Dictionary<Guid, DateTimeOffset>> LatestPaymentsAsync(Guid[] invoiceIds, CancellationToken cancellationToken)
    {
        if (invoiceIds.Length == 0)
        {
            return [];
        }

        var rows = await LatestPaymentPerInvoice(invoiceIds).ToListAsync(cancellationToken);

        return rows.ToDictionary(row => row.InvoiceId, row => row.Latest);
    }

    private sealed record LatestPayment(Guid InvoiceId, DateTimeOffset Latest);

    private IQueryable<LatestPayment> LatestPaymentPerInvoice(Guid[] invoiceIds) =>
        from allocation in dbContext.PaymentAllocations.AsNoTracking()
        join payment in dbContext.Payments.AsNoTracking() on allocation.PaymentId equals payment.Id
        where invoiceIds.Contains(allocation.InvoiceId) && payment.Status != PaymentStatus.Refunded
        group payment.PaidAt by allocation.InvoiceId into paid
        select new LatestPayment(paid.Key, paid.Max());

    // BR-08: paid and partially paid show the latest payment date, sent the send date, drafts nothing.
    private static LastActivity? Activity(InvoiceFact fact, Dictionary<Guid, DateTimeOffset> latest, TimeZoneInfo zone) =>
        fact.Status switch
        {
            InvoiceStatus.Paid when latest.TryGetValue(fact.Id, out var paid) => new LastActivity("paid", OrganizationTime.LocalDate(paid, zone)),
            InvoiceStatus.PartiallyPaid when latest.TryGetValue(fact.Id, out var paid) => new LastActivity("payment", OrganizationTime.LocalDate(paid, zone)),
            InvoiceStatus.Sent when fact.SentAt is { } sent => new LastActivity("sent", OrganizationTime.LocalDate(sent, zone)),
            _ => null,
        };

    private static PaymentRow ToRow(PaymentFact fact, OrgInfo org, TimeZoneInfo zone) =>
        new(
            fact.PaymentId,
            InvoiceHubRules.DisplayNumber(org.PaymentPrefix, fact.PaymentNumber),
            OrganizationTime.LocalDate(fact.PaidAt, zone),
            fact.CustomerName,
            fact.InvoiceId,
            InvoiceHubRules.DisplayNumber(org.InvoicePrefix, fact.InvoiceNumber),
            PaymentMethodCodes.Code(fact.Method),
            fact.Reference,
            QuoteCalculator.Money(fact.Amount),
            fact.Currency,
            fact.ReceivedByName,
            OnlinePaymentRules.PaymentStatusCode(fact.Status),
            QuoteCalculator.Money(fact.RefundedAmount));

    /// <summary>Invoices of the organization inside the caller branch scope, never void (BR-02).</summary>
    private IQueryable<Invoice> VisibleInvoices(Guid organizationId, BranchScope scope)
    {
        var invoices = dbContext.Invoices.AsNoTracking()
            .Where(invoice => invoice.OrganizationId == organizationId && invoice.Status != InvoiceStatus.Void);

        if (!scope.All)
        {
            var ids = scope.BranchIds.ToArray();
            invoices = invoices.Where(invoice => ids.Contains(invoice.BranchId));
        }

        return invoices;
    }

    private async Task<bool> BranchVisibleAsync(BillingActor actor, Guid branchId, CancellationToken cancellationToken) =>
        actor.Scope.Contains(branchId)
        && await dbContext.Branches.AsNoTracking().AnyAsync(
            branch => branch.OrganizationId == actor.OrganizationId && branch.Id == branchId, cancellationToken);

    private async Task<OrgInfo> ReadOrgAsync(Guid organizationId, CancellationToken cancellationToken) =>
        await dbContext.Organizations.AsNoTracking()
            .Where(organization => organization.Id == organizationId)
            .Select(organization => new OrgInfo(
                organization.Timezone,
                organization.Currency,
                organization.InvoicePrefix,
                organization.WorkOrderPrefix,
                organization.PaymentPrefix))
            .SingleAsync(cancellationToken);

    private static string FullName(string? first, string? last) =>
        string.Join(' ', new[] { first, last }.Where(part => !string.IsNullOrWhiteSpace(part))).Trim();
}
