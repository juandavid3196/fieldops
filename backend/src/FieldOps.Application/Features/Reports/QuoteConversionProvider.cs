using FieldOps.Application.Features.BusinessOverview;
using FieldOps.Application.Features.Quotes;
using FieldOps.Application.Features.ServiceRequests;

namespace FieldOps.Application.Features.Reports;

internal sealed class QuoteConversionProvider(IReportsStore store) : IReportDataProvider
{
    public string Key => ReportKeys.QuoteConversion;

    public async Task<ReportResult> LoadAsync(ReportContext context, CancellationToken cancellationToken) =>
        QuoteConversionBuilder.Build(await store.LoadQuotesAsync(context, cancellationToken), context);
}

/// <summary>Quote conversion (reports-workspace BR-10): event-based sent and accepted quotes, as in business-overview BR-06.</summary>
internal static class QuoteConversionBuilder
{
    private sealed record Line(
        QuoteFact Fact,
        QuoteDisplayStatus Status,
        bool Sent,
        bool Accepted,
        DateTimeOffset? AcceptedAt,
        decimal AcceptedShown,
        decimal AcceptedSummed,
        decimal SentSummed,
        bool OtherCurrency);

    private sealed record Sums(int Sent, int Accepted, decimal SentValue, decimal AcceptedValue, int OtherCurrency);

    public static ReportResult Build(QuotesSource source, ReportContext context)
    {
        var current = Window(source, context, context.CurrentStart, context.CurrentEnd);
        var previous = Window(source, context, context.PreviousStart, context.CurrentStart);
        var now = Sum(current);
        var before = Sum(previous);

        var rows = current.Select(line => ToRow(line, context)).ToList();
        var totals = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["firstSentAt"] = now.Sent,
            ["acceptedAt"] = now.Accepted,
            ["acceptedValue"] = ReportRules.Money(now.AcceptedValue),
        };

        return new ReportResult(
            [
                ReportRules.Count("quotesSent", "Quotes sent", now.Sent, before.Sent),
                ReportRules.Count("quotesAccepted", "Quotes accepted", now.Accepted, before.Accepted),
                ReportRules.Percent(
                    "conversion",
                    "Conversion",
                    BusinessOverviewRules.ConversionPercent(now.Accepted, now.Sent),
                    BusinessOverviewRules.ConversionPercent(before.Accepted, before.Sent)),
                ReportRules.Currency("sentValue", "Sent value", now.SentValue, before.SentValue),
                ReportRules.Currency("acceptedValue", "Accepted value", now.AcceptedValue, before.AcceptedValue),
            ],
            now.OtherCurrency > 0 ? now.OtherCurrency : null,
            BuildChart(current, context),
            rows,
            totals);
    }

    // The quotes of one window: sent and/or accepted in it, then the status, category and event filters (BR-10).
    private static List<Line> Window(QuotesSource source, ReportContext context, DateTimeOffset start, DateTimeOffset end)
    {
        var query = context.Query;
        var currency = context.Organization.Currency;
        var lines = new List<Line>();

        foreach (var fact in source.Quotes)
        {
            var sent = fact.FirstSentAt is { } at && at >= start && at < end;
            var accepted = fact.Acceptances.Where(item => item.At >= start && item.At < end).ToList();
            var isAccepted = accepted.Count > 0;

            if (!sent && !isAccepted)
            {
                continue;
            }

            var status = QuotesWorkspaceRules.Display(fact.Status, fact.CurrentVersionNo, fact.ShownValidUntil, context.Today);

            if ((query.Event == "sent" && !sent)
                || (query.Event == "accepted" && !isAccepted)
                || (query.Status != "all" && QuotesWorkspaceRules.StatusCode(status) != query.Status)
                || (query.CategoryId is { } categoryId && fact.CategoryId != categoryId))
            {
                continue;
            }

            var sentInCurrency = string.Equals(fact.FirstSentCurrency, currency, StringComparison.Ordinal);
            var other = (sent && !sentInCurrency)
                || accepted.Any(item => !string.Equals(item.Currency, currency, StringComparison.Ordinal));

            lines.Add(new Line(
                fact,
                status,
                sent,
                isAccepted,
                isAccepted ? accepted.Min(item => item.At) : null,
                accepted.Sum(item => item.Total),
                accepted.Where(item => string.Equals(item.Currency, currency, StringComparison.Ordinal)).Sum(item => item.Total),
                sent && sentInCurrency ? fact.FirstSentTotal ?? 0m : 0m,
                other));
        }

        return lines;
    }

    private static Sums Sum(List<Line> lines) =>
        new(
            lines.Count(line => line.Sent),
            lines.Count(line => line.Accepted),
            lines.Sum(line => line.SentSummed),
            lines.Sum(line => line.AcceptedSummed),
            lines.Count(line => line.OtherCurrency));

    private static ReportRow ToRow(Line line, ReportContext context)
    {
        var fact = line.Fact;
        var number = RequestCardRules.DisplayNumber(context.Organization.QuotePrefix, fact.Number);
        var customer = !string.IsNullOrWhiteSpace(fact.CustomerName) ? fact.CustomerName : fact.GuestName ?? string.Empty;
        var label = QuotesWorkspaceRules.StatusLabel(line.Status);

        var cells = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["quote"] = number,
            ["customer"] = customer,
            ["request"] = RequestCardRules.DisplayNumber(context.Organization.RequestPrefix, fact.RequestNumber),
            ["service"] = RequestCardRules.Title(fact.ServiceName, fact.CategoryName, fact.Description),
            ["branch"] = fact.BranchId is { } branchId ? context.BranchName(branchId, "No branch") : "No branch",
            ["firstSentAt"] = line.Sent ? fact.FirstSentAt?.ToUniversalTime() : null,
            ["acceptedAt"] = line.AcceptedAt?.ToUniversalTime(),
            ["status"] = QuotesWorkspaceRules.StatusCode(line.Status),
            ["statusLabel"] = label,
            ["total"] = fact.ShownTotal is { } total ? ReportRules.Money(total) : null,
            ["currency"] = fact.ShownCurrency ?? context.Organization.Currency,
            ["acceptedValue"] = line.Accepted ? ReportRules.Money(line.AcceptedShown) : null,
            ["sentInPeriod"] = line.Sent,
            ["acceptedInPeriod"] = line.Accepted,
        };

        var sort = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["quote"] = fact.Number,
            ["status"] = label,
        };

        return new ReportRow(
            fact.QuoteId,
            number,
            fact.Number,
            cells,
            ReportRules.Links(context.Role, customer: fact.CustomerId, quote: fact.QuoteId, request: fact.RequestId),
            sort);
    }

    // Grouped bars per period bucket: sent quotes by the first sent date, accepted quotes by the acceptance date.
    private static ReportChart BuildChart(List<Line> lines, ReportContext context)
    {
        var sentDates = lines.Where(line => line.Sent && line.Fact.FirstSentAt is not null)
            .Select(line => OrganizationTime.LocalDate(line.Fact.FirstSentAt!.Value, context.Zone))
            .ToList();
        var acceptedDates = lines.Where(line => line.AcceptedAt is not null)
            .Select(line => OrganizationTime.LocalDate(line.AcceptedAt!.Value, context.Zone))
            .ToList();

        return new ReportChart(
            "grouped_series",
            [new ReportSeries("sent", "Sent"), new ReportSeries("accepted", "Accepted")],
            [.. context.Period.Buckets.Select(bucket => new ReportPoint(
                ReportRules.BucketLabel(bucket),
                ReportRules.Day(bucket.Start),
                new Dictionary<string, decimal>(StringComparer.Ordinal)
                {
                    ["sent"] = sentDates.Count(date => date >= bucket.Start && date <= bucket.End),
                    ["accepted"] = acceptedDates.Count(date => date >= bucket.Start && date <= bucket.End),
                }))]);
    }
}
