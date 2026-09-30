namespace FieldOps.Api.Authorization;

/// <summary>Role-based authorization policies of the products and services catalog (BR-01).</summary>
public static class CatalogPolicies
{
    /// <summary><c>owner</c>, <c>operations_manager</c>, <c>dispatcher</c>, <c>accounting</c>, <c>viewer</c>: reads and export.</summary>
    public const string View = "CatalogView";

    /// <summary><c>owner</c>, <c>operations_manager</c>: create, update, activate, deactivate and image changes.</summary>
    public const string Manage = "CatalogManage";

    /// <summary><c>owner</c> only: the CSV template and import.</summary>
    public const string Import = "CatalogImport";
}
