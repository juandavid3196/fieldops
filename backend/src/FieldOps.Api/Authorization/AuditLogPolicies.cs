namespace FieldOps.Api.Authorization;

/// <summary>Role-based authorization policy of the audit trail (audit-trail BR-09).</summary>
public static class AuditLogPolicies
{
    /// <summary><c>owner</c>, <c>operations_manager</c>, <c>accounting</c>: every audit-log endpoint.</summary>
    public const string Read = "AuditLogRead";
}
