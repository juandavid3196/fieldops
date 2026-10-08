using System.Globalization;

namespace FieldOps.Application.Features.BillingReview;

/// <summary>A billed quote line: a non-optional line or an optional line selected in the approved response (BR-07).</summary>
public sealed record BilledLine(
    Guid Id,
    string Name,
    bool IsService,
    decimal Quantity,
    string Unit,
    decimal UnitPrice,
    decimal TaxRate,
    decimal LineSubtotal,
    decimal LineTax,
    decimal LineTotal,
    int SortOrder);

/// <summary>A planned material with the quantity used across non-cancelled visits.</summary>
public sealed record PlannedMaterialFact(
    Guid Id, Guid? QuoteLineId, string Description, string Unit, decimal PlannedQuantity, decimal UsedQuantity);

/// <summary>A material recorded in a visit without a planned material.</summary>
public sealed record AddedMaterialFact(string Description, string Unit, decimal Quantity);

/// <summary>Everything the pure variance rules need about one work order.</summary>
public sealed record VarianceFacts(
    IReadOnlyList<BilledLine> BilledLines,
    IReadOnlyList<PlannedMaterialFact> PlannedMaterials,
    IReadOnlyList<AddedMaterialFact> AddedMaterials,
    long ActualWorkSeconds);

/// <summary>Labor and material variance of a work order plus the comparison rows (completed-jobs-review BR-07 to BR-09).</summary>
public sealed record VarianceResult(string LaborVariance, bool MaterialVariance, IReadOnlyList<BillingLine> Lines)
{
    public bool HasVariance => LaborVariance != BillingCodes.LaborNone || MaterialVariance;
}

/// <summary>Pure variance rules. Variances never change billed amounts (BR-09).</summary>
public static class VarianceCalculator
{
    private const long MinimumToleranceSeconds = 900;

    private const decimal ToleranceShare = 0.10m;

    private static readonly string[] HourlyUnits = ["h", "hr", "hrs", "hour", "hours"];

    public static bool IsHourly(BilledLine line) =>
        line.IsService && HourlyUnits.Contains(line.Unit.Trim(), StringComparer.OrdinalIgnoreCase);

    /// <summary>Σ quantity × 3600 of the billed hourly lines, rounded to whole seconds (BR-08).</summary>
    public static long ApprovedLaborSeconds(IEnumerable<BilledLine> billed) =>
        (long)Math.Round(billed.Where(IsHourly).Sum(line => line.Quantity * 3600m), MidpointRounding.AwayFromZero);

    /// <summary>Over, under or none by max(15 minutes, 10 % of the approved time); with nothing approved any time is over (BR-08).</summary>
    public static string LaborVariance(long approvedSeconds, long actualSeconds)
    {
        if (approvedSeconds <= 0)
        {
            return actualSeconds > 0 ? BillingCodes.LaborOver : BillingCodes.LaborNone;
        }

        var tolerance = Math.Max(MinimumToleranceSeconds, approvedSeconds * ToleranceShare);
        var difference = actualSeconds - approvedSeconds;

        if (difference > tolerance)
        {
            return BillingCodes.LaborOver;
        }

        return -difference > tolerance ? BillingCodes.LaborUnder : BillingCodes.LaborNone;
    }

    /// <summary>True when a planned material was used in another quantity or a material was added (BR-09).</summary>
    public static bool MaterialVariance(
        IReadOnlyList<PlannedMaterialFact> planned, IReadOnlyList<AddedMaterialFact> added) =>
        added.Count > 0 || planned.Any(material => material.UsedQuantity != material.PlannedQuantity);

    public static (string Labor, bool Material) Classify(VarianceFacts facts) =>
        (LaborVariance(ApprovedLaborSeconds(facts.BilledLines), facts.ActualWorkSeconds),
            MaterialVariance(facts.PlannedMaterials, facts.AddedMaterials));

    public static VarianceResult Analyze(VarianceFacts facts)
    {
        var approvedSeconds = ApprovedLaborSeconds(facts.BilledLines);
        var (labor, material) = Classify(facts);
        var laborStatus = StatusOf(labor);
        var hourlyCount = facts.BilledLines.Count(IsHourly);
        var rows = new List<BillingLine>();
        var index = 0;

        foreach (var line in facts.BilledLines.OrderBy(candidate => candidate.SortOrder).ThenBy(candidate => candidate.Id))
        {
            index++;
            rows.Add(QuoteRow(index, line, facts, hourlyCount, laborStatus));
        }

        if (hourlyCount != 1 && (hourlyCount >= 2 || facts.ActualWorkSeconds > 0))
        {
            rows.Add(new BillingLine(
                null,
                BillingCodes.KindLaborTotal,
                "Labor total",
                approvedSeconds == 0 ? "0h" : FormatDuration(approvedSeconds),
                FormatDuration(facts.ActualWorkSeconds),
                null,
                null,
                null,
                laborStatus));
        }

        foreach (var added in facts.AddedMaterials)
        {
            rows.Add(NotInQuote(added.Description, added.Quantity, added.Unit));
        }

        foreach (var planned in facts.PlannedMaterials.Where(item => item.QuoteLineId is null && item.UsedQuantity > 0m))
        {
            rows.Add(NotInQuote(planned.Description, planned.UsedQuantity, planned.Unit));
        }

        return new VarianceResult(labor, material, rows);
    }

    /// <summary>"&lt;h&gt;h", "&lt;h&gt;h &lt;m&gt;m" or "&lt;m&gt;m" with minutes rounded down (BR-08).</summary>
    public static string FormatDuration(long seconds)
    {
        var minutes = Math.Max(0, seconds) / 60;
        var hours = minutes / 60;
        var rest = minutes % 60;

        if (hours > 0)
        {
            return rest > 0
                ? string.Create(CultureInfo.InvariantCulture, $"{hours}h {rest}m")
                : string.Create(CultureInfo.InvariantCulture, $"{hours}h");
        }

        return string.Create(CultureInfo.InvariantCulture, $"{rest}m");
    }

    /// <summary>The quantity without trailing zeros followed by its unit.</summary>
    public static string FormatQuantity(decimal quantity, string unit) =>
        string.IsNullOrWhiteSpace(unit)
            ? FormatNumber(quantity)
            : string.Create(CultureInfo.InvariantCulture, $"{FormatNumber(quantity)} {unit.Trim()}");

    public static string FormatNumber(decimal quantity) => quantity.ToString("0.###", CultureInfo.InvariantCulture);

    private static string StatusOf(string labor) => labor switch
    {
        BillingCodes.LaborOver => BillingCodes.StatusOver,
        BillingCodes.LaborUnder => BillingCodes.StatusUnder,
        _ => BillingCodes.StatusMatches,
    };

    private static BillingLine QuoteRow(int index, BilledLine line, VarianceFacts facts, int hourlyCount, string laborStatus)
    {
        var tax = line.TaxRate > 0m ? BillingCodes.TaxTaxable : BillingCodes.TaxNonTaxable;

        if (IsHourly(line))
        {
            var duration = FormatDuration((long)Math.Round(line.Quantity * 3600m, MidpointRounding.AwayFromZero));

            return hourlyCount == 1
                ? new BillingLine(index, BillingCodes.KindQuote, line.Name, duration, FormatDuration(facts.ActualWorkSeconds), duration, tax, line.LineSubtotal, laborStatus)
                : new BillingLine(index, BillingCodes.KindQuote, line.Name, duration, null, duration, tax, line.LineSubtotal, BillingCodes.StatusSeeLaborTotal);
        }

        var approved = FormatQuantity(line.Quantity, line.Unit);

        if (line.IsService)
        {
            return new BillingLine(index, BillingCodes.KindQuote, line.Name, approved, approved, approved, tax, line.LineSubtotal, BillingCodes.StatusMatches);
        }

        var linked = facts.PlannedMaterials.Where(material => material.QuoteLineId == line.Id).ToList();

        if (linked.Count == 0)
        {
            return new BillingLine(index, BillingCodes.KindQuote, line.Name, approved, null, approved, tax, line.LineSubtotal, BillingCodes.StatusNotTracked);
        }

        var planned = linked.Sum(material => material.PlannedQuantity);
        var used = linked.Sum(material => material.UsedQuantity);
        var status = used == planned ? BillingCodes.StatusMatches : used > planned ? BillingCodes.StatusOver : BillingCodes.StatusUnder;

        return new BillingLine(index, BillingCodes.KindQuote, line.Name, approved, FormatQuantity(used, line.Unit), approved, tax, line.LineSubtotal, status);
    }

    private static BillingLine NotInQuote(string description, decimal quantity, string unit) =>
        new(null, BillingCodes.KindNotInQuote, description, null, FormatQuantity(quantity, unit), "0", null, null, BillingCodes.StatusNotInQuote);
}
