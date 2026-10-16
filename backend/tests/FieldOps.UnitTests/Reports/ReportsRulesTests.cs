using System.Net;
using System.Text;
using FieldOps.Application.Features.Access;
using FieldOps.Application.Features.BusinessOverview;
using FieldOps.Application.Features.OperationsDashboard;
using FieldOps.Application.Features.Reports;
using FieldOps.Application.Features.ServiceRequests;
using FieldOps.Domain.WorkOrders;

namespace FieldOps.UnitTests.Reports;

/// <summary>On-time classification, duration variance, CSV layout and escaping of the reports workspace (reports-workspace BR-11, BR-14, BR-15; AC-10, AC-15, AC-16).</summary>
public class ReportsRulesTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 1, 14, 0, 0, TimeSpan.Zero);

    // BR-11: limit = arrival window end, else start + 15 min; started on or before the limit is on time; unstarted past it is "not started"
    // only for scheduled, assigned or on the way visits; every other unstarted visit is not due (not eligible).
    [Theory]
    [InlineData(VisitStatus.Completed, 15, null, 0, ArrivalResult.OnTime)]
    [InlineData(VisitStatus.Completed, 16, null, 0, ArrivalResult.Late)]
    [InlineData(VisitStatus.InProgress, 30, 30, 0, ArrivalResult.OnTime)]
    [InlineData(VisitStatus.InProgress, 31, 30, 0, ArrivalResult.Late)]
    [InlineData(VisitStatus.Scheduled, null, null, 16, ArrivalResult.NotStarted)]
    [InlineData(VisitStatus.Assigned, null, null, 15, ArrivalResult.NotDue)]
    [InlineData(VisitStatus.OnTheWay, null, 30, 31, ArrivalResult.NotStarted)]
    [InlineData(VisitStatus.Paused, null, null, 600, ArrivalResult.NotDue)]
    public void Arrival_LimitReferenceAndMissingTimestamps_ClassifyTheVisit(
        VisitStatus status, int? startedMinutes, int? windowMinutes, int referenceMinutes, ArrivalResult expected)
    {
        var visit = new DashboardArrival(
            status,
            Start,
            windowMinutes is { } window ? Start.AddMinutes(window) : null,
            startedMinutes is { } started ? Start.AddMinutes(started) : null);

        Assert.Equal(expected, OperationsDashboardRules.Arrival(visit, Start.AddMinutes(referenceMinutes)));
    }

    // BR-11: actual = completed - started - pauses, only for the completed group; variance = actual - estimate; missing data is "—".
    [Theory]
    [InlineData(VisitStatus.Completed, 120, 600, 90, 120, 30)]
    [InlineData(VisitStatus.Approved, 150, 0, 120, 150, 30)]
    [InlineData(VisitStatus.NeedsCorrection, 60, 0, 90, 60, -30)]
    [InlineData(VisitStatus.Completed, 60, 0, null, 60, null)]
    [InlineData(VisitStatus.InProgress, 60, 0, 60, null, null)]
    public void Duration_CompletedVisits_ReturnActualAndVariance(
        VisitStatus status, int workedMinutes, int pauseSeconds, int? estimate, int? expectedActual, int? expectedVariance)
    {
        var visit = new JobVisit(
            Guid.NewGuid(), 1, Guid.NewGuid(), 1, "Job", Guid.NewGuid(), "Customer", Guid.NewGuid(), Guid.NewGuid(), estimate, status,
            Start, null, Start, Start.AddMinutes(workedMinutes) + TimeSpan.FromSeconds(pauseSeconds), pauseSeconds, null, null, null);

        var (actual, variance) = JobsOnTimeBuilder.Duration(visit, OperationsDashboardRules.StatusGroup(status));

        Assert.Equal((expectedActual, expectedVariance), (actual, variance));
    }

    // BR-14: two blocks with a Section column, formulas in free text neutralized, numbers (even negative) untouched,
    // the summary Previous column empty without comparison, and no Currency column cell lost.
    [Fact]
    public void Csv_RowsTotalsAndSummary_UseTwoBlocksAndEscapeOnlyFreeText()
    {
        var definition = ReportCatalog.Find(ReportKeys.JobProfitability)!;
        var organization = new ReportOrganization("Acme", "UTC", "USD", "WO", "INV", "Q", "REQ");
        var query = new ReportQuery(
            new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 7), null, "all", null, null, "all", "all", "all", "completedAt", true, 1, 10, "csv", false);
        var context = new ReportContext(
            new MembershipCall(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), IPAddress.Loopback),
            "accounting",
            organization,
            TimeZoneInfo.Utc,
            Start,
            new DateOnly(2026, 10, 7),
            BusinessOverviewRules.ResolvePeriod(query.From, query.To),
            query,
            BranchScope.Everything,
            [],
            new ReportDirectory([], [], []));
        var cells = new Dictionary<string, object?>
        {
            ["job"] = "WO-1",
            ["completedAt"] = Start,
            ["customer"] = "=SUM(A1)",
            ["service"] = "Plumbing, \"deep\"\nclean",
            ["technician"] = "Unassigned",
            ["revenue"] = 100m,
            ["estimatedCost"] = 150m,
            ["grossProfit"] = -50m,
            ["margin"] = -50.0m,
            ["branch"] = "Main",
            ["invoicing"] = "Invoiced",
        };
        var row = new ReportRow(Guid.NewGuid(), "WO-1", 1, cells, ReportLinks.None, new Dictionary<string, object?>());
        var result = new ReportResult(
            [ReportRules.Currency("revenue", "Revenue", 100m, 80m), ReportRules.Percent("margin", "Estimated gross margin", -50m, null, 1)],
            null,
            new ReportChart("category_bars", [], []),
            [row],
            new Dictionary<string, object?> { ["revenue"] = 100m, ["grossProfit"] = -50m });

        var file = ReportsExport.Csv(new ReportExportInput(definition, context, result, [row], false));
        var lines = Encoding.UTF8.GetString(file.Content).Split("\r\n");

        Assert.Equal("job-profitability-2026-10-01-to-2026-10-07.csv", file.FileName);
        Assert.Equal("Section,Job,Completed,Customer,Service,Technician,Revenue,Est. cost,Est. gross profit,Est. margin,Branch,Invoicing,Currency", lines[0]);
        Assert.StartsWith("Rows,WO-1,2026-10-01 14:00,'=SUM(A1),\"Plumbing, \"\"deep\"\"\nclean\",Unassigned,", lines[1], StringComparison.Ordinal);
        Assert.Contains("100.00,150.00,-50.00,-50.0,Main,Invoiced,USD", string.Join("\n", lines));
        Assert.Contains("Totals,,,,,,100.00,,-50.00,,,,USD", string.Join("\n", lines));
        Assert.Contains("\r\n\r\nSection,Metric,Current,Previous,Unit\r\n", Encoding.UTF8.GetString(file.Content));
        Assert.Contains("Summary,Revenue,100.00,,USD", lines);
        Assert.Contains("Summary,Estimated gross margin,-50.0,,percent", lines);
        Assert.Equal(1, file.RowCount);

        // BR-15: the PDF model carries the disclaimer of the profitability report and the formatted rows.
        var pdf = ReportsExport.Pdf(new ReportExportInput(definition, context, result, [row], false), "Acme");

        Assert.Equal(ReportCatalog.Disclaimer, pdf.Disclaimer);
        Assert.Equal("Oct 1, 2026 – Oct 7, 2026", pdf.Period);
        Assert.Equal("Oct 1, 2026 2:00 PM", pdf.Rows[0][1]);
    }
}
