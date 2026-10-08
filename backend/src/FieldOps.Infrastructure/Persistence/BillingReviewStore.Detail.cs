using FieldOps.Application.Features.Access;
using FieldOps.Application.Features.BillingReview;
using FieldOps.Application.Features.ServiceRequests;
using FieldOps.Application.Features.TechnicianVisits;
using FieldOps.Domain.WorkOrders;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Persistence;

internal sealed partial class BillingReviewStore
{
    private static readonly Dictionary<string, string> AuditLabels = new(StringComparer.Ordinal)
    {
        ["work_order.created"] = "Work order created",
        ["work_order.status_changed"] = "Work order status changed",
        ["work_order.billing_note_updated"] = "Billing note updated",
        ["work_order.billing_follow_up_marked"] = "Marked for follow-up",
        ["work_order.billing_follow_up_cleared"] = "Follow-up removed",
        ["visit.dispatched"] = "Visit scheduled",
        ["visit.travel_started"] = "Technician on the way",
        ["visit.arrived"] = "Technician arrived",
        ["visit.job_started"] = "Job started",
        ["visit.paused"] = "Job paused",
        ["visit.resumed"] = "Job resumed",
        ["visit.material_recorded"] = "Material recorded",
        ["visit.evidence_added"] = "Photo added",
        ["visit.evidence_deleted"] = "Photo removed",
        ["visit.completed"] = "Job completed",
    };

    public async Task<BillingReviewDetail?> GetDetailAsync(
        Guid organizationId,
        BranchScope scope,
        Guid workOrderId,
        bool canAct,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var order = await QueueOrders(organizationId, scope).SingleOrDefaultAsync(candidate => candidate.Id == workOrderId, cancellationToken);

        if (order is null)
        {
            return null;
        }

        var organization = await ReadOrganizationAsync(organizationId, cancellationToken);
        var zone = OrganizationTime.FindZone(organization.Timezone);
        var facts = (await LoadFactsAsync(
            organizationId, [(order.Id, order.QuoteVersionId)], organization.RequireSignature, cancellationToken))[order.Id];
        var variance = VarianceCalculator.Analyze(facts.Variance);
        var verification = CompletionVerifier.Verify(facts.Verification);
        var quote = facts.Quote;
        var taxLabel = TaxLabel(quote);

        var customer = await dbContext.Customers.AsNoTracking()
            .Where(candidate => candidate.OrganizationId == organizationId && candidate.Id == order.CustomerId)
            .Select(candidate => candidate.DisplayName)
            .SingleAsync(cancellationToken);
        var property = await dbContext.Properties.AsNoTracking()
            .Where(candidate => candidate.OrganizationId == organizationId && candidate.Id == order.PropertyId)
            .Select(candidate => new { candidate.AddressLine1, candidate.AddressLine2, candidate.City, candidate.StateRegion, candidate.PostalCode })
            .SingleAsync(cancellationToken);
        var quoteNumber = await (
            from version in dbContext.QuoteVersions.AsNoTracking()
            join quoteRow in dbContext.Quotes.AsNoTracking() on version.QuoteId equals quoteRow.Id
            where version.OrganizationId == organizationId && version.Id == order.QuoteVersionId
            select quoteRow.QuoteNumber)
            .SingleAsync(cancellationToken);
        var completedAt = await dbContext.Visits.AsNoTracking()
            .Where(visit => visit.WorkOrderId == order.Id && visit.Status == VisitStatus.Completed)
            .MaxAsync(visit => visit.ActualCompletedAt, cancellationToken);
        var completedBy = await LoadCompletedByAsync(organizationId, [order.Id], cancellationToken);

        var (evidence, workEvidence) = await LoadWorkEvidenceAsync(organizationId, order.Id, cancellationToken);
        var audit = await LoadAuditTrailAsync(organizationId, order.Id, cancellationToken);

        var terms = BillingTerms.FromQuoteTerms(quote.Terms);
        var issueDate = OrganizationTime.LocalDate(now, zone);
        FollowUpView? followUp = null;

        if (order.BillingFollowUpAt is { } followUpAt && order.BillingFollowUpByUserId is { } followUpBy)
        {
            followUp = new FollowUpView(followUpAt, await ReadUserNameAsync(followUpBy, cancellationToken));
        }

        return new BillingReviewDetail(
            new BillingHeader(
                order.Id,
                DisplayNumber(organization.WorkOrderPrefix, order.WorkOrderNumber),
                order.Title,
                DisplayNumber(organization.QuotePrefix, quoteNumber),
                customer,
                QuoteStore.FormatAddress(property.AddressLine1, property.AddressLine2, property.City, property.StateRegion, property.PostalCode) ?? string.Empty,
                completedBy.GetValueOrDefault(order.Id),
                completedAt ?? order.UpdatedAt,
                organization.Timezone),
            variance.Lines,
            variance.LaborVariance,
            variance.MaterialVariance,
            new BillingTotals(
                quote.Subtotal, quote.Subtotal, quote.Discount, taxLabel, quote.Tax, quote.Total, 0.00m, quote.Currency),
            verification,
            CompletionVerifier.IsReady(verification, variance.LaborVariance, variance.MaterialVariance),
            evidence,
            workEvidence,
            audit,
            new InvoiceDefaultsView(
                DisplayNumber(organization.InvoicePrefix, organization.NextInvoiceNumber),
                issueDate,
                terms,
                BillingTerms.DueDate(issueDate, terms),
                taxLabel,
                quote.Currency),
            order.BillingReviewNote,
            followUp,
            canAct);
    }

    public async Task<VisitEvidenceImage?> GetEvidenceAsync(
        Guid organizationId, BranchScope scope, Guid workOrderId, Guid evidenceId, CancellationToken cancellationToken)
    {
        if (!await QueueOrders(organizationId, scope).AnyAsync(order => order.Id == workOrderId, cancellationToken))
        {
            return null;
        }

        var row = await (
            from evidence in dbContext.VisitEvidences.AsNoTracking()
            join visit in dbContext.Visits.AsNoTracking() on evidence.VisitId equals visit.Id
            where visit.OrganizationId == organizationId
                && visit.WorkOrderId == workOrderId
                && evidence.Id == evidenceId
                && evidence.Content != null
            select new { evidence.MimeType, evidence.Content })
            .FirstOrDefaultAsync(cancellationToken);

        return row is null ? null : new VisitEvidenceImage(row.MimeType, row.Content!);
    }

    private async Task<(EvidenceSummary Summary, List<WorkEvidenceVisit> Visits)> LoadWorkEvidenceAsync(
        Guid organizationId, Guid orderId, CancellationToken cancellationToken)
    {
        var visits = await dbContext.Visits.AsNoTracking()
            .Where(visit => visit.OrganizationId == organizationId && visit.WorkOrderId == orderId && visit.Status != VisitStatus.Cancelled)
            .OrderBy(visit => visit.VisitNumber)
            .Select(visit => new { visit.Id, visit.VisitNumber, visit.CompletionSummary })
            .ToListAsync(cancellationToken);
        var visitIds = visits.Select(visit => visit.Id).ToArray();

        var checklist = await dbContext.VisitChecklistItems.AsNoTracking()
            .Where(item => visitIds.Contains(item.VisitId))
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.Id)
            .Select(item => new { item.VisitId, item.Label, item.IsRequired, item.IsCompleted })
            .ToListAsync(cancellationToken);
        var materials = await dbContext.VisitMaterials.AsNoTracking()
            .Where(material => visitIds.Contains(material.VisitId))
            .OrderBy(material => material.Description)
            .ThenBy(material => material.Id)
            .Select(material => new { material.VisitId, material.Description, material.Quantity, material.Unit, material.PlannedMaterialId })
            .ToListAsync(cancellationToken);
        var evidence = await dbContext.VisitEvidences.AsNoTracking()
            .Where(item => visitIds.Contains(item.VisitId))
            .OrderBy(item => item.CreatedAt)
            .ThenBy(item => item.Id)
            .Select(item => new { item.Id, item.VisitId, item.EvidenceType, item.Caption })
            .ToListAsync(cancellationToken);
        var signoffs = await dbContext.CustomerSignoffs.AsNoTracking()
            .Where(signoff => visitIds.Contains(signoff.VisitId))
            .Select(signoff => new
            {
                signoff.VisitId,
                signoff.AcknowledgementMethod,
                signoff.SignerName,
                signoff.SignerRelationship,
                signoff.Comments,
                signoff.AbsenceReason,
                Captured = signoff.SignatureContent != null || signoff.SignatureStorageKey != null,
            })
            .ToListAsync(cancellationToken);

        var typeOrder = new[] { VisitEvidenceType.Before, VisitEvidenceType.During, VisitEvidenceType.After, VisitEvidenceType.Incident, VisitEvidenceType.Other };
        var thumbnails = evidence
            .Where(item => item.EvidenceType is VisitEvidenceType.Before or VisitEvidenceType.After)
            .OrderBy(item => item.EvidenceType == VisitEvidenceType.Before ? 0 : 1)
            .Take(3)
            .Select(item => new EvidenceThumbnail(item.Id, CodeOf(item.EvidenceType)))
            .ToList();

        var perVisit = visits
            .Select(visit =>
            {
                var signoff = signoffs.FirstOrDefault(candidate => candidate.VisitId == visit.Id);

                return new WorkEvidenceVisit(
                    visit.Id,
                    visit.VisitNumber,
                    checklist.Where(item => item.VisitId == visit.Id)
                        .Select(item => new WorkChecklistItem(item.Label, item.IsRequired, item.IsCompleted))
                        .ToList(),
                    materials.Where(material => material.VisitId == visit.Id)
                        .Select(material => new WorkMaterial(
                            material.Description,
                            VarianceCalculator.FormatNumber(material.Quantity),
                            material.Unit,
                            material.PlannedMaterialId is null ? "added" : "planned"))
                        .ToList(),
                    evidence.Where(item => item.VisitId == visit.Id)
                        .OrderBy(item => Array.IndexOf(typeOrder, item.EvidenceType))
                        .Select(item => new WorkEvidenceItem(item.Id, CodeOf(item.EvidenceType), item.Caption))
                        .ToList(),
                    string.IsNullOrWhiteSpace(visit.CompletionSummary) ? null : visit.CompletionSummary.Trim(),
                    signoff is null
                        ? null
                        : new WorkAcknowledgment(
                            signoff.AcknowledgementMethod,
                            signoff.SignerName,
                            signoff.SignerRelationship,
                            signoff.Comments ?? signoff.AbsenceReason,
                            signoff.Captured));
            })
            .ToList();

        return (new EvidenceSummary(evidence.Count, thumbnails), perVisit);
    }

    private async Task<List<AuditEntryView>> LoadAuditTrailAsync(Guid organizationId, Guid orderId, CancellationToken cancellationToken)
    {
        var visitIds = await dbContext.Visits.AsNoTracking()
            .Where(visit => visit.OrganizationId == organizationId && visit.WorkOrderId == orderId)
            .Select(visit => (Guid?)visit.Id)
            .ToArrayAsync(cancellationToken);

        // Action, actor and time only: before/after data, metadata and IP addresses are never read (BR-12).
        var rows = await dbContext.AuditLogs.AsNoTracking()
            .Where(entry => entry.OrganizationId == organizationId
                && ((entry.EntityType == AuditWorkOrder && entry.EntityId == orderId)
                    || (entry.EntityType == "visit" && visitIds.Contains(entry.EntityId))))
            .OrderByDescending(entry => entry.OccurredAt)
            .ThenByDescending(entry => entry.Id)
            .Take(BillingMessages.MaxAuditRows)
            .Select(entry => new { entry.Action, entry.ActorUserId, entry.OccurredAt })
            .ToListAsync(cancellationToken);
        var actorIds = rows.Where(row => row.ActorUserId is not null).Select(row => row.ActorUserId!.Value).Distinct().ToArray();
        var names = await dbContext.Users.AsNoTracking()
            .Where(user => actorIds.Contains(user.Id))
            .Select(user => new { user.Id, user.FirstName, user.LastName })
            .ToDictionaryAsync(user => user.Id, user => FullName(user.FirstName, user.LastName), cancellationToken);

        return rows
            .Select(row => new AuditEntryView(
                AuditLabels.GetValueOrDefault(row.Action, "Activity recorded"),
                row.ActorUserId is { } actor && names.TryGetValue(actor, out var name) && name.Length > 0 ? name : "System",
                row.OccurredAt))
            .ToList();
    }

    private async Task<string> ReadUserNameAsync(Guid userId, CancellationToken cancellationToken)
    {
        var name = await dbContext.Users.AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new { user.FirstName, user.LastName })
            .SingleOrDefaultAsync(cancellationToken);

        return name is null ? string.Empty : FullName(name.FirstName, name.LastName);
    }

    private static string CodeOf(VisitEvidenceType type) => type.ToString().ToLowerInvariant();
}
