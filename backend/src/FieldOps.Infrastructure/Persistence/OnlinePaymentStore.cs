using System.Text.Json;
using System.Text.Json.Nodes;
using FieldOps.Application.Features.InvoicePayments;
using FieldOps.Application.Features.OnlinePayments;
using FieldOps.Application.Features.Organizations;
using FieldOps.Application.Features.PortalAccess;
using FieldOps.Application.Features.Quotes;
using FieldOps.Application.Features.ServiceRequests;
using FieldOps.Domain.Invoices;
using FieldOps.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace FieldOps.Infrastructure.Persistence;

/// <summary>
/// Persistence of the customer payment flow (customer-invoice-payments). Public calls start from the token row: the
/// organization and invoice never come from the client. Locking rules: the invoice of a public mutation is locked through
/// the token (see <see cref="LockInvoiceAsync"/>); the webhook locks attempt, invoice, organization in that order; an
/// attempt row is never locked while the same transaction holds the invoice lock, so the lazy expiry
/// (<see cref="MarkFailedIfPendingAsync"/>) runs before any invoice lock and takes only the attempt row.
/// </summary>
internal sealed partial class OnlinePaymentStore(
    FieldOpsDbContext dbContext,
    IPaymentGateway gateway,
    IBankDetailsEncryptor encryptor,
    TimeProvider timeProvider,
    ILogger<OnlinePaymentStore> logger) : IOnlinePaymentStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private const string AttemptEntityType = "invoice_payment_attempt";

    private const string InvoiceEntityType = "invoice";

    public async Task<InvoiceRef?> ResolveAsync(ResourceAccess access, CancellationToken cancellationToken)
    {
        var invoice = await InvoiceLinkStore.ValidInvoices(dbContext, access, Now())
            .Select(candidate => new InvoiceRef(candidate.OrganizationId, candidate.Id))
            .SingleOrDefaultAsync(cancellationToken);

        return invoice;
    }

    public async Task<IReadOnlyList<ExpiredAttempt>> FindExpiredAsync(
        Guid organizationId, Guid invoiceId, DateTimeOffset now, CancellationToken cancellationToken) =>
        await dbContext.InvoicePaymentAttempts.AsNoTracking()
            .Where(attempt => attempt.OrganizationId == organizationId
                && attempt.InvoiceId == invoiceId
                && attempt.Method == PaymentMethod.CardOnline
                && attempt.Status == PaymentAttemptStatus.Pending
                && attempt.ExpiresAt != null
                && attempt.ExpiresAt <= now)
            .OrderBy(attempt => attempt.CreatedAt)
            .Select(attempt => new ExpiredAttempt(attempt.Id, attempt.ProviderPaymentIntentId))
            .ToListAsync(cancellationToken);

    public async Task<bool> MarkFailedIfPendingAsync(Guid attemptId, string category, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var changed = await dbContext.InvoicePaymentAttempts
            .Where(attempt => attempt.Id == attemptId && attempt.Status == PaymentAttemptStatus.Pending)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(attempt => attempt.Status, PaymentAttemptStatus.Failed)
                    .SetProperty(attempt => attempt.FailureCategory, category)
                    .SetProperty(attempt => attempt.UpdatedAt, now),
                cancellationToken);

        if (changed == 0)
        {
            await transaction.RollbackAsync(cancellationToken);

            return false;
        }

        var attempt = await dbContext.InvoicePaymentAttempts.AsNoTracking()
            .Where(candidate => candidate.Id == attemptId)
            .Select(candidate => new { candidate.OrganizationId, candidate.InvoiceId })
            .SingleAsync(cancellationToken);

        AuditFailed(attempt.OrganizationId, await BranchOfAsync(attempt.OrganizationId, attempt.InvoiceId, cancellationToken), attemptId, category);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return true;
    }

    public async Task<CardIntentPrepared> PrepareCardIntentAsync(
        ResourceAccess access, Guid idempotencyKey, DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            return await PrepareCardIntentOnceAsync(access, idempotencyKey, now, cancellationToken);
        }
        catch (DbUpdateException exception) when (IsAttemptUniqueViolation(exception))
        {
            // A concurrent request won the pending index or the key: run once more, so the guards decide.
            dbContext.ChangeTracker.Clear();

            return await PrepareCardIntentOnceAsync(access, idempotencyKey, now, cancellationToken);
        }
    }

    private async Task<CardIntentPrepared> PrepareCardIntentOnceAsync(
        ResourceAccess access, Guid idempotencyKey, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        if (await LockInvoiceAsync(access, now, cancellationToken) is not { } invoiceId)
        {
            return new CardIntentPrepared.Unavailable();
        }

        var invoice = await dbContext.Invoices.AsNoTracking().SingleAsync(candidate => candidate.Id == invoiceId, cancellationToken);

        // Step 1: the key was used before. Another invoice, or another method, is a conflict.
        var existing = await dbContext.InvoicePaymentAttempts.AsNoTracking()
            .FirstOrDefaultAsync(
                attempt => attempt.OrganizationId == invoice.OrganizationId && attempt.IdempotencyKey == idempotencyKey,
                cancellationToken);

        if (existing is not null)
        {
            return existing.InvoiceId == invoice.Id && existing.Method == PaymentMethod.CardOnline
                ? new CardIntentPrepared.Replay(Facts(existing))
                : new CardIntentPrepared.Rejected(OnlinePaymentMessages.IdempotencyConflictCode, OnlinePaymentMessages.IdempotencyConflictTitle);
        }

        // Step 2: only a sent or partially paid invoice with a balance can be paid.
        if (invoice.Status is not (InvoiceStatus.Sent or InvoiceStatus.PartiallyPaid) || invoice.BalanceDue <= 0m)
        {
            return new CardIntentPrepared.Rejected(OnlinePaymentMessages.NotPayableCode, OnlinePaymentMessages.NotPayableTitle);
        }

        // Step 3: card payments need every Stripe key.
        if (!gateway.Capabilities.CardAvailable)
        {
            return new CardIntentPrepared.Rejected(OnlinePaymentMessages.CardUnavailableCode, OnlinePaymentMessages.CardUnavailableTitle);
        }

        // Step 4: the expiry evaluation ran before this lock; a pending attempt here is unresolved.
        if (await dbContext.InvoicePaymentAttempts.AsNoTracking().AnyAsync(
            attempt => attempt.InvoiceId == invoice.Id
                && attempt.Method == PaymentMethod.CardOnline
                && attempt.Status == PaymentAttemptStatus.Pending,
            cancellationToken))
        {
            return new CardIntentPrepared.Rejected(OnlinePaymentMessages.PaymentInProgressCode, OnlinePaymentMessages.PaymentInProgressTitle);
        }

        // Step 5: the attempt for exactly the balance, committed before the provider is called.
        var created = InvoicePaymentAttempt.CreateCard(invoice.OrganizationId, invoice.Id, invoice.BalanceDue, invoice.Currency, idempotencyKey, now);

        dbContext.InvoicePaymentAttempts.Add(created);
        AuditAttempt(access, invoice, "invoice_payment_attempt.created", created.Id, new
        {
            attemptId = created.Id,
            method = PaymentMethodCodes.Code(created.Method),
            amount = QuoteCalculator.Money(created.Amount),
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new CardIntentPrepared.Created(Facts(created));
    }

    public async Task StoreIntentAsync(Guid attemptId, string intentId, DateTimeOffset now, CancellationToken cancellationToken) =>
        await dbContext.InvoicePaymentAttempts
            .Where(attempt => attempt.Id == attemptId && attempt.ProviderPaymentIntentId == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(attempt => attempt.ProviderPaymentIntentId, intentId)
                    .SetProperty(attempt => attempt.UpdatedAt, now),
                cancellationToken);

    public async Task<PaymentStatusView?> GetStatusAsync(ResourceAccess access, Guid attemptId, CancellationToken cancellationToken)
    {
        var invoice = await InvoiceLinkStore.ValidInvoices(dbContext, access, Now()).SingleOrDefaultAsync(cancellationToken);

        if (invoice is null)
        {
            return null;
        }

        var attempt = await dbContext.InvoicePaymentAttempts.AsNoTracking()
            .FirstOrDefaultAsync(
                candidate => candidate.Id == attemptId
                    && candidate.OrganizationId == invoice.OrganizationId
                    && candidate.InvoiceId == invoice.Id
                    && candidate.Method == PaymentMethod.CardOnline,
                cancellationToken);

        if (attempt is null)
        {
            return null;
        }

        PublicPaymentItem? payment = null;

        if (attempt.PaymentId is { } paymentId)
        {
            var org = await dbContext.Organizations.AsNoTracking()
                .Where(organization => organization.Id == invoice.OrganizationId)
                .Select(organization => new { organization.PaymentPrefix, organization.Timezone })
                .SingleAsync(cancellationToken);
            var stored = await dbContext.Payments.AsNoTracking()
                .SingleAsync(candidate => candidate.OrganizationId == invoice.OrganizationId && candidate.Id == paymentId, cancellationToken);

            payment = PublicPaymentMapper.ToItem(stored, org.PaymentPrefix, OrganizationTime.FindZone(org.Timezone));
        }

        return new PaymentStatusView(
            attempt.Id,
            OnlinePaymentRules.AttemptStatusCode(attempt.Status),
            attempt.FailureCategory,
            payment,
            new PaymentStatusInvoice(
                InvoiceHubRules.StoredStatus(invoice.Status),
                QuoteCalculator.Money(invoice.AmountPaid),
                QuoteCalculator.Money(invoice.BalanceDue)));
    }

    public async Task<BankNoticeResult> ReportBankTransferAsync(
        ResourceAccess access, Guid idempotencyKey, DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            return await ReportBankTransferOnceAsync(access, idempotencyKey, now, cancellationToken);
        }
        catch (DbUpdateException exception) when (IsAttemptUniqueViolation(exception))
        {
            dbContext.ChangeTracker.Clear();

            return await ReportBankTransferOnceAsync(access, idempotencyKey, now, cancellationToken);
        }
    }

    private async Task<BankNoticeResult> ReportBankTransferOnceAsync(
        ResourceAccess access, Guid idempotencyKey, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        if (await LockInvoiceAsync(access, now, cancellationToken) is not { } invoiceId)
        {
            return new BankNoticeResult.Unavailable();
        }

        var invoice = await dbContext.Invoices.AsNoTracking().SingleAsync(candidate => candidate.Id == invoiceId, cancellationToken);
        var org = await dbContext.Organizations.AsNoTracking()
            .Where(organization => organization.Id == invoice.OrganizationId)
            .Select(organization => new
            {
                organization.Name,
                organization.Email,
                organization.Timezone,
                organization.InvoicePrefix,
                BankConfigured = organization.BankName != null,
            })
            .SingleAsync(cancellationToken);
        var zone = OrganizationTime.FindZone(org.Timezone);

        // A key already used for another invoice or method is a conflict (the same key on this invoice falls through).
        var keyed = await dbContext.InvoicePaymentAttempts.AsNoTracking()
            .FirstOrDefaultAsync(
                attempt => attempt.OrganizationId == invoice.OrganizationId && attempt.IdempotencyKey == idempotencyKey,
                cancellationToken);

        if (keyed is not null && (keyed.InvoiceId != invoice.Id || keyed.Method != PaymentMethod.BankTransfer))
        {
            return new BankNoticeResult.Rejected(OnlinePaymentMessages.IdempotencyConflictCode, OnlinePaymentMessages.IdempotencyConflictTitle);
        }

        if (invoice.Status is not (InvoiceStatus.Sent or InvoiceStatus.PartiallyPaid) || invoice.BalanceDue <= 0m)
        {
            return new BankNoticeResult.Rejected(OnlinePaymentMessages.NotPayableCode, OnlinePaymentMessages.NotPayableTitle);
        }

        if (!org.BankConfigured || !encryptor.IsAvailable)
        {
            return new BankNoticeResult.Rejected(OnlinePaymentMessages.BankUnavailableCode, OnlinePaymentMessages.BankUnavailableTitle);
        }

        var pending = await dbContext.InvoicePaymentAttempts.AsNoTracking()
            .Where(attempt => attempt.InvoiceId == invoice.Id
                && attempt.Method == PaymentMethod.BankTransfer
                && attempt.Status == PaymentAttemptStatus.Pending)
            .OrderByDescending(attempt => attempt.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (pending is not null)
        {
            return new BankNoticeResult.Reported(false, OrganizationTime.LocalDate(pending.CreatedAt, zone), null);
        }

        var created = InvoicePaymentAttempt.CreateBankTransfer(
            invoice.OrganizationId, invoice.Id, invoice.BalanceDue, invoice.Currency, idempotencyKey, now);

        dbContext.InvoicePaymentAttempts.Add(created);
        AuditInvoice(access, invoice, "invoice.bank_transfer_reported", new { amount = QuoteCalculator.Money(created.Amount) });
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var reportedOn = OrganizationTime.LocalDate(created.CreatedAt, zone);
        var email = string.IsNullOrWhiteSpace(org.Email)
            ? null
            : new BankTransferNoticeData(
                org.Email.Trim(),
                org.Name,
                InvoiceHubRules.DisplayNumber(org.InvoicePrefix, invoice.InvoiceNumber),
                created.Amount,
                created.Currency,
                reportedOn);

        return new BankNoticeResult.Reported(true, reportedOn, email);
    }

    public async Task<ReviewResult> SubmitReviewAsync(
        ResourceAccess access, int rating, string? comment, DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            return await SubmitReviewOnceAsync(access, rating, comment, now, cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: { } constraint,
        } && constraint.Contains("work_order_id", StringComparison.Ordinal))
        {
            // The unique index of the work order is the backstop of two concurrent submissions.
            dbContext.ChangeTracker.Clear();

            return new ReviewResult.Exists();
        }
    }

    private async Task<ReviewResult> SubmitReviewOnceAsync(
        ResourceAccess access, int rating, string? comment, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        if (await LockInvoiceAsync(access, now, cancellationToken) is not { } invoiceId)
        {
            return new ReviewResult.Unavailable();
        }

        var invoice = await dbContext.Invoices.AsNoTracking().SingleAsync(candidate => candidate.Id == invoiceId, cancellationToken);

        if (invoice.Status != InvoiceStatus.Paid)
        {
            return new ReviewResult.NotAvailable();
        }

        if (await dbContext.InvoiceReviews.AsNoTracking().AnyAsync(
            review => review.OrganizationId == invoice.OrganizationId && review.WorkOrderId == invoice.WorkOrderId,
            cancellationToken))
        {
            return new ReviewResult.Exists();
        }

        var visit = await dbContext.Visits.AsNoTracking()
            .Where(candidate => candidate.OrganizationId == invoice.OrganizationId
                && candidate.WorkOrderId == invoice.WorkOrderId
                && candidate.ActualCompletedAt != null
                && (candidate.Status == Domain.WorkOrders.VisitStatus.Completed || candidate.Status == Domain.WorkOrders.VisitStatus.Approved))
            .OrderByDescending(candidate => candidate.ActualCompletedAt)
            .ThenByDescending(candidate => candidate.VisitNumber)
            .Select(candidate => candidate.Id)
            .Cast<Guid?>()
            .FirstOrDefaultAsync(cancellationToken);
        var technicianId = visit is null
            ? null
            : await dbContext.VisitAssignments.AsNoTracking()
                .Where(assignment => assignment.VisitId == visit && assignment.IsPrimary && assignment.UnassignedAt == null)
                .Select(assignment => (Guid?)assignment.TechnicianId)
                .FirstOrDefaultAsync(cancellationToken);

        dbContext.InvoiceReviews.Add(InvoiceReview.Create(invoice.OrganizationId, invoice.WorkOrderId, invoice.Id, technicianId, rating, comment, now));
        AuditInvoice(access, invoice, "invoice.review_submitted", new { rating });
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new ReviewResult.Created();
    }

    public async Task MarkReceiptSentAsync(Guid paymentId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.Payments
                .Where(payment => payment.Id == paymentId && payment.ReceiptSentAt == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(payment => payment.ReceiptSentAt, (DateTimeOffset?)now), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The payment is committed and the email went out: only the marker is missing.
            logger.LogWarning(
                "Receipt marker not saved. PaymentId={PaymentId} Category={Category}", paymentId, exception.GetType().Name);
        }
    }

    // The invoice row of the token (or of the portal session and id), locked and validated by the same statement
    // (BR-01; customer portal BR-31); null for an unusable token or a resource that is not the customer's.
    private async Task<Guid?> LockInvoiceAsync(ResourceAccess access, DateTimeOffset now, CancellationToken cancellationToken)
    {
        List<Guid> locked;

        if (access is ResourceAccess.Portal portal)
        {
            var organizationId = portal.Scope.OrganizationId;
            var customerId = portal.Scope.CustomerId;
            var invoiceId = portal.ResourceId;

            locked = await dbContext.Database
                .SqlQuery<Guid>(
                    $"""
                    SELECT i.id AS "Value" FROM invoices i
                    WHERE i.id = {invoiceId} AND i.organization_id = {organizationId} AND i.customer_id = {customerId}
                      AND i.status NOT IN ('draft', 'void') AND i.customer_snapshot IS NOT NULL
                    FOR UPDATE OF i
                    """)
                .ToListAsync(cancellationToken);
        }
        else
        {
            var hash = Application.Features.Quotes.QuoteAccessTokens.Hash(((ResourceAccess.Token)access).Raw);

            locked = await dbContext.Database
                .SqlQuery<Guid>(
                    $"""
                    SELECT i.id AS "Value" FROM invoices i
                    JOIN invoice_access_tokens t ON t.organization_id = i.organization_id AND t.invoice_id = i.id
                    WHERE t.token_hash = {hash} AND t.revoked_at IS NULL AND t.expires_at > {now}
                      AND i.status NOT IN ('draft', 'void') AND i.customer_snapshot IS NOT NULL
                    FOR UPDATE OF i
                    """)
                .ToListAsync(cancellationToken);
        }

        return locked.Count == 1 ? locked[0] : null;
    }

    private static CardAttemptFacts Facts(InvoicePaymentAttempt attempt) =>
        new(
            attempt.Id,
            OnlinePaymentRules.AttemptStatusCode(attempt.Status),
            QuoteCalculator.Money(attempt.Amount),
            attempt.Currency,
            attempt.ProviderPaymentIntentId,
            attempt.OrganizationId,
            attempt.InvoiceId);

    // The pending index and the key index are the concurrency backstops of the attempt inserts.
    private static bool IsAttemptUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: { } constraint,
        } && (constraint.Contains("ux_invoice_payment_attempts_pending", StringComparison.Ordinal)
            || constraint.EndsWith("idempotency_key", StringComparison.Ordinal));

    private async Task<Guid> BranchOfAsync(Guid organizationId, Guid invoiceId, CancellationToken cancellationToken) =>
        await dbContext.Invoices.AsNoTracking()
            .Where(invoice => invoice.OrganizationId == organizationId && invoice.Id == invoiceId)
            .Select(invoice => invoice.BranchId)
            .SingleAsync(cancellationToken);

    private DateTimeOffset Now() => timeProvider.GetUtcNow();

    // BR-26: ids, codes, numbers and amounts only; no token, secret, provider id, card data or contact data. A portal
    // action records the portal user as the actor and the channel (customer portal BR-31).
    private void AuditAttempt(ResourceAccess? access, Invoice invoice, string action, Guid attemptId, object metadata) =>
        AddAudit(invoice.OrganizationId, invoice.BranchId, action, AttemptEntityType, attemptId, null, null, metadata, access);

    private void AuditInvoice(ResourceAccess? access, Invoice invoice, string action, object metadata) =>
        AddAudit(invoice.OrganizationId, invoice.BranchId, action, InvoiceEntityType, invoice.Id, null, null, metadata, access);

    private void AuditFailed(Guid organizationId, Guid branchId, Guid attemptId, string category) =>
        AddAudit(
            organizationId,
            branchId,
            "invoice_payment_attempt.failed",
            AttemptEntityType,
            attemptId,
            null,
            null,
            new { attemptId, failureCategory = category });

    private void AddAudit(
        Guid organizationId,
        Guid branchId,
        string action,
        string entityType,
        Guid entityId,
        object? before,
        object? after,
        object? metadata,
        ResourceAccess? access = null)
    {
        var portal = access as ResourceAccess.Portal;
        var details = metadata is null ? null : JsonSerializer.SerializeToNode(metadata, JsonOptions)?.AsObject();

        if (portal is not null)
        {
            details ??= [];
            details["channel"] = "portal";
        }

        dbContext.AuditLogs.Add(AuditLog.Create(
            organizationId,
            action,
            entityType,
            portal?.Scope.UserId,
            entityId,
            branchId,
            null,
            before is null ? null : JsonSerializer.Serialize(before, JsonOptions),
            after is null ? null : JsonSerializer.Serialize(after, JsonOptions),
            details?.ToJsonString(JsonOptions)));
    }
}
