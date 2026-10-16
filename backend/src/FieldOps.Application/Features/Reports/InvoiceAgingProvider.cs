using FieldOps.Application.Features.InvoicePayments;
using FieldOps.Application.Features.ServiceRequests;

namespace FieldOps.Application.Features.Reports;

internal sealed class InvoiceAgingProvider(IReportsStore store) : IReportDataProvider
{
    public string Key => ReportKeys.InvoiceAging;

    public async Task<ReportResult> LoadAsync(ReportContext context, CancellationToken cancellationToken) =>
        InvoiceAgingBuilder.Build(await store.LoadAgingAsync(context, cancellationToken), context);
}

/// <summary>Invoice aging (reports-workspace BR-09): open invoices as of today, bucketed and labelled by the Invoices hub rules.</summary>
internal static class InvoiceAgingBuilder
{
    private static readonly (string Code, string Label)[] Buckets =
    [
        ("current", "Current"),
        ("1_30", "1–30 days"),
        ("31_60", "31–60 days"),
        ("60_plus", "60+ days"),
    ];

    public static string BucketCode(AgingBucket bucket) => Buckets[(int)bucket].Code;

    public static ReportResult Build(AgingSource source, ReportContext context)
    {
        var query = context.Query;
        var today = context.Today;

        var lines = source.Invoices
            .Select(invoice =>
            {
                var bucket = InvoiceHubRules.Bucket(invoice.DueDate, today);
                var display = InvoiceHubRules.Display(invoice.Status, invoice.DueDate, today);

                return (Invoice: invoice, Bucket: bucket, Display: display);
            })
            .Where(line => query.Bucket == "all" || BucketCode(line.Bucket) == query.Bucket)
            .Where(line => query.Status == "all" || line.Display.Status == query.Status)
            .ToList();

        var amounts = new decimal[4];

        foreach (var line in lines)
        {
            amounts[(int)line.Bucket] += line.Invoice.BalanceDue;
        }

        var overdueLines = lines.Where(line => line.Display.DaysOverdue is not null).ToList();
        var outstanding = lines.Sum(line => line.Invoice.BalanceDue);

        var rows = lines.Select(line =>
        {
            var invoice = line.Invoice;
            var number = RequestCardRules.DisplayNumber(context.Organization.InvoicePrefix, invoice.Number);
            var cells = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["invoice"] = number,
                ["customer"] = invoice.CustomerName,
                ["job"] = RequestCardRules.DisplayNumber(context.Organization.WorkOrderPrefix, invoice.OrderNumber),
                ["issueDate"] = invoice.IssueDate,
                ["dueDate"] = invoice.DueDate,
                ["daysOverdue"] = line.Display.DaysOverdue,
                ["bucket"] = BucketCode(line.Bucket),
                ["bucketLabel"] = Buckets[(int)line.Bucket].Label,
                ["status"] = line.Display.Status,
                ["statusLabel"] = InvoiceHubRules.StatusLabel(line.Display.Status, line.Display.DaysOverdue),
                ["total"] = ReportRules.Money(invoice.Total),
                ["balanceDue"] = ReportRules.Money(invoice.BalanceDue),
                ["branch"] = context.BranchName(invoice.BranchId),
            };
            var sort = new Dictionary<string, object?>(StringComparer.Ordinal) { ["invoice"] = invoice.Number };

            return new ReportRow(
                invoice.Id,
                number,
                invoice.Number,
                cells,
                ReportRules.Links(context.Role, job: invoice.OrderId, customer: invoice.CustomerId, invoice: invoice.Id),
                sort);
        }).ToList();

        var totals = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["total"] = ReportRules.Money(lines.Sum(line => line.Invoice.Total)),
            ["balanceDue"] = ReportRules.Money(outstanding),
        };

        return new ReportResult(
            [
                ReportRules.Currency("outstanding", "Outstanding", outstanding, null),
                ReportRules.Currency("overdue", "Overdue", overdueLines.Sum(line => line.Invoice.BalanceDue), null),
                ReportRules.Count("overdueInvoices", "Overdue invoices", overdueLines.Count, null),
                ReportRules.Currency("current", "Current", amounts[0], null),
                ReportRules.Currency("days1To30", "1–30 days", amounts[1], null),
                ReportRules.Currency("days31To60", "31–60 days", amounts[2], null),
                ReportRules.Currency("days60Plus", "60+ days", amounts[3], null),
            ],
            null,
            new ReportChart(
                "bucket_bars",
                [new ReportSeries("balance", "Balance due")],
                [.. Buckets.Select((bucket, index) => new ReportPoint(
                    bucket.Label,
                    null,
                    new Dictionary<string, decimal>(StringComparer.Ordinal) { ["balance"] = ReportRules.Money(amounts[index]) }))]),
            rows,
            totals);
    }
}
