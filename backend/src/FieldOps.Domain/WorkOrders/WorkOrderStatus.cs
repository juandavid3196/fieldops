namespace FieldOps.Domain.WorkOrders;

public enum WorkOrderStatus
{
    Draft,
    ReadyToSchedule,
    Scheduled,
    InProgress,
    Completed,
    ApprovedForBilling,
    Cancelled,
}
