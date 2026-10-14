using FieldOps.Application.Features.OperationsDashboard;
using FieldOps.Application.Features.Team;

namespace FieldOps.Application.Features.BusinessOverview;

/// <summary>Raw query values; every value is optional text so a malformed value becomes a 400 (business-overview BR-03).</summary>
public sealed record BusinessOverviewQueryText(string? From, string? To, string? BranchId, string? Compare);

public sealed record BusinessBranchOption(Guid Id, string Name);

public sealed record BusinessOptionsView(
    string Timezone, string Currency, DateOnly Today, decimal MarginTarget, IReadOnlyList<BusinessBranchOption> Branches);

public sealed record MoneyPair(decimal Current, decimal Previous);

public sealed record PercentPair(decimal? Current, decimal? Previous);

public sealed record CountPair(int Current, int Previous);

public sealed record QuoteConversionView(int Accepted, int Sent, int? Percent);

public sealed record OutstandingView(decimal Amount, decimal OverdueAmount, int OverdueCount);

public sealed record BusinessKpisView(
    MoneyPair Revenue,
    MoneyPair GrossProfit,
    PercentPair GrossMarginPercent,
    QuoteConversionView QuoteConversion,
    OutstandingView Outstanding,
    CountPair JobsCompleted);

public sealed record SeriesPointView(
    DateOnly Start,
    DateOnly End,
    decimal Revenue,
    decimal GrossProfit,
    decimal Collected,
    decimal PreviousRevenue,
    decimal PreviousGrossProfit,
    decimal PreviousCollected);

public sealed record AgingView(decimal Current, decimal Days1To30, decimal Days31To60, decimal Days60Plus);

public sealed record CashFlowView(decimal Invoiced, decimal Collected, decimal Outstanding, decimal Overdue, AgingView Aging);

public sealed record SalesPipelineView(int Requests, int QuotesSent, int Accepted, int JobsCreated);

public sealed record BranchRowView(
    Guid BranchId,
    string Name,
    bool IsActive,
    decimal Revenue,
    decimal GrossProfit,
    decimal? MarginPercent,
    int JobsCompleted,
    int? OnTimePercent,
    decimal? Rating,
    int ReviewCount);

public sealed record BranchTotalsView(decimal Revenue, decimal? MarginPercent, int JobsCompleted, int? OnTimePercent);

public sealed record ServiceRowView(
    Guid CategoryId,
    string Name,
    decimal Revenue,
    decimal GrossProfit,
    decimal? MarginPercent,
    int JobCount,
    decimal AverageJobValue);

public sealed record TeamCapacityView(
    int Technicians,
    int? UtilizationPercent,
    int ScheduledMinutes,
    int AvailableMinutes,
    int OvertimeMinutes,
    int PreviousOvertimeMinutes,
    int? Next7DaysPercent);

public sealed record OwnerAlertView(
    string Type,
    string Severity,
    int? Count,
    decimal? Amount,
    Guid? BranchId,
    Guid? CategoryId,
    string? Name,
    decimal? MarginPercent,
    int? OvertimeMinutes,
    int? PreviousOvertimeMinutes,
    decimal? ChangePercent);

public sealed record BusinessOverviewView(
    DateOnly From,
    DateOnly To,
    DateOnly PreviousFrom,
    DateOnly PreviousTo,
    string Timezone,
    string Currency,
    decimal MarginTarget,
    string Granularity,
    BusinessKpisView Kpis,
    IReadOnlyList<SeriesPointView> Series,
    CashFlowView CashFlow,
    SalesPipelineView Pipeline,
    IReadOnlyList<BranchRowView> Branches,
    BranchTotalsView? BranchTotals,
    IReadOnlyList<ServiceRowView> Services,
    TeamCapacityView Team,
    IReadOnlyList<OwnerAlertView> Alerts);

public sealed record BusinessExportFile(byte[] Content, string FileName);

/// <summary>The organization settings every period and money figure depends on.</summary>
public sealed record BusinessOrganization(string Timezone, string Currency);

public sealed record BusinessBranchInfo(Guid Id, string Name, bool IsActive);

public sealed record BusinessCategoryInfo(Guid Id, string Name);

/// <summary>A validated query: the resolved period, the optional active branch and the organization-local today.</summary>
public sealed record BusinessOverviewQuery(string TimezoneId, BusinessPeriod Period, Guid? BranchId, DateOnly Today);

/// <summary>Recognized-invoice sums grouped by issue date, branch and service category (BR-05); the cost already follows the line source rule.</summary>
public sealed record BusinessInvoiceFact(
    DateOnly IssueDate, Guid BranchId, Guid CategoryId, decimal Revenue, decimal Total, decimal Cost);

/// <summary>Distinct work orders of the recognized invoices of the current period per category (BR-16).</summary>
public sealed record BusinessServiceJobsFact(Guid CategoryId, int Jobs);

/// <summary>One payment allocation with the payment figures needed for the refund-adjusted amount (BR-13).</summary>
public sealed record BusinessCollectionFact(DateTimeOffset PaidAt, decimal Allocation, decimal PaymentAmount, decimal Refunded);

/// <summary>The open balances of outstanding invoices grouped by due date (BR-07, BR-13).</summary>
public sealed record BusinessDueFact(DateOnly? DueDate, int Count, decimal Balance);

public sealed record BusinessCompletionFact(Guid BranchId, DateTimeOffset CompletedAt);

public sealed record BusinessArrivalFact(Guid BranchId, DashboardArrival Arrival);

public sealed record BusinessReviewFact(Guid BranchId, int Count, int RatingSum);

public sealed record BusinessTechnician(Guid Id, TechnicianSchedule Schedule, IReadOnlyList<DashboardInterval> Assessments);

/// <summary>Plain, already filtered data of the selected organization, branch and period window.</summary>
public sealed record BusinessOverviewSource(
    IReadOnlyList<BusinessBranchInfo> Branches,
    IReadOnlyList<BusinessCategoryInfo> Categories,
    IReadOnlyList<BusinessInvoiceFact> Invoices,
    IReadOnlyList<BusinessServiceJobsFact> ServiceJobs,
    IReadOnlyList<BusinessCollectionFact> Collections,
    IReadOnlyList<BusinessDueFact> Outstanding,
    int Requests,
    int QuotesSent,
    int QuotesAccepted,
    int JobsCreated,
    IReadOnlyList<BusinessCompletionFact> Completions,
    IReadOnlyList<BusinessArrivalFact> Arrivals,
    IReadOnlyList<BusinessReviewFact> Reviews,
    IReadOnlyList<BusinessTechnician> Technicians);

/// <summary>Persistence port of the business overview. Every method is scoped to one organization (read-only).</summary>
public interface IBusinessOverviewStore
{
    Task<BusinessOrganization> GetOrganizationAsync(Guid organizationId, CancellationToken cancellationToken);

    Task<IReadOnlyList<BusinessBranchOption>> GetActiveBranchesAsync(Guid organizationId, CancellationToken cancellationToken);

    /// <summary>True only for an active branch of the organization.</summary>
    Task<bool> BranchIsActiveAsync(Guid organizationId, Guid branchId, CancellationToken cancellationToken);

    Task<BusinessOverviewSource> LoadAsync(Guid organizationId, BusinessOverviewQuery query, CancellationToken cancellationToken);
}
