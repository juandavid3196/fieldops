namespace FieldOps.Application.Features.AuditTrail;

public enum AuditImpact
{
    Low,
    Medium,
    High,
}

/// <summary>One action code of the catalog (audit-trail BR-01, BR-02); the source is the member default of BR-02 (4), (5).</summary>
public sealed record AuditCatalogEntry(
    string Code, string Label, AuditImpact Impact, string Source, string EntityType, bool IsFinancial);

/// <summary>
/// Single read-only source of the action catalog: label, impact, source and financial flag per code, and the entity
/// labels. Impact and financial flags are computed once at build time from BR-02, with the explicit High list first. The
/// same sets drive the SQL filters, so reads and the in-memory presentation cannot drift.
/// </summary>
public static class AuditCatalog
{
    public const string ExportGenerated = "export.generated";

    public const string SignedIn = "auth.signed_in";

    public const string SignedOut = "auth.signed_out";

    public const string MobileSource = "Mobile app";

    public const string WebSource = "Web app";

    private static readonly string[] HighList =
    [
        "user.access_updated", "user.suspended", "user.reactivated", "user.invitation_revoked",
        "organization.settings_updated", "organization.bank_details_updated", "branch.set_as_main",
        "payment.recorded", "payment.refunded", "invoice.payment_applied", "invoice.refund_applied",
        "customer.archived", "customer.imported", "catalog_item.imported", "quote.cancelled",
    ];

    private static readonly string[] MediumList =
    [
        ExportGenerated, "user.invited", "branch.created", "branch.updated", "invoice.created", "invoice.sent",
        "quote.sent", "quote.approved", "quote.rejected", "quote.revised", "work_order.created", "visit.completed",
        "catalog_item.updated", "technician_profile.updated",
    ];

    private static readonly string[] MediumSuffixes =
        [".status_changed", ".assigned", ".cancelled", ".deactivated", ".archived", ".branch_changed"];

    private static readonly string[] FinancialPrefixes =
        ["invoice.", "payment.", "payment_intent.", "invoice_payment_attempt.", "work_order.billing_"];

    private static readonly string[] FieldProgressCodes =
    [
        "visit.travel_started", "visit.on_the_way", "visit.arrived", "visit.job_started", "visit.started", "visit.paused",
        "visit.resumed", "visit.material_recorded", "visit.evidence_added", "visit.evidence_deleted", "visit.completed",
    ];

    private static readonly Dictionary<string, string> EntityLabels = new(StringComparer.Ordinal)
    {
        ["service_request"] = "Request",
        ["quote"] = "Quote",
        ["work_order"] = "Work order",
        ["visit"] = "Visit",
        ["customer"] = "Customer",
        ["invoice"] = "Invoice",
        ["payment"] = "Payment",
        ["catalog_item"] = "Product or service",
        ["organization_user"] = "User access",
        ["user_invitation"] = "Invitation",
        ["technician_profile"] = "Team member",
        ["organization"] = "Settings",
        ["branch"] = "Branch",
        ["user"] = "User account",
        ["export"] = "Export",
    };

    private static readonly Dictionary<string, AuditCatalogEntry> Entries = Build();

    public static IReadOnlyCollection<AuditCatalogEntry> All => Entries.Values;

    public static IReadOnlyCollection<string> Codes => Entries.Keys;

    /// <summary>Codes of High impact, for SQL.</summary>
    public static string[] HighCodes { get; } = [.. Entries.Values.Where(entry => entry.Impact == AuditImpact.High).Select(entry => entry.Code)];

    /// <summary>Codes of Medium impact, for SQL.</summary>
    public static string[] MediumCodes { get; } = [.. Entries.Values.Where(entry => entry.Impact == AuditImpact.Medium).Select(entry => entry.Code)];

    /// <summary>Codes whose catalog entry is financial (BR-02), for SQL. <c>export.generated</c> depends on its metadata instead.</summary>
    public static string[] FinancialCodes { get; } = [.. Entries.Values.Where(entry => entry.IsFinancial).Select(entry => entry.Code)];

    /// <summary>The report types that make an <c>export.generated</c> row financial.</summary>
    public static string[] FinancialReportTypes { get; } = ["invoices", "payments", "billing_review"];

    public static bool TryGet(string code, out AuditCatalogEntry entry) => Entries.TryGetValue(code, out entry!);

    /// <summary>The catalog entry, or the humanized, Low impact, non-financial fallback of an unknown code (BR-02).</summary>
    public static AuditCatalogEntry Resolve(string code) =>
        Entries.TryGetValue(code, out var entry)
            ? entry
            : new AuditCatalogEntry(code, Humanize(code), AuditImpact.Low, WebSource, string.Empty, false);

    public static string Label(string code) => Resolve(code).Label;

    public static string ImpactCode(AuditImpact impact) =>
        impact switch
        {
            AuditImpact.High => "high",
            AuditImpact.Medium => "medium",
            _ => "low",
        };

    public static bool TryParseImpact(string? value, out AuditImpact impact)
    {
        impact = AuditImpact.Low;

        switch (value?.Trim().ToLowerInvariant())
        {
            case "low":
                return true;
            case "medium":
                impact = AuditImpact.Medium;

                return true;
            case "high":
                impact = AuditImpact.High;

                return true;
            default:
                return false;
        }
    }

    /// <summary>The distinct entity types of the catalog.</summary>
    public static IReadOnlyList<string> EntityTypes { get; } =
        [.. Entries.Values.Select(entry => entry.EntityType).Distinct(StringComparer.Ordinal).OrderBy(type => EntityLabel(type), StringComparer.Ordinal)];

    /// <summary>The BR-01 label of an entity type; a humanized fallback for types BR-01 does not name.</summary>
    public static string EntityLabel(string entityType) =>
        EntityLabels.TryGetValue(entityType, out var label) ? label : Humanize(entityType);

    /// <summary>"foo.bar_baz" becomes "Foo bar baz".</summary>
    public static string Humanize(string text)
    {
        var words = text.Replace('.', ' ').Replace('_', ' ').Trim();

        return words.Length == 0 ? text : char.ToUpperInvariant(words[0]) + words[1..];
    }

    private static Dictionary<string, AuditCatalogEntry> Build()
    {
        (string Code, string Label, string Entity)[] definitions =
        [
            ("auth.signed_in", "Signed in", "user"),
            ("auth.signed_out", "Signed out", "user"),
            ("organization.registered", "Organization registered", "organization"),
            ("organization.settings_updated", "Settings updated", "organization"),
            ("organization.bank_details_updated", "Bank details updated", "organization"),
            ("organization.logo_updated", "Logo updated", "organization"),
            ("organization.logo_removed", "Logo removed", "organization"),
            ("branch.created", "Branch created", "branch"),
            ("branch.updated", "Branch updated", "branch"),
            ("branch.set_as_main", "Main branch changed", "branch"),
            ("branch.deactivated", "Branch deactivated", "branch"),
            ("branch.reactivated", "Branch reactivated", "branch"),
            ("user.invited", "User invited", "user_invitation"),
            ("user.invitation_resent", "Invitation resent", "user_invitation"),
            ("user.invitation_revoked", "Invitation revoked", "user_invitation"),
            ("user.invitation_accepted", "Invitation accepted", "user_invitation"),
            ("user.access_updated", "Access updated", "organization_user"),
            ("user.suspended", "User suspended", "organization_user"),
            ("user.reactivated", "User reactivated", "organization_user"),
            ("customer.created", "Customer created", "customer"),
            ("customer.updated", "Customer updated", "customer"),
            ("customer.archived", "Customer archived", "customer"),
            ("customer.reactivated", "Customer reactivated", "customer"),
            ("customer.imported", "Customers imported", "customer"),
            ("customer.message_sent", "Customer message sent", "customer"),
            ("customer_tag.created", "Tag created", "customer_tag"),
            ("customer_note.created", "Note added", "customer_note"),
            ("property.created", "Property created", "property"),
            ("property.updated", "Property updated", "property"),
            ("property.primary_changed", "Primary property changed", "property"),
            ("property.archived", "Property archived", "property"),
            ("property.reactivated", "Property reactivated", "property"),
            ("catalog_item.created", "Product or service created", "catalog_item"),
            ("catalog_item.updated", "Product or service updated", "catalog_item"),
            ("catalog_item.activated", "Product or service activated", "catalog_item"),
            ("catalog_item.deactivated", "Product or service deactivated", "catalog_item"),
            ("catalog_item.image_updated", "Image updated", "catalog_item"),
            ("catalog_item.image_removed", "Image removed", "catalog_item"),
            ("catalog_item.imported", "Products and services imported", "catalog_item"),
            ("catalog_category.created", "Category created", "catalog_category"),
            ("catalog_category.renamed", "Category renamed", "catalog_category"),
            ("catalog_category.deactivated", "Category deactivated", "catalog_category"),
            ("catalog_category.reactivated", "Category reactivated", "catalog_category"),
            ("technician_profile.created", "Team member created", "technician_profile"),
            ("technician_profile.updated", "Team member updated", "technician_profile"),
            ("technician_profile.activated", "Team member activated", "technician_profile"),
            ("technician_profile.deactivated", "Team member deactivated", "technician_profile"),
            ("technician_profile.account_linked", "Account linked", "technician_profile"),
            ("technician_profile.account_unlinked", "Account unlinked", "technician_profile"),
            ("technician_profile.skills_availability_updated", "Skills and availability updated", "technician_profile"),
            ("technician_exception.created", "Time off created", "technician_exception"),
            ("technician_exception.updated", "Time off updated", "technician_exception"),
            ("technician_exception.activated", "Time off activated", "technician_exception"),
            ("technician_exception.cancelled", "Time off cancelled", "technician_exception"),
            ("skill.created", "Skill created", "skill"),
            ("skill.updated", "Skill updated", "skill"),
            ("skill.activated", "Skill activated", "skill"),
            ("skill.deactivated", "Skill deactivated", "skill"),
            ("checklist_template.created", "Checklist template created", "checklist_template"),
            ("service_request.created", "Request created", "service_request"),
            ("service_request.status_changed", "Status changed", "service_request"),
            ("service_request.ready_for_quote", "Marked ready for quote", "service_request"),
            ("service_request.assigned", "Assignment changed", "service_request"),
            ("service_request.priority_changed", "Priority changed", "service_request"),
            ("service_request.branch_changed", "Branch changed", "service_request"),
            ("service_request.assessment_scheduled", "Assessment scheduled", "service_request"),
            ("service_request.assessment_rescheduled", "Assessment rescheduled", "service_request"),
            ("service_request.assessment_cancelled", "Assessment cancelled", "service_request"),
            ("service_request.assessment_completed", "Assessment completed", "service_request"),
            ("service_request.attachments_added", "Attachments added", "service_request"),
            ("service_request.internal_note_added", "Internal note added", "service_request"),
            ("service_request.information_requested", "Information requested", "service_request"),
            ("service_request.customer_response_logged", "Customer response logged", "service_request"),
            ("service_request.quoted", "Quoted", "service_request"),
            ("quote.created", "Quote created", "quote"),
            ("quote.draft_saved", "Draft saved", "quote"),
            ("quote.draft_discarded", "Draft discarded", "quote"),
            ("quote.sent", "Quote sent", "quote"),
            ("quote.email_resent", "Quote email resent", "quote"),
            ("quote.revised", "Quote revised", "quote"),
            ("quote.cancelled", "Quote cancelled", "quote"),
            ("quote.approved", "Quote approved", "quote"),
            ("quote.rejected", "Quote rejected", "quote"),
            ("quote.clarification_requested", "Clarification requested", "quote"),
            ("quote.expired", "Quote expired", "quote"),
            ("work_order.created", "Work order created", "work_order"),
            ("work_order.status_changed", "Status changed", "work_order"),
            ("work_order.billing_note_updated", "Billing note updated", "work_order"),
            ("work_order.billing_follow_up_marked", "Billing follow-up marked", "work_order"),
            ("work_order.billing_follow_up_cleared", "Billing follow-up cleared", "work_order"),
            ("visit.dispatched", "Visit dispatched", "visit"),
            ("visit.assigned", "Assignment changed", "visit"),
            ("visit.on_the_way", "On the way", "visit"),
            ("visit.travel_started", "Travel started", "visit"),
            ("visit.arrived", "Arrived", "visit"),
            ("visit.started", "Job started", "visit"),
            ("visit.job_started", "Job started", "visit"),
            ("visit.paused", "Job paused", "visit"),
            ("visit.resumed", "Job resumed", "visit"),
            ("visit.material_recorded", "Material recorded", "visit"),
            ("visit.evidence_added", "Evidence added", "visit"),
            ("visit.evidence_deleted", "Evidence deleted", "visit"),
            ("visit.completed", "Visit completed", "visit"),
            ("visit.reschedule_requested", "Reschedule requested", "visit"),
            ("invoice.created", "Invoice created", "invoice"),
            ("invoice.draft_saved", "Draft saved", "invoice"),
            ("invoice.sent", "Invoice sent", "invoice"),
            ("invoice.email_resent", "Invoice email resent", "invoice"),
            ("invoice.payment_applied", "Payment applied", "invoice"),
            ("invoice.refund_applied", "Refund applied", "invoice"),
            ("invoice.bank_transfer_reported", "Bank transfer reported", "invoice"),
            ("invoice.review_submitted", "Review submitted", "invoice"),
            ("payment.recorded", "Payment recorded", "payment"),
            ("payment.refunded", "Payment refunded", "payment"),
            ("invoice_payment_attempt.created", "Payment attempt started", "invoice_payment_attempt"),
            ("invoice_payment_attempt.failed", "Payment attempt failed", "invoice_payment_attempt"),
            ("invoice_payment_attempt.needs_attention", "Payment needs attention", "invoice_payment_attempt"),
            ("payment_intent.succeeded", "Online payment succeeded", "invoice_payment_attempt"),
            ("payment_intent.payment_failed", "Online payment failed", "invoice_payment_attempt"),
            ("payment_intent.canceled", "Online payment canceled", "invoice_payment_attempt"),
            ("portal.signed_in", "Portal sign-in", "customer_contact"),
            ("portal.account_switched", "Portal account switched", "customer_contact"),
            ("portal.access_activated", "Portal access activated", "customer_contact"),
            ("portal.access_removed", "Portal access removed", "customer_contact"),
            ("portal.invitation_sent", "Portal invitation sent", "customer_contact"),
            ("portal.message_sent", "Portal message sent", "customer_contact"),
            (ExportGenerated, "Export generated", "export"),
        ];

        return definitions.ToDictionary(
            definition => definition.Code,
            definition => new AuditCatalogEntry(
                definition.Code,
                definition.Label,
                ImpactOf(definition.Code),
                FieldProgressCodes.Contains(definition.Code, StringComparer.Ordinal) ? MobileSource : WebSource,
                definition.Entity,
                FinancialPrefixes.Any(prefix => definition.Code.StartsWith(prefix, StringComparison.Ordinal))),
            StringComparer.Ordinal);
    }

    // BR-02: the explicit High list takes precedence over the Medium patterns.
    private static AuditImpact ImpactOf(string code)
    {
        if (HighList.Contains(code, StringComparer.Ordinal))
        {
            return AuditImpact.High;
        }

        return MediumList.Contains(code, StringComparer.Ordinal)
            || MediumSuffixes.Any(suffix => code.EndsWith(suffix, StringComparison.Ordinal))
                ? AuditImpact.Medium
                : AuditImpact.Low;
    }
}
