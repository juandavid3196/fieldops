using FieldOps.Application.Features.BillingReview;

namespace FieldOps.UnitTests.BillingReview;

/// <summary>Variance, terms and CSV rules of the completed jobs review (completed-jobs-review AC-06, AC-07, AC-12, AC-17).</summary>
public class BillingReviewRulesTests
{
    private static BilledLine Service(string name, decimal quantity, string unit, int sort = 0, decimal rate = 0m) =>
        new(Guid.NewGuid(), name, true, quantity, unit, 100m, rate, quantity * 100m, 0m, quantity * 100m, sort);

    private static BilledLine Product(Guid id, string name, decimal quantity, int sort = 0) =>
        new(id, name, false, quantity, "ea", 10m, 8m, quantity * 10m, 0m, quantity * 10m, sort);

    // Tolerance = max(15 minutes, 10 % of the approved time); exactly at the tolerance is still no variance.
    [Theory]
    [InlineData(3600, 3600, "none")]
    [InlineData(3600, 4500, "none")]
    [InlineData(3600, 4560, "over")]
    [InlineData(3600, 2700, "none")]
    [InlineData(3600, 2639, "under")]
    [InlineData(14400, 15840, "none")]
    [InlineData(14400, 15841, "over")]
    [InlineData(14400, 12959, "under")]
    [InlineData(7200, 6120, "under")]
    [InlineData(0, 0, "none")]
    [InlineData(0, 60, "over")]
    public void LaborVariance_AppliesToleranceAndZeroApproved(long approved, long actual, string expected) =>
        Assert.Equal(expected, VarianceCalculator.LaborVariance(approved, actual));

    [Fact]
    public void Analyze_BuildsLaborRowsMaterialStatusesAndNotInQuoteRows()
    {
        var hose = Guid.NewGuid();
        var pipe = Guid.NewGuid();
        var untracked = Guid.NewGuid();
        var billed = new[]
        {
            Service("Diagnosis", 1m, "hr", 0, 8.25m),
            Service("Repair", 1.5m, "Hours", 1),
            Service("Cleanup", 2m, "ea", 2),
            Product(hose, "Hose", 2m, 3),
            Product(pipe, "Pipe", 3m, 4),
            Product(untracked, "Valve", 1m, 5),
        };
        var planned = new[]
        {
            new PlannedMaterialFact(Guid.NewGuid(), hose, "Hose", "ea", 2m, 2m),
            new PlannedMaterialFact(Guid.NewGuid(), pipe, "Pipe", "ea", 3m, 5m),
            new PlannedMaterialFact(Guid.NewGuid(), null, "Tape", "ea", 1m, 1m),
        };
        var added = new[] { new AddedMaterialFact("Clamp", "ea", 2m) };

        var result = VarianceCalculator.Analyze(new VarianceFacts(billed, planned, added, 2 * 3600 + 20 * 60 + 59));

        // 2h30m approved over two hourly lines; 2h20m actual is within the 15 minute tolerance.
        Assert.Equal("none", result.LaborVariance);
        Assert.True(result.MaterialVariance);

        var rows = result.Lines;
        Assert.Equal(["quote", "quote", "quote", "quote", "quote", "quote", "labor_total", "not_in_quote", "not_in_quote"], rows.Select(row => row.Kind));
        Assert.Equal(["see_labor_total", "see_labor_total", "matches", "matches", "over", "not_tracked"], rows.Take(6).Select(row => row.Status));
        Assert.Equal([1, 2, 3, 4, 5, 6], rows.Take(6).Select(row => row.Index!.Value));
        Assert.Equal("taxable", rows[0].Tax);
        Assert.Equal("non_taxable", rows[1].Tax);
        Assert.Equal("1h", rows[0].Approved);
        Assert.Null(rows[0].Actual);
        Assert.Equal("5 ea", rows[4].Actual);
        Assert.Null(rows[5].Actual);

        var total = rows[6];
        Assert.Equal(("2h 30m", "2h 20m", "matches", null), (total.Approved, total.Actual, total.Status, total.Amount));

        Assert.Equal(["Clamp", "Tape"], rows.Skip(7).Select(row => row.Name));
        Assert.All(rows.Skip(7), row => Assert.Equal(("not_in_quote", "0", null, null), (row.Status, row.Billable, row.Tax, row.Amount)));
    }

    [Fact]
    public void Analyze_SingleHourlyLineShowsActualTimeAndStatus_AndNoLaborTotalWithoutHourlyLines()
    {
        var single = VarianceCalculator.Analyze(new VarianceFacts([Service("Repair", 2m, "h")], [], [], 6120));

        Assert.Equal("under", single.LaborVariance);
        Assert.Single(single.Lines);
        Assert.Equal(("2h", "1h 42m", "under"), (single.Lines[0].Approved, single.Lines[0].Actual, single.Lines[0].Status));

        // Nothing hourly approved: worked time creates a "Labor total" with "0h" approved, no time creates none.
        var worked = VarianceCalculator.Analyze(new VarianceFacts([Service("Flat fee", 1m, "ea")], [], [], 45 * 60));
        Assert.Equal("over", worked.LaborVariance);
        Assert.Equal(("labor_total", "0h", "45m", "over"), (worked.Lines[1].Kind, worked.Lines[1].Approved, worked.Lines[1].Actual, worked.Lines[1].Status));
        Assert.Single(VarianceCalculator.Analyze(new VarianceFacts([Service("Flat fee", 1m, "ea")], [], [], 0)).Lines);
    }

    [Theory]
    [InlineData("Payment due within 15 days of the invoice date.", "net_15", "2026-10-23", "Net 15")]
    [InlineData("Payment due within 30 days of the invoice date.", "net_30", "2026-11-07", "Net 30")]
    [InlineData("Payment due upon completion.", "due_upon_receipt", "2026-10-08", "Due upon receipt")]
    [InlineData("Net 45, with a 2% early discount.", "due_upon_receipt", "2026-10-08", "Due upon receipt")]
    [InlineData(null, "due_upon_receipt", "2026-10-08", "Due upon receipt")]
    public void Terms_MapFrozenQuoteTextAndComputeDueDate(string? quoteTerms, string code, string due, string label)
    {
        var terms = BillingTerms.FromQuoteTerms(quoteTerms);

        Assert.Equal(code, terms);
        Assert.Equal(DateOnly.Parse(due), BillingTerms.DueDate(new DateOnly(2026, 10, 8), terms));
        Assert.Equal(label, BillingTerms.Label(terms));
    }

    [Theory]
    [InlineData("Plain", "Plain")]
    [InlineData("=SUM(A1)", "'=SUM(A1)")]
    [InlineData("+1", "'+1")]
    [InlineData("-5", "'-5")]
    [InlineData("@cmd", "'@cmd")]
    [InlineData("\tTabbed", "'\tTabbed")]
    [InlineData("Smith, John", "\"Smith, John\"")]
    [InlineData("Say \"hi\"", "\"Say \"\"hi\"\"\"")]
    [InlineData("=1,2", "\"'=1,2\"")]
    [InlineData("Line\nbreak", "\"Line\nbreak\"")]
    [InlineData(null, "")]
    public void CsvCell_NeutralizesFormulasThenQuotes(string? value, string expected) =>
        Assert.Equal(expected, CsvCell.Escape(value));
}
