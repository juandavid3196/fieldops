using FieldOps.Application.Features.BillingReview;
using FieldOps.Application.Features.Quotes;
using FieldOps.Domain.Quotes;
using FieldOps.Domain.WorkOrders;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Persistence;

internal sealed partial class BillingReviewStore
{
    /// <summary>
    /// The approved quote, variance facts and verification facts of the work orders, read with a fixed number of grouped
    /// queries (no per-order round trips). Cancelled visits never count (BR-08, BR-09, BR-11).
    /// </summary>
    private async Task<Dictionary<Guid, OrderFacts>> LoadFactsAsync(
        Guid organizationId,
        IReadOnlyCollection<(Guid OrderId, Guid VersionId)> orders,
        bool requireSignature,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, OrderFacts>();

        if (orders.Count == 0)
        {
            return result;
        }

        var orderIds = orders.Select(order => order.OrderId).ToArray();
        var versionIds = orders.Select(order => order.VersionId).Distinct().ToArray();

        var quotes = await LoadQuotesAsync(organizationId, versionIds, cancellationToken);

        var visits = await dbContext.Visits.AsNoTracking()
            .Where(visit => visit.OrganizationId == organizationId
                && orderIds.Contains(visit.WorkOrderId)
                && visit.Status != VisitStatus.Cancelled)
            .Select(visit => new
            {
                visit.Id,
                visit.WorkOrderId,
                visit.VisitNumber,
                visit.Status,
                visit.ActualCompletedAt,
                HasSummary = visit.CompletionSummary != null && visit.CompletionSummary.Trim() != string.Empty,
            })
            .ToListAsync(cancellationToken);
        var visitIds = visits.Select(visit => visit.Id).ToArray();
        var completedIds = visits.Where(visit => visit.Status == VisitStatus.Completed).Select(visit => visit.Id).ToArray();

        var checklists = await dbContext.VisitChecklistItems.AsNoTracking()
            .Where(item => visitIds.Contains(item.VisitId))
            .GroupBy(item => item.VisitId)
            .Select(group => new
            {
                VisitId = group.Key,
                Total = group.Count(),
                Done = group.Count(item => item.IsCompleted),
                RequiredIncomplete = group.Count(item => item.IsRequired && !item.IsCompleted),
            })
            .ToListAsync(cancellationToken);

        var entries = await dbContext.VisitTimeEntries.AsNoTracking()
            .Where(entry => visitIds.Contains(entry.VisitId) && entry.EntryType == VisitTimeEntryType.Work && entry.EndedAt != null)
            .Select(entry => new { entry.VisitId, entry.StartedAt, EndedAt = entry.EndedAt!.Value })
            .ToListAsync(cancellationToken);

        var materials = await dbContext.VisitMaterials.AsNoTracking()
            .Where(material => visitIds.Contains(material.VisitId))
            .Select(material => new { material.VisitId, material.PlannedMaterialId, material.Description, material.Quantity, material.Unit })
            .ToListAsync(cancellationToken);

        var planned = await dbContext.WorkOrderPlannedMaterials.AsNoTracking()
            .Where(material => material.OrganizationId == organizationId && orderIds.Contains(material.WorkOrderId))
            .OrderBy(material => material.SortOrder)
            .ThenBy(material => material.Id)
            .Select(material => new { material.Id, material.WorkOrderId, material.QuoteLineId, material.Description, material.Unit, material.Quantity })
            .ToListAsync(cancellationToken);

        var photos = await dbContext.VisitEvidences.AsNoTracking()
            .Where(evidence => completedIds.Contains(evidence.VisitId)
                && (evidence.EvidenceType == VisitEvidenceType.Before || evidence.EvidenceType == VisitEvidenceType.After))
            .Select(evidence => new { evidence.VisitId, evidence.EvidenceType })
            .Distinct()
            .ToListAsync(cancellationToken);

        var signoffs = await dbContext.CustomerSignoffs.AsNoTracking()
            .Where(signoff => completedIds.Contains(signoff.VisitId))
            .Select(signoff => new { signoff.VisitId, signoff.AcknowledgementMethod, signoff.SignerName })
            .ToListAsync(cancellationToken);

        var visitsByOrder = visits.ToLookup(visit => visit.WorkOrderId);
        var entriesByVisit = entries.ToLookup(entry => entry.VisitId);
        var materialsByVisit = materials.ToLookup(material => material.VisitId);
        var checklistByVisit = checklists.ToDictionary(item => item.VisitId);
        var plannedByOrder = planned.ToLookup(item => item.WorkOrderId);
        var photoSet = photos.Select(photo => (photo.VisitId, photo.EvidenceType)).ToHashSet();
        var signoffByVisit = signoffs.ToDictionary(signoff => signoff.VisitId);

        foreach (var (orderId, versionId) in orders)
        {
            var quote = quotes[versionId];
            var orderVisits = visitsByOrder[orderId].ToList();
            var completed = orderVisits.Where(visit => visit.Status == VisitStatus.Completed).ToList();

            var seconds = (long)Math.Floor(orderVisits
                .SelectMany(visit => entriesByVisit[visit.Id])
                .Sum(entry => (entry.EndedAt - entry.StartedAt).TotalSeconds));

            var orderMaterials = orderVisits.SelectMany(visit => materialsByVisit[visit.Id]).ToList();
            var plannedFacts = plannedByOrder[orderId]
                .Select(item => new PlannedMaterialFact(
                    item.Id,
                    item.QuoteLineId,
                    item.Description,
                    item.Unit,
                    item.Quantity,
                    orderMaterials.Where(material => material.PlannedMaterialId == item.Id).Sum(material => material.Quantity)))
                .ToList();
            var added = orderMaterials
                .Where(material => material.PlannedMaterialId is null)
                .Select(material => new AddedMaterialFact(material.Description, material.Unit, material.Quantity))
                .ToList();

            var orderChecklists = orderVisits.Where(visit => checklistByVisit.ContainsKey(visit.Id)).Select(visit => checklistByVisit[visit.Id]).ToList();
            var latest = completed
                .OrderByDescending(visit => visit.ActualCompletedAt)
                .ThenByDescending(visit => visit.VisitNumber)
                .FirstOrDefault();
            var latestSignoff = latest is not null && signoffByVisit.TryGetValue(latest.Id, out var found) ? found : null;
            var completedSignoffs = completed.Select(visit => signoffByVisit.GetValueOrDefault(visit.Id)).ToList();

            var verification = new VerificationFacts(
                orderChecklists.Sum(item => item.Total),
                orderChecklists.Sum(item => item.Done),
                orderChecklists.Sum(item => item.RequiredIncomplete),
                completed.Count(visit =>
                    !photoSet.Contains((visit.Id, VisitEvidenceType.Before)) || !photoSet.Contains((visit.Id, VisitEvidenceType.After))),
                completedSignoffs.Count(signoff => signoff is null),
                completedSignoffs.Count(signoff => signoff is not null && signoff.AcknowledgementMethod != AcknowledgementMethods.Signed),
                orderMaterials.Count > 0,
                orderVisits.Any(visit => visit.HasSummary),
                seconds,
                requireSignature,
                latestSignoff?.AcknowledgementMethod,
                latestSignoff?.SignerName);

            result[orderId] = new OrderFacts(
                quote,
                new VarianceFacts(quote.Lines, plannedFacts, added, seconds),
                verification,
                latestSignoff?.AcknowledgementMethod);
        }

        return result;
    }

    private async Task<Dictionary<Guid, ApprovedQuote>> LoadQuotesAsync(
        Guid organizationId, Guid[] versionIds, CancellationToken cancellationToken)
    {
        var versions = await dbContext.QuoteVersions.AsNoTracking()
            .Where(version => version.OrganizationId == organizationId && versionIds.Contains(version.Id))
            .Select(version => new
            {
                version.Id,
                version.Currency,
                version.Terms,
                version.Subtotal,
                version.DiscountTotal,
                version.TaxTotal,
                version.Total,
            })
            .ToListAsync(cancellationToken);
        var responses = await dbContext.QuoteResponses.AsNoTracking()
            .Where(response => response.OrganizationId == organizationId
                && versionIds.Contains(response.QuoteVersionId)
                && response.Response == QuoteStatus.Approved)
            .Select(response => new
            {
                response.Id,
                response.QuoteVersionId,
                response.Subtotal,
                response.DiscountTotal,
                response.TaxTotal,
                response.Total,
            })
            .ToListAsync(cancellationToken);
        var selected = await dbContext.QuoteResponseOptionalLines.AsNoTracking()
            .Where(line => line.OrganizationId == organizationId && versionIds.Contains(line.QuoteVersionId))
            .Select(line => new { line.QuoteResponseId, line.QuoteLineId })
            .ToListAsync(cancellationToken);
        var lines = await dbContext.QuoteLines.AsNoTracking()
            .Where(line => line.OrganizationId == organizationId && versionIds.Contains(line.QuoteVersionId))
            .Select(line => new
            {
                line.QuoteVersionId,
                line.IsOptional,
                Billed = new BilledLine(
                    line.Id,
                    line.Name,
                    line.LineType == FieldOps.Domain.Catalog.CatalogItemType.Service,
                    line.Quantity,
                    line.Unit,
                    line.UnitPrice,
                    line.TaxRate,
                    line.LineSubtotal,
                    line.LineTax,
                    line.LineTotal,
                    line.SortOrder),
            })
            .ToListAsync(cancellationToken);

        var quotes = new Dictionary<Guid, ApprovedQuote>();

        foreach (var version in versions)
        {
            var response = responses.FirstOrDefault(candidate => candidate.QuoteVersionId == version.Id);
            var chosen = response is null
                ? []
                : selected.Where(line => line.QuoteResponseId == response.Id).Select(line => line.QuoteLineId).ToHashSet();
            var billed = lines
                .Where(line => line.QuoteVersionId == version.Id && (!line.IsOptional || chosen.Contains(line.Billed.Id)))
                .Select(line => line.Billed)
                .OrderBy(line => line.SortOrder)
                .ThenBy(line => line.Id)
                .ToList();

            quotes[version.Id] = new ApprovedQuote(
                version.Id,
                version.Currency,
                version.Terms,
                QuoteCalculator.Money(response?.Subtotal ?? version.Subtotal),
                QuoteCalculator.Money(response?.DiscountTotal ?? version.DiscountTotal),
                QuoteCalculator.Money(response?.TaxTotal ?? version.TaxTotal),
                QuoteCalculator.Money(response?.Total ?? version.Total),
                billed);
        }

        return quotes;
    }

    /// <summary>The name of the active primary assignee of the latest completed visit of each work order (BR-03).</summary>
    private async Task<Dictionary<Guid, string>> LoadCompletedByAsync(
        Guid organizationId, Guid[] orderIds, CancellationToken cancellationToken)
    {
        var completed = await dbContext.Visits.AsNoTracking()
            .Where(visit => visit.OrganizationId == organizationId
                && orderIds.Contains(visit.WorkOrderId)
                && visit.Status == VisitStatus.Completed)
            .Select(visit => new { visit.Id, visit.WorkOrderId, visit.VisitNumber, visit.ActualCompletedAt })
            .ToListAsync(cancellationToken);
        var latest = completed
            .GroupBy(visit => visit.WorkOrderId)
            .Select(group => group.OrderByDescending(visit => visit.ActualCompletedAt).ThenByDescending(visit => visit.VisitNumber).First())
            .ToList();
        var latestIds = latest.Select(visit => visit.Id).ToArray();
        var names = await (
            from assignment in dbContext.VisitAssignments.AsNoTracking()
            join profile in dbContext.TechnicianProfiles.AsNoTracking() on assignment.TechnicianId equals profile.Id
            where latestIds.Contains(assignment.VisitId) && assignment.IsPrimary && assignment.UnassignedAt == null
            select new { assignment.VisitId, profile.FirstName, profile.LastName })
            .ToListAsync(cancellationToken);

        return latest
            .Select(visit => (visit.WorkOrderId, Name: names.Where(name => name.VisitId == visit.Id).Select(name => FullName(name.FirstName, name.LastName)).FirstOrDefault()))
            .Where(item => item.Name is not null)
            .ToDictionary(item => item.WorkOrderId, item => item.Name!);
    }
}
