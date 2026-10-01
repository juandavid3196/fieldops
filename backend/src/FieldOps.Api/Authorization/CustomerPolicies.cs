namespace FieldOps.Api.Authorization;

/// <summary>Role-based authorization policies of customer management (BR-01).</summary>
public static class CustomerPolicies
{
    /// <summary><c>owner</c>, <c>operations_manager</c>, <c>dispatcher</c>, <c>accounting</c>, <c>viewer</c>: reads.</summary>
    public const string View = "CustomerView";

    /// <summary><c>owner</c>, <c>dispatcher</c>: create, edit, archive, reactivate, duplicate check and tag creation.</summary>
    public const string Manage = "CustomerManage";

    /// <summary><c>owner</c> only: the CSV template, preview and import.</summary>
    public const string Import = "CustomerImport";
}
