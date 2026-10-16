using FieldOps.Application.Features.AuditTrail;
using FieldOps.Domain.Notifications;

namespace FieldOps.Infrastructure.Persistence;

/// <summary>
/// The single visibility predicate of the audit trail (audit-trail BR-09 to BR-11). Every read (list, summary, filters,
/// detail and export) starts from it, in this order: the session organization, the branch scope and, for Accounting, the
/// financial events only.
/// </summary>
internal static class AuditLogVisibility
{
    private const string ReportTypeKey = "reportType";

    public static IQueryable<AuditLog> Apply(IQueryable<AuditLog> query, AuditViewer viewer)
    {
        var organizationId = viewer.OrganizationId;
        query = query.Where(audit => audit.OrganizationId == organizationId);

        // BR-11: all-branches members see every branch and the null-branch events; a limited member only the linked branches.
        if (!viewer.Scope.All)
        {
            var scoped = viewer.Scope.BranchIds.ToArray();
            query = query.Where(audit => audit.BranchId != null && scoped.Contains(audit.BranchId.Value));
        }

        // BR-10: Accounting sees the catalog's financial codes and the exports of financial reports.
        if (AuditAccess.IsFinancialOnly(viewer.RoleCode))
        {
            var financial = AuditCatalog.FinancialCodes;
            var reportTypes = AuditCatalog.FinancialReportTypes;

            query = query.Where(audit =>
                financial.Contains(audit.Action)
                || (audit.Action == AuditCatalog.ExportGenerated
                    && FieldOpsDbContext.PayloadText(audit.Metadata, ReportTypeKey) != null
                    && reportTypes.Contains(FieldOpsDbContext.PayloadText(audit.Metadata, ReportTypeKey)!)));
        }

        return query;
    }
}
