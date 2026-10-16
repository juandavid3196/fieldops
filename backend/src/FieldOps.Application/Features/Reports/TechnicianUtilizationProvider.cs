using FieldOps.Application.Features.BusinessOverview;
using FieldOps.Application.Features.OperationsDashboard;
using FieldOps.Domain.WorkOrders;

namespace FieldOps.Application.Features.Reports;

internal sealed class TechnicianUtilizationProvider(IReportsStore store) : IReportDataProvider
{
    public string Key => ReportKeys.TechnicianUtilization;

    public async Task<ReportResult> LoadAsync(ReportContext context, CancellationToken cancellationToken) =>
        TechnicianUtilizationBuilder.Build(await store.LoadUtilizationAsync(context, cancellationToken), context);
}

/// <summary>Technician utilization (reports-workspace BR-12): the capacity of business-overview BR-17 per technician plus delivery.</summary>
internal static class TechnicianUtilizationBuilder
{
    private sealed record Line(UtilizationTechnician Technician, BusinessOverviewBuilder.Load Load, int Completed, int? OnTime);

    public static ReportResult Build(UtilizationSource source, ReportContext context)
    {
        var period = context.Period;
        var lines = new List<Line>();
        double previousScheduled = 0;
        double previousAvailable = 0;
        double previousOvertime = 0;

        foreach (var technician in source.Technicians)
        {
            var one = new[] { new BusinessTechnician(technician.Id, technician.Schedule, technician.Assessments) };
            var load = BusinessOverviewBuilder.CapacityLoad(one, period.From, period.Days, context.Zone);
            var before = BusinessOverviewBuilder.CapacityLoad(one, period.PreviousFrom, period.Days, context.Zone);
            var visits = source.Visits.Where(visit => visit.TechnicianId == technician.Id).ToList();

            var completed = visits.Count(visit =>
                IsCompleted(visit.Status)
                && visit.ActualCompletedAt >= context.CurrentStart
                && visit.ActualCompletedAt < context.CurrentEnd);
            var (eligible, onTime) = OperationsDashboardRules.OnTime(
                visits
                    .Where(visit => visit.ScheduledStart >= context.CurrentStart && visit.ScheduledStart < context.CurrentEnd)
                    .Select(visit => new DashboardArrival(visit.Status, visit.ScheduledStart, visit.ArrivalWindowEnd, visit.ActualStartedAt)),
                context.Reference);

            previousScheduled += before.Scheduled;
            previousAvailable += before.Available;
            previousOvertime += before.Overtime;
            lines.Add(new Line(technician, load, completed, OperationsDashboardRules.Percent(onTime, eligible)));
        }

        var scheduled = lines.Sum(line => line.Load.Scheduled);
        var available = lines.Sum(line => line.Load.Available);
        var overtime = lines.Sum(line => line.Load.Overtime);
        var rows = lines.Select(line => ToRow(line, context)).ToList();

        var totals = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["availableMinutes"] = ReportRules.WholeMinutes(available),
            ["scheduledMinutes"] = ReportRules.WholeMinutes(scheduled),
            ["utilization"] = OperationsDashboardRules.Percent(scheduled, available),
            ["overtimeMinutes"] = ReportRules.WholeMinutes(overtime),
            ["completedVisits"] = lines.Sum(line => line.Completed),
        };

        return new ReportResult(
            [
                ReportRules.Count("technicians", "Technicians", lines.Count, null),
                ReportRules.HoursMetric("availableHours", "Available hours", available, previousAvailable),
                ReportRules.HoursMetric("scheduledHours", "Scheduled hours", scheduled, previousScheduled),
                ReportRules.Percent(
                    "utilization",
                    "Utilization",
                    OperationsDashboardRules.Percent(scheduled, available),
                    OperationsDashboardRules.Percent(previousScheduled, previousAvailable)),
                ReportRules.HoursMetric("overtimeHours", "Scheduled overtime", overtime, previousOvertime),
            ],
            null,
            BuildChart(lines),
            rows,
            totals);
    }

    private static bool IsCompleted(VisitStatus status) =>
        status is VisitStatus.Completed or VisitStatus.NeedsCorrection or VisitStatus.Approved;

    private static ReportRow ToRow(Line line, ReportContext context)
    {
        var technician = line.Technician;
        var utilization = OperationsDashboardRules.Percent(line.Load.Scheduled, line.Load.Available);

        var cells = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["technician"] = technician.Name,
            ["branch"] = context.BranchName(technician.BranchId),
            ["availableMinutes"] = ReportRules.WholeMinutes(line.Load.Available),
            ["scheduledMinutes"] = ReportRules.WholeMinutes(line.Load.Scheduled),
            ["utilization"] = utilization,
            ["overtimeMinutes"] = ReportRules.WholeMinutes(line.Load.Overtime),
            ["completedVisits"] = line.Completed,
            ["onTime"] = line.OnTime,
        };

        return new ReportRow(
            technician.Id,
            technician.Name,
            0,
            cells,
            ReportRules.Links(context.Role, technician: technician.Id),
            new Dictionary<string, object?>(StringComparer.Ordinal));
    }

    // Horizontal bars of the 15 technicians with the most scheduled hours; the percent is shown as text.
    private static ReportChart BuildChart(List<Line> lines) =>
        new(
            "technician_bars",
            [new ReportSeries("utilization", "Utilization")],
            [.. lines
                .OrderByDescending(line => line.Load.Scheduled)
                .ThenBy(line => line.Technician.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(line => line.Technician.Id)
                .Take(15)
                .Select(line => new ReportPoint(
                    line.Technician.Name,
                    null,
                    new Dictionary<string, decimal>(StringComparer.Ordinal)
                    {
                        ["utilization"] = OperationsDashboardRules.Percent(line.Load.Scheduled, line.Load.Available) ?? 0,
                    }))]);
}
