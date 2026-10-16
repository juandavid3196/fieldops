using System.Reflection;
using System.Text.RegularExpressions;
using FieldOps.Application.Features.AuditTrail;

namespace FieldOps.UnitTests.AuditTrail;

/// <summary>The action catalog of audit-trail BR-01 and BR-02.</summary>
public sealed partial class AuditCatalogTests
{
    // Written independently of the catalog: every code the application writes today plus the two new ones.
    private static readonly string[] WrittenCodes =
    [
        "auth.signed_in", "auth.signed_out", "auth.password_changed", "user.profile_updated", "user.photo_updated", "user.photo_removed", "export.generated",
        "organization.registered", "organization.settings_updated", "organization.bank_details_updated", "organization.logo_updated", "organization.logo_removed",
        "branch.created", "branch.updated", "branch.set_as_main", "branch.deactivated", "branch.reactivated",
        "user.invited", "user.invitation_resent", "user.invitation_revoked", "user.invitation_accepted", "user.access_updated", "user.suspended", "user.reactivated",
        "customer.created", "customer.updated", "customer.archived", "customer.reactivated", "customer.imported", "customer.message_sent",
        "customer_tag.created", "customer_note.created",
        "property.created", "property.updated", "property.primary_changed", "property.archived", "property.reactivated",
        "catalog_item.created", "catalog_item.updated", "catalog_item.activated", "catalog_item.deactivated", "catalog_item.image_updated", "catalog_item.image_removed", "catalog_item.imported",
        "catalog_category.created", "catalog_category.renamed", "catalog_category.deactivated", "catalog_category.reactivated",
        "technician_profile.created", "technician_profile.updated", "technician_profile.activated", "technician_profile.deactivated", "technician_profile.account_linked", "technician_profile.account_unlinked", "technician_profile.skills_availability_updated",
        "technician_exception.created", "technician_exception.updated", "technician_exception.activated", "technician_exception.cancelled",
        "skill.created", "skill.updated", "skill.activated", "skill.deactivated",
        "checklist_template.created",
        "service_request.created", "service_request.status_changed", "service_request.ready_for_quote", "service_request.assigned", "service_request.priority_changed", "service_request.branch_changed",
        "service_request.assessment_scheduled", "service_request.assessment_rescheduled", "service_request.assessment_cancelled", "service_request.assessment_completed",
        "service_request.attachments_added", "service_request.internal_note_added", "service_request.information_requested", "service_request.customer_response_logged", "service_request.quoted",
        "quote.created", "quote.draft_saved", "quote.draft_discarded", "quote.sent", "quote.email_resent", "quote.revised", "quote.cancelled", "quote.approved", "quote.rejected", "quote.clarification_requested", "quote.expired",
        "work_order.created", "work_order.status_changed", "work_order.billing_note_updated", "work_order.billing_follow_up_marked", "work_order.billing_follow_up_cleared",
        "visit.dispatched", "visit.assigned", "visit.on_the_way", "visit.travel_started", "visit.arrived", "visit.started", "visit.job_started", "visit.paused", "visit.resumed",
        "visit.material_recorded", "visit.evidence_added", "visit.evidence_deleted", "visit.completed", "visit.reschedule_requested",
        "invoice.created", "invoice.draft_saved", "invoice.sent", "invoice.email_resent", "invoice.payment_applied", "invoice.refund_applied", "invoice.bank_transfer_reported", "invoice.review_submitted",
        "payment.recorded", "payment.refunded",
        "invoice_payment_attempt.created", "invoice_payment_attempt.failed", "invoice_payment_attempt.needs_attention",
        "payment_intent.succeeded", "payment_intent.payment_failed", "payment_intent.canceled",
        "portal.signed_in", "portal.account_switched", "portal.access_activated", "portal.access_removed", "portal.invitation_sent", "portal.message_sent",
    ];

    [GeneratedRegex(@"^[a-z_]+\.[a-z_]+$")]
    private static partial Regex ActionCode();

    // AC-05: impact precedence, source order, financial flag and the unknown code fallback, table driven.
    [Fact]
    public void Resolve_ImpactSourceFinancialAndUnknownCodes_FollowTheCatalogRules()
    {
        foreach (var (code, impact) in new (string, AuditImpact)[]
        {
            ("user.access_updated", AuditImpact.High),
            ("payment.recorded", AuditImpact.High),
            ("quote.cancelled", AuditImpact.High),
            ("customer.archived", AuditImpact.High),
            ("catalog_item.imported", AuditImpact.High),
            ("export.generated", AuditImpact.Medium),
            ("work_order.status_changed", AuditImpact.Medium),
            ("service_request.assigned", AuditImpact.Medium),
            ("technician_exception.cancelled", AuditImpact.Medium),
            ("skill.deactivated", AuditImpact.Medium),
            ("service_request.branch_changed", AuditImpact.Medium),
            ("visit.completed", AuditImpact.Medium),
            ("invoice.sent", AuditImpact.Medium),
            ("auth.signed_in", AuditImpact.Low),
            ("customer.updated", AuditImpact.Low),
            ("quote.expired", AuditImpact.Low),
            ("visit.paused", AuditImpact.Low),
        })
        {
            Assert.Equal(impact, AuditCatalog.Resolve(code).Impact);
        }

        foreach (var (code, financial) in new (string, bool)[]
        {
            ("invoice.sent", true),
            ("payment.recorded", true),
            ("payment_intent.succeeded", true),
            ("invoice_payment_attempt.failed", true),
            ("work_order.billing_note_updated", true),
            ("work_order.status_changed", false),
            ("quote.sent", false),
            ("export.generated", false),
            ("invoice.never_written", false),
        })
        {
            Assert.Equal(financial, AuditCatalog.Resolve(code).IsFinancial);
        }

        Assert.Equal("Payment recorded", AuditCatalog.Label("payment.recorded"));
        Assert.Equal("Status changed", AuditCatalog.Label("work_order.status_changed"));
        Assert.Equal("Assignment changed", AuditCatalog.Label("visit.assigned"));
        Assert.Equal(
            ("Request", "Quote", "Work order", "Product or service", "User access", "Invitation", "Team member", "Settings", "User account", "Export", "Customer tag"),
            (AuditCatalog.EntityLabel("service_request"), AuditCatalog.EntityLabel("quote"), AuditCatalog.EntityLabel("work_order"),
                AuditCatalog.EntityLabel("catalog_item"), AuditCatalog.EntityLabel("organization_user"), AuditCatalog.EntityLabel("user_invitation"),
                AuditCatalog.EntityLabel("technician_profile"), AuditCatalog.EntityLabel("organization"), AuditCatalog.EntityLabel("user"),
                AuditCatalog.EntityLabel("export"), AuditCatalog.EntityLabel("customer_tag")));

        var unknown = AuditCatalog.Resolve("foo.bar_baz");
        Assert.Equal(("Foo bar baz", AuditImpact.Low, false), (unknown.Label, unknown.Impact, unknown.IsFinancial));
        Assert.False(AuditCatalog.TryGet("foo.bar_baz", out _));

        // The SQL sets are the catalog, with High taking precedence over Medium.
        Assert.Empty(AuditCatalog.HighCodes.Intersect(AuditCatalog.MediumCodes));
        Assert.Contains("quote.cancelled", AuditCatalog.HighCodes);
        Assert.DoesNotContain("quote.cancelled", AuditCatalog.MediumCodes);
        Assert.DoesNotContain("export.generated", AuditCatalog.FinancialCodes);

        // BR-02 source order: a non-member actor, public-link actions, system, field progress, web.
        var actor = Guid.NewGuid();

        foreach (var (actorId, member, code, source) in new (Guid?, bool, string, string)[]
        {
            (actor, false, "portal.message_sent", "Customer"),
            (actor, false, "visit.arrived", "Customer"),
            (null, false, "service_request.created", "Customer"),
            (null, false, "invoice.bank_transfer_reported", "Customer"),
            (null, false, "customer.message_sent", "Customer"),
            (null, false, "payment_intent.succeeded", "System"),
            (null, false, "quote.expired", "System"),
            (actor, true, "visit.job_started", "Mobile app"),
            (actor, true, "visit.completed", "Mobile app"),
            (actor, true, "visit.dispatched", "Web app"),
            (actor, true, "foo.bar_baz", "Web app"),
        })
        {
            Assert.Equal(source, AuditSourceResolver.Resolve(actorId, member, code));
        }

        // BR-09, BR-10: Accounting filters only financial codes and the export row; the others everything of the catalog.
        Assert.True(AuditAccess.CanSee("accounting", "payment.recorded"));
        Assert.True(AuditAccess.CanSee("accounting", "export.generated"));
        Assert.False(AuditAccess.CanSee("accounting", "quote.sent"));
        Assert.False(AuditAccess.CanSee("owner", "foo.bar_baz"));
        Assert.True(AuditAccess.CanSee("operations_manager", "quote.sent"));
    }

    // Completeness guard: a hard-coded list of the written codes and a reflection scan of the audit action constants.
    [Fact]
    public void Catalog_CoversEveryWrittenActionCode()
    {
        Assert.All(WrittenCodes, code => Assert.True(AuditCatalog.TryGet(code, out _), $"{code} is missing from the audit catalog."));

        var constants = typeof(AuditCatalog).Assembly.GetTypes()
            .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                .Where(field => field.IsLiteral && field.FieldType == typeof(string)
                    && (type.Name.Contains("Audit", StringComparison.Ordinal) || field.Name.Contains("Audit", StringComparison.Ordinal)))
                .Select(field => (Name: $"{type.Name}.{field.Name}", Value: (string)field.GetRawConstantValue()!)))
            .Where(constant => ActionCode().IsMatch(constant.Value))
            .ToList();

        Assert.True(constants.Count > 40, "The scan found too few audit action constants.");
        Assert.All(constants, constant => Assert.True(AuditCatalog.TryGet(constant.Value, out _), $"{constant.Name} ({constant.Value}) is missing from the audit catalog."));
    }
}
