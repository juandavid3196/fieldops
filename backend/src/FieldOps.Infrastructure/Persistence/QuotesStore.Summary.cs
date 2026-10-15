using FieldOps.Application.Features.Access;
using FieldOps.Application.Features.Quotes;
using FieldOps.Application.Features.ServiceRequests;
using FieldOps.Domain.Requests;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Persistence;

internal sealed partial class QuotesStore
{
    /// <summary>
    /// BR-11 and BR-12: the metrics of the effective branch set only. The organization time zone work is done here in C#,
    /// and the queries receive the day and month bounds as values.
    /// </summary>
    public async Task<QuotesSummary> SummaryAsync(
        Guid organizationId, BranchScope scope, Guid? branchId, CancellationToken cancellationToken)
    {
        var organization = await ReadOrganizationAsync(organizationId, cancellationToken);
        var currency = organization.Currency;
        var zone = OrganizationTime.FindZone(organization.Timezone);
        var today = OrganizationTime.LocalDate(timeProvider.GetUtcNow(), zone);
        var attentionEnd = today.AddDays(QuotesWorkspaceRules.AttentionDays);
        var monthStart = OrganizationTime.StartOfDayUtc(new DateOnly(today.Year, today.Month, 1), zone);
        var nextMonthStart = OrganizationTime.StartOfDayUtc(new DateOnly(today.Year, today.Month, 1).AddMonths(1), zone);

        var rows = Rows(organizationId, scope, branchId, today);

        // Amounts only add the shown versions in the organization currency; the others are counted, not summed.
        var open = await rows
            .Where(row => row.DisplayStatus == QuoteDisplayStatus.Draft
                || row.DisplayStatus == QuoteDisplayStatus.AwaitingResponse
                || row.DisplayStatus == QuoteDisplayStatus.ClarificationRequested)
            .GroupBy(row => row.DisplayStatus)
            .Select(group => new
            {
                Status = group.Key,
                Count = group.Count(),
                Amount = group.Sum(row => row.ShownTotal != null && row.ShownCurrency == currency ? row.ShownTotal.Value : 0m),
            })
            .ToListAsync(cancellationToken);

        var attention = await rows
            .Where(row => row.DisplayStatus == QuoteDisplayStatus.ClarificationRequested
                || (row.DisplayStatus == QuoteDisplayStatus.AwaitingResponse
                    && row.ShownValidUntil != null
                    && row.ShownValidUntil >= today
                    && row.ShownValidUntil <= attentionEnd))
            .CountAsync(cancellationToken);

        var approved = await rows
            .Where(row => row.DisplayStatus == QuoteDisplayStatus.Approved
                && row.RespondedAt != null
                && row.RespondedAt >= monthStart
                && row.RespondedAt < nextMonthStart)
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Count = group.Count(),
                Amount = group.Sum(row => row.ResponseTotal != null && row.ShownCurrency == currency ? row.ResponseTotal.Value : 0m),
            })
            .FirstOrDefaultAsync(cancellationToken);

        var requests = dbContext.ServiceRequests.AsNoTracking()
            .Where(request => request.OrganizationId == organizationId && request.Status == RequestStatus.ReadyForQuote);

        if (!scope.All)
        {
            var ids = scope.BranchIds.ToArray();
            requests = requests.Where(request => request.BranchId == null || ids.Contains(request.BranchId.Value));
        }

        if (branchId is { } selected)
        {
            requests = requests.Where(request => request.BranchId == selected);
        }

        var ready = await requests.CountAsync(cancellationToken);
        var drafts = open.FirstOrDefault(row => row.Status == QuoteDisplayStatus.Draft);
        var awaiting = open.Where(row => row.Status != QuoteDisplayStatus.Draft).ToList();

        return new QuotesSummary(
            currency,
            new QuotesMetric(drafts?.Count ?? 0, drafts?.Amount ?? 0m),
            new QuotesMetric(awaiting.Sum(row => row.Count), awaiting.Sum(row => row.Amount)),
            new QuotesMetric(approved?.Count ?? 0, approved?.Amount ?? 0m),
            new QuotesAttentionMetric(attention),
            ready);
    }
}
