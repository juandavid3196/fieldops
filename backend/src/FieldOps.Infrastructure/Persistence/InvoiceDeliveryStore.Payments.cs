using FieldOps.Application.Features.BillingReview;
using FieldOps.Application.Features.InvoicePayments;
using FieldOps.Application.Features.OnlinePayments;
using FieldOps.Application.Features.Quotes;
using FieldOps.Application.Features.ServiceRequests;
using FieldOps.Domain.Invoices;
using FieldOps.Domain.Notifications;
using FieldOps.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FieldOps.Infrastructure.Persistence;

/// <summary>
/// Recording an external payment (invoices-payments-management BR-12 to BR-14, BR-18). It reuses the locked mutation
/// skeleton of the invoice delivery: the invoice row is locked first, then the organization row that owns the payment
/// counter, and every guard reads the state as it is after both locks.
/// </summary>
internal sealed partial class InvoiceDeliveryStore : IInvoicePaymentStore
{
    private const string IdempotencyConstraintSuffix = "idempotency_key";

    private const string AuditPaymentEntityType = "payment";

    public async Task<BillingOutcome<PaymentRecorded>> RecordAsync(
        BillingActor actor, Guid invoiceId, PaymentInput input, DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            return await RecordOnceAsync(actor, invoiceId, input, now, cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException postgres && IsIdempotencyViolation(postgres))
        {
            // A concurrent request holding the same key won the unique index: run once more, so guard 1 decides.
            dbContext.ChangeTracker.Clear();

            return await RecordOnceAsync(actor, invoiceId, input, now, cancellationToken);
        }
    }

    private static bool IsIdempotencyViolation(PostgresException postgres) =>
        postgres.SqlState == PostgresErrorCodes.UniqueViolation
        && postgres.ConstraintName?.EndsWith(IdempotencyConstraintSuffix, StringComparison.Ordinal) == true;

    private Task<BillingOutcome<PaymentRecorded>> RecordOnceAsync(
        BillingActor actor, Guid invoiceId, PaymentInput input, DateTimeOffset now, CancellationToken cancellationToken) =>
        MutateAsync<PaymentRecorded>(
            actor,
            invoiceId,
            async invoice =>
            {
                var organizationId = actor.OrganizationId;

                // The organization row owns the counter; it is locked after the invoice row (BR-13 lock order).
                var numbers = await dbContext.Database
                    .SqlQuery<long>(
                        $"""
                        SELECT next_payment_number AS "Value" FROM organizations
                        WHERE id = {organizationId}
                        FOR UPDATE
                        """)
                    .ToListAsync(cancellationToken);

                if (numbers.Count != 1)
                {
                    throw new InvalidOperationException("The organization is no longer available.");
                }

                var number = numbers[0];
                var org = await dbContext.Organizations.AsNoTracking()
                    .Where(organization => organization.Id == organizationId)
                    .Select(organization => new
                    {
                        organization.Name,
                        organization.Phone,
                        organization.Timezone,
                        organization.PaymentPrefix,
                        organization.InvoicePrefix,
                    })
                    .SingleAsync(cancellationToken);
                var zone = OrganizationTime.FindZone(org.Timezone);

                // BR-16 is part of the body rules (BR-11), so it precedes every guard: the receiver must be an active member of
                // the session organization; inactive, foreign and unknown ids are the same field error.
                if (!await dbContext.OrganizationUsers.AsNoTracking().AnyAsync(
                    member => member.OrganizationId == organizationId
                        && member.UserId == input.ReceivedByUserId
                        && member.Status == UserStatus.Active,
                    cancellationToken))
                {
                    return Fail<PaymentRecorded>(new BillingOutcome<PaymentRecorded>.Invalid(
                        new Dictionary<string, string[]>(StringComparer.Ordinal) { ["receivedByUserId"] = [InvoicePaymentMessages.ReceivedByInvalid] }));
                }

                // Guard 1: the key was used before (BR-14), whatever the status or the stale token of the invoice now.
                var existing = await ExistingPayment(organizationId, input.IdempotencyKey).SingleOrDefaultAsync(cancellationToken);

                if (existing is not null)
                {
                    var same = existing.InvoiceId == invoice.Id
                        && existing.Payment.Amount == input.Amount
                        && OrganizationTime.LocalDate(existing.Payment.PaidAt, zone) == input.PaidDate
                        && existing.Payment.Method == input.Method
                        && existing.Payment.ExternalReference == input.Reference
                        && existing.Payment.ReceivedByUserId == input.ReceivedByUserId
                        && existing.Payment.Notes == input.Note;

                    return same
                        ? Step<PaymentRecorded>.Done(async () => new PaymentRecorded(
                            false,
                            await PaymentRowAsync(existing.Payment, invoice, org.PaymentPrefix, org.InvoicePrefix, zone, cancellationToken),
                            Summary(invoice),
                            null))
                        : Fail<PaymentRecorded>(Conflict<PaymentRecorded>(
                            InvoicePaymentMessages.IdempotencyConflictCode, InvoicePaymentMessages.IdempotencyConflictTitle));
                }

                // Guard 2: only a sent or partially paid invoice receives payments.
                if (invoice.Status is not (InvoiceStatus.Sent or InvoiceStatus.PartiallyPaid))
                {
                    return Fail<PaymentRecorded>(Conflict<PaymentRecorded>(
                        InvoicePaymentMessages.NotPayableCode, InvoicePaymentMessages.NotPayableTitle));
                }

                // Guard 2b (customer-invoice-payments BR-31 f): a pending online card attempt holds the invoice. The expiry
                // evaluation of BR-14 ran before the invoice lock, so a pending attempt here is still unresolved.
                if (await dbContext.InvoicePaymentAttempts.AsNoTracking().AnyAsync(
                    attempt => attempt.InvoiceId == invoice.Id
                        && attempt.Method == PaymentMethod.CardOnline
                        && attempt.Status == PaymentAttemptStatus.Pending,
                    cancellationToken))
                {
                    return Fail<PaymentRecorded>(Conflict<PaymentRecorded>(
                        InvoicePaymentMessages.PaymentInProgressCode, InvoicePaymentMessages.PaymentInProgressTitle));
                }

                // Guard 3: the invoice moved since the client read it.
                if (IsStale(invoice, input.UpdatedAt))
                {
                    return Fail<PaymentRecorded>(ChangedOutcome<PaymentRecorded>());
                }

                // Guards 4 and 5: field errors only, nothing is written.
                var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

                if (input.Amount > invoice.BalanceDue)
                {
                    errors["amount"] = [InvoicePaymentMessages.AmountOverBalance(invoice.BalanceDue, invoice.Currency)];
                }

                if (invoice.IssueDate is { } issueDate && input.PaidDate < issueDate)
                {
                    errors["paidDate"] = [InvoicePaymentMessages.PaidDateBeforeIssue];
                }

                if (errors.Count > 0)
                {
                    return Fail<PaymentRecorded>(new BillingOutcome<PaymentRecorded>.Invalid(errors));
                }

                // Effect: the counter moves only if the whole transaction commits.
                await dbContext.Organizations
                    .Where(organization => organization.Id == organizationId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(organization => organization.NextPaymentNumber, number + 1), cancellationToken);

                // BR-11 b / BR-31 e: the receipt sequence is one more than the receipts the invoice already holds, read under
                // the invoice lock, so it is gap-free and unique.
                var receiptCount = await PublicPaymentMapper.ReceiptCountAsync(dbContext, organizationId, invoice.Id, cancellationToken);
                var before = AuditState(invoice);
                var payment = Payment.Create(
                    organizationId,
                    invoice.CustomerId,
                    number,
                    input.Method,
                    input.Amount,
                    invoice.Currency,
                    InvoiceHubRules.DayStartUtc(input.PaidDate, zone),
                    input.ReceivedByUserId,
                    input.IdempotencyKey,
                    actor.UserId,
                    input.Reference,
                    input.Note,
                    OnlinePaymentRules.ReceiptNumber(invoice.InvoiceNumber, receiptCount + 1));

                dbContext.Payments.Add(payment);
                dbContext.PaymentAllocations.Add(PaymentAllocation.Create(payment.Id, invoice.Id, input.Amount));
                invoice.ApplyPayment(input.Amount, now);

                // BR-18: ids, numbers, amounts, statuses and codes only; never the reference, note, recipient, names or key.
                AuditRow(
                    actor,
                    invoice,
                    "payment.recorded",
                    AuditPaymentEntityType,
                    payment.Id,
                    null,
                    new { amount = QuoteCalculator.Money(payment.Amount), currency = payment.Currency, method = PaymentMethodCodes.Code(payment.Method) },
                    new { paymentNumber = number, invoiceId = invoice.Id, receiptRequested = input.SendReceipt });
                AuditRow(
                    actor,
                    invoice,
                    "invoice.payment_applied",
                    AuditEntityType,
                    invoice.Id,
                    before,
                    AuditState(invoice),
                    new { paymentId = payment.Id });

                return Step<PaymentRecorded>.Done(async () =>
                {
                    var receipt = input.SendReceipt && !string.IsNullOrWhiteSpace(invoice.RecipientEmail)
                        ? new PaymentReceiptData(
                            payment.Id,
                            invoice.RecipientEmail,
                            org.Name,
                            org.Phone,
                            InvoiceHubRules.DisplayNumber(org.PaymentPrefix, number),
                            payment.Amount,
                            payment.Currency,
                            input.PaidDate,
                            PaymentMethodCodes.Label(payment.Method),
                            InvoiceHubRules.DisplayNumber(org.InvoicePrefix, invoice.InvoiceNumber),
                            invoice.BalanceDue)
                        : null;

                    return new PaymentRecorded(
                        true,
                        await PaymentRowAsync(payment, invoice, org.PaymentPrefix, org.InvoicePrefix, zone, cancellationToken),
                        Summary(invoice),
                        receipt);
                });
            },
            cancellationToken);

    private sealed record ExistingPaymentRow(Payment Payment, Guid? InvoiceId);

    // The payment recorded with this key in the organization and the invoice of its allocation (guard 1).
    private IQueryable<ExistingPaymentRow> ExistingPayment(Guid organizationId, Guid idempotencyKey) =>
        dbContext.Payments.AsNoTracking()
            .Where(payment => payment.OrganizationId == organizationId && payment.IdempotencyKey == idempotencyKey)
            .Select(payment => new ExistingPaymentRow(
                payment,
                dbContext.PaymentAllocations
                    .Where(allocation => allocation.PaymentId == payment.Id)
                    .Select(allocation => (Guid?)allocation.InvoiceId)
                    .FirstOrDefault()));

    private static object AuditState(Invoice invoice) =>
        new
        {
            status = InvoiceHubRules.StoredStatus(invoice.Status),
            amountPaid = QuoteCalculator.Money(invoice.AmountPaid),
            balanceDue = QuoteCalculator.Money(invoice.BalanceDue),
        };

    private static PaymentInvoiceSummary Summary(Invoice invoice) =>
        new(
            invoice.Id,
            InvoiceHubRules.StoredStatus(invoice.Status),
            QuoteCalculator.Money(invoice.AmountPaid),
            QuoteCalculator.Money(invoice.BalanceDue),
            invoice.UpdatedAt);

    private async Task<PaymentRow> PaymentRowAsync(
        Payment payment, Invoice invoice, string paymentPrefix, string invoicePrefix, TimeZoneInfo zone, CancellationToken cancellationToken)
    {
        var customerName = await dbContext.Customers.AsNoTracking()
            .Where(customer => customer.OrganizationId == payment.OrganizationId && customer.Id == payment.CustomerId)
            .Select(customer => customer.DisplayName)
            .FirstOrDefaultAsync(cancellationToken);
        var receivedBy = payment.ReceivedByUserId is { } receiverId
            ? await dbContext.Users.AsNoTracking()
                .Where(user => user.Id == receiverId)
                .Select(user => user.FirstName + " " + user.LastName)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        return new PaymentRow(
            payment.Id,
            InvoiceHubRules.DisplayNumber(paymentPrefix, payment.PaymentNumber),
            OrganizationTime.LocalDate(payment.PaidAt, zone),
            customerName ?? string.Empty,
            invoice.Id,
            InvoiceHubRules.DisplayNumber(invoicePrefix, invoice.InvoiceNumber),
            PaymentMethodCodes.Code(payment.Method),
            payment.ExternalReference,
            QuoteCalculator.Money(payment.Amount),
            payment.Currency,
            payment.ReceivedByUserId is null ? null : receivedBy ?? string.Empty,
            OnlinePaymentRules.PaymentStatusCode(payment.Status),
            QuoteCalculator.Money(payment.RefundedAmount));
    }

    private void AuditRow(
        BillingActor actor, Invoice invoice, string action, string entityType, Guid entityId, object? before, object? after, object? metadata) =>
        dbContext.AuditLogs.Add(AuditLog.Create(
            actor.OrganizationId,
            action,
            entityType,
            actor.UserId,
            entityId,
            invoice.BranchId,
            actor.IpAddress,
            Serialize(before),
            Serialize(after),
            Serialize(metadata)));
}
