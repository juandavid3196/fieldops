namespace FieldOps.Api.Authorization;

/// <summary>Role-based authorization policies of the dispatch calendar (dispatch-calendar BR-01).</summary>
public static class DispatchPolicies
{
    /// <summary><c>owner</c>, <c>operations_manager</c>, <c>dispatcher</c>: evaluation and dispatch.</summary>
    public const string Manage = "DispatchManage";

    /// <summary>Manage plus <c>viewer</c>: options, calendar, unscheduled list and visit detail.</summary>
    public const string Read = "DispatchRead";
}
