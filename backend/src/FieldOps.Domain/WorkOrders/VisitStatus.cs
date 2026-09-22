namespace FieldOps.Domain.WorkOrders;

public enum VisitStatus
{
    Unscheduled,
    Scheduled,
    Assigned,
    OnTheWay,
    InProgress,
    Paused,
    Completed,
    NeedsCorrection,
    Approved,
    Cancelled,
}
