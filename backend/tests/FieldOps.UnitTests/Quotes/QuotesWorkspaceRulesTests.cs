using FieldOps.Application.Features.Quotes;
using FieldOps.Domain.Quotes;
using FieldOps.Domain.WorkOrders;

namespace FieldOps.UnitTests.Quotes;

public class QuotesWorkspaceRulesTests
{
    // BR-15: eligibility by display status, revision state, work order state and policy flags (AC-14).
    [Theory]
    [InlineData(QuoteDisplayStatus.Draft, QuoteStatus.Draft, true, 0, null, true, true, true, "view,continue_draft,open_request")]
    [InlineData(QuoteDisplayStatus.Draft, QuoteStatus.Draft, true, 0, null, false, true, true, "view,open_request")]
    [InlineData(QuoteDisplayStatus.AwaitingResponse, QuoteStatus.Sent, false, 1, null, true, true, true, "view,revise,open_request")]
    [InlineData(QuoteDisplayStatus.AwaitingResponse, QuoteStatus.Sent, true, 2, null, true, true, true, "view,continue_revision,open_request")]
    [InlineData(QuoteDisplayStatus.Expired, QuoteStatus.Sent, false, 1, null, true, true, true, "view,revise,open_request")]
    [InlineData(QuoteDisplayStatus.Rejected, QuoteStatus.Rejected, false, 1, null, true, true, true, "view,revise,open_request")]
    [InlineData(QuoteDisplayStatus.Approved, QuoteStatus.Approved, false, 1, null, true, true, true, "view,create_work_order,open_request")]
    [InlineData(QuoteDisplayStatus.Approved, QuoteStatus.Approved, false, 1, WorkOrderStatus.Draft, true, true, true, "view,continue_work_order,open_request")]
    [InlineData(QuoteDisplayStatus.Approved, QuoteStatus.Approved, false, 1, WorkOrderStatus.Scheduled, true, true, true, "view,view_job,open_request")]
    [InlineData(QuoteDisplayStatus.Approved, QuoteStatus.Approved, false, 1, WorkOrderStatus.Scheduled, false, false, false, "view,open_request")]
    [InlineData(QuoteDisplayStatus.Cancelled, QuoteStatus.Cancelled, true, 0, null, true, true, true, "view,open_request")]
    public void Actions_StateAndFlags_ListOnlyEligibleActionsInOrder(
        QuoteDisplayStatus display,
        QuoteStatus stored,
        bool hasMutable,
        int currentVersion,
        WorkOrderStatus? workOrder,
        bool canManage,
        bool canManageOrders,
        bool canReadOrders,
        string expected)
    {
        // The cancelled draft keeps its mutable version, yet is not a draft to continue.
        var actions = QuotesWorkspaceRules.Actions(
            display, stored, hasMutable, currentVersion, workOrder is not null, workOrder, canManage, canManageOrders, canReadOrders);

        Assert.Equal(expected.Split(','), actions);
    }

    // BR-05, BR-06, BR-07: tab membership, status codes per tab and the date window start.
    [Fact]
    public void TabsStatusesAndWindows_FollowTheRules()
    {
        Assert.Equal(7, QuotesWorkspaceRules.TabStatuses(QuotesTab.All).Length);
        Assert.Equal(
            ["rejected", "expired", "cancelled"],
            QuotesWorkspaceRules.TabStatuses(QuotesTab.Closed).Select(QuotesWorkspaceRules.StatusCode));
        Assert.True(QuotesWorkspaceRules.TryParseStatus("expired", QuotesTab.Closed, out var status) && status == QuoteDisplayStatus.Expired);
        Assert.False(QuotesWorkspaceRules.TryParseStatus("expired", QuotesTab.Sent, out _));
        Assert.False(QuotesWorkspaceRules.TryParseStatus("sent", QuotesTab.All, out _));

        var today = new DateOnly(2026, 10, 14);
        Assert.Equal(today, QuotesWorkspaceRules.WindowStart(QuotesDateRange.Today, today));
        Assert.Equal(today.AddDays(-6), QuotesWorkspaceRules.WindowStart(QuotesDateRange.Last7Days, today));
        Assert.Equal(today.AddDays(-89), QuotesWorkspaceRules.WindowStart(QuotesDateRange.Last90Days, today));
        Assert.Null(QuotesWorkspaceRules.WindowStart(QuotesDateRange.Any, today));
    }
}
