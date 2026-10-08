namespace FieldOps.Api.Authorization;

/// <summary>Role-based authorization policies of the completed jobs review (completed-jobs-review BR-01).</summary>
public static class BillingReviewPolicies
{
    /// <summary><c>owner</c>, <c>operations_manager</c>, <c>dispatcher</c>, <c>accounting</c>, <c>viewer</c>: queue, detail, evidence, export, options.</summary>
    public const string Read = "BillingReviewRead";

    /// <summary><c>owner</c>, <c>accounting</c>: review update and invoice generation.</summary>
    public const string Act = "BillingReviewAct";
}
