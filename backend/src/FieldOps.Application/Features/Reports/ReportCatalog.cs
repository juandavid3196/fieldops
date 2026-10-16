using FieldOps.Application.Features.AuditTrail;
using FieldOps.Application.Features.InvoicePayments;

namespace FieldOps.Application.Features.Reports;

public static class ReportKeys
{
    public const string JobProfitability = "job_profitability";

    public const string InvoiceAging = "invoice_aging";

    public const string QuoteConversion = "quote_conversion";

    public const string JobsOnTime = "jobs_on_time";

    public const string TechnicianUtilization = "technician_utilization";
}

/// <summary>How a cell is formatted in the CSV and the PDF (reports-workspace BR-14, BR-15).</summary>
public enum ReportValueKind
{
    Text,
    Date,
    DateTime,
    Money,
    Percent1,
    Percent0,
    Minutes,
    Hours,
    Count,
}

/// <param name="Key">The row cell printed in this column (a label cell for tags).</param>
public sealed record ReportColumn(string Key, string Label, ReportValueKind Kind);

public sealed record ReportDefinitionText(string Term, string Text);

/// <summary>One catalog entry (BR-01, BR-02) with the static rules of its report: columns, sort allowlist and filters.</summary>
public sealed record ReportDefinition(
    string Key,
    string Group,
    string Name,
    string Description,
    string Noun,
    bool IsFinancial,
    IReadOnlyList<string> Roles,
    bool UsesPeriod,
    IReadOnlyList<ReportColumn> Columns,
    IReadOnlyList<string> SortKeys,
    string DefaultSort,
    bool DefaultDescending,
    IReadOnlyList<string> StatusValues,
    IReadOnlyList<ReportDefinitionText> Definitions,
    string? Disclaimer)
{
    public bool HasMoney => Columns.Any(column => column.Kind == ReportValueKind.Money);
}

/// <summary>The roles that may open the destination module of a row link (BR-13, AS-04), mirroring the existing read policies.</summary>
public static class ReportLinkAccess
{
    private static readonly string[] Schedule = ["owner", "operations_manager", "dispatcher", "viewer"];

    private static readonly string[] Everyone = ["owner", "operations_manager", "dispatcher", "accounting", "viewer"];

    private static readonly string[] Team = ["owner", "operations_manager", "dispatcher"];

    public static bool CanOpenJob(string role) => Schedule.Contains(role, StringComparer.Ordinal);

    public static bool CanOpenCustomer(string role) => Everyone.Contains(role, StringComparer.Ordinal);

    public static bool CanOpenInvoice(string role) => Everyone.Contains(role, StringComparer.Ordinal);

    public static bool CanOpenQuote(string role) => Everyone.Contains(role, StringComparer.Ordinal);

    public static bool CanOpenRequest(string role) => Everyone.Contains(role, StringComparer.Ordinal);

    public static bool CanOpenTechnician(string role) => Team.Contains(role, StringComparer.Ordinal);
}

/// <summary>The catalog, the role matrix and the per-report static rules (reports-workspace BR-01, BR-02, BR-08 to BR-12, BR-20).</summary>
public static class ReportCatalog
{
    public const string Disclaimer =
        "Profitability figures are operational estimates based on configured product and service costs. They exclude overhead, payroll, "
        + "payroll taxes, depreciation, and other accounting expenses. They are not net income and not a substitute for accounting software.";

    /// <summary>Every role that sees at least one report: the coarse policy of every endpoint.</summary>
    public static readonly string[] ReportRoles = ["owner", "operations_manager", "dispatcher", "accounting"];

    private static readonly string[] Financial = ["owner", "operations_manager", "accounting"];

    private static readonly string[] Operational = ["owner", "operations_manager", "dispatcher"];

    public static readonly IReadOnlyList<ReportDefinition> All =
    [
        new(
            ReportKeys.JobProfitability,
            "Billing & payments",
            "Job profitability",
            "Compare revenue, estimated costs, and estimated gross margin for completed jobs.",
            "jobs",
            true,
            Financial,
            true,
            [
                new("job", "Job", ReportValueKind.Text),
                new("completedAt", "Completed", ReportValueKind.DateTime),
                new("customer", "Customer", ReportValueKind.Text),
                new("service", "Service", ReportValueKind.Text),
                new("technician", "Technician", ReportValueKind.Text),
                new("revenue", "Revenue", ReportValueKind.Money),
                new("estimatedCost", "Est. cost", ReportValueKind.Money),
                new("grossProfit", "Est. gross profit", ReportValueKind.Money),
                new("margin", "Est. margin", ReportValueKind.Percent1),
                new("branch", "Branch", ReportValueKind.Text),
                new("invoicing", "Invoicing", ReportValueKind.Text),
            ],
            ["completedAt", "job", "customer", "category", "technician", "revenue", "estimatedCost", "grossProfit", "margin", "branch"],
            "completedAt",
            true,
            ["all", "completed", "approved_for_billing"],
            [
                new("Revenue", "Invoiced amount of recognized invoices for jobs completed in the period, net of discounts, excluding tax. "
                    + "Revenue is grouped by job completion, so it can differ from revenue by invoice date in Business Overview."),
                new("Estimated cost", "Quantity × configured unit cost of the product or service on each invoice line."),
                new("Estimated gross profit", "Revenue minus estimated cost."),
                new("Estimated gross margin", "Estimated gross profit ÷ revenue × 100."),
                new("Not invoiced", "Completed jobs with no issued invoice yet. They count as jobs but add no revenue or cost."),
            ],
            Disclaimer),
        new(
            ReportKeys.InvoiceAging,
            "Billing & payments",
            "Invoice aging",
            "Review open balances by days past due as of today.",
            "invoices",
            true,
            Financial,
            false,
            [
                new("invoice", "Invoice", ReportValueKind.Text),
                new("customer", "Customer", ReportValueKind.Text),
                new("job", "Job", ReportValueKind.Text),
                new("issueDate", "Issue date", ReportValueKind.Date),
                new("dueDate", "Due date", ReportValueKind.Date),
                new("daysOverdue", "Days overdue", ReportValueKind.Count),
                new("bucketLabel", "Bucket", ReportValueKind.Text),
                new("statusLabel", "Status", ReportValueKind.Text),
                new("total", "Total", ReportValueKind.Money),
                new("balanceDue", "Balance due", ReportValueKind.Money),
                new("branch", "Branch", ReportValueKind.Text),
            ],
            ["daysOverdue", "dueDate", "issueDate", "invoice", "customer", "total", "balanceDue", "branch"],
            "daysOverdue",
            true,
            ["all", InvoiceStatusFilters.Sent, InvoiceStatusFilters.PartiallyPaid, InvoiceStatusFilters.Overdue],
            [
                new("Outstanding", "Balance due of invoices that are sent or partially paid, as of today."),
                new("Overdue", "Balance due of outstanding invoices past their due date."),
                new("Current", "Not past due."),
                new("1–30 days", "Balance due 1 to 30 days past due."),
                new("31–60 days", "Balance due 31 to 60 days past due."),
                new("60+ days", "Balance due more than 60 days past due."),
            ],
            null),
        new(
            ReportKeys.QuoteConversion,
            "Sales & quotes",
            "Quote conversion",
            "Track quotes sent and accepted in the period.",
            "quotes",
            false,
            Operational,
            true,
            [
                new("quote", "Quote", ReportValueKind.Text),
                new("customer", "Customer", ReportValueKind.Text),
                new("request", "Request", ReportValueKind.Text),
                new("service", "Service", ReportValueKind.Text),
                new("branch", "Branch", ReportValueKind.Text),
                new("firstSentAt", "First sent", ReportValueKind.DateTime),
                new("acceptedAt", "Accepted", ReportValueKind.DateTime),
                new("statusLabel", "Status", ReportValueKind.Text),
                new("total", "Total", ReportValueKind.Money),
                new("acceptedValue", "Accepted value", ReportValueKind.Money),
            ],
            ["firstSentAt", "acceptedAt", "quote", "customer", "total", "status", "branch"],
            "firstSentAt",
            true,
            ["all", "awaiting_response", "clarification_requested", "expired", "approved", "rejected", "cancelled"],
            [
                new("Quotes sent", "Quotes whose first sent version was sent in the period."),
                new("Quotes accepted", "Quotes approved by the customer in the period."),
                new("Conversion", "Quotes accepted ÷ quotes sent × 100. "
                    + "Conversion can exceed 100% when quotes sent in earlier periods are accepted in this one."),
            ],
            null),
        new(
            ReportKeys.JobsOnTime,
            "Operations",
            "Completed jobs & on-time performance",
            "Review scheduled visits, completion, on-time arrival, and duration.",
            "visits",
            false,
            Operational,
            true,
            [
                new("job", "Job", ReportValueKind.Text),
                new("scheduledStart", "Scheduled", ReportValueKind.DateTime),
                new("customer", "Customer", ReportValueKind.Text),
                new("service", "Service", ReportValueKind.Text),
                new("technician", "Technician", ReportValueKind.Text),
                new("statusLabel", "Status", ReportValueKind.Text),
                new("arrivalLimit", "Arrival limit", ReportValueKind.DateTime),
                new("actualStart", "Actual start", ReportValueKind.DateTime),
                new("onTimeLabel", "On-time result", ReportValueKind.Text),
                new("estimatedMinutes", "Est. duration", ReportValueKind.Minutes),
                new("actualMinutes", "Actual duration", ReportValueKind.Minutes),
                new("varianceMinutes", "Variance", ReportValueKind.Minutes),
                new("branch", "Branch", ReportValueKind.Text),
            ],
            ["scheduledStart", "job", "customer", "technician", "status", "onTime", "variance", "branch"],
            "scheduledStart",
            true,
            ["all", "scheduled", "active", "completed"],
            [
                new("Scheduled visits", "Visits scheduled in the period, excluding unscheduled and cancelled visits."),
                new("Completion rate", "Completed visits ÷ scheduled visits."),
                new("On-time arrival", "Started by the arrival window end, or within 15 minutes of the scheduled start."),
                new("Duration variance", "Actual duration minus estimated duration, averaged over completed visits with both."),
                new("Jobs completed", "Work orders completed in the period."),
            ],
            null),
        new(
            ReportKeys.TechnicianUtilization,
            "Operations",
            "Technician utilization",
            "Compare available and scheduled hours, overtime, and delivery by technician.",
            "technicians",
            false,
            Operational,
            true,
            [
                new("technician", "Technician", ReportValueKind.Text),
                new("branch", "Branch", ReportValueKind.Text),
                new("availableMinutes", "Available h", ReportValueKind.Hours),
                new("scheduledMinutes", "Scheduled h", ReportValueKind.Hours),
                new("utilization", "Utilization", ReportValueKind.Percent0),
                new("overtimeMinutes", "Overtime h", ReportValueKind.Hours),
                new("completedVisits", "Completed visits", ReportValueKind.Count),
                new("onTime", "On-time arrival", ReportValueKind.Percent0),
            ],
            ["utilization", "technician", "branch", "availableMinutes", "scheduledMinutes", "overtimeMinutes", "completedVisits", "onTime"],
            "utilization",
            true,
            ["all"],
            [
                new("Available hours", "Hours technicians are available in the period, after breaks and time off."),
                new("Scheduled hours", "Hours of visits and assessments scheduled in the period."),
                new("Utilization", "Scheduled hours ÷ available hours."),
                new("Scheduled overtime", "Scheduled hours beyond the available hours of each day."),
            ],
            null),
    ];

    public static ReportDefinition? Find(string? key) =>
        All.FirstOrDefault(definition => string.Equals(definition.Key, key, StringComparison.Ordinal));

    public static IReadOnlyList<ReportDefinition> VisibleTo(string role) =>
        [.. All.Where(definition => definition.Roles.Contains(role, StringComparer.Ordinal))];

    /// <summary>The report types (<c>report_&lt;key&gt;</c>) of the reports a role may see.</summary>
    public static string[] ExportTypesFor(string role) => [.. VisibleTo(role).Select(definition => ReportTypeOf(definition.Key))];

    public static string ReportTypeOf(string key) => ExportReportTypes.Report(key);

    public static string StatusLabel(string reportKey, string code) => (reportKey, code) switch
    {
        (ReportKeys.JobProfitability, "completed") => "Completed",
        (ReportKeys.JobProfitability, "approved_for_billing") => "Approved for billing",
        (ReportKeys.JobProfitability, _) => "All completed",
        (ReportKeys.InvoiceAging, InvoiceStatusFilters.Sent) => "Sent",
        (ReportKeys.InvoiceAging, InvoiceStatusFilters.PartiallyPaid) => "Partially paid",
        (ReportKeys.InvoiceAging, InvoiceStatusFilters.Overdue) => "Overdue",
        (ReportKeys.QuoteConversion, "awaiting_response") => "Awaiting response",
        (ReportKeys.QuoteConversion, "clarification_requested") => "Clarification requested",
        (ReportKeys.QuoteConversion, "expired") => "Expired",
        (ReportKeys.QuoteConversion, "approved") => "Approved",
        (ReportKeys.QuoteConversion, "rejected") => "Rejected",
        (ReportKeys.QuoteConversion, "cancelled") => "Cancelled",
        (ReportKeys.JobsOnTime, "scheduled") => "Scheduled",
        (ReportKeys.JobsOnTime, "active") => "Active",
        (ReportKeys.JobsOnTime, "completed") => "Completed",
        _ => "All",
    };
}
