using FieldOps.Application.Features.TechnicianVisits;
using FieldOps.Domain.WorkOrders;

namespace FieldOps.UnitTests.TechnicianVisits;

/// <summary>The acknowledgment field matrix and the work order aggregate rule (mobile-job-completion BR-06, BR-09).</summary>
public class VisitCompletionRulesTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1];

    [Theory]
    [InlineData("signed", "Pat", "customer", true, "ok", true, "signed|Pat|customer|True|ok|-|-")]
    [InlineData("signed", " Pat ", "tenant", true, null, true, "signed|Pat|tenant|True|-|-|-")]
    [InlineData("customer_absent", null, null, false, " Away ", false, "customer_absent|-|-|False|-|Away|Away")]
    [InlineData("customer_refused", " Rita ", null, false, "No", false, "customer_refused|Rita|-|False|-|No|No")]
    [InlineData("remote_confirmation", "Rey", "other", true, "Phone", false, "remote_confirmation|Rey|other|True|Phone|-|Phone")]
    public void Validate_MapsEachMethodToTheSignoffAndVisitColumns(
        string method, string? name, string? relationship, bool reviewed, string? comment, bool withSignature, string expected)
    {
        var errors = new Dictionary<string, string[]>();
        var input = new CompleteInput(
            method, name, relationship, comment, reviewed ? "true" : null, withSignature, withSignature ? Png : null, "image/png");

        var result = VisitCompletionRules.Validate(input, errors);

        Assert.Empty(errors);
        Assert.NotNull(result);
        Assert.Equal(
            expected,
            string.Join(
                '|',
                result.Method,
                result.SignerName ?? "-",
                result.Relationship ?? "-",
                result.ReviewConfirmed,
                result.Comments ?? "-",
                result.AbsenceReason ?? "-",
                result.WithoutSignatureReason ?? "-"));
        Assert.Equal(withSignature, result.Signature is not null);
    }

    [Theory]
    [InlineData("scheduled", "one_time", null, "1:1:in_progress", true)]
    [InlineData("in_progress", "recurring", 3, "1:1:in_progress,2:2:unscheduled", false)]
    [InlineData("in_progress", "recurring", 3, "1:1:in_progress,2:2:completed", false)]
    [InlineData("in_progress", "recurring", 3, "1:1:completed,2:2:in_progress,3:3:in_progress", false)]
    [InlineData("in_progress", "recurring", 3, "1:1:completed,2:2:completed,3:3:in_progress", true)]
    [InlineData("in_progress", "recurring", 2, "1:1:cancelled,2:2:in_progress", true)]
    [InlineData("in_progress", "recurring", 2, "1:1:in_progress,2:2:approved", true)]
    [InlineData("approved_for_billing", "recurring", 2, "1:1:in_progress,2:2:completed", false)]
    [InlineData("completed", "one_time", null, "1:1:in_progress", false)]
    [InlineData("cancelled", "one_time", null, "1:1:in_progress", false)]
    public void CompletesWorkOrder_RequiresAnOpenOrderTheLastOccurrenceAndNoOtherOpenVisit(
        string order, string jobType, int? count, string visits, bool expected)
    {
        var states = visits.Split(',')
            .Select(item => item.Split(':'))
            .Select(parts => new OrderVisitState(
                new Guid(int.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture), 0, 0, new byte[8]),
                int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture),
                Enum.Parse<VisitStatus>(parts[2].Replace("_", string.Empty, StringComparison.Ordinal), ignoreCase: true)))
            .ToList();
        var completing = states.First(state => state.Status == VisitStatus.InProgress).Id;
        var status = Enum.Parse<WorkOrderStatus>(order.Replace("_", string.Empty, StringComparison.Ordinal), ignoreCase: true);

        Assert.Equal(expected, VisitCompletionRules.CompletesWorkOrder(status, jobType, (short?)count, states, completing));
    }
}
