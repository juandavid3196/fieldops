using FieldOps.Domain.Requests;
using FieldOps.Domain.WorkOrders;

namespace FieldOps.Application.Features.PortalDashboard;

public sealed record QuoteCandidate(Guid Id, string Status, DateOnly? ValidUntil, DateTimeOffset SentAt);

public sealed record InvoiceCandidate(Guid Id, string Status, decimal BalanceDue, DateOnly? DueDate, long Number);

public sealed record VisitCandidate(Guid VisitId, VisitStatus Status, DateTimeOffset? ScheduledStart);

public sealed record ActivityFacts(
    Guid WorkOrderId,
    string Title,
    DateOnly CompletedOn,
    string WorkOrderNumber,
    Guid? InvoiceId,
    string? InvoiceStatus,
    string? InvoiceNumber,
    decimal? InvoiceTotal,
    string? InvoiceCurrency,
    Guid? ReceiptPaymentId);

/// <summary>Selection rules of the dashboard cards (customer portal BR-20 … BR-23, BR-26), as pure functions.</summary>
public static class PortalSelectionRules
{
    private static readonly HashSet<string> QuoteStatuses = new(StringComparer.Ordinal) { "sent", "clarification_requested" };

    private static readonly HashSet<string> DueStatuses = new(StringComparer.Ordinal) { "sent", "partially_paid", "overdue" };

    /// <summary>
    /// BR-20: quotes awaiting an answer whose valid-until day has not passed (<paramref name="today"/> is the organization
    /// day). The earliest valid-until (none last), then the newest sent time; the count of the others.
    /// </summary>
    public static (QuoteCandidate? Pick, int MoreCount) PickActionQuote(IEnumerable<QuoteCandidate> candidates, DateOnly today)
    {
        var ordered = candidates
            .Where(candidate => QuoteStatuses.Contains(candidate.Status) && (candidate.ValidUntil is null || candidate.ValidUntil >= today))
            .OrderBy(candidate => candidate.ValidUntil is null)
            .ThenBy(candidate => candidate.ValidUntil)
            .ThenByDescending(candidate => candidate.SentAt)
            .ThenBy(candidate => candidate.Id)
            .ToList();

        return (ordered.FirstOrDefault(), Math.Max(0, ordered.Count - 1));
    }

    /// <summary>BR-21: invoices with a balance in sent, partially paid or overdue; earliest due date (none last), then the lowest number.</summary>
    public static (InvoiceCandidate? Pick, int MoreCount) PickPaymentDue(IEnumerable<InvoiceCandidate> candidates)
    {
        var ordered = candidates
            .Where(candidate => DueStatuses.Contains(candidate.Status) && candidate.BalanceDue > 0m)
            .OrderBy(candidate => candidate.DueDate is null)
            .ThenBy(candidate => candidate.DueDate)
            .ThenBy(candidate => candidate.Number)
            .ToList();

        return (ordered.FirstOrDefault(), Math.Max(0, ordered.Count - 1));
    }

    /// <summary>BR-21: the date is overdue when the status says so or the due day is before today.</summary>
    public static bool IsOverdue(string status, DateOnly? dueDate, DateOnly today) =>
        status == "overdue" || (dueDate is { } due && due < today);

    /// <summary>BR-22: visits in progress first, then the earliest start from the start of today (organization time).</summary>
    public static VisitCandidate? PickUpcoming(IEnumerable<VisitCandidate> candidates, DateTimeOffset startOfToday)
    {
        var scheduled = candidates
            .Where(candidate => candidate.ScheduledStart is not null && PortalVisitRules.IsUpcomingStatus(candidate.Status))
            .ToList();

        return scheduled
            .Where(candidate => candidate.Status is VisitStatus.OnTheWay or VisitStatus.InProgress or VisitStatus.Paused)
            .OrderBy(candidate => candidate.ScheduledStart)
            .ThenBy(candidate => candidate.VisitId)
            .FirstOrDefault()
            ?? scheduled
                .Where(candidate => candidate.ScheduledStart >= startOfToday)
                .OrderBy(candidate => candidate.ScheduledStart)
                .ThenBy(candidate => candidate.VisitId)
                .FirstOrDefault();
    }

    /// <summary>BR-26: the activity row of a completed work order and its non-void, non-draft invoice.</summary>
    public static PortalActivityRow ToActivityRow(ActivityFacts facts, string defaultCurrency)
    {
        switch (facts.InvoiceStatus)
        {
            case "paid":
                return new PortalActivityRow(
                    facts.WorkOrderId, facts.Title, facts.CompletedOn, facts.WorkOrderNumber, facts.InvoiceTotal,
                    facts.InvoiceCurrency ?? defaultCurrency, "paid", facts.InvoiceId, facts.ReceiptPaymentId);
            case "sent" or "partially_paid" or "overdue":
                return new PortalActivityRow(
                    facts.WorkOrderId, facts.Title, facts.CompletedOn, facts.InvoiceNumber ?? facts.WorkOrderNumber, facts.InvoiceTotal,
                    facts.InvoiceCurrency ?? defaultCurrency, "invoice_due", facts.InvoiceId, null);
            default:
                return new PortalActivityRow(
                    facts.WorkOrderId, facts.Title, facts.CompletedOn, facts.WorkOrderNumber, null, defaultCurrency, "completed", null, null);
        }
    }
}

/// <summary>Visit labels and eligibility (customer portal BR-22, BR-29, BR-32, BR-38).</summary>
public static class PortalVisitRules
{
    public const string Scheduled = "scheduled";

    public const string OnTheWay = "on_the_way";

    public const string InProgress = "in_progress";

    public const string Completed = "completed";

    /// <summary>BR-38: the stepper position of a visit status.</summary>
    public static string ProgressStep(VisitStatus status) => status switch
    {
        VisitStatus.OnTheWay => OnTheWay,
        VisitStatus.InProgress or VisitStatus.Paused => InProgress,
        VisitStatus.Completed or VisitStatus.NeedsCorrection or VisitStatus.Approved => Completed,
        _ => Scheduled,
    };

    public static string StatusCode(VisitStatus status) => status switch
    {
        VisitStatus.Unscheduled => "unscheduled",
        VisitStatus.Scheduled => "scheduled",
        VisitStatus.Assigned => "assigned",
        VisitStatus.OnTheWay => "on_the_way",
        VisitStatus.InProgress => "in_progress",
        VisitStatus.Paused => "paused",
        VisitStatus.Completed => "completed",
        VisitStatus.NeedsCorrection => "needs_correction",
        VisitStatus.Approved => "approved",
        VisitStatus.Cancelled => "cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    /// <summary>BR-22: the statuses of the Upcoming tab.</summary>
    public static bool IsUpcomingStatus(VisitStatus status) =>
        status is VisitStatus.Scheduled or VisitStatus.Assigned or VisitStatus.OnTheWay or VisitStatus.InProgress or VisitStatus.Paused;

    /// <summary>BR-29: the statuses of the Past tab (with a scheduled start).</summary>
    public static bool IsPastStatus(VisitStatus status) =>
        status is VisitStatus.Completed or VisitStatus.NeedsCorrection or VisitStatus.Approved or VisitStatus.Cancelled;

    public static bool IsCompletedStatus(VisitStatus status) =>
        status is VisitStatus.Completed or VisitStatus.NeedsCorrection or VisitStatus.Approved;

    /// <summary>BR-32: scheduled or assigned, starting in the future, with no pending request.</summary>
    public static bool CanRequestReschedule(VisitStatus status, DateTimeOffset? scheduledStart, DateTimeOffset now, bool hasPendingRequest) =>
        status is VisitStatus.Scheduled or VisitStatus.Assigned
        && scheduledStart is { } start
        && start > now
        && !hasPendingRequest;

    /// <summary>BR-32: a request is pending while the visit still starts at the time the request recorded.</summary>
    public static bool IsPending(DateTimeOffset originalScheduledStart, DateTimeOffset? currentScheduledStart) =>
        currentScheduledStart is { } current && current == originalScheduledStart;
}

/// <summary>Request labels (customer portal BR-23, BR-39).</summary>
public static class PortalRequestRules
{
    public static readonly RequestStatus[] ActiveStatuses =
    [
        RequestStatus.New,
        RequestStatus.NeedsReview,
        RequestStatus.AssessmentScheduled,
        RequestStatus.ReadyForQuote,
        RequestStatus.Quoted,
    ];

    public static string StatusCode(RequestStatus status) => status switch
    {
        RequestStatus.New => "new",
        RequestStatus.NeedsReview => "needs_review",
        RequestStatus.AssessmentScheduled => "assessment_scheduled",
        RequestStatus.ReadyForQuote => "ready_for_quote",
        RequestStatus.Quoted => "quoted",
        RequestStatus.Converted => "converted",
        RequestStatus.Cancelled => "cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    /// <summary>BR-39: the customer-facing status label.</summary>
    public static string StatusLabel(RequestStatus status) => status switch
    {
        RequestStatus.New => "Submitted",
        RequestStatus.NeedsReview => "Under review",
        RequestStatus.AssessmentScheduled => "Assessment scheduled",
        RequestStatus.ReadyForQuote => "Preparing quote",
        RequestStatus.Quoted => "Quote ready",
        RequestStatus.Converted => "Job created",
        RequestStatus.Cancelled => "Cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };
}
