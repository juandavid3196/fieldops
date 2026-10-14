namespace FieldOps.Api.Authorization;

/// <summary>Role-based authorization policy of the business overview (business-overview BR-02).</summary>
public static class BusinessOverviewPolicies
{
    /// <summary><c>owner</c> only: options, overview and export.</summary>
    public const string Read = "BusinessOverviewRead";
}
