namespace FieldOps.Api.Authorization;

/// <summary>Role-based authorization policies of the work order creation (create-work-order BR-01).</summary>
public static class WorkOrderPolicies
{
    /// <summary><c>owner</c>, <c>operations_manager</c>, <c>dispatcher</c>: the editor, draft, create and checklist templates.</summary>
    public const string Manage = "WorkOrderManage";

    /// <summary>Manage plus <c>viewer</c>: the jobs list and detail.</summary>
    public const string Read = "WorkOrderRead";
}
