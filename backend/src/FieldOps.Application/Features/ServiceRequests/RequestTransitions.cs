using FieldOps.Domain.Requests;

namespace FieldOps.Application.Features.ServiceRequests;

/// <summary>Operations that can change the status of a request (States and transitions).</summary>
public enum RequestAction
{
    StartReview,
    Assign,
    RequestInformation,
    ScheduleAssessment,
    CancelAssessment,
    MarkReadyForQuote,
    CompleteAssessment,
    QuoteSent,
    ConvertToWorkOrder,
    MoveToReview,
    Cancel,
}

/// <summary>
/// The status transition table. Pure and stateless: every status-changing operation asks it for the
/// target status, so an invalid or concurrent-stale transition is a single decision (FR-06).
/// </summary>
public static class RequestTransitions
{
    private static readonly RequestStatus[] OpenStatuses =
    [
        RequestStatus.New,
        RequestStatus.NeedsReview,
        RequestStatus.AssessmentScheduled,
        RequestStatus.ReadyForQuote,
    ];

    /// <summary>The statuses shown on the board and open to mutations.</summary>
    public static IReadOnlyList<RequestStatus> Open => OpenStatuses;

    public static bool IsOpen(RequestStatus status) => OpenStatuses.Contains(status);

    /// <summary>
    /// The status after the action, which equals <paramref name="from"/> when the action does not move
    /// the request (assign or request information outside <c>new</c>). False when the action is not
    /// allowed from <paramref name="from"/>.
    /// </summary>
    public static bool TryApply(RequestStatus from, RequestAction action, out RequestStatus to)
    {
        to = from;

        switch (action)
        {
            case RequestAction.StartReview when from == RequestStatus.New:
                to = RequestStatus.NeedsReview;
                return true;

            case RequestAction.Assign or RequestAction.RequestInformation when IsOpen(from):
                to = from == RequestStatus.New ? RequestStatus.NeedsReview : from;
                return true;

            case RequestAction.ScheduleAssessment when from is RequestStatus.New or RequestStatus.NeedsReview:
                to = RequestStatus.AssessmentScheduled;
                return true;

            case RequestAction.CancelAssessment when from == RequestStatus.AssessmentScheduled:
                to = RequestStatus.NeedsReview;
                return true;

            case RequestAction.MarkReadyForQuote when from is RequestStatus.New or RequestStatus.NeedsReview:
                to = RequestStatus.ReadyForQuote;
                return true;

            case RequestAction.CompleteAssessment when from == RequestStatus.AssessmentScheduled:
                to = RequestStatus.ReadyForQuote;
                return true;

            case RequestAction.QuoteSent when from == RequestStatus.ReadyForQuote:
                to = RequestStatus.Quoted;
                return true;

            case RequestAction.ConvertToWorkOrder when from == RequestStatus.Quoted:
                to = RequestStatus.Converted;
                return true;

            case RequestAction.MoveToReview when from == RequestStatus.ReadyForQuote:
                to = RequestStatus.NeedsReview;
                return true;

            case RequestAction.Cancel when IsOpen(from):
                to = RequestStatus.Cancelled;
                return true;

            default:
                return false;
        }
    }

    public static string Code(RequestStatus status) => status switch
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

    public static bool TryParseCode(string? code, out RequestStatus status)
    {
        foreach (var candidate in Enum.GetValues<RequestStatus>())
        {
            if (string.Equals(Code(candidate), code, StringComparison.Ordinal))
            {
                status = candidate;
                return true;
            }
        }

        status = default;
        return false;
    }

    /// <summary>Human label of a status for activity entries ("Needs review").</summary>
    public static string Label(RequestStatus status) => status switch
    {
        RequestStatus.New => "New",
        RequestStatus.NeedsReview => "Needs review",
        RequestStatus.AssessmentScheduled => "Assessment scheduled",
        RequestStatus.ReadyForQuote => "Ready for quote",
        RequestStatus.Quoted => "Quoted",
        RequestStatus.Converted => "Converted",
        RequestStatus.Cancelled => "Cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };
}
