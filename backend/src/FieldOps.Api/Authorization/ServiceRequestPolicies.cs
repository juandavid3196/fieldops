namespace FieldOps.Api.Authorization;

/// <summary>Role-based authorization policies of the requests pipeline (BR-01).</summary>
public static class ServiceRequestPolicies
{
    /// <summary><c>owner</c>, <c>operations_manager</c>, <c>dispatcher</c>, <c>accounting</c>, <c>viewer</c>: pipeline, metrics, detail, options and attachment download.</summary>
    public const string View = "ServiceRequestView";

    /// <summary><c>owner</c>, <c>dispatcher</c>: every mutation and the customer options.</summary>
    public const string Manage = "ServiceRequestManage";
}
