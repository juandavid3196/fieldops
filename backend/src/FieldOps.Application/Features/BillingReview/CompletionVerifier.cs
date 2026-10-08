using System.Globalization;

namespace FieldOps.Application.Features.BillingReview;

/// <summary>Aggregated completion data of a work order over its non-cancelled visits (BR-11).</summary>
public sealed record VerificationFacts(
    int ChecklistTotal,
    int ChecklistCompleted,
    int RequiredIncomplete,
    int VisitsMissingPhotos,
    int VisitsMissingAcknowledgment,
    int VisitsNotSigned,
    bool MaterialsRecorded,
    bool SummaryRecorded,
    long ActualWorkSeconds,
    bool RequireSignature,
    string? LatestMethod,
    string? LatestSignerName);

/// <summary>Completion verification shared by the queue, the detail and the invoice generation (BR-11, BR-18).</summary>
public static class CompletionVerifier
{
    public static IReadOnlyList<VerificationItem> Verify(VerificationFacts facts)
    {
        var photos = facts.VisitsMissingPhotos == 0;
        var acknowledged = facts.VisitsMissingAcknowledgment == 0 && (!facts.RequireSignature || facts.VisitsNotSigned == 0);

        return
        [
            new VerificationItem(
                BillingCodes.CheckChecklist,
                facts.RequiredIncomplete == 0,
                true,
                string.Create(CultureInfo.InvariantCulture, $"{facts.ChecklistCompleted}/{facts.ChecklistTotal} tasks complete")),
            new VerificationItem(
                BillingCodes.CheckPhotos,
                photos,
                true,
                photos ? "Before and after photos" : "Before and after photos missing"),
            new VerificationItem(BillingCodes.CheckAcknowledgment, acknowledged, true, AcknowledgmentLabel(facts, acknowledged)),
            new VerificationItem(
                BillingCodes.CheckMaterials,
                facts.MaterialsRecorded,
                false,
                facts.MaterialsRecorded ? "Materials recorded" : "No materials recorded"),
            new VerificationItem(
                BillingCodes.CheckLabor,
                facts.ActualWorkSeconds > 0,
                false,
                facts.ActualWorkSeconds > 0
                    ? $"Labor time recorded: {VarianceCalculator.FormatDuration(facts.ActualWorkSeconds)}"
                    : "No labor time recorded"),
            new VerificationItem(
                BillingCodes.CheckSummary,
                facts.SummaryRecorded,
                false,
                facts.SummaryRecorded ? "Completion summary recorded" : "No completion summary"),
        ];
    }

    public static bool MandatoryMet(IReadOnlyList<VerificationItem> items) =>
        items.Where(item => item.Mandatory).All(item => item.Met);

    /// <summary>Ready = no variance and every mandatory item met (BR-05).</summary>
    public static bool IsReady(IReadOnlyList<VerificationItem> items, string laborVariance, bool materialVariance) =>
        laborVariance == BillingCodes.LaborNone && !materialVariance && MandatoryMet(items);

    public static string MethodLabel(string? method) => method switch
    {
        "signed" => "Signature obtained",
        "customer_absent" => "Customer not available",
        "customer_refused" => "Customer declined to sign",
        "remote_confirmation" => "Remote confirmation",
        _ => "Customer acknowledgment",
    };

    private static string AcknowledgmentLabel(VerificationFacts facts, bool met)
    {
        if (!met)
        {
            return facts.RequireSignature ? "Customer signature missing" : "Customer acknowledgment missing";
        }

        if (facts.LatestMethod == "signed")
        {
            return string.IsNullOrWhiteSpace(facts.LatestSignerName)
                ? "Customer signature"
                : $"Customer signature: {facts.LatestSignerName.Trim()}";
        }

        return $"Customer acknowledgment: {MethodLabel(facts.LatestMethod)}";
    }
}
