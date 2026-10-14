using FieldOps.Application.Features.InvoicePayments;
using FieldOps.Application.Features.OperationsDashboard;
using FieldOps.Application.Features.Quotes;
using FieldOps.Application.Features.ServiceRequests;
using FieldOps.Application.Features.Team;
using FieldOps.Domain.Invoices;

namespace FieldOps.Application.Features.BusinessOverview;

/// <summary>Assembles the business overview from plain store data (business-overview BR-05 to BR-18). Pure.</summary>
public static class BusinessOverviewBuilder
{
    private const string OverdueType = "overdue_invoices";

    private const string BranchMarginType = "branch_margin";

    private const string ServiceMarginType = "service_margin";

    private const string OvertimeType = "overtime";

    private sealed record Sums(decimal Revenue, decimal Total, decimal Cost)
    {
        public decimal Profit => Revenue - Cost;
    }

    private sealed record Load(double Scheduled, double Available, double Overtime);

    public static BusinessOverviewView Build(
        BusinessOverviewSource source, BusinessOverviewQuery query, string currency, DateTimeOffset now)
    {
        var period = query.Period;
        var zone = OrganizationTime.FindZone(query.TimezoneId);
        var today = query.Today;
        var currentStart = OrganizationTime.StartOfDayUtc(period.From, zone);
        var currentEnd = OrganizationTime.StartOfDayUtc(period.To.AddDays(1), zone);
        var previousStart = OrganizationTime.StartOfDayUtc(period.PreviousFrom, zone);

        var current = SumInvoices(source.Invoices, period.From, period.To);
        var previous = SumInvoices(source.Invoices, period.PreviousFrom, period.PreviousTo);

        var completedCurrent = source.Completions.Count(item => item.CompletedAt >= currentStart && item.CompletedAt < currentEnd);
        var completedPrevious = source.Completions.Count(item => item.CompletedAt >= previousStart && item.CompletedAt < currentStart);

        // Outstanding, overdue and aging are as of today and independent of the period (BR-07, BR-13).
        var buckets = new decimal[4];
        var overdueCount = 0;

        foreach (var due in source.Outstanding)
        {
            buckets[(int)InvoiceHubRules.Bucket(due.DueDate, today)] += due.Balance;

            if (InvoiceHubRules.Display(InvoiceStatus.Sent, due.DueDate, today).DaysOverdue is not null)
            {
                overdueCount += due.Count;
            }
        }

        var outstanding = buckets.Sum();
        var overdue = buckets[1] + buckets[2] + buckets[3];
        var collected = Collected(source.Collections, period.From, period.To, zone);

        var branches = BuildBranches(source, period, currentStart, currentEnd, now, out var totals);
        var services = BuildServices(source, period);
        var team = BuildTeam(source, period, today, zone);

        var view = new BusinessOverviewView(
            period.From,
            period.To,
            period.PreviousFrom,
            period.PreviousTo,
            query.TimezoneId,
            currency,
            BusinessOverviewMessages.MarginTarget,
            period.Granularity,
            new BusinessKpisView(
                new MoneyPair(Money(current.Revenue), Money(previous.Revenue)),
                new MoneyPair(Money(current.Profit), Money(previous.Profit)),
                new PercentPair(
                    BusinessOverviewRules.Margin(current.Profit, current.Revenue),
                    BusinessOverviewRules.Margin(previous.Profit, previous.Revenue)),
                new QuoteConversionView(
                    source.QuotesAccepted, source.QuotesSent, BusinessOverviewRules.ConversionPercent(source.QuotesAccepted, source.QuotesSent)),
                new OutstandingView(Money(outstanding), Money(overdue), overdueCount),
                new CountPair(completedCurrent, completedPrevious)),
            BuildSeries(source, period, zone),
            new CashFlowView(
                Money(current.Total),
                Money(collected),
                Money(outstanding),
                Money(overdue),
                new AgingView(Money(buckets[0]), Money(buckets[1]), Money(buckets[2]), Money(buckets[3]))),
            new SalesPipelineView(source.Requests, source.QuotesSent, source.QuotesAccepted, source.JobsCreated),
            branches,
            totals,
            services,
            team,
            []);

        return view with { Alerts = BuildAlerts(view, query.BranchId is null) };
    }

    private static Sums SumInvoices(IEnumerable<BusinessInvoiceFact> facts, DateOnly from, DateOnly to)
    {
        decimal revenue = 0;
        decimal total = 0;
        decimal cost = 0;

        foreach (var fact in facts.Where(item => item.IssueDate >= from && item.IssueDate <= to))
        {
            revenue += fact.Revenue;
            total += fact.Total;
            cost += fact.Cost;
        }

        return new Sums(revenue, total, cost);
    }

    private static decimal Collected(IEnumerable<BusinessCollectionFact> facts, DateOnly from, DateOnly to, TimeZoneInfo zone) =>
        facts
            .Where(item =>
            {
                var date = OrganizationTime.LocalDate(item.PaidAt, zone);

                return date >= from && date <= to;
            })
            .Sum(item => BusinessOverviewRules.NetAllocation(item.Allocation, item.PaymentAmount, item.Refunded));

    private static List<SeriesPointView> BuildSeries(BusinessOverviewSource source, BusinessPeriod period, TimeZoneInfo zone) =>
        [.. period.Buckets.Select(bucket =>
        {
            var shift = -period.Days;
            var current = SumInvoices(source.Invoices, bucket.Start, bucket.End);
            var previous = SumInvoices(source.Invoices, bucket.Start.AddDays(shift), bucket.End.AddDays(shift));

            return new SeriesPointView(
                bucket.Start,
                bucket.End,
                Money(current.Revenue),
                Money(current.Profit),
                Money(Collected(source.Collections, bucket.Start, bucket.End, zone)),
                Money(previous.Revenue),
                Money(previous.Profit),
                Money(Collected(source.Collections, bucket.Start.AddDays(shift), bucket.End.AddDays(shift), zone)));
        })];

    private static List<BranchRowView> BuildBranches(
        BusinessOverviewSource source,
        BusinessPeriod period,
        DateTimeOffset currentStart,
        DateTimeOffset currentEnd,
        DateTimeOffset now,
        out BranchTotalsView? totals)
    {
        var reference = now < currentEnd ? now : currentEnd;
        var rows = new List<(BranchRowView Row, IReadOnlyList<DashboardArrival> Arrivals, decimal Profit)>();

        foreach (var branch in source.Branches)
        {
            var sums = SumInvoices(source.Invoices.Where(item => item.BranchId == branch.Id), period.From, period.To);
            var jobs = source.Completions.Count(item =>
                item.BranchId == branch.Id && item.CompletedAt >= currentStart && item.CompletedAt < currentEnd);
            var arrivals = source.Arrivals.Where(item => item.BranchId == branch.Id).Select(item => item.Arrival).ToList();
            var (eligible, onTime) = OperationsDashboardRules.OnTime(arrivals, reference);
            var reviews = source.Reviews.Where(item => item.BranchId == branch.Id).ToList();
            var reviewCount = reviews.Sum(item => item.Count);

            if (!branch.IsActive && sums.Revenue == 0 && jobs == 0 && eligible == 0 && reviewCount == 0)
            {
                continue;
            }

            var margin = BusinessOverviewRules.Margin(sums.Profit, sums.Revenue);

            rows.Add((
                new BranchRowView(
                    branch.Id,
                    branch.Name,
                    branch.IsActive,
                    Money(sums.Revenue),
                    Money(sums.Profit),
                    margin,
                    jobs,
                    OperationsDashboardRules.Percent(onTime, eligible),
                    reviewCount > 0
                        ? Math.Round((decimal)reviews.Sum(item => item.RatingSum) / reviewCount, 1, MidpointRounding.AwayFromZero)
                        : null,
                    reviewCount),
                arrivals,
                sums.Profit));
        }

        var ordered = rows
            .OrderByDescending(item => item.Row.Revenue)
            .ThenBy(item => item.Row.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Row.BranchId)
            .ToList();

        totals = null;

        if (ordered.Count >= 2)
        {
            var revenue = ordered.Sum(item => item.Row.Revenue);
            var profit = ordered.Sum(item => item.Profit);
            var (eligible, onTime) = OperationsDashboardRules.OnTime(ordered.SelectMany(item => item.Arrivals), reference);

            totals = new BranchTotalsView(
                revenue,
                BusinessOverviewRules.Margin(profit, revenue),
                ordered.Sum(item => item.Row.JobsCompleted),
                OperationsDashboardRules.Percent(onTime, eligible));
        }

        return [.. ordered.Select(item => item.Row)];
    }

    private static List<ServiceRowView> BuildServices(BusinessOverviewSource source, BusinessPeriod period)
    {
        var names = source.Categories.ToDictionary(item => item.Id, item => item.Name);
        var jobs = source.ServiceJobs.ToDictionary(item => item.CategoryId, item => item.Jobs);

        return [.. source.Invoices
            .Where(item => item.IssueDate >= period.From && item.IssueDate <= period.To)
            .GroupBy(item => item.CategoryId)
            .Select(group => (Id: group.Key, Revenue: group.Sum(item => item.Revenue), Cost: group.Sum(item => item.Cost)))
            .Where(item => item.Revenue > 0)
            .Select(item =>
            {
                var count = jobs.GetValueOrDefault(item.Id);
                var profit = item.Revenue - item.Cost;

                return new ServiceRowView(
                    item.Id,
                    names.GetValueOrDefault(item.Id) ?? string.Empty,
                    Money(item.Revenue),
                    Money(profit),
                    BusinessOverviewRules.Margin(profit, item.Revenue),
                    count,
                    count > 0 ? Money(item.Revenue / count) : 0m);
            })
            .OrderByDescending(row => row.Revenue)
            .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.CategoryId)];
    }

    private static TeamCapacityView BuildTeam(BusinessOverviewSource source, BusinessPeriod period, DateOnly today, TimeZoneInfo zone)
    {
        var current = CapacityLoad(source.Technicians, period.From, period.Days, zone);
        var previous = CapacityLoad(source.Technicians, period.PreviousFrom, period.Days, zone);
        var next = CapacityLoad(source.Technicians, today, 7, zone);

        return new TeamCapacityView(
            source.Technicians.Count,
            OperationsDashboardRules.Percent(current.Scheduled, current.Available),
            Whole(current.Scheduled),
            Whole(current.Available),
            Whole(current.Overtime),
            Whole(previous.Overtime),
            OperationsDashboardRules.Percent(next.Scheduled, next.Available));
    }

    // Per technician and local day, the workload and availability of operations-dashboard BR-08 (visits plus assessments).
    private static Load CapacityLoad(IReadOnlyList<BusinessTechnician> technicians, DateOnly start, int days, TimeZoneInfo zone)
    {
        double scheduled = 0;
        double available = 0;
        double overtime = 0;

        foreach (var technician in technicians)
        {
            var commitments = technician.Assessments
                .Select(item => new SlotCommitment(item.Start, item.End, CommitmentKinds.Assessment, "Assessment", null))
                .ToList();

            for (var offset = 0; offset < days; offset++)
            {
                var dayStart = OrganizationTime.StartOfDayUtc(start.AddDays(offset), zone);
                var dayEnd = OrganizationTime.StartOfDayUtc(start.AddDays(offset + 1), zone);
                var workload = AssessmentSlotEvaluator.Workload(technician.Schedule, commitments, dayStart, dayEnd);
                var minutes = TechnicianAvailabilityCalculator.AvailableMinutes(technician.Schedule, dayStart, dayEnd);

                scheduled += workload.ScheduledMinutes;
                available += minutes;
                overtime += Math.Max(0, workload.ScheduledMinutes - minutes);
            }
        }

        return new Load(scheduled, available, overtime);
    }

    // BR-18 order: overdue invoices, below-target branches (All only), below-target services, rising overtime.
    private static List<OwnerAlertView> BuildAlerts(BusinessOverviewView view, bool allBranches)
    {
        var alerts = new List<OwnerAlertView>();
        var outstanding = view.Kpis.Outstanding;

        if (outstanding.OverdueCount > 0)
        {
            alerts.Add(new OwnerAlertView(
                OverdueType, "critical", outstanding.OverdueCount, outstanding.OverdueAmount, null, null, null, null, null, null, null));
        }

        if (allBranches)
        {
            alerts.AddRange(view.Branches
                .Where(row => row.IsActive && BusinessOverviewRules.BelowTarget(row.Revenue, row.MarginPercent))
                .OrderBy(row => row.MarginPercent)
                .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
                .Select(row => new OwnerAlertView(
                    BranchMarginType, "warning", null, null, row.BranchId, null, row.Name, row.MarginPercent, null, null, null)));
        }

        alerts.AddRange(view.Services
            .Where(row => BusinessOverviewRules.BelowTarget(row.Revenue, row.MarginPercent))
            .OrderBy(row => row.MarginPercent)
            .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .Select(row => new OwnerAlertView(
                ServiceMarginType, "warning", null, null, null, row.CategoryId, row.Name, row.MarginPercent, null, null, null)));

        var team = view.Team;

        if (team.PreviousOvertimeMinutes > 0
            && BusinessOverviewRules.ChangePercent(team.OvertimeMinutes, team.PreviousOvertimeMinutes) is { } change
            && change >= BusinessOverviewMessages.OvertimeAlertPercent)
        {
            alerts.Add(new OwnerAlertView(
                OvertimeType, "warning", null, null, null, null, null, null, team.OvertimeMinutes, team.PreviousOvertimeMinutes, change));
        }

        return alerts;
    }

    private static decimal Money(decimal value) => QuoteCalculator.Money(value);

    private static int Whole(double minutes) => (int)Math.Round(minutes, MidpointRounding.AwayFromZero);
}
