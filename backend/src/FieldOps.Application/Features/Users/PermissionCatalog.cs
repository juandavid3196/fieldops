namespace FieldOps.Application.Features.Users;

public sealed record RoleDefinition(
    string Code,
    string Name,
    string Summary,
    bool ForcesAllBranches,
    bool HasTeamProfile);

public sealed record PermissionLevel(string Level, string Label);

public sealed record ModuleDefinition(
    string Key,
    string Name,
    IReadOnlyDictionary<string, PermissionLevel> Levels);

/// <summary>
/// Read-only, display-only role and module catalog (BR-06, BR-07, BR-13).
/// Enforcement stays on the role-code policies; nothing here grants access.
/// </summary>
public static class PermissionCatalog
{
    public const string Owner = "owner";

    public const string OperationsManager = "operations_manager";

    public const string Dispatcher = "dispatcher";

    public const string Technician = "technician";

    public const string Accounting = "accounting";

    public const string Viewer = "viewer";

    public static IReadOnlyList<RoleDefinition> Roles { get; } =
    [
        new(
            Owner,
            "Owner",
            "Full access to every module, company settings, users and the audit log.",
            ForcesAllBranches: true,
            HasTeamProfile: false),
        new(
            OperationsManager,
            "Operations Manager",
            "Can view most modules and edit work orders, scheduling and team in all branches. Cannot change company settings.",
            ForcesAllBranches: true,
            HasTeamProfile: true),
        new(
            Dispatcher,
            "Dispatcher",
            "Can manage customers, requests, quotes, work orders, scheduling, and technician assignment in assigned branches. Cannot change company settings or view the audit log.",
            ForcesAllBranches: false,
            HasTeamProfile: true),
        new(
            Technician,
            "Technician",
            "Can see assigned work orders and visits and their own team profile. No access to invoices, reports or company settings.",
            ForcesAllBranches: false,
            HasTeamProfile: true),
        new(
            Accounting,
            "Accounting",
            "Can manage invoices and payments and view completed work and financial reports. Cannot schedule work or change company settings.",
            ForcesAllBranches: false,
            HasTeamProfile: false),
        new(
            Viewer,
            "Viewer",
            "Read-only access to modules explicitly granted. Company settings and audit log stay unavailable.",
            ForcesAllBranches: false,
            HasTeamProfile: false),
    ];

    public static IReadOnlyList<ModuleDefinition> Modules { get; } =
    [
        Module("overview", "Overview", "full", "view", "view", "none", "view", "view_if_granted"),
        Module("customers", "Customers", "full", "view", "edit", "assigned_only", "view", "view_if_granted"),
        Module("requests_quotes", "Requests & quotes", "full", "view", "edit", "none", "view", "view_if_granted"),
        Module(
            "work_orders_schedule",
            "Work orders & schedule",
            "full",
            "edit",
            "edit",
            "assigned_only",
            "completed_only",
            "view_if_granted"),
        Module("invoices_payments", "Invoices & payments", "full", "view", "view", "none", "edit", "view_if_granted"),
        Module("products_services", "Products & services", "full", "edit", "view", "none", "view", "view"),
        Module("reports", "Reports", "full", "view", "view", "none", "financial_only", "view_if_granted"),
        Module("team", "Team", "full", "edit", "view", "own_profile", "none", "none"),
        Module("company_settings", "Company settings", "full", "none", "none", "none", "none", "none"),
        Module("audit_log", "Audit log", "full", "view", "none", "none", "financial_only", "none"),
    ];

    public static bool IsRoleCode(string? code) =>
        Roles.Any(role => string.Equals(role.Code, code, StringComparison.Ordinal));

    public static RoleDefinition? Find(string? code) =>
        Roles.FirstOrDefault(role => string.Equals(role.Code, code, StringComparison.Ordinal));

    public static string LevelLabel(string level) =>
        level switch
        {
            "full" => "Full",
            "edit" => "Edit",
            "view" => "View",
            "assigned_only" => "Assigned only",
            "completed_only" => "Completed only",
            "financial_only" => "Financial only",
            "own_profile" => "Own profile",
            "view_if_granted" => "View if granted",
            "none" => "None",
            _ => throw new ArgumentOutOfRangeException(nameof(level)),
        };

    // Levels are given in BR-06 role order.
    private static ModuleDefinition Module(string key, string name, params string[] levels)
    {
        var levelsByRole = new Dictionary<string, PermissionLevel>(StringComparer.Ordinal);

        for (var i = 0; i < Roles.Count; i++)
        {
            levelsByRole[Roles[i].Code] = new PermissionLevel(levels[i], LevelLabel(levels[i]));
        }

        return new ModuleDefinition(key, name, levelsByRole);
    }
}
