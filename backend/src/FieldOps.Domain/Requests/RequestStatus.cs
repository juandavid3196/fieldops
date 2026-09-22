namespace FieldOps.Domain.Requests;

public enum RequestStatus
{
    New,
    NeedsReview,
    AssessmentScheduled,
    ReadyForQuote,
    Quoted,
    Converted,
    Cancelled,
}
