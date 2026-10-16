using FieldOps.Application.Features.OperationsDashboard;
using FieldOps.Application.Features.Team;
using FieldOps.Domain.Invoices;
using FieldOps.Domain.Quotes;
using FieldOps.Domain.WorkOrders;

namespace FieldOps.Application.Features.Reports;

public sealed record ProfitabilityOrder(
    Guid OrderId,
    long Number,
    Guid BranchId,
    Guid CustomerId,
    string CustomerName,
    Guid CategoryId,
    string CategoryName,
    WorkOrderStatus Status,
    DateTimeOffset CompletedAt,
    Guid? TechnicianId);

/// <summary>A recognized invoice of a loaded work order: revenue is subtotal minus discount, tax excluded.</summary>
public sealed record ProfitabilityInvoice(Guid OrderId, Guid InvoiceId, long InvoiceNumber, decimal Revenue);

public sealed record ProfitabilityCost(Guid OrderId, decimal Cost);

public sealed record ReportTechnicianName(Guid Id, string Name, Guid BranchId);

/// <summary>The work orders completed in the previous and current period (filters applied), their invoices, costs and technicians.</summary>
public sealed record ProfitabilitySource(
    IReadOnlyList<ProfitabilityOrder> Orders,
    IReadOnlyList<ProfitabilityInvoice> Invoices,
    IReadOnlyList<ProfitabilityCost> Costs,
    IReadOnlyList<ReportTechnicianName> Technicians);

public sealed record AgingInvoice(
    Guid Id,
    long Number,
    Guid CustomerId,
    string CustomerName,
    Guid OrderId,
    long OrderNumber,
    DateOnly? IssueDate,
    DateOnly? DueDate,
    InvoiceStatus Status,
    decimal Total,
    decimal BalanceDue,
    Guid BranchId);

public sealed record AgingSource(IReadOnlyList<AgingInvoice> Invoices);

public sealed record QuoteAcceptance(DateTimeOffset At, decimal Total, string Currency);

public sealed record QuoteFact(
    Guid QuoteId,
    long Number,
    Guid RequestId,
    long RequestNumber,
    Guid? CustomerId,
    string? CustomerName,
    string? GuestName,
    Guid? BranchId,
    Guid? CategoryId,
    string? ServiceName,
    string? CategoryName,
    string Description,
    QuoteStatus Status,
    int CurrentVersionNo,
    DateOnly? ShownValidUntil,
    decimal? ShownTotal,
    string? ShownCurrency,
    DateTimeOffset? FirstSentAt,
    decimal? FirstSentTotal,
    string? FirstSentCurrency,
    IReadOnlyList<QuoteAcceptance> Acceptances);

/// <summary>Visible quotes with a sent or accepted event in the previous or current period (the filters of the query not applied).</summary>
public sealed record QuotesSource(IReadOnlyList<QuoteFact> Quotes);

public sealed record JobVisit(
    Guid VisitId,
    int VisitNumber,
    Guid OrderId,
    long OrderNumber,
    string Title,
    Guid CustomerId,
    string CustomerName,
    Guid CategoryId,
    Guid BranchId,
    int? EstimatedMinutes,
    VisitStatus Status,
    DateTimeOffset ScheduledStart,
    DateTimeOffset? ArrivalWindowEnd,
    DateTimeOffset? ActualStartedAt,
    DateTimeOffset? ActualCompletedAt,
    int PauseSeconds,
    Guid? TechnicianId,
    string? TechnicianName,
    Guid? TechnicianBranchId);

/// <summary>Visits scheduled in the previous or current period, and the completion instants of the work orders completed in them.</summary>
public sealed record JobsSource(IReadOnlyList<JobVisit> Visits, IReadOnlyList<DateTimeOffset> OrderCompletions);

public sealed record UtilizationTechnician(
    Guid Id, string Name, Guid BranchId, TechnicianSchedule Schedule, IReadOnlyList<DashboardInterval> Assessments);

public sealed record UtilizationVisit(
    Guid TechnicianId,
    VisitStatus Status,
    DateTimeOffset ScheduledStart,
    DateTimeOffset? ArrivalWindowEnd,
    DateTimeOffset? ActualStartedAt,
    DateTimeOffset? ActualCompletedAt);

public sealed record UtilizationSource(
    IReadOnlyList<UtilizationTechnician> Technicians, IReadOnlyList<UtilizationVisit> Visits);
