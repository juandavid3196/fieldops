using FieldOps.Application.Features.BusinessOverview;
using FieldOps.Application.Features.ServiceRequests;
using FieldOps.Domain.WorkOrders;

namespace FieldOps.Application.Features.Reports;

internal sealed class JobProfitabilityProvider(IReportsStore store) : IReportDataProvider
{
    public string Key => ReportKeys.JobProfitability;

    public async Task<ReportResult> LoadAsync(ReportContext context, CancellationToken cancellationToken) =>
        JobProfitabilityBuilder.Build(await store.LoadProfitabilityAsync(context, cancellationToken), context);
}

/// <summary>Job profitability (reports-workspace BR-08). Pure: revenue by completion, the business-overview BR-05 cost and BR-09 target.</summary>
internal static class JobProfitabilityBuilder
{
    private const string Unassigned = "Unassigned";

    private sealed record Line(ProfitabilityOrder Order, bool Invoiced, decimal Revenue, decimal Cost, Guid? InvoiceId)
    {
        public decimal Profit => Revenue - Cost;

        public decimal? Margin => Invoiced ? BusinessOverviewRules.Margin(Profit, Revenue) : null;
    }

    private sealed record Sums(int Jobs, int NotInvoiced, decimal Revenue, decimal Cost)
    {
        public decimal Profit => Revenue - Cost;
    }

    public static ReportResult Build(ProfitabilitySource source, ReportContext context)
    {
        var query = context.Query;
        var revenue = source.Invoices.GroupBy(item => item.OrderId).ToDictionary(group => group.Key, group => group.Sum(item => item.Revenue));
        var latestInvoice = source.Invoices
            .GroupBy(item => item.OrderId)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(item => item.InvoiceNumber).First().InvoiceId);
        var costs = source.Costs.GroupBy(item => item.OrderId).ToDictionary(group => group.Key, group => group.Sum(item => item.Cost));
        var technicians = source.Technicians.ToDictionary(item => item.Id);

        List<Line> Window(DateTimeOffset start, DateTimeOffset end) =>
        [
            .. source.Orders
                .Where(order => order.CompletedAt >= start && order.CompletedAt < end && StatusMatches(query.Status, order.Status))
                .Select(order => new Line(
                    order,
                    revenue.ContainsKey(order.OrderId),
                    revenue.GetValueOrDefault(order.OrderId),
                    costs.GetValueOrDefault(order.OrderId),
                    latestInvoice.GetValueOrDefault(order.OrderId))),
        ];

        var current = Window(context.CurrentStart, context.CurrentEnd);
        var previous = Window(context.PreviousStart, context.CurrentStart);
        var now = Sum(current);
        var before = Sum(previous);

        var rows = current.Select(line => ToRow(line, context, technicians)).ToList();

        var totals = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["revenue"] = ReportRules.Money(now.Revenue),
            ["estimatedCost"] = ReportRules.Money(now.Cost),
            ["grossProfit"] = ReportRules.Money(now.Profit),
            ["margin"] = BusinessOverviewRules.Margin(now.Profit, now.Revenue),
        };

        return new ReportResult(
            [
                ReportRules.Count("completedJobs", "Completed jobs", now.Jobs, before.Jobs),
                ReportRules.Count("notInvoiced", "Not invoiced", now.NotInvoiced, before.NotInvoiced),
                ReportRules.Currency("revenue", "Revenue", now.Revenue, before.Revenue),
                ReportRules.Currency("estimatedCost", "Estimated cost", now.Cost, before.Cost),
                ReportRules.Currency("estimatedGrossProfit", "Estimated gross profit", now.Profit, before.Profit),
                ReportRules.Percent(
                    "estimatedGrossMargin",
                    "Estimated gross margin",
                    BusinessOverviewRules.Margin(now.Profit, now.Revenue),
                    BusinessOverviewRules.Margin(before.Profit, before.Revenue),
                    1),
            ],
            null,
            BuildChart(current),
            rows,
            totals);
    }

    private static bool StatusMatches(string filter, WorkOrderStatus status) =>
        filter switch
        {
            "completed" => status == WorkOrderStatus.Completed,
            "approved_for_billing" => status == WorkOrderStatus.ApprovedForBilling,
            _ => true,
        };

    private static Sums Sum(List<Line> lines) =>
        new(
            lines.Count,
            lines.Count(line => !line.Invoiced),
            lines.Sum(line => line.Revenue),
            lines.Sum(line => line.Cost));

    private static ReportRow ToRow(Line line, ReportContext context, Dictionary<Guid, ReportTechnicianName> technicians)
    {
        var order = line.Order;
        var technician = order.TechnicianId is { } id ? technicians.GetValueOrDefault(id) : null;
        var branch = context.BranchName(order.BranchId);
        var job = RequestCardRules.DisplayNumber(context.Organization.WorkOrderPrefix, order.Number);
        var technicianName = technician?.Name ?? Unassigned;
        decimal? Money(decimal value) => line.Invoiced ? ReportRules.Money(value) : null;

        var cells = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["job"] = job,
            ["completedAt"] = order.CompletedAt.ToUniversalTime(),
            ["customer"] = order.CustomerName,
            ["service"] = order.CategoryName,
            ["technician"] = technicianName,
            ["revenue"] = Money(line.Revenue),
            ["estimatedCost"] = Money(line.Cost),
            ["grossProfit"] = Money(line.Profit),
            ["margin"] = line.Margin,
            ["belowTarget"] = line.Invoiced && BusinessOverviewRules.BelowTarget(line.Revenue, line.Margin),
            ["branch"] = branch,
            ["invoiced"] = line.Invoiced,
            ["invoicing"] = line.Invoiced ? "Invoiced" : "Not invoiced",
        };

        var sort = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["job"] = order.Number,
            ["category"] = order.CategoryName,
        };

        return new ReportRow(
            order.OrderId,
            job,
            order.Number,
            cells,
            ReportRules.Links(
                context.Role,
                job: order.OrderId,
                customer: order.CustomerId,
                invoice: line.InvoiceId,
                technician: technician is { } found && context.Scope.Contains(found.BranchId) ? found.Id : null),
            sort);
    }

    // BR-08: invoiced categories with revenue, by revenue then name; the 10 best plus the rest as "Other categories".
    private static ReportChart BuildChart(List<Line> lines)
    {
        var groups = lines
            .Where(line => line.Invoiced)
            .GroupBy(line => (line.Order.CategoryId, line.Order.CategoryName))
            .Select(group => (Name: group.Key.CategoryName, Revenue: group.Sum(line => line.Revenue), Cost: group.Sum(line => line.Cost)))
            .Where(item => item.Revenue > 0)
            .OrderByDescending(item => item.Revenue)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var points = groups.Take(10).Select(item => Point(item.Name, item.Revenue, item.Cost)).ToList();

        if (groups.Count > 10)
        {
            var rest = groups.Skip(10).ToList();

            points.Add(Point("Other categories", rest.Sum(item => item.Revenue), rest.Sum(item => item.Cost)));
        }

        return new ReportChart(
            "category_bars", [new ReportSeries("revenue", "Revenue"), new ReportSeries("estimatedCost", "Estimated cost")], points);
    }

    private static ReportPoint Point(string label, decimal revenue, decimal cost) =>
        new(
            label,
            null,
            new Dictionary<string, decimal>(StringComparer.Ordinal)
            {
                ["revenue"] = ReportRules.Money(revenue),
                ["estimatedCost"] = ReportRules.Money(cost),
            });
}
