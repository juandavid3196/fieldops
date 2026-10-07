using FieldOps.Application.Features.TechnicianVisits;

namespace FieldOps.UnitTests.TechnicianVisits;

/// <summary>Quantity limits of the planned and additional materials (mobile-job-progress BR-09, BR-10).</summary>
public class VisitProgressRulesTests
{
    [Theory]
    [InlineData("1.5", true, true)]
    [InlineData("1.500", true, true)]
    [InlineData("1.2300", true, true)]
    [InlineData("99999.999", true, true)]
    [InlineData("0", true, true)]
    [InlineData("0", false, false)]
    [InlineData("0.001", false, true)]
    [InlineData("1.2345", true, false)]
    [InlineData("99999.9991", true, false)]
    [InlineData("100000", true, false)]
    [InlineData("-0.5", true, false)]
    [InlineData(null, true, false)]
    public void Quantity_AcceptsUpToThreeDecimalsWithinTheLimits(string? text, bool allowZero, bool valid)
    {
        decimal? quantity = text is null ? null : decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        var errors = new Dictionary<string, string[]>();

        var result = VisitProgressRules.Quantity(quantity, allowZero, "quantity", errors);

        Assert.Equal(valid, result is not null);
        Assert.Equal(!valid, errors.ContainsKey("quantity"));
    }
}
