namespace FieldOps.Api.Authorization;

/// <summary>Role-based authorization policies of team management (BR-01).</summary>
public static class TeamPolicies
{
    /// <summary><c>owner</c>, <c>operations_manager</c>, <c>dispatcher</c>: reads.</summary>
    public const string View = "TeamView";

    /// <summary><c>owner</c>, <c>operations_manager</c>: mutations and linkable accounts.</summary>
    public const string Manage = "TeamManage";

    /// <summary><c>owner</c>, <c>operations_manager</c>, <c>dispatcher</c>, <c>technician</c>: the skills and availability page read.</summary>
    public const string SkillsView = "TeamSkillsView";

    /// <summary><c>technician</c>: the own profile (<c>GET /team/me</c>).</summary>
    public const string Self = "TeamSelf";
}
