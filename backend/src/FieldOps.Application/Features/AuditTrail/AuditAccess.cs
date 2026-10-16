namespace FieldOps.Application.Features.AuditTrail;

/// <summary>Role gate and visibility of the audit trail (audit-trail BR-09, BR-10) and the target module access (BR-25).</summary>
public static class AuditAccess
{
    public const string Owner = "owner";

    public const string OperationsManager = "operations_manager";

    public const string Accounting = "accounting";

    public static readonly string[] ReadRoles = [Owner, OperationsManager, Accounting];

    private static readonly string[] AllInternal = ["owner", "operations_manager", "dispatcher", "accounting", "viewer"];

    private static readonly string[] ScheduleReaders = ["owner", "operations_manager", "dispatcher", "viewer"];

    private static readonly string[] TeamReaders = ["owner", "operations_manager", "dispatcher"];

    private static readonly string[] SettingsReaders = ["owner", "viewer"];

    /// <summary>The module read policy roles of each target kind, mirroring the destination screens.</summary>
    private static readonly Dictionary<string, string[]> KindReaders = new(StringComparer.Ordinal)
    {
        [AuditTargetKinds.ServiceRequest] = AllInternal,
        [AuditTargetKinds.Quote] = AllInternal,
        [AuditTargetKinds.WorkOrder] = ScheduleReaders,
        [AuditTargetKinds.Customer] = AllInternal,
        [AuditTargetKinds.Invoice] = AllInternal,
        [AuditTargetKinds.CatalogItem] = AllInternal,
        [AuditTargetKinds.TechnicianProfile] = TeamReaders,
        [AuditTargetKinds.OrganizationUser] = SettingsReaders,
        [AuditTargetKinds.UserInvitation] = SettingsReaders,
        [AuditTargetKinds.Organization] = SettingsReaders,
        [AuditTargetKinds.Branch] = SettingsReaders,
    };

    public static bool CanRead(string roleCode) => ReadRoles.Contains(roleCode, StringComparer.Ordinal);

    public static bool IsFinancialOnly(string roleCode) => string.Equals(roleCode, Accounting, StringComparison.Ordinal);

    /// <summary>
    /// Whether the role may filter by a catalog action code. Accounting sees financial codes, and <c>export.generated</c>
    /// only through its metadata (the row level rule lives in the visibility predicate).
    /// </summary>
    public static bool CanSee(string roleCode, string code) =>
        AuditCatalog.TryGet(code, out var entry)
        && (!IsFinancialOnly(roleCode) || entry.IsFinancial || string.Equals(code, AuditCatalog.ExportGenerated, StringComparison.Ordinal));

    public static IReadOnlyList<AuditCatalogEntry> VisibleEntries(string roleCode) =>
        [.. AuditCatalog.All.Where(entry => CanSee(roleCode, entry.Code))];

    public static bool CanOpenKind(string roleCode, string kind) =>
        KindReaders.TryGetValue(kind, out var roles) && roles.Contains(roleCode, StringComparer.Ordinal);
}

/// <summary>The target kinds of BR-25. A visit targets its work order and a payment its invoice.</summary>
public static class AuditTargetKinds
{
    public const string ServiceRequest = "service_request";

    public const string Quote = "quote";

    public const string WorkOrder = "work_order";

    public const string Customer = "customer";

    public const string Invoice = "invoice";

    public const string CatalogItem = "catalog_item";

    public const string TechnicianProfile = "technician_profile";

    public const string OrganizationUser = "organization_user";

    public const string UserInvitation = "user_invitation";

    public const string Organization = "organization";

    public const string Branch = "branch";

    /// <summary>The kind an entity type opens, or null when its entity has no destination.</summary>
    public static string? ForEntityType(string entityType) =>
        entityType switch
        {
            "visit" => WorkOrder,
            "payment" => Invoice,
            ServiceRequest or Quote or WorkOrder or Customer or Invoice or CatalogItem or TechnicianProfile
                or OrganizationUser or UserInvitation or Organization or Branch => entityType,
            _ => null,
        };
}
