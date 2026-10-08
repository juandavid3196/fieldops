using System.Text.RegularExpressions;
using FieldOps.Application.Features.Access;
using FieldOps.Application.Features.BillingReview;
using FieldOps.Application.Features.Quotes;
using FieldOps.Application.Features.ServiceRequests;
using FieldOps.Domain.WorkOrders;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Persistence;

internal sealed partial class BillingReviewStore
{
    private sealed record QueueRow(
        Guid Id,
        long Number,
        string Title,
        Guid BranchId,
        Guid VersionId,
        string CustomerName,
        DateTimeOffset CompletedAt,
        bool FollowUp);

    private sealed record ClassifiedRow(
        QueueRow Row, decimal ApprovedTotal, string Currency, string Labor, bool Material, bool Ready)
    {
        public bool HasVariance => Labor != BillingCodes.LaborNone || Material;
    }

    private sealed record QueueSet(OrganizationInfo Organization, List<ClassifiedRow> Rows);

    public async Task<BillingOutcome<BillingQueuePage>> GetQueueAsync(
        Guid organizationId, BranchScope scope, BillingQuery query, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (await LoadQueueSetAsync(organizationId, scope, query, now, cancellationToken) is not { } set)
        {
            return new BillingOutcome<BillingQueuePage>.NotFound();
        }

        // Metrics ignore the variance filter and the tab; the tab counts apply the variance filter (BR-06).
        var metricRows = set.Rows;
        var tabbed = metricRows.Where(row => MatchesVariance(row, query.Variance)).ToList();
        var shown = tabbed.Where(row => MatchesTab(row, query.Tab)).ToList();
        var items = shown
            .OrderByDescending(row => row.Row.CompletedAt)
            .ThenByDescending(row => row.Row.Number)
            .Skip((query.Page - 1) * BillingMessages.PageSize)
            .Take(BillingMessages.PageSize)
            .Select(row => new BillingQueueItem(
                row.Row.Id,
                DisplayNumber(set.Organization.WorkOrderPrefix, row.Row.Number),
                row.Row.Title,
                row.Row.CustomerName,
                row.ApprovedTotal,
                row.Currency,
                row.Row.CompletedAt,
                row.Labor,
                row.Material,
                row.Ready,
                row.Row.FollowUp))
            .ToList();

        return new BillingOutcome<BillingQueuePage>.Succeeded(new BillingQueuePage(
            items,
            query.Page,
            BillingMessages.PageSize,
            shown.Count,
            new BillingQueueTabs(tabbed.Count, tabbed.Count(row => row.HasVariance), tabbed.Count(row => row.Ready)),
            new BillingQueueMetrics(
                metricRows.Count,
                metricRows.Count(row => row.HasVariance),
                metricRows.Count(row => row.Ready),
                QuoteCalculator.Money(metricRows.Sum(row => row.ApprovedTotal)),
                set.Organization.Currency)));
    }

    public async Task<BillingOutcome<BillingExport>> ExportAsync(
        Guid organizationId, BranchScope scope, BillingQuery query, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (await LoadQueueSetAsync(organizationId, scope, query, now, cancellationToken) is not { } set)
        {
            return new BillingOutcome<BillingExport>.NotFound();
        }

        var shown = set.Rows
            .Where(row => MatchesVariance(row, query.Variance) && MatchesTab(row, query.Tab))
            .OrderByDescending(row => row.Row.CompletedAt)
            .ThenByDescending(row => row.Row.Number)
            .ToList();

        if (shown.Count > BillingMessages.MaxExportRows)
        {
            return new BillingOutcome<BillingExport>.Conflict(BillingCodes.ExportTooLarge, BillingMessages.ExportTooLargeTitle);
        }

        var branchIds = shown.Select(row => row.Row.BranchId).Distinct().ToArray();
        var branchNames = await dbContext.Branches.AsNoTracking()
            .Where(branch => branch.OrganizationId == organizationId && branchIds.Contains(branch.Id))
            .ToDictionaryAsync(branch => branch.Id, branch => branch.Name, cancellationToken);
        var completedBy = await LoadCompletedByAsync(organizationId, shown.Select(row => row.Row.Id).ToArray(), cancellationToken);
        var zone = OrganizationTime.FindZone(set.Organization.Timezone);

        var rows = shown
            .Select(row => new BillingExportRow(
                DisplayNumber(set.Organization.WorkOrderPrefix, row.Row.Number),
                row.Row.CustomerName,
                row.Row.Title,
                branchNames.GetValueOrDefault(row.Row.BranchId, string.Empty),
                completedBy.GetValueOrDefault(row.Row.Id, string.Empty),
                row.Row.CompletedAt,
                row.ApprovedTotal,
                row.Currency,
                row.Labor,
                row.Material,
                row.Ready,
                row.Row.FollowUp))
            .ToList();

        return new BillingOutcome<BillingExport>.Succeeded(
            new BillingExport(set.Organization.Timezone, OrganizationTime.LocalDate(now, zone), rows));
    }

    /// <summary>The filtered queue (branch, completion date, technician and search) with every row classified; null for a foreign or out-of-scope branch or technician.</summary>
    private async Task<QueueSet?> LoadQueueSetAsync(
        Guid organizationId, BranchScope scope, BillingQuery query, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var organization = await ReadOrganizationAsync(organizationId, cancellationToken);

        if (query.BranchId is { } branchId
            && (!scope.Contains(branchId)
                || !await dbContext.Branches.AsNoTracking().AnyAsync(
                    branch => branch.OrganizationId == organizationId && branch.Id == branchId, cancellationToken)))
        {
            return null;
        }

        if (query.TechnicianId is { } technicianId)
        {
            var profileBranch = await dbContext.TechnicianProfiles.AsNoTracking()
                .Where(profile => profile.OrganizationId == organizationId && profile.Id == technicianId)
                .Select(profile => (Guid?)profile.BranchId)
                .SingleOrDefaultAsync(cancellationToken);

            if (profileBranch is not { } profileBranchId || !scope.Contains(profileBranchId))
            {
                return null;
            }
        }

        var orders = QueueOrders(organizationId, scope);

        if (query.BranchId is { } filterBranch)
        {
            orders = orders.Where(order => order.BranchId == filterBranch);
        }

        if (query.TechnicianId is { } filterTechnician)
        {
            orders = orders.Where(order => dbContext.VisitAssignments.Any(assignment =>
                assignment.TechnicianId == filterTechnician
                && assignment.IsPrimary
                && assignment.UnassignedAt == null
                && dbContext.Visits.Any(visit =>
                    visit.Id == assignment.VisitId
                    && visit.WorkOrderId == order.Id
                    && visit.Status != VisitStatus.Cancelled)));
        }

        if (query.Search is { } search)
        {
            var pattern = $"%{search.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal)}%";
            var number = ParseNumber(search, organization.WorkOrderPrefix);

            orders = orders.Where(order =>
                EF.Functions.ILike(order.Title, pattern)
                || (number != null && order.WorkOrderNumber == number)
                || dbContext.Customers.Any(customer =>
                    customer.OrganizationId == order.OrganizationId
                    && customer.Id == order.CustomerId
                    && EF.Functions.ILike(customer.DisplayName, pattern)));
        }

        var shaped = orders.Select(order => new
        {
            order.Id,
            order.WorkOrderNumber,
            order.Title,
            order.BranchId,
            order.QuoteVersionId,
            order.UpdatedAt,
            FollowUp = order.BillingFollowUpAt != null,
            CustomerName = dbContext.Customers
                .Where(customer => customer.OrganizationId == order.OrganizationId && customer.Id == order.CustomerId)
                .Select(customer => customer.DisplayName)
                .FirstOrDefault(),
            Completed = dbContext.Visits
                .Where(visit => visit.WorkOrderId == order.Id && visit.Status == VisitStatus.Completed)
                .Max(visit => visit.ActualCompletedAt),
        });

        if (CompletedCutoff(query.Completed, now, OrganizationTime.FindZone(organization.Timezone)) is { } cutoff)
        {
            shaped = shaped.Where(row => (row.Completed ?? row.UpdatedAt) >= cutoff);
        }

        var loaded = await shaped.ToListAsync(cancellationToken);
        var rows = loaded
            .Select(row => new QueueRow(
                row.Id,
                row.WorkOrderNumber,
                row.Title,
                row.BranchId,
                row.QuoteVersionId,
                row.CustomerName ?? string.Empty,
                row.Completed ?? row.UpdatedAt,
                row.FollowUp))
            .ToList();

        var facts = await LoadFactsAsync(
            organizationId, rows.Select(row => (row.Id, row.VersionId)).ToList(), organization.RequireSignature, cancellationToken);
        var classified = new List<ClassifiedRow>(rows.Count);

        foreach (var row in rows)
        {
            var fact = facts[row.Id];
            var (labor, material) = VarianceCalculator.Classify(fact.Variance);
            var ready = CompletionVerifier.IsReady(CompletionVerifier.Verify(fact.Verification), labor, material);

            classified.Add(new ClassifiedRow(row, fact.Quote.Total, fact.Quote.Currency, labor, material, ready));
        }

        return new QueueSet(organization, classified);
    }

    /// <summary>00:00 of the local date N-1 days before today in the organization timezone (BR-04); null for all time.</summary>
    private static DateTimeOffset? CompletedCutoff(string range, DateTimeOffset now, TimeZoneInfo zone)
    {
        var days = range switch
        {
            "7d" => 7,
            "30d" => 30,
            "90d" => 90,
            _ => 0,
        };

        return days == 0 ? null : OrganizationTime.StartOfDayUtc(OrganizationTime.LocalDate(now, zone).AddDays(-(days - 1)), zone);
    }

    /// <summary>A work order number written as digits or as <c>&lt;prefix&gt;-&lt;digits&gt;</c> (BR-04).</summary>
    private static long? ParseNumber(string search, string prefix)
    {
        var text = search.Trim();
        var match = Regex.Match(text, @"^(?:(?<prefix>[^\s-]+)-)?(?<digits>\d{1,18})$", RegexOptions.CultureInvariant);

        if (!match.Success)
        {
            return null;
        }

        if (match.Groups["prefix"].Success
            && !string.Equals(match.Groups["prefix"].Value, prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return long.Parse(match.Groups["digits"].Value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static bool MatchesVariance(ClassifiedRow row, string variance) => variance switch
    {
        "none" => !row.HasVariance,
        "any" => row.HasVariance,
        "labor" => row.Labor != BillingCodes.LaborNone,
        "material" => row.Material,
        _ => true,
    };

    private static bool MatchesTab(ClassifiedRow row, string tab) => tab switch
    {
        "variances" => row.HasVariance,
        "ready" => row.Ready,
        _ => true,
    };
}
