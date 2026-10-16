using FieldOps.Domain.Quotes;
using FieldOps.Domain.WorkOrders;

namespace FieldOps.Application.Features.Quotes;

public enum QuotesTab
{
    All,
    Drafts,
    Sent,
    Approved,
    Closed,
}

public enum QuotesSort
{
    Updated,
    Newest,
    Oldest,
    Total,
    Expiring,
}

public enum QuotesDateRange
{
    Any,
    Today,
    Last7Days,
    Last30Days,
    Last90Days,
}

/// <summary>The display status of a quote (quotes-management BR-03), derived in the database from the stored status and the shown version.</summary>
public enum QuoteDisplayStatus
{
    Draft,
    AwaitingResponse,
    ClarificationRequested,
    Expired,
    Approved,
    Rejected,
    Cancelled,
}

public static class QuoteRowActions
{
    public const string View = "view";

    public const string ContinueDraft = "continue_draft";

    public const string ContinueRevision = "continue_revision";

    public const string Revise = "revise";

    public const string CreateWorkOrder = "create_work_order";

    public const string ContinueWorkOrder = "continue_work_order";

    public const string ViewJob = "view_job";

    public const string OpenRequest = "open_request";
}

/// <summary>Pure rules of the quotes workspace: tabs, filters, sorting, display codes and row actions.</summary>
public static class QuotesWorkspaceRules
{
    public const int SearchMaxLength = 100;

    /// <summary>Needs attention window (BR-11): today through today + 7 days, inclusive.</summary>
    public const int AttentionDays = 7;

    private static readonly QuoteDisplayStatus[] AllStatuses = Enum.GetValues<QuoteDisplayStatus>();

    public static bool TryParseTab(string? value, out QuotesTab tab)
    {
        tab = QuotesTab.All;

        switch (value)
        {
            case null or "" or "all":
                return true;
            case "drafts":
                tab = QuotesTab.Drafts;

                return true;
            case "sent":
                tab = QuotesTab.Sent;

                return true;
            case "approved":
                tab = QuotesTab.Approved;

                return true;
            case "closed":
                tab = QuotesTab.Closed;

                return true;
            default:
                return false;
        }
    }

    public static bool TryParseSort(string? value, out QuotesSort sort)
    {
        sort = QuotesSort.Updated;

        switch (value)
        {
            case null or "" or "updated":
                return true;
            case "newest":
                sort = QuotesSort.Newest;

                return true;
            case "oldest":
                sort = QuotesSort.Oldest;

                return true;
            case "total":
                sort = QuotesSort.Total;

                return true;
            case "expiring":
                sort = QuotesSort.Expiring;

                return true;
            default:
                return false;
        }
    }

    public static bool TryParseDate(string? value, out QuotesDateRange range)
    {
        range = QuotesDateRange.Any;

        switch (value)
        {
            case null or "" or "any":
                return true;
            case "today":
                range = QuotesDateRange.Today;

                return true;
            case "7d":
                range = QuotesDateRange.Last7Days;

                return true;
            case "30d":
                range = QuotesDateRange.Last30Days;

                return true;
            case "90d":
                range = QuotesDateRange.Last90Days;

                return true;
            default:
                return false;
        }
    }

    /// <summary>BR-07: a window of N days covers today and the previous N - 1 local days.</summary>
    public static DateOnly? WindowStart(QuotesDateRange range, DateOnly today) => range switch
    {
        QuotesDateRange.Today => today,
        QuotesDateRange.Last7Days => today.AddDays(-6),
        QuotesDateRange.Last30Days => today.AddDays(-29),
        QuotesDateRange.Last90Days => today.AddDays(-89),
        _ => null,
    };

    /// <summary>BR-05: the display statuses of a tab.</summary>
    public static QuoteDisplayStatus[] TabStatuses(QuotesTab tab) => tab switch
    {
        QuotesTab.Drafts => [QuoteDisplayStatus.Draft],
        QuotesTab.Sent => [QuoteDisplayStatus.AwaitingResponse, QuoteDisplayStatus.ClarificationRequested],
        QuotesTab.Approved => [QuoteDisplayStatus.Approved],
        QuotesTab.Closed => [QuoteDisplayStatus.Rejected, QuoteDisplayStatus.Expired, QuoteDisplayStatus.Cancelled],
        _ => AllStatuses,
    };

    public static string StatusCode(QuoteDisplayStatus status) => status switch
    {
        QuoteDisplayStatus.Draft => "draft",
        QuoteDisplayStatus.AwaitingResponse => "awaiting_response",
        QuoteDisplayStatus.ClarificationRequested => "clarification_requested",
        QuoteDisplayStatus.Expired => "expired",
        QuoteDisplayStatus.Approved => "approved",
        QuoteDisplayStatus.Rejected => "rejected",
        _ => "cancelled",
    };

    /// <summary>BR-03 in memory (the workspace derives the same value in SQL): used by the reports workspace.</summary>
    public static QuoteDisplayStatus Display(QuoteStatus status, int currentVersionNo, DateOnly? shownValidUntil, DateOnly today)
    {
        if (status == QuoteStatus.Cancelled)
        {
            return QuoteDisplayStatus.Cancelled;
        }

        if (currentVersionNo == 0)
        {
            return QuoteDisplayStatus.Draft;
        }

        return status switch
        {
            QuoteStatus.Approved => QuoteDisplayStatus.Approved,
            QuoteStatus.Rejected => QuoteDisplayStatus.Rejected,
            QuoteStatus.Expired => QuoteDisplayStatus.Expired,
            QuoteStatus.Sent or QuoteStatus.ClarificationRequested =>
                shownValidUntil is { } validUntil && validUntil < today
                    ? QuoteDisplayStatus.Expired
                    : status == QuoteStatus.Sent ? QuoteDisplayStatus.AwaitingResponse : QuoteDisplayStatus.ClarificationRequested,
            _ => QuoteDisplayStatus.Draft,
        };
    }

    public static string StatusLabel(QuoteDisplayStatus status) => status switch
    {
        QuoteDisplayStatus.Draft => "Draft",
        QuoteDisplayStatus.AwaitingResponse => "Awaiting response",
        QuoteDisplayStatus.ClarificationRequested => "Clarification requested",
        QuoteDisplayStatus.Expired => "Expired",
        QuoteDisplayStatus.Approved => "Approved",
        QuoteDisplayStatus.Rejected => "Rejected",
        _ => "Cancelled",
    };

    /// <summary>BR-06: a blank status means no filter; a code outside the tab's set is invalid.</summary>
    public static bool TryParseStatus(string? value, QuotesTab tab, out QuoteDisplayStatus? status)
    {
        status = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        foreach (var candidate in TabStatuses(tab))
        {
            if (string.Equals(StatusCode(candidate), value, StringComparison.Ordinal))
            {
                status = candidate;

                return true;
            }
        }

        return false;
    }

    /// <summary>BR-04: the number of the revision shown, at least 1.</summary>
    public static int RevisionNo(int currentVersionNo) => Math.Max(currentVersionNo, 1);

    /// <summary>BR-04: a mutable version exists next to an already sent one.</summary>
    public static bool RevisionInProgress(bool hasMutableVersion, int currentVersionNo) =>
        hasMutableVersion && currentVersionNo >= 1;

    /// <summary>BR-15: the row actions in contract order. Eligibility only; the frontend builds the targets.</summary>
    public static IReadOnlyList<string> Actions(
        QuoteDisplayStatus display,
        QuoteStatus storedStatus,
        bool hasMutableVersion,
        int currentVersionNo,
        bool hasWorkOrder,
        WorkOrderStatus? workOrderStatus,
        bool canManage,
        bool canManageWorkOrders,
        bool canReadWorkOrders)
    {
        var actions = new List<string> { QuoteRowActions.View };

        if (canManage && display == QuoteDisplayStatus.Draft)
        {
            actions.Add(QuoteRowActions.ContinueDraft);
        }

        if (canManage && RevisionInProgress(hasMutableVersion, currentVersionNo))
        {
            actions.Add(QuoteRowActions.ContinueRevision);
        }

        if (canManage
            && !hasMutableVersion
            && storedStatus is QuoteStatus.Sent or QuoteStatus.ClarificationRequested or QuoteStatus.Rejected or QuoteStatus.Expired)
        {
            actions.Add(QuoteRowActions.Revise);
        }

        if (canManageWorkOrders && display == QuoteDisplayStatus.Approved)
        {
            if (!hasWorkOrder)
            {
                actions.Add(QuoteRowActions.CreateWorkOrder);
            }
            else if (workOrderStatus == WorkOrderStatus.Draft)
            {
                actions.Add(QuoteRowActions.ContinueWorkOrder);
            }
        }

        if (canReadWorkOrders && hasWorkOrder && workOrderStatus is { } status && status != WorkOrderStatus.Draft)
        {
            actions.Add(QuoteRowActions.ViewJob);
        }

        actions.Add(QuoteRowActions.OpenRequest);

        return actions;
    }
}
