using System.Net;
using FieldOps.Application.Features.Access;
using FieldOps.Application.Features.TechnicianVisits;

namespace FieldOps.Application.Features.BillingReview;

/// <summary>Machine codes of the completed jobs review (completed-jobs-review BR-03 to BR-21).</summary>
public static class BillingCodes
{
    public const string LaborNone = "none";

    public const string LaborOver = "over";

    public const string LaborUnder = "under";

    public const string StatusMatches = "matches";

    public const string StatusOver = "over";

    public const string StatusUnder = "under";

    public const string StatusNotInQuote = "not_in_quote";

    public const string StatusNotTracked = "not_tracked";

    public const string StatusSeeLaborTotal = "see_labor_total";

    public const string KindQuote = "quote";

    public const string KindLaborTotal = "labor_total";

    public const string KindNotInQuote = "not_in_quote";

    public const string TaxTaxable = "taxable";

    public const string TaxNonTaxable = "non_taxable";

    public const string CheckChecklist = "checklist";

    public const string CheckPhotos = "photos";

    public const string CheckAcknowledgment = "acknowledgment";

    public const string CheckMaterials = "materials";

    public const string CheckLabor = "labor";

    public const string CheckSummary = "summary";

    public const string WorkOrderStatusInvalid = "work_order_status_invalid";

    public const string CompletionRequirementsUnmet = "completion_requirements_unmet";

    public const string VarianceConfirmationRequired = "variance_confirmation_required";

    public const string InvoiceTotalsMismatch = "invoice_totals_mismatch";

    public const string ExportTooLarge = "export_too_large";

    public const string ReasonManual = "manual";

    public const string ReasonReturnedToQueue = "returned_to_queue";

    public const string ReasonRequirementsUnmet = "requirements_unmet";

    public static readonly string[] CompletedRanges = ["7d", "30d", "90d", "all"];

    public static readonly string[] VarianceFilters = ["all", "none", "any", "labor", "material"];

    public static readonly string[] Tabs = ["all", "variances", "ready"];
}

/// <summary>Messages and limits of the completed jobs review.</summary>
public static class BillingMessages
{
    public const int PageSize = 20;

    public const int MaxSearchLength = 100;

    public const int MaxNoteLength = 500;

    public const int MaxExportRows = 5000;

    public const int MaxAuditRows = 50;

    public const int IssueDateWindowDays = 30;

    public const string BranchInvalid = "Select a valid branch.";

    public const string CompletedInvalid = "Select a valid completion date range.";

    public const string TechnicianInvalid = "Select a valid technician.";

    public const string VarianceInvalid = "Select a valid variance status.";

    public const string SearchTooLong = "Use 100 characters or fewer.";

    public const string TabInvalid = "Select a valid tab.";

    public const string PageInvalid = "Use a page number of 1 or more.";

    public const string NoteTooLong = "Use 500 characters or fewer.";

    public const string ReviewEmpty = "Send a note or a follow-up change.";

    public const string ReasonInvalid = "Select a valid reason.";

    public const string IssueDateInvalid = "Choose an issue date within the last 30 days.";

    public const string PaymentTermsInvalid = "Select payment terms.";

    public const string AcknowledgeRequired = "Confirm the variances to generate this invoice.";

    public const string WorkOrderStatusInvalidTitle = "This job is no longer ready for invoicing.";

    public const string RequirementsUnmetTitle =
        "Complete the required closing items before generating an invoice. This job was marked for follow-up.";

    public const string VarianceConfirmationTitle = "Confirm the variances to generate this invoice.";

    public const string TotalsMismatchTitle =
        "The approved quote amounts don't match its lines. Review the quote before invoicing.";

    public const string ExportTooLargeTitle = "Narrow the filters to export 5,000 jobs or fewer.";
}

public sealed record NamedOption(Guid Id, string Name);

public sealed record BillingOptionsView(
    string Timezone, string Currency, IReadOnlyList<NamedOption> Branches, IReadOnlyList<NamedOption> Technicians, bool CanAct);

public sealed record BillingQueueItem(
    Guid WorkOrderId,
    string Number,
    string Title,
    string CustomerName,
    decimal ApprovedTotal,
    string Currency,
    DateTimeOffset CompletedAt,
    string LaborVariance,
    bool MaterialVariance,
    bool Ready,
    bool FollowUp);

public sealed record BillingQueueTabs(int All, int Variances, int Ready);

public sealed record BillingQueueMetrics(int NeedsReview, int WithVariances, int ReadyToInvoice, decimal CompletedValue, string Currency);

public sealed record BillingQueuePage(
    IReadOnlyList<BillingQueueItem> Items, int Page, int PageSize, int Total, BillingQueueTabs Tabs, BillingQueueMetrics Metrics);

/// <summary>Raw queue query values before parsing (BR-04, BR-05).</summary>
public sealed record BillingQueryText(
    string? BranchId, string? Completed, string? TechnicianId, string? Variance, string? Search, string? Tab, string? Page);

public sealed record BillingQuery(
    Guid? BranchId, string Completed, Guid? TechnicianId, string Variance, string? Search, string Tab, int Page);

public sealed record BillingExportRow(
    string Number,
    string Customer,
    string Job,
    string Branch,
    string Technician,
    DateTimeOffset CompletedAt,
    decimal ApprovedTotal,
    string Currency,
    string LaborVariance,
    bool MaterialVariance,
    bool Ready,
    bool FollowUp);

public sealed record BillingExport(string Timezone, DateOnly OrganizationDate, IReadOnlyList<BillingExportRow> Rows);

public sealed record BillingCsvFile(string FileName, byte[] Content);

public sealed record BillingLine(
    int? Index,
    string Kind,
    string Name,
    string? Approved,
    string? Actual,
    string? Billable,
    string? Tax,
    decimal? Amount,
    string Status);

public sealed record VerificationItem(string Key, bool Met, bool Mandatory, string Label);

public sealed record BillingHeader(
    Guid WorkOrderId,
    string Number,
    string Title,
    string QuoteNumber,
    string CustomerName,
    string PropertyAddress,
    string? CompletedByName,
    DateTimeOffset CompletedAt,
    string Timezone);

public sealed record BillingTotals(
    decimal ApprovedSubtotal,
    decimal InvoiceSubtotal,
    decimal DiscountTotal,
    string TaxLabel,
    decimal TaxTotal,
    decimal InvoiceTotal,
    decimal Variance,
    string Currency);

public sealed record EvidenceThumbnail(Guid Id, string Type);

public sealed record EvidenceSummary(int Count, IReadOnlyList<EvidenceThumbnail> Thumbnails);

public sealed record WorkChecklistItem(string Label, bool Required, bool Completed);

public sealed record WorkMaterial(string Description, string Quantity, string Unit, string Origin);

public sealed record WorkEvidenceItem(Guid Id, string Type, string? Caption);

public sealed record WorkAcknowledgment(
    string Method, string? SignerName, string? Relationship, string? Comment, bool SignatureCaptured);

public sealed record WorkEvidenceVisit(
    Guid VisitId,
    int VisitNumber,
    IReadOnlyList<WorkChecklistItem> Checklist,
    IReadOnlyList<WorkMaterial> Materials,
    IReadOnlyList<WorkEvidenceItem> Evidence,
    string? CompletionSummary,
    WorkAcknowledgment? Acknowledgment);

public sealed record AuditEntryView(string Label, string ActorName, DateTimeOffset OccurredAt);

public sealed record InvoiceDefaultsView(
    string NumberPreview, DateOnly IssueDate, string PaymentTerms, DateOnly DueDate, string TaxLabel, string Currency);

public sealed record FollowUpView(DateTimeOffset At, string ByName);

public sealed record BillingReviewDetail(
    BillingHeader Header,
    IReadOnlyList<BillingLine> Lines,
    string LaborVariance,
    bool MaterialVariance,
    BillingTotals Totals,
    IReadOnlyList<VerificationItem> Verification,
    bool Ready,
    EvidenceSummary Evidence,
    IReadOnlyList<WorkEvidenceVisit> WorkEvidence,
    IReadOnlyList<AuditEntryView> AuditTrail,
    InvoiceDefaultsView InvoiceDefaults,
    string? Note,
    FollowUpView? FollowUp,
    bool CanAct);

/// <summary>Raw review body: <c>NoteSent</c> tells an absent note from an explicit null.</summary>
public sealed record ReviewBodyText(bool NoteSent, string? Note, bool? FollowUp, string? Reason);

public sealed record ReviewInput(bool NoteSent, string? Note, bool? FollowUp, string Reason);

public sealed record ReviewResult(string? Note, FollowUpView? FollowUp);

/// <summary>Raw generate body; booleans stay nullable so a missing one is a 400.</summary>
public sealed record GenerateBodyText(string? IssueDate, string? PaymentTerms, string? Note, bool? AcknowledgeVariances);

public sealed record GenerateInput(DateOnly IssueDate, string PaymentTerms, string? Note, bool AcknowledgeVariances);

public sealed record InvoiceRef(Guid Id, string Number, string Status, decimal Total, string Currency);

public sealed record GenerateResult(bool Changed, InvoiceRef Invoice);

public sealed record BillingOrganization(string Timezone, string Currency);

public sealed record BillingActor(Guid OrganizationId, Guid UserId, BranchScope Scope, IPAddress? IpAddress);

/// <summary>What the caller may do with a work order: unknown or outside scope, visible, or visible and in the queue (BR-03).</summary>
public enum BillingAccess
{
    NotVisible,
    Visible,
    QueueMember,
}

public abstract record BillingOutcome<T>
{
    private BillingOutcome()
    {
    }

    public sealed record Succeeded(T Value) : BillingOutcome<T>;

    /// <summary>A missing, foreign, out-of-scope or no longer queued resource: one identical outcome.</summary>
    public sealed record NotFound : BillingOutcome<T>;

    public sealed record Invalid(IReadOnlyDictionary<string, string[]> Errors) : BillingOutcome<T>;

    public sealed record Conflict(string Code, string Title) : BillingOutcome<T>;
}

public interface IBillingReviewStore
{
    Task<BillingOrganization> GetOrganizationAsync(Guid organizationId, CancellationToken cancellationToken);

    Task<BillingOptionsView> GetOptionsAsync(
        Guid organizationId, BranchScope scope, bool canAct, CancellationToken cancellationToken);

    Task<BillingOutcome<BillingQueuePage>> GetQueueAsync(
        Guid organizationId, BranchScope scope, BillingQuery query, DateTimeOffset now, CancellationToken cancellationToken);

    Task<BillingOutcome<BillingExport>> ExportAsync(
        Guid organizationId, BranchScope scope, BillingQuery query, DateTimeOffset now, CancellationToken cancellationToken);

    Task<BillingAccess> GetAccessAsync(
        Guid organizationId, BranchScope scope, Guid workOrderId, CancellationToken cancellationToken);

    Task<BillingReviewDetail?> GetDetailAsync(
        Guid organizationId, BranchScope scope, Guid workOrderId, bool canAct, DateTimeOffset now, CancellationToken cancellationToken);

    Task<VisitEvidenceImage?> GetEvidenceAsync(
        Guid organizationId, BranchScope scope, Guid workOrderId, Guid evidenceId, CancellationToken cancellationToken);

    Task<BillingOutcome<ReviewResult>> UpdateReviewAsync(
        BillingActor actor, Guid workOrderId, ReviewInput input, DateTimeOffset now, CancellationToken cancellationToken);

    Task<BillingOutcome<GenerateResult>> GenerateInvoiceAsync(
        BillingActor actor, Guid workOrderId, GenerateInput input, DateTimeOffset now, CancellationToken cancellationToken);
}
