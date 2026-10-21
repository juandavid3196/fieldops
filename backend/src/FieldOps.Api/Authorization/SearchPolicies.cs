namespace FieldOps.Api.Authorization;

/// <summary>Role-based authorization policy of the global search (global-search BR-01).</summary>
public static class SearchPolicies
{
    /// <summary><c>owner</c>, <c>operations_manager</c>, <c>dispatcher</c>, <c>accounting</c> and <c>viewer</c>; technicians and unknown roles get 403.</summary>
    public const string Global = "GlobalSearch";
}
