using System.Globalization;
using FieldOps.Application.Features.Access;
using FieldOps.Application.Features.BillingReview;
using FieldOps.Application.Features.OnlinePayments;
using FieldOps.Application.Features.ServiceRequests;

namespace FieldOps.Application.Features.InvoicePayments;

internal static class HubHandlerSupport
{
    public static async Task<BillingActor> ActorAsync(IBranchScopeResolver scopes, MembershipCall call, CancellationToken cancellationToken) =>
        BillingHandlerSupport.Actor(call, await BillingHandlerSupport.ScopeAsync(scopes, call, cancellationToken));

    public static BillingOutcome<TTarget> Forward<TSource, TTarget>(BillingOutcome<TSource> outcome, Func<TSource, TTarget> map) =>
        outcome switch
        {
            BillingOutcome<TSource>.Succeeded succeeded => new BillingOutcome<TTarget>.Succeeded(map(succeeded.Value)),
            BillingOutcome<TSource>.Invalid invalid => new BillingOutcome<TTarget>.Invalid(invalid.Errors),
            BillingOutcome<TSource>.Conflict conflict => new BillingOutcome<TTarget>.Conflict(conflict.Code, conflict.Title),
            _ => new BillingOutcome<TTarget>.NotFound(),
        };
}

/// <summary>GET /invoices/options (BR-19): members are returned only to action roles.</summary>
public sealed class GetHubOptionsHandler(IInvoiceHubStore store, IBranchScopeResolver scopes)
{
    public async Task<HubOptions> HandleAsync(MembershipCall call, bool canAct, CancellationToken cancellationToken) =>
        await store.GetOptionsAsync(await HubHandlerSupport.ActorAsync(scopes, call, cancellationToken), canAct, cancellationToken);
}

/// <summary>GET /invoices/customers (BR-19).</summary>
public sealed class SearchHubCustomersHandler(IInvoiceHubStore store, IBranchScopeResolver scopes)
{
    public async Task<BillingOutcome<IReadOnlyList<NamedOption>>> HandleAsync(
        MembershipCall call, string? search, CancellationToken cancellationToken)
    {
        var outcome = InvoiceHubQueryParser.ParseCustomerSearch(search);

        if (outcome is not BillingOutcome<string?>.Succeeded parsed)
        {
            return HubHandlerSupport.Forward<string?, IReadOnlyList<NamedOption>>(outcome, _ => []);
        }

        var actor = await HubHandlerSupport.ActorAsync(scopes, call, cancellationToken);

        return new BillingOutcome<IReadOnlyList<NamedOption>>.Succeeded(
            await store.GetCustomersAsync(actor, parsed.Value, cancellationToken));
    }
}

/// <summary>GET /invoices/overview (BR-03 to BR-06).</summary>
public sealed class GetHubOverviewHandler(IInvoiceHubStore store, IBranchScopeResolver scopes, TimeProvider timeProvider)
{
    public async Task<BillingOutcome<HubOverview>> HandleAsync(MembershipCall call, string? branchId, CancellationToken cancellationToken)
    {
        var parsed = InvoiceHubQueryParser.ParseBranch(branchId);

        if (parsed is not BillingOutcome<Guid?>.Succeeded branch)
        {
            return HubHandlerSupport.Forward<Guid?, HubOverview>(parsed, _ => throw new InvalidOperationException());
        }

        return await store.GetOverviewAsync(
            await HubHandlerSupport.ActorAsync(scopes, call, cancellationToken), branch.Value, timeProvider.GetUtcNow(), cancellationToken);
    }
}

/// <summary>GET /invoices (BR-07, BR-08).</summary>
public sealed class ListHubInvoicesHandler(IInvoiceHubStore store, IBranchScopeResolver scopes, TimeProvider timeProvider)
{
    public async Task<BillingOutcome<InvoicePage>> HandleAsync(
        MembershipCall call, InvoiceQueryText text, bool canAct, CancellationToken cancellationToken)
    {
        var parsed = InvoiceHubQueryParser.ParseInvoices(text, paged: true);

        if (parsed is not BillingOutcome<InvoiceQuery>.Succeeded query)
        {
            return HubHandlerSupport.Forward<InvoiceQuery, InvoicePage>(parsed, _ => throw new InvalidOperationException());
        }

        return await store.GetInvoicesAsync(
            await HubHandlerSupport.ActorAsync(scopes, call, cancellationToken), query.Value, canAct, timeProvider.GetUtcNow(), cancellationToken);
    }
}

/// <summary>GET /invoices/payments (BR-09).</summary>
public sealed class ListHubPaymentsHandler(IInvoiceHubStore store, IBranchScopeResolver scopes, TimeProvider timeProvider)
{
    public async Task<BillingOutcome<PaymentPage>> HandleAsync(
        MembershipCall call, PaymentQueryText text, CancellationToken cancellationToken)
    {
        var parsed = InvoiceHubQueryParser.ParsePayments(text, paged: true);

        if (parsed is not BillingOutcome<PaymentQuery>.Succeeded query)
        {
            return HubHandlerSupport.Forward<PaymentQuery, PaymentPage>(parsed, _ => throw new InvalidOperationException());
        }

        return await store.GetPaymentsAsync(
            await HubHandlerSupport.ActorAsync(scopes, call, cancellationToken), query.Value, timeProvider.GetUtcNow(), cancellationToken);
    }
}

/// <summary>GET /invoices/export (BR-10): the invoice filters without paging, escaped like the billing review export.</summary>
public sealed class ExportHubInvoicesHandler(IInvoiceHubStore store, IBranchScopeResolver scopes, TimeProvider timeProvider)
{
    private static readonly string[] Header =
        ["Invoice", "Customer", "Work order", "Branch", "Invoice date", "Due date", "Status", "Total", "Amount paid", "Balance", "Currency"];

    public async Task<BillingOutcome<BillingCsvFile>> HandleAsync(
        MembershipCall call, InvoiceQueryText text, CancellationToken cancellationToken)
    {
        var parsed = InvoiceHubQueryParser.ParseInvoices(text, paged: false);

        if (parsed is not BillingOutcome<InvoiceQuery>.Succeeded query)
        {
            return HubHandlerSupport.Forward<InvoiceQuery, BillingCsvFile>(parsed, _ => throw new InvalidOperationException());
        }

        var export = await store.ExportInvoicesAsync(
            await HubHandlerSupport.ActorAsync(scopes, call, cancellationToken), query.Value, timeProvider.GetUtcNow(), cancellationToken);

        return HubHandlerSupport.Forward(export, Compose);
    }

    private static BillingCsvFile Compose(InvoiceExport export)
    {
        var rows = new List<string> { CsvCell.Row(Header) };

        foreach (var row in export.Rows)
        {
            rows.Add(CsvCell.Row(
            [
                row.Number,
                row.CustomerName,
                row.WorkOrderNumber,
                row.BranchName,
                Date(row.IssueDate),
                Date(row.DueDate),
                InvoiceHubRules.StatusLabel(row.Status, row.DaysOverdue),
                Money(row.Total),
                Money(row.AmountPaid),
                Money(row.BalanceDue),
                row.Currency,
            ]));
        }

        return new BillingCsvFile($"invoices-{Date(export.OrganizationDate)}.csv", CsvCell.Document(rows));
    }

    internal static string Date(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;

    internal static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
}

/// <summary>GET /invoices/payments/export (BR-10).</summary>
public sealed class ExportHubPaymentsHandler(IInvoiceHubStore store, IBranchScopeResolver scopes, TimeProvider timeProvider)
{
    private static readonly string[] Header =
        ["Payment", "Date", "Customer", "Invoice", "Method", "Reference", "Amount", "Currency", "Received by", "Status"];

    public async Task<BillingOutcome<BillingCsvFile>> HandleAsync(
        MembershipCall call, PaymentQueryText text, CancellationToken cancellationToken)
    {
        var parsed = InvoiceHubQueryParser.ParsePayments(text, paged: false);

        if (parsed is not BillingOutcome<PaymentQuery>.Succeeded query)
        {
            return HubHandlerSupport.Forward<PaymentQuery, BillingCsvFile>(parsed, _ => throw new InvalidOperationException());
        }

        var export = await store.ExportPaymentsAsync(
            await HubHandlerSupport.ActorAsync(scopes, call, cancellationToken), query.Value, timeProvider.GetUtcNow(), cancellationToken);

        return HubHandlerSupport.Forward(export, Compose);
    }

    // The CSV amount is the net amount (amount - refunded amount); the status column tells whether a refund applies (BR-31).
    private static string StatusLabel(string status) => status switch
    {
        "partially_refunded" => "Partially refunded",
        "refunded" => "Refunded",
        _ => "Succeeded",
    };

    private static BillingCsvFile Compose(PaymentExport export)
    {
        var rows = new List<string> { CsvCell.Row(Header) };

        foreach (var row in export.Rows)
        {
            rows.Add(CsvCell.Row(
            [
                row.Number,
                ExportHubInvoicesHandler.Date(row.PaidDate),
                row.CustomerName,
                row.InvoiceNumber,
                PaymentMethodCodes.TryParse(row.Method, out var method) ? PaymentMethodCodes.Label(method) : row.Method,
                row.Reference,
                ExportHubInvoicesHandler.Money(row.Amount - row.RefundedAmount),
                row.Currency,
                row.ReceivedByName ?? string.Empty,
                StatusLabel(row.Status),
            ]));
        }

        return new BillingCsvFile($"payments-{ExportHubInvoicesHandler.Date(export.OrganizationDate)}.csv", CsvCell.Document(rows));
    }
}

/// <summary>
/// POST /invoices/{id}/payments (BR-11 to BR-18). The body is validated before anything is read or written; the store
/// does the guarded write in one transaction, and the receipt is emailed after the commit without ever undoing it.
/// </summary>
public sealed class RecordInvoicePaymentHandler(
    IInvoicePaymentStore store,
    IBillingReviewStore organizations,
    IBranchScopeResolver scopes,
    IPaymentReceiptNotifier notifier,
    PaymentAttemptExpirer expirer,
    TimeProvider timeProvider)
{
    public async Task<BillingOutcome<PaymentRecordResponse>> HandleAsync(
        MembershipCall call, Guid invoiceId, PaymentBodyText body, CancellationToken cancellationToken)
    {
        var actor = await HubHandlerSupport.ActorAsync(scopes, call, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var organization = await organizations.GetOrganizationAsync(call.OrganizationId, cancellationToken);
        var today = OrganizationTime.LocalDate(now, OrganizationTime.FindZone(organization.Timezone));
        var validated = PaymentFieldRules.Validate(body, today);

        if (validated is not BillingOutcome<PaymentInput>.Succeeded input)
        {
            return HubHandlerSupport.Forward<PaymentInput, PaymentRecordResponse>(validated, _ => throw new InvalidOperationException());
        }

        // customer-invoice-payments BR-14: an expired pending card attempt is evaluated before the invoice lock is taken.
        await expirer.EvaluateAsync(call.OrganizationId, invoiceId, cancellationToken);

        var recorded = await store.RecordAsync(actor, invoiceId, input.Value, now, cancellationToken);

        if (recorded is not BillingOutcome<PaymentRecorded>.Succeeded succeeded)
        {
            return HubHandlerSupport.Forward<PaymentRecorded, PaymentRecordResponse>(recorded, _ => throw new InvalidOperationException());
        }

        var result = succeeded.Value;
        var status = InvoicePaymentMessages.EmailNotSent;

        if (result.Changed && input.Value.SendReceipt && result.Receipt is not null)
        {
            status = await notifier.SendAsync(result.Receipt, cancellationToken) == PaymentReceiptStatus.Sent
                ? InvoicePaymentMessages.EmailSent
                : InvoicePaymentMessages.EmailFailed;
        }

        return new BillingOutcome<PaymentRecordResponse>.Succeeded(
            new PaymentRecordResponse(result.Changed, status, result.Payment, result.Invoice));
    }
}
