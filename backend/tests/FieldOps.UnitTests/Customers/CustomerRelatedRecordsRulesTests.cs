using FieldOps.Application.Features.Customers;
using FieldOps.Application.Features.WorkOrders;
using FieldOps.Domain.WorkOrders;

namespace FieldOps.UnitTests.Customers;

/// <summary>Pure rules of customer-related-records: the Activity whitelist, the activity query, the list query and the Jobs derivations.</summary>
public class CustomerRelatedRecordsRulesTests
{
    // BR-10, BR-11 (AC-14): only catalog actions are shown, each under its type with the module that gates it and its link.
    [Theory]
    [InlineData("customer.created", "customer", "customer", null, true)]
    [InlineData("property.updated", "customer", "property", null, true)]
    [InlineData("customer_note.created", "customer", "customer_note", null, true)]
    [InlineData("service_request.quoted", "requests", "service_request", "request", true)]
    [InlineData("portal.message_sent", "messages", "customer_contact", null, true)]
    [InlineData("quote.clarification_requested", "quotes", "quote", "quote", true)]
    [InlineData("work_order.status_changed", "jobs", "work_order", "job", true)]
    [InlineData("visit.reschedule_requested", "visits", "visit", "job", true)]
    [InlineData("invoice.bank_transfer_reported", "invoices", "invoice", "invoice", true)]
    [InlineData("payment.refunded", "payments", "payment", "invoice", true)]
    [InlineData("visit.paused", "", "", null, false)]
    [InlineData("invoice.draft_saved", "", "", null, false)]
    [InlineData("quote.draft_saved", "", "", null, false)]
    [InlineData("customer.imported", "", "", null, false)]
    [InlineData("property.deleted", "", "", null, false)]
    public void Catalog_MapsWhitelistedActionsAndRejectsTheRest(
        string action, string type, string entityType, string? link, bool whitelisted)
    {
        Assert.Equal(whitelisted, CustomerActivityCatalog.TryGet(action, out var entry));

        if (whitelisted)
        {
            Assert.Equal((type, entityType, link), (entry.Type, entry.EntityType, entry.LinkKind));
        }

        Assert.Equal(action.StartsWith("property.", StringComparison.Ordinal) && whitelisted, CustomerActivityCatalog.NamesProperty(action));

        // Availability follows the module read policies (BR-10); Customer and Messages need no module.
        Assert.Equal(
            ["customer", "messages"],
            CustomerActivityCatalog.AvailableTypes(new CustomerRecordAccess(false, false, false)));
        Assert.Equal(
            ["customer", "requests", "messages", "quotes", "invoices", "payments"],
            CustomerActivityCatalog.AvailableTypes(new CustomerRecordAccess(true, false, true)));
    }

    // BR-12 (AC-09, AC-16): type, period and cursor validation; a type the caller cannot read is invalid, not empty.
    [Fact]
    public void ActivityRules_ValidateTypePeriodAndCursor()
    {
        var accounting = new CustomerRecordAccess(true, false, true);
        var now = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

        Assert.True(CustomerActivityRules.TryParseTypes(null, accounting, out var all));
        Assert.Equal(CustomerActivityCatalog.AvailableTypes(accounting), all);
        Assert.True(CustomerActivityRules.TryParseTypes("payments", accounting, out var payments));
        Assert.Equal(["payments"], payments);

        foreach (var invalid in new[] { "jobs", "visits", "Jobs", "unknown", "all " })
        {
            Assert.False(CustomerActivityRules.TryParseTypes(invalid, accounting, out _), invalid);
        }

        Assert.True(CustomerActivityRules.TryParsePeriod(null, now, out var defaultSince));
        Assert.Equal(now.AddDays(-90), defaultSince);
        Assert.True(CustomerActivityRules.TryParsePeriod("last_12_months", now, out var year));
        Assert.Equal(now.AddMonths(-12), year);
        Assert.True(CustomerActivityRules.TryParsePeriod("all", now, out var unbounded));
        Assert.Null(unbounded);
        Assert.False(CustomerActivityRules.TryParsePeriod("last_year", now, out _));

        var cursor = new CustomerActivityCursor(now.AddTicks(1234), 987654321012);
        var text = CustomerActivityRules.EncodeCursor(cursor);
        Assert.True(CustomerActivityRules.TryDecodeCursor(text, out var decoded));
        Assert.Equal(cursor, decoded);

        foreach (var invalid in new[] { "not base64 !", "AAAA", "MTIz", "Zm9vLmJhcg", "LTEuMQ" })
        {
            Assert.False(CustomerActivityRules.TryDecodeCursor(invalid, out _), invalid);
        }
    }

    // BR-04: one parser for the four tabs; page and page size bounds and the module status sets.
    [Theory]
    [InlineData(CustomerRecordKind.Requests, "assessment_scheduled", "1", "10", null)]
    [InlineData(CustomerRecordKind.Requests, "draft", "1", "10", "status")]
    [InlineData(CustomerRecordKind.Quotes, "awaiting_response", "2", "25", null)]
    [InlineData(CustomerRecordKind.Quotes, "converted", null, null, "status")]
    [InlineData(CustomerRecordKind.Jobs, "needs_correction", null, "50", null)]
    [InlineData(CustomerRecordKind.Jobs, "draft", null, null, "status")]
    [InlineData(CustomerRecordKind.Invoices, "overdue", null, null, null)]
    [InlineData(CustomerRecordKind.Invoices, "void", null, null, "status")]
    [InlineData(CustomerRecordKind.Invoices, null, "0", null, "page")]
    [InlineData(CustomerRecordKind.Invoices, null, "abc", null, "page")]
    [InlineData(CustomerRecordKind.Jobs, null, null, "20", "pageSize")]
    [InlineData(CustomerRecordKind.Jobs, null, null, "0", "pageSize")]
    public void ListParser_AcceptsModuleStatusesAndBoundsPaging(
        CustomerRecordKind kind, string? status, string? page, string? pageSize, string? errorKey)
    {
        var query = CustomerRecordRules.Parse(kind, new CustomerRecordQueryText(null, status, page, pageSize), out var errors);

        Assert.Equal(errorKey is null, query is not null);
        Assert.Equal(errorKey is null ? Array.Empty<string>() : new[] { errorKey }, errors.Keys.ToArray());

        var tooLong = CustomerRecordRules.Parse(kind, new CustomerRecordQueryText(new string('s', 101), null, null, null), out var searchErrors);
        Assert.Null(tooLong);
        Assert.Equal(["search"], searchErrors.Keys.ToArray());
        Assert.NotNull(CustomerRecordRules.Parse(kind, new CustomerRecordQueryText("  " + new string('s', 100) + "  ", null, null, null), out _));
    }

    // BR-07 (AC-04): current visit, operational status and the Next visit / Completed column.
    [Fact]
    public void JobRules_DeriveCurrentVisitStatusAndVisitColumn()
    {
        var now = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
        CustomerJobVisit Visit(int number, VisitStatus status, int startDays, int? completedDays = null) =>
            new(
                Guid.NewGuid(),
                number,
                status,
                now.AddDays(startDays),
                now.AddDays(startDays).AddHours(2),
                completedDays is { } done ? now.AddDays(done) : null);

        var past = Visit(1, VisitStatus.Completed, -10, -9);
        var later = Visit(2, VisitStatus.Assigned, 5);
        var soon = Visit(3, VisitStatus.Scheduled, 2);
        var stale = Visit(4, VisitStatus.Scheduled, -3);
        var visits = new[] { past, later, soon, stale };

        // Lowest open visit number wins; closed work only counts when nothing is open.
        Assert.Equal(later, CustomerJobRules.CurrentVisit(visits));
        Assert.Equal(past, CustomerJobRules.CurrentVisit([past, Visit(2, VisitStatus.Cancelled, 1)]));
        Assert.Null(CustomerJobRules.CurrentVisit([]));

        Assert.Equal(JobStatus.Assigned, CustomerJobRules.Status(WorkOrderStatus.Scheduled, later, anyNeedsCorrection: false));
        Assert.Equal(JobStatus.Paused, CustomerJobRules.Status(WorkOrderStatus.InProgress, Visit(1, VisitStatus.Paused, 0), false));
        Assert.Equal(JobStatus.NeedsCorrection, CustomerJobRules.Status(WorkOrderStatus.Completed, past, anyNeedsCorrection: true));
        Assert.Equal(JobStatus.Cancelled, CustomerJobRules.Status(WorkOrderStatus.Cancelled, null, false));

        // Earliest upcoming scheduled/assigned/on-the-way visit; past and other-status visits never count.
        Assert.Equal((now.AddDays(2), null), CustomerJobRules.VisitColumn(JobStatus.Assigned, visits, now));
        Assert.Equal((null, null), CustomerJobRules.VisitColumn(JobStatus.ReadyToSchedule, [stale, Visit(5, VisitStatus.InProgress, 1)], now));

        // Completed jobs show the latest completion (actual, else scheduled end) and no next visit.
        Assert.Equal((null, now.AddDays(-9)), CustomerJobRules.VisitColumn(JobStatus.Completed, visits, now));
        Assert.Equal(
            (null, now.AddDays(-4).AddHours(2)),
            CustomerJobRules.VisitColumn(JobStatus.ApprovedForBilling, [past, Visit(2, VisitStatus.Approved, -4)], now));
    }
}
