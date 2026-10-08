using FieldOps.Application.Features.BillingReview;
using FieldOps.Application.Features.InvoiceDelivery;
using FieldOps.Application.Validation;
using FieldOps.Domain.Invoices;
using FieldOps.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FieldOps.Infrastructure.Persistence;

internal sealed partial class InvoiceDeliveryStore
{
    private const string SerializationFailure = "40001";

    private const string DeadlockDetected = "40P01";

    /// <summary>Save draft (BR-09 to BR-11): not draft, then stale, then the field rules, then the write.</summary>
    public Task<BillingOutcome<InvoiceDetail>> SaveDraftAsync(
        BillingActor actor, Guid invoiceId, DeliveryBodyText body, CancellationToken cancellationToken) =>
        MutateAsync<InvoiceDetail>(
            actor,
            invoiceId,
            invoice =>
            {
                if (invoice.Status != InvoiceStatus.Draft)
                {
                    return Task.FromResult(Fail<InvoiceDetail>(Conflict<InvoiceDetail>(InvoiceDeliveryMessages.NotDraftCode, InvoiceDeliveryMessages.NotDraftTitle)));
                }

                if (IsStale(invoice, body.UpdatedAt))
                {
                    return Task.FromResult(Fail<InvoiceDetail>(ChangedOutcome<InvoiceDetail>()));
                }

                var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

                if (InvoiceDeliveryRules.Validate(body, recipientRequired: false, errors) is not { } input)
                {
                    return Task.FromResult(Fail<InvoiceDetail>(new BillingOutcome<InvoiceDetail>.Invalid(errors)));
                }

                var changedFields = InvoiceDeliveryRules.ChangedFields(
                    invoice.PaymentTerms, invoice.RecipientEmail, invoice.DeliveryMessage, input);

                if (invoice.UpdateDelivery(
                    input.PaymentTerms,
                    InvoiceDeliveryRules.DueDate(invoice.IssueDate, input.PaymentTerms),
                    input.RecipientEmail,
                    input.Message,
                    timeProvider.GetUtcNow()))
                {
                    // BR-30: field names only, never the values.
                    Audit(actor, invoice, "invoice.draft_saved", null, null, new { changedFields });
                }

                return Task.FromResult(Step<InvoiceDetail>.Done(() => DetailAsync(invoice, cancellationToken)));
            },
            cancellationToken);

    /// <summary>
    /// Send (BR-13 to BR-16), in the BR-14 order inside the lock: an invoice that is no longer a draft is a no-op,
    /// then the stale check, then the field rules, then one transaction with every write.
    /// </summary>
    public Task<BillingOutcome<InvoiceSent>> SendAsync(
        BillingActor actor, Guid invoiceId, DeliveryBodyText body, string tokenHash, CancellationToken cancellationToken) =>
        MutateAsync<InvoiceSent>(
            actor,
            invoiceId,
            async invoice =>
            {
                if (invoice.Status != InvoiceStatus.Draft)
                {
                    return Step<InvoiceSent>.Done(async () => new InvoiceSent(false, await DetailAsync(invoice, cancellationToken), null));
                }

                if (IsStale(invoice, body.UpdatedAt))
                {
                    return Fail<InvoiceSent>(ChangedOutcome<InvoiceSent>());
                }

                var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

                if (InvoiceDeliveryRules.Validate(body, recipientRequired: true, errors) is not { } input)
                {
                    return Fail<InvoiceSent>(new BillingOutcome<InvoiceSent>.Invalid(errors));
                }

                var now = timeProvider.GetUtcNow();
                var reader = new InvoicePreviewReader(dbContext);
                var (loaded, snapshot) = await reader.ReadLiveAsync(invoice, cancellationToken);

                invoice.MarkSent(
                    input.PaymentTerms,
                    InvoiceDeliveryRules.DueDate(invoice.IssueDate, input.PaymentTerms),
                    input.RecipientEmail!,
                    input.Message,
                    Serialize(snapshot)!,
                    now);

                await RevokeTokensAsync(invoice, now, cancellationToken);

                // The status change reaches the database before the token and the audit row, inside the same transaction.
                await dbContext.SaveChangesAsync(cancellationToken);

                AddToken(actor, invoice, tokenHash, now);
                Audit(
                    actor,
                    invoice,
                    "invoice.sent",
                    new Dictionary<string, object?> { ["status"] = "draft" },
                    new Dictionary<string, object?> { ["status"] = "sent" },
                    new { invoiceNumber = loaded.Preview.Number, total = invoice.Total, currency = invoice.Currency, notified = "email" });

                var email = EmailData(invoice, loaded);

                return Step<InvoiceSent>.Done(async () => new InvoiceSent(true, await DetailAsync(invoice, cancellationToken), email));
            },
            cancellationToken);

    /// <summary>Resend (BR-17): a sent invoice with a stored recipient rotates its token; nothing else changes.</summary>
    public Task<BillingOutcome<InvoiceResent>> ResendAsync(
        BillingActor actor, Guid invoiceId, string? updatedAt, string tokenHash, CancellationToken cancellationToken) =>
        MutateAsync<InvoiceResent>(
            actor,
            invoiceId,
            async invoice =>
            {
                if (invoice.Status != InvoiceStatus.Sent || string.IsNullOrWhiteSpace(invoice.RecipientEmail))
                {
                    return Fail<InvoiceResent>(Conflict<InvoiceResent>(InvoiceDeliveryMessages.NotSentCode, InvoiceDeliveryMessages.NotSentTitle));
                }

                if (IsStale(invoice, updatedAt))
                {
                    return Fail<InvoiceResent>(ChangedOutcome<InvoiceResent>());
                }

                var now = timeProvider.GetUtcNow();
                var loaded = await new InvoicePreviewReader(dbContext).ReadAsync(invoice, cancellationToken);

                await RevokeTokensAsync(invoice, now, cancellationToken);
                AddToken(actor, invoice, tokenHash, now);
                invoice.Touch(now);
                Audit(actor, invoice, "invoice.email_resent", null, null, new { invoiceNumber = loaded.Preview.Number });

                var email = EmailData(invoice, loaded);

                return Step<InvoiceResent>.Done(async () => new InvoiceResent(await DetailAsync(invoice, cancellationToken), email));
            },
            cancellationToken);

    private async Task<InvoiceDetail> DetailAsync(Invoice invoice, CancellationToken cancellationToken) =>
        await BuildDetailAsync(invoice, true, cancellationToken);

    // A missing or unreadable updatedAt can never equal the stored value (Delivery field rules: 409 invoice_changed).
    private static bool IsStale(Invoice invoice, string? updatedAt) =>
        !UpdatedAtValidation.TryParse(updatedAt, out var parsed) || parsed != invoice.UpdatedAt;

    private static InvoiceEmailData EmailData(Invoice invoice, InvoicePreviewReader.Loaded loaded) =>
        new(
            invoice.Id,
            invoice.RecipientEmail!,
            loaded.OrganizationName,
            loaded.OrganizationPhone,
            loaded.Preview.Number,
            invoice.Total,
            invoice.Currency,
            invoice.DueDate,
            invoice.DeliveryMessage ?? string.Empty);

    // The links of the invoice stop working once a new one exists (BR-14, BR-17).
    private Task<int> RevokeTokensAsync(Invoice invoice, DateTimeOffset now, CancellationToken cancellationToken) =>
        dbContext.InvoiceAccessTokens
            .Where(token => token.OrganizationId == invoice.OrganizationId && token.InvoiceId == invoice.Id && token.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(token => token.RevokedAt, (DateTimeOffset?)now), cancellationToken);

    private void AddToken(BillingActor actor, Invoice invoice, string tokenHash, DateTimeOffset now) =>
        dbContext.InvoiceAccessTokens.Add(InvoiceAccessToken.Create(
            actor.OrganizationId,
            invoice.Id,
            tokenHash,
            now.AddDays(InvoiceDeliveryMessages.TokenLifetimeDays),
            actor.UserId,
            now));

    // BR-30: ids, status codes, numbers, totals, currency and field names only; never texts, contact data or tokens.
    private void Audit(BillingActor actor, Invoice invoice, string action, object? before, object? after, object? metadata) =>
        dbContext.AuditLogs.Add(AuditLog.Create(
            actor.OrganizationId,
            action,
            AuditEntityType,
            actor.UserId,
            invoice.Id,
            invoice.BranchId,
            actor.IpAddress,
            Serialize(before),
            Serialize(after),
            Serialize(metadata)));

    private static BillingOutcome<T>.Conflict Conflict<T>(string code, string title) => new(code, title);

    private async Task<BillingOutcome<T>> MutateAsync<T>(
        BillingActor actor,
        Guid invoiceId,
        Func<Invoice, Task<Step<T>>> apply,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ExecuteAsync(actor, invoiceId, apply, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            dbContext.ChangeTracker.Clear();

            return ChangedOutcome<T>();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException postgres)
        {
            dbContext.ChangeTracker.Clear();

            if (IsConflictState(postgres.SqlState))
            {
                return ChangedOutcome<T>();
            }

            // The database detail can quote the failing row: only state and constraint travel.
            throw new InvalidOperationException(
                $"The invoice change could not be saved (SqlState {postgres.SqlState}, constraint {postgres.ConstraintName}).");
        }
        catch (PostgresException postgres) when (IsConflictState(postgres.SqlState))
        {
            dbContext.ChangeTracker.Clear();

            return ChangedOutcome<T>();
        }
    }

    private static BillingOutcome<T> ChangedOutcome<T>() =>
        Conflict<T>(InvoiceDeliveryMessages.ChangedCode, InvoiceDeliveryMessages.ChangedTitle);

    private static bool IsConflictState(string sqlState) => sqlState is SerializationFailure or DeadlockDetected;

    // The invoice row is locked inside the caller organization and branch scope, then read: a missing, foreign,
    // out-of-scope or void invoice is one identical "not found", and all state is read after the lock (READ COMMITTED).
    private async Task<BillingOutcome<T>> ExecuteAsync<T>(
        BillingActor actor,
        Guid invoiceId,
        Func<Invoice, Task<Step<T>>> apply,
        CancellationToken cancellationToken)
    {
        var organizationId = actor.OrganizationId;
        var scopeAll = actor.Scope.All;
        var scopeIds = actor.Scope.BranchIds.ToArray();

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var locked = await dbContext.Database
            .SqlQuery<Guid>(
                $"""
                SELECT id AS "Value" FROM invoices
                WHERE id = {invoiceId} AND organization_id = {organizationId} AND status <> 'void'
                  AND ({scopeAll} OR branch_id = ANY({scopeIds}))
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);

        if (locked.Count != 1)
        {
            return new BillingOutcome<T>.NotFound();
        }

        var invoice = await dbContext.Invoices.SingleAsync(
            candidate => candidate.Id == invoiceId && candidate.OrganizationId == organizationId, cancellationToken);

        var step = await apply(invoice);

        if (step.Failure is not null)
        {
            await transaction.RollbackAsync(cancellationToken);

            return step.Failure;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new BillingOutcome<T>.Succeeded(await step.Build!());
    }

    private static Step<T> Fail<T>(BillingOutcome<T> failure) => Step<T>.Fail(failure);

    private readonly record struct Step<T>(BillingOutcome<T>? Failure, Func<Task<T>>? Build)
    {
        public static Step<T> Done(Func<Task<T>> build) => new(null, build);

        public static Step<T> Fail(BillingOutcome<T> failure) => new(failure, null);
    }
}
