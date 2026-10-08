using System.Text.Json;
using FieldOps.Application.Features.Access;
using FieldOps.Application.Features.BillingReview;
using FieldOps.Application.Features.Quotes;
using FieldOps.Application.Features.ServiceRequests;
using FieldOps.Domain.Invoices;
using FieldOps.Domain.Quotes;
using FieldOps.Domain.WorkOrders;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Persistence;

/// <summary>
/// Completed jobs review persistence (completed-jobs-review). Every read starts from the session organization and the
/// branch scope of the work order (BR-02); a missing, foreign, out-of-scope or no longer queued work order is one
/// identical "not found". Writes live in <c>BillingReviewStore.Review</c> and <c>BillingReviewStore.Generate</c>.
/// </summary>
internal sealed partial class BillingReviewStore(FieldOpsDbContext dbContext) : IBillingReviewStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private const string AuditWorkOrder = "work_order";

    public async Task<BillingOrganization> GetOrganizationAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var organization = await ReadOrganizationAsync(organizationId, cancellationToken);

        return new BillingOrganization(organization.Timezone, organization.Currency);
    }

    public async Task<BillingOptionsView> GetOptionsAsync(
        Guid organizationId, BranchScope scope, bool canAct, CancellationToken cancellationToken)
    {
        var organization = await ReadOrganizationAsync(organizationId, cancellationToken);
        var branches = dbContext.Branches.AsNoTracking().Where(branch => branch.OrganizationId == organizationId);
        var technicians = dbContext.TechnicianProfiles.AsNoTracking().Where(profile => profile.OrganizationId == organizationId);

        if (!scope.All)
        {
            var ids = scope.BranchIds.ToArray();
            branches = branches.Where(branch => ids.Contains(branch.Id));
            technicians = technicians.Where(profile => ids.Contains(profile.BranchId));
        }

        var branchOptions = await branches
            .OrderBy(branch => branch.Name)
            .ThenBy(branch => branch.Id)
            .Select(branch => new NamedOption(branch.Id, branch.Name))
            .ToListAsync(cancellationToken);
        var technicianRows = await technicians
            .OrderBy(profile => profile.FirstName)
            .ThenBy(profile => profile.LastName)
            .ThenBy(profile => profile.Id)
            .Select(profile => new { profile.Id, profile.FirstName, profile.LastName })
            .ToListAsync(cancellationToken);

        return new BillingOptionsView(
            organization.Timezone,
            organization.Currency,
            branchOptions,
            technicianRows.Select(row => new NamedOption(row.Id, FullName(row.FirstName, row.LastName))).ToList(),
            canAct);
    }

    public async Task<BillingAccess> GetAccessAsync(
        Guid organizationId, BranchScope scope, Guid workOrderId, CancellationToken cancellationToken)
    {
        if (!await VisibleOrders(organizationId, scope).AnyAsync(order => order.Id == workOrderId, cancellationToken))
        {
            return BillingAccess.NotVisible;
        }

        return await QueueOrders(organizationId, scope).AnyAsync(order => order.Id == workOrderId, cancellationToken)
            ? BillingAccess.QueueMember
            : BillingAccess.Visible;
    }

    /// <summary>Work orders of the organization whose branch is in the caller scope (BR-02).</summary>
    private IQueryable<WorkOrder> VisibleOrders(Guid organizationId, BranchScope scope)
    {
        var orders = dbContext.WorkOrders.AsNoTracking().Where(order => order.OrganizationId == organizationId);

        if (!scope.All)
        {
            var ids = scope.BranchIds.ToArray();
            orders = orders.Where(order => ids.Contains(order.BranchId));
        }

        return orders;
    }

    /// <summary>Visible completed work orders without a non-void invoice (BR-03).</summary>
    private IQueryable<WorkOrder> QueueOrders(Guid organizationId, BranchScope scope) =>
        VisibleOrders(organizationId, scope).Where(order =>
            order.Status == WorkOrderStatus.Completed
            && !dbContext.Invoices.Any(invoice =>
                invoice.OrganizationId == order.OrganizationId
                && invoice.WorkOrderId == order.Id
                && invoice.Status != InvoiceStatus.Void));

    private async Task<OrganizationInfo> ReadOrganizationAsync(Guid organizationId, CancellationToken cancellationToken) =>
        await dbContext.Organizations.AsNoTracking()
            .Where(organization => organization.Id == organizationId)
            .Select(organization => new OrganizationInfo(
                organization.Timezone,
                organization.Currency,
                organization.WorkOrderPrefix,
                organization.QuotePrefix,
                organization.InvoicePrefix,
                organization.NextInvoiceNumber,
                organization.RequireCustomerSignature))
            .SingleAsync(cancellationToken);

    private static string FullName(string? first, string? last) =>
        string.Join(' ', new[] { first, last }.Where(part => !string.IsNullOrWhiteSpace(part))).Trim();

    private static string DisplayNumber(string prefix, long number) => RequestCardRules.DisplayNumber(prefix, number);

    private static string? Serialize(object? value) => value is null ? null : JsonSerializer.Serialize(value, JsonOptions);

    private static string InvoiceStatusCode(InvoiceStatus status) => status switch
    {
        InvoiceStatus.Draft => "draft",
        InvoiceStatus.Sent => "sent",
        InvoiceStatus.PartiallyPaid => "partially_paid",
        InvoiceStatus.Paid => "paid",
        InvoiceStatus.Overdue => "overdue",
        _ => "void",
    };

    private sealed record OrganizationInfo(
        string Timezone,
        string Currency,
        string WorkOrderPrefix,
        string QuotePrefix,
        string InvoicePrefix,
        long NextInvoiceNumber,
        bool RequireSignature);

    /// <summary>The approved quote of a work order: totals of the approved response and the billed lines (BR-07, BR-10).</summary>
    private sealed record ApprovedQuote(
        Guid VersionId,
        string Currency,
        string? Terms,
        decimal Subtotal,
        decimal Discount,
        decimal Tax,
        decimal Total,
        IReadOnlyList<BilledLine> Lines);

    private sealed record OrderFacts(
        ApprovedQuote Quote,
        VarianceFacts Variance,
        VerificationFacts Verification,
        string? LatestSignoffMethod);

    // Stays here so the queue, the detail and the generation classify a work order from the same facts.
    private static IReadOnlyList<VerificationItem> Verify(OrderFacts facts) => CompletionVerifier.Verify(facts.Verification);

    private static string TaxLabel(ApprovedQuote quote) => QuoteCalculator.TaxLabel(quote.Lines.Select(line => line.TaxRate));
}
