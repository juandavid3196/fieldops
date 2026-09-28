namespace FieldOps.Api.Authorization;

/// <summary>Role-based authorization policies for this spec's endpoints (BR-10).</summary>
public static class CompanySettingsPolicies
{
    /// <summary><c>owner</c> or <c>viewer</c>: the GET endpoints.</summary>
    public const string View = "CompanySettingsView";

    /// <summary><c>owner</c> only: every mutating endpoint.</summary>
    public const string Manage = "CompanySettingsManage";
}
