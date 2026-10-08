using FieldOps.Application.Features.BillingReview;
using FieldOps.Application.Features.WorkOrders;
using FieldOps.Domain.Invoices;
using FieldOps.Domain.Notifications;
using FieldOps.Domain.WorkOrders;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FieldOps.Infrastructure.Persistence;

internal sealed partial class BillingReviewStore
{
    private const string ActiveInvoiceIndex = "ux_invoices_work_order_active";

    /// <summary>
    /// The invoice generation (BR-17 to BR-20), one READ COMMITTED transaction: the work order row, then the organization
    /// row are locked and all state is read after the locks. The unique index on the active invoice is the backstop of a
    /// lost race and reads as "already exists".
    /// </summary>
    public async Task<BillingOutcome<GenerateResult>> GenerateInvoiceAsync(
        BillingActor actor, Guid workOrderId, GenerateInput input, DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            return await ExecuteGenerateAsync(actor, workOrderId, input, now, cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres
            && postgres.ConstraintName == ActiveInvoiceIndex)
        {
            dbContext.ChangeTracker.Clear();

            var organization = await ReadOrganizationAsync(actor.OrganizationId, cancellationToken);
            var existing = await ReadActiveInvoiceAsync(actor.OrganizationId, workOrderId, organization.InvoicePrefix, cancellationToken);

            return existing is null
                ? throw new InvalidOperationException("The invoice of the work order could not be generated.")
                : new BillingOutcome<GenerateResult>.Succeeded(new GenerateResult(false, existing));
        }
    }

    private async Task<BillingOutcome<GenerateResult>> ExecuteGenerateAsync(
        BillingActor actor, Guid workOrderId, GenerateInput input, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var organizationId = actor.OrganizationId;

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // (1) The work order row, then (2) the organization row (which owns the invoice counter).
        if (!await LockWorkOrderAsync(actor, workOrderId, cancellationToken))
        {
            return new BillingOutcome<GenerateResult>.NotFound();
        }

        var numbers = await dbContext.Database
            .SqlQuery<long>(
                $"""
                SELECT next_invoice_number AS "Value" FROM organizations
                WHERE id = {organizationId}
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);

        if (numbers.Count != 1)
        {
            throw new InvalidOperationException("The organization is no longer available.");
        }

        var number = numbers[0];

        // (3) Everything below reads the state as it is after the locks.
        var organization = await ReadOrganizationAsync(organizationId, cancellationToken);
        var order = await dbContext.WorkOrders.SingleAsync(
            candidate => candidate.OrganizationId == organizationId && candidate.Id == workOrderId, cancellationToken);

        // Guard 1: an active invoice already exists.
        if (await ReadActiveInvoiceAsync(organizationId, workOrderId, organization.InvoicePrefix, cancellationToken) is { } existing)
        {
            return new BillingOutcome<GenerateResult>.Succeeded(new GenerateResult(false, existing));
        }

        // Guard 2: only a completed work order is invoiced.
        if (order.Status != WorkOrderStatus.Completed)
        {
            return new BillingOutcome<GenerateResult>.Conflict(BillingCodes.WorkOrderStatusInvalid, BillingMessages.WorkOrderStatusInvalidTitle);
        }

        var facts = (await LoadFactsAsync(
            organizationId, [(order.Id, order.QuoteVersionId)], organization.RequireSignature, cancellationToken))[order.Id];
        var verification = CompletionVerifier.Verify(facts.Verification);
        var variance = VarianceCalculator.Analyze(facts.Variance);

        // Guard 3: an unmet mandatory item saves the note, marks the follow-up and commits without any invoice.
        if (!CompletionVerifier.MandatoryMet(verification))
        {
            var noteBefore = order.BillingReviewNote;
            var wasMarked = order.BillingFollowUpAt is not null;

            if (order.UpdateBillingReview(input.Note, true, actor.UserId, now))
            {
                AddReviewAudits(
                    actor, order.Id, order.BranchId, noteBefore, order.BillingReviewNote, wasMarked, true, BillingCodes.ReasonRequirementsUnmet);

                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }

            return new BillingOutcome<GenerateResult>.Conflict(BillingCodes.CompletionRequirementsUnmet, BillingMessages.RequirementsUnmetTitle);
        }

        // Guard 4: variances need the explicit confirmation.
        if (variance.HasVariance && !input.AcknowledgeVariances)
        {
            return new BillingOutcome<GenerateResult>.Conflict(BillingCodes.VarianceConfirmationRequired, BillingMessages.VarianceConfirmationTitle);
        }

        // Guard 5: the billed lines must add up to the approved totals.
        var quote = facts.Quote;

        if (quote.Lines.Sum(line => line.LineSubtotal) != quote.Subtotal
            || quote.Lines.Sum(line => line.LineTax) != quote.Tax
            || quote.Lines.Sum(line => line.LineTotal) != quote.Total)
        {
            return new BillingOutcome<GenerateResult>.Conflict(BillingCodes.InvoiceTotalsMismatch, BillingMessages.TotalsMismatchTitle);
        }

        // (a) The number and the counter; the counter moves only if the whole transaction commits.
        await dbContext.Organizations
            .Where(candidate => candidate.Id == organizationId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(candidate => candidate.NextInvoiceNumber, number + 1)
                    .SetProperty(candidate => candidate.UpdatedAt, now),
                cancellationToken);

        // (b) and (c) The draft invoice and its lines copy the approved quote.
        var invoice = Invoice.Create(
            organizationId,
            order.BranchId,
            number,
            order.Id,
            order.CustomerId,
            quote.Currency,
            quote.Subtotal,
            quote.Tax,
            quote.Total,
            quote.Total,
            actor.UserId,
            input.IssueDate,
            BillingTerms.DueDate(input.IssueDate, input.PaymentTerms),
            quote.Discount,
            input.PaymentTerms);

        dbContext.Invoices.Add(invoice);
        dbContext.InvoiceLines.AddRange(quote.Lines.Select(line => InvoiceLine.Create(
            invoice.Id,
            line.Name,
            line.Quantity,
            line.Unit,
            line.UnitPrice,
            line.LineSubtotal,
            line.LineTax,
            line.LineTotal,
            line.Id,
            null,
            line.TaxRate,
            line.SortOrder)));

        // (d) The work order, (e) its completed visits.
        order.MarkApprovedForBilling(input.Note, now);

        var completedVisits = await dbContext.Visits
            .Where(visit => visit.OrganizationId == organizationId
                && visit.WorkOrderId == workOrderId
                && visit.Status == VisitStatus.Completed)
            .ToListAsync(cancellationToken);

        foreach (var visit in completedVisits)
        {
            visit.Approve(actor.UserId, now);
            dbContext.VisitStatusHistories.Add(
                VisitStatusHistory.Create(visit.Id, VisitStatus.Completed, VisitStatus.Approved, actor.UserId, now));
        }

        // (f) Ids, statuses, numbers and flags only.
        dbContext.AuditLogs.Add(AuditLog.Create(
            organizationId,
            "invoice.created",
            "invoice",
            actor.UserId,
            invoice.Id,
            order.BranchId,
            actor.IpAddress,
            null,
            Serialize(new Dictionary<string, object?>
            {
                ["status"] = "draft",
                ["invoiceNumber"] = number,
                ["total"] = quote.Total,
                ["currency"] = quote.Currency,
            }),
            Serialize(new Dictionary<string, object?>
            {
                ["workOrderId"] = order.Id,
                ["laborVariance"] = variance.LaborVariance != BillingCodes.LaborNone,
                ["materialVariance"] = variance.MaterialVariance,
            })));
        dbContext.AuditLogs.Add(AuditLog.Create(
            organizationId,
            "work_order.status_changed",
            AuditWorkOrder,
            actor.UserId,
            order.Id,
            order.BranchId,
            actor.IpAddress,
            Serialize(new Dictionary<string, object?> { ["status"] = WorkOrderCodes.StatusCode(WorkOrderStatus.Completed) }),
            Serialize(new Dictionary<string, object?> { ["status"] = WorkOrderCodes.StatusCode(WorkOrderStatus.ApprovedForBilling) }),
            Serialize(new Dictionary<string, object?> { ["invoiceId"] = invoice.Id })));

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new BillingOutcome<GenerateResult>.Succeeded(new GenerateResult(
            true,
            new InvoiceRef(invoice.Id, DisplayNumber(organization.InvoicePrefix, number), InvoiceStatusCode(invoice.Status), invoice.Total, invoice.Currency)));
    }

    private async Task<InvoiceRef?> ReadActiveInvoiceAsync(
        Guid organizationId, Guid workOrderId, string prefix, CancellationToken cancellationToken)
    {
        var row = await dbContext.Invoices.AsNoTracking()
            .Where(invoice => invoice.OrganizationId == organizationId
                && invoice.WorkOrderId == workOrderId
                && invoice.Status != InvoiceStatus.Void)
            .OrderBy(invoice => invoice.CreatedAt)
            .Select(invoice => new { invoice.Id, invoice.InvoiceNumber, invoice.Status, invoice.Total, invoice.Currency })
            .FirstOrDefaultAsync(cancellationToken);

        return row is null
            ? null
            : new InvoiceRef(row.Id, DisplayNumber(prefix, row.InvoiceNumber), InvoiceStatusCode(row.Status), row.Total, row.Currency);
    }
}
