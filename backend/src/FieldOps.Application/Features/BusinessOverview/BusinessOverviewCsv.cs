using System.Globalization;
using FieldOps.Application.Features.BillingReview;

namespace FieldOps.Application.Features.BusinessOverview;

/// <summary>The CSV of business-overview BR-20. Aggregates only; values are those of the overview view.</summary>
public static class BusinessOverviewCsv
{
    private static readonly string[] Header = ["Section", "Metric", "Dimension", "Current", "Previous", "Unit"];

    public static BusinessExportFile Build(BusinessOverviewView view, bool compare)
    {
        var rows = new List<string>
        {
            CsvCell.Row(Header),
        };
        var currency = view.Currency;

        void Add(string section, string metric, string dimension, string? current, string? previous, string unit) =>
            rows.Add(CsvCell.Row([section, metric, dimension, current, compare ? previous : null, unit]));

        var kpis = view.Kpis;

        Add("KPIs", "Revenue", string.Empty, Money(kpis.Revenue.Current), Money(kpis.Revenue.Previous), currency);
        Add("KPIs", "Estimated gross profit", string.Empty, Money(kpis.GrossProfit.Current), Money(kpis.GrossProfit.Previous), currency);
        Add("KPIs", "Estimated gross margin", string.Empty, Percent(kpis.GrossMarginPercent.Current), Percent(kpis.GrossMarginPercent.Previous), "percent");
        Add("KPIs", "Quote conversion", string.Empty, Count(kpis.QuoteConversion.Percent), null, "percent");
        Add("KPIs", "Outstanding invoices", string.Empty, Money(kpis.Outstanding.Amount), null, currency);
        Add("KPIs", "Overdue amount", string.Empty, Money(kpis.Outstanding.OverdueAmount), null, currency);
        Add("KPIs", "Jobs completed", string.Empty, Count(kpis.JobsCompleted.Current), Count(kpis.JobsCompleted.Previous), "count");

        foreach (var point in view.Series)
        {
            var start = point.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            Add("Chart", "Revenue", start, Money(point.Revenue), Money(point.PreviousRevenue), currency);
            Add("Chart", "Estimated profit", start, Money(point.GrossProfit), Money(point.PreviousGrossProfit), currency);
            Add("Chart", "Cash collected", start, Money(point.Collected), Money(point.PreviousCollected), currency);
        }

        var cash = view.CashFlow;

        Add("Cash flow", "Invoiced", string.Empty, Money(cash.Invoiced), null, currency);
        Add("Cash flow", "Collected", string.Empty, Money(cash.Collected), null, currency);
        Add("Cash flow", "Outstanding", string.Empty, Money(cash.Outstanding), null, currency);
        Add("Cash flow", "Overdue", string.Empty, Money(cash.Overdue), null, currency);
        Add("Aging", "Current", string.Empty, Money(cash.Aging.Current), null, currency);
        Add("Aging", "1-30 days", string.Empty, Money(cash.Aging.Days1To30), null, currency);
        Add("Aging", "31-60 days", string.Empty, Money(cash.Aging.Days31To60), null, currency);
        Add("Aging", "60+ days", string.Empty, Money(cash.Aging.Days60Plus), null, currency);

        var pipeline = view.Pipeline;

        Add("Sales pipeline", "Requests", string.Empty, Count(pipeline.Requests), null, "count");
        Add("Sales pipeline", "Quotes sent", string.Empty, Count(pipeline.QuotesSent), null, "count");
        Add("Sales pipeline", "Accepted", string.Empty, Count(pipeline.Accepted), null, "count");
        Add("Sales pipeline", "Jobs created", string.Empty, Count(pipeline.JobsCreated), null, "count");

        foreach (var branch in view.Branches)
        {
            var name = branch.IsActive ? branch.Name : $"{branch.Name} (inactive)";

            Add("Branch performance", "Revenue", name, Money(branch.Revenue), null, currency);
            Add("Branch performance", "Estimated margin", name, Percent(branch.MarginPercent), null, "percent");
            Add("Branch performance", "Jobs completed", name, Count(branch.JobsCompleted), null, "count");
            Add("Branch performance", "On-time", name, Count(branch.OnTimePercent), null, "percent");
            Add("Branch performance", "Rating", name, branch.Rating?.ToString("0.0", CultureInfo.InvariantCulture), null, "rating");
        }

        if (view.BranchTotals is { } totals)
        {
            Add("Branch performance", "Revenue", "Total", Money(totals.Revenue), null, currency);
            Add("Branch performance", "Estimated margin", "Total", Percent(totals.MarginPercent), null, "percent");
            Add("Branch performance", "Jobs completed", "Total", Count(totals.JobsCompleted), null, "count");
            Add("Branch performance", "On-time", "Total", Count(totals.OnTimePercent), null, "percent");
        }

        foreach (var service in view.Services)
        {
            Add("Service profitability", "Revenue", service.Name, Money(service.Revenue), null, currency);
            Add("Service profitability", "Estimated margin", service.Name, Percent(service.MarginPercent), null, "percent");
            Add("Service profitability", "Average job value", service.Name, Money(service.AverageJobValue), null, currency);
        }

        var team = view.Team;

        Add("Team & capacity", "Technicians", string.Empty, Count(team.Technicians), null, "count");
        Add("Team & capacity", "Utilization", string.Empty, Count(team.UtilizationPercent), null, "percent");
        Add("Team & capacity", "Scheduled overtime", string.Empty, Count(Hours(team.OvertimeMinutes)), Count(Hours(team.PreviousOvertimeMinutes)), "hours");
        Add("Team & capacity", "Next 7 days (scheduled)", string.Empty, Count(team.Next7DaysPercent), null, "percent");

        var fileName = string.Create(
            CultureInfo.InvariantCulture, $"business-overview-{view.From:yyyy-MM-dd}-to-{view.To:yyyy-MM-dd}.csv");

        return new BusinessExportFile(CsvCell.Document(rows), fileName);
    }

    private static string Money(decimal value) => BusinessOverviewRules.Money(value);

    private static string? Percent(decimal? value) => value?.ToString("0.0", CultureInfo.InvariantCulture);

    private static string? Count(int? value) => value?.ToString(CultureInfo.InvariantCulture);

    private static int Hours(int minutes) => (int)Math.Round(minutes / 60d, MidpointRounding.AwayFromZero);
}
