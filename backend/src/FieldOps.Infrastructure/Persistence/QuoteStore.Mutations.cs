using FieldOps.Application.Features.Quotes;
using FieldOps.Application.Features.ServiceRequests;
using FieldOps.Domain.Notifications;
using FieldOps.Domain.Quotes;
using FieldOps.Domain.Requests;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FieldOps.Infrastructure.Persistence;

internal sealed partial class QuoteStore
{
    private const string SerializationFailure = "40001";

    private const string DeadlockDetected = "40P01";

    private const string UniqueViolation = "23505";

    private const string OpenQuoteIndex = "ux_quotes_request_open";

    private static readonly QuoteStatus[] RevisableStatuses =
    [
        QuoteStatus.Sent,
        QuoteStatus.ClarificationRequested,
        QuoteStatus.Rejected,
        QuoteStatus.Expired,
    ];

    public async Task<QuoteOutcome<QuoteCalculation>> CalculateAsync(
        QuoteActor actor, Guid quoteId, QuoteDraft draft, CancellationToken cancellationToken)
    {
        var quote = await VisibleQuotes(actor.OrganizationId, actor.Scope)
            .Where(candidate => candidate.Id == quoteId)
            .SingleOrDefaultAsync(cancellationToken);

        if (quote is null)
        {
            return new QuoteOutcome<QuoteCalculation>.NotFound();
        }

        var (calculation, errors, _) = await PrepareAsync(actor.OrganizationId, draft, cancellationToken);

        if (calculation is null)
        {
            return new QuoteOutcome<QuoteCalculation>.Invalid(errors);
        }

        var hasDraft = await dbContext.QuoteVersions.AsNoTracking().AnyAsync(
            version => version.OrganizationId == actor.OrganizationId && version.QuoteId == quoteId && !version.IsImmutable,
            cancellationToken);

        if (!hasDraft)
        {
            return Conflict<QuoteCalculation>(QuoteMessages.NoDraftCode);
        }

        return quote.Status == QuoteStatus.Cancelled
            ? Conflict<QuoteCalculation>(QuoteMessages.QuoteChangedCode)
            : new QuoteOutcome<QuoteCalculation>.Succeeded(calculation);
    }

    public Task<QuoteOutcome<QuoteDetail>> SaveDraftAsync(
        QuoteActor actor, Guid quoteId, DateTimeOffset updatedAt, QuoteDraft draft, CancellationToken cancellationToken) =>
        MutateAsync<QuoteDetail>(
            actor,
            quoteId,
            async locked =>
            {
                var (calculation, errors, organization) = await PrepareAsync(actor.OrganizationId, draft, cancellationToken);

                if (calculation is null)
                {
                    return Fail<QuoteDetail>(new QuoteOutcome<QuoteDetail>.Invalid(errors));
                }

                var version = await LoadMutableVersionAsync(locked.Quote, cancellationToken);

                if (version is null)
                {
                    return Fail<QuoteDetail>(Conflict<QuoteDetail>(QuoteMessages.NoDraftCode));
                }

                if (IsStale(locked.Quote, updatedAt))
                {
                    return Fail<QuoteDetail>(Conflict<QuoteDetail>(QuoteMessages.QuoteChangedCode));
                }

                dbContext.Entry(locked.Quote).Property(quote => quote.UpdatedAt).OriginalValue = updatedAt;
                await ApplyDraftAsync(locked, version, draft, calculation, organization, cancellationToken);
                locked.Quote.Touch(timeProvider.GetUtcNow());
                Audit(actor, locked.Quote, "quote.draft_saved", null, null, new { versionNo = version.VersionNo, lineCount = draft.Lines.Count });

                return Step<QuoteDetail>.Done(() => DetailAsync(actor, quoteId, cancellationToken));
            },
            cancellationToken);

    public Task<QuoteOutcome<QuoteSent>> SendAsync(
        QuoteActor actor,
        Guid quoteId,
        DateTimeOffset updatedAt,
        QuoteDraft draft,
        string tokenHash,
        CancellationToken cancellationToken) =>
        MutateAsync<QuoteSent>(
            actor,
            quoteId,
            async locked =>
            {
                // An approved quote is final for revisions (customer-quote-approval BR-24).
                if (locked.Quote.Status == QuoteStatus.Approved)
                {
                    return Fail<QuoteSent>(Conflict<QuoteSent>(QuoteMessages.QuoteChangedCode));
                }

                var (calculation, errors, organization) = await PrepareAsync(actor.OrganizationId, draft, cancellationToken);
                var (recipient, _) = await ResolveRecipientAsync(actor.OrganizationId, locked.Request, cancellationToken);

                if (recipient is null)
                {
                    errors["recipient"] = [ServiceRequestMessages.NoEmail];
                }

                if (calculation is null || errors.Count > 0)
                {
                    return Fail<QuoteSent>(new QuoteOutcome<QuoteSent>.Invalid(errors));
                }

                var quote = locked.Quote;
                var version = await LoadMutableVersionAsync(quote, cancellationToken);

                if (version is null)
                {
                    return Fail<QuoteSent>(Conflict<QuoteSent>(QuoteMessages.NoDraftCode));
                }

                if (IsStale(quote, updatedAt))
                {
                    return Fail<QuoteSent>(Conflict<QuoteSent>(QuoteMessages.QuoteChangedCode));
                }

                // The first send needs the request in ready_for_quote; a revision leaves the request as it is.
                if (quote.Status == QuoteStatus.Draft && locked.Request.Status != RequestStatus.ReadyForQuote)
                {
                    return Fail<QuoteSent>(Conflict<QuoteSent>(ServiceRequestMessages.RequestChangedCode));
                }

                var now = timeProvider.GetUtcNow();
                var previousStatus = quote.Status;

                dbContext.Entry(quote).Property(candidate => candidate.UpdatedAt).OriginalValue = updatedAt;
                await ApplyDraftAsync(locked, version, draft, calculation, organization, cancellationToken);
                version.Freeze(now);

                // Earlier versions stop being reachable once this one is sent (BR-24).
                await dbContext.QuoteAccessTokens
                    .Where(token => token.OrganizationId == actor.OrganizationId
                        && token.RevokedAt == null
                        && dbContext.QuoteVersions.Any(candidate => candidate.Id == token.QuoteVersionId
                            && candidate.QuoteId == quote.Id
                            && candidate.Id != version.Id))
                    .ExecuteUpdateAsync(setters => setters.SetProperty(token => token.RevokedAt, (DateTimeOffset?)now), cancellationToken);

                dbContext.QuoteAccessTokens.Add(QuoteAccessToken.Create(
                    actor.OrganizationId,
                    version.Id,
                    tokenHash,
                    QuoteAccessTokens.ExpiresAt(draft.ValidUntil, OrganizationTime.FindZone(organization.Timezone)),
                    actor.UserId,
                    now));

                quote.MarkSent(version.VersionNo);
                quote.Touch(now);

                Audit(
                    actor,
                    quote,
                    "quote.sent",
                    new Dictionary<string, object?> { ["status"] = QuoteStatusCodes.Code(previousStatus) },
                    new Dictionary<string, object?> { ["status"] = "sent" },
                    new { versionNo = version.VersionNo, total = calculation.Total, currency = calculation.Currency, notified = "email" });

                if (locked.Request.Status == RequestStatus.ReadyForQuote)
                {
                    MoveRequestToQuoted(actor, locked.Request, quote.Id);
                }

                var email = await BuildEmailDataAsync(organization, locked.Request, quote, version, recipient!, cancellationToken);

                return Step<QuoteSent>.Done(async () => new QuoteSent(await DetailAsync(actor, quoteId, cancellationToken), email));
            },
            cancellationToken);

    public Task<QuoteOutcome<QuoteSent>> ResendEmailAsync(
        QuoteActor actor, Guid quoteId, DateTimeOffset updatedAt, string tokenHash, CancellationToken cancellationToken) =>
        MutateAsync<QuoteSent>(
            actor,
            quoteId,
            async locked =>
            {
                var quote = locked.Quote;
                var (recipient, _) = await ResolveRecipientAsync(actor.OrganizationId, locked.Request, cancellationToken);

                if (recipient is null)
                {
                    return Fail<QuoteSent>(new QuoteOutcome<QuoteSent>.Invalid(new Dictionary<string, string[]>(StringComparer.Ordinal)
                    {
                        ["recipient"] = [ServiceRequestMessages.NoEmail],
                    }));
                }

                if (quote.Status != QuoteStatus.Sent || quote.UpdatedAt != updatedAt)
                {
                    return Fail<QuoteSent>(Conflict<QuoteSent>(QuoteMessages.QuoteChangedCode));
                }

                var version = await dbContext.QuoteVersions.AsNoTracking().SingleOrDefaultAsync(
                    candidate => candidate.OrganizationId == actor.OrganizationId
                        && candidate.QuoteId == quote.Id
                        && candidate.VersionNo == quote.CurrentVersionNo
                        && candidate.IsImmutable,
                    cancellationToken);

                var now = timeProvider.GetUtcNow();
                var organization = await ReadOrganizationAsync(actor.OrganizationId, cancellationToken);

                // The link of a version whose last day has passed could not be valid: the check constraint
                // requires the expiry to follow the creation.
                if (version?.ValidUntil is not { } validUntil
                    || QuoteAccessTokens.ExpiresAt(validUntil, OrganizationTime.FindZone(organization.Timezone)) <= now)
                {
                    return Fail<QuoteSent>(Conflict<QuoteSent>(QuoteMessages.QuoteChangedCode));
                }

                dbContext.Entry(quote).Property(candidate => candidate.UpdatedAt).OriginalValue = updatedAt;

                await dbContext.QuoteAccessTokens
                    .Where(token => token.OrganizationId == actor.OrganizationId
                        && token.QuoteVersionId == version.Id
                        && token.RevokedAt == null)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(token => token.RevokedAt, (DateTimeOffset?)now), cancellationToken);

                dbContext.QuoteAccessTokens.Add(QuoteAccessToken.Create(
                    actor.OrganizationId,
                    version.Id,
                    tokenHash,
                    QuoteAccessTokens.ExpiresAt(validUntil, OrganizationTime.FindZone(organization.Timezone)),
                    actor.UserId,
                    now));

                quote.Touch(now);
                Audit(actor, quote, "quote.email_resent", null, null, new { versionNo = version.VersionNo });

                var email = await BuildEmailDataAsync(organization, locked.Request, quote, version, recipient, cancellationToken);

                return Step<QuoteSent>.Done(async () => new QuoteSent(await DetailAsync(actor, quoteId, cancellationToken), email));
            },
            cancellationToken);

    public Task<QuoteOutcome<QuoteDetail>> ReviseAsync(
        QuoteActor actor, Guid quoteId, DateTimeOffset updatedAt, CancellationToken cancellationToken) =>
        MutateAsync<QuoteDetail>(
            actor,
            quoteId,
            async locked =>
            {
                var quote = locked.Quote;

                if (!RevisableStatuses.Contains(quote.Status)
                    || quote.UpdatedAt != updatedAt
                    || await LoadMutableVersionAsync(quote, cancellationToken) is not null)
                {
                    return Fail<QuoteDetail>(Conflict<QuoteDetail>(QuoteMessages.QuoteChangedCode));
                }

                var source = await dbContext.QuoteVersions.AsNoTracking()
                    .Where(candidate => candidate.OrganizationId == actor.OrganizationId
                        && candidate.QuoteId == quote.Id
                        && candidate.IsImmutable)
                    .OrderByDescending(candidate => candidate.VersionNo)
                    .FirstOrDefaultAsync(cancellationToken);

                if (source is null)
                {
                    return Fail<QuoteDetail>(Conflict<QuoteDetail>(QuoteMessages.QuoteChangedCode));
                }

                var organization = await ReadOrganizationAsync(actor.OrganizationId, cancellationToken);
                var lines = await LoadLinesAsync(actor.OrganizationId, source.Id, cancellationToken);
                var drafts = lines
                    .Select(line => new QuoteLineDraft(
                        line.CatalogItemId,
                        line.LineType,
                        line.Name,
                        line.Description,
                        line.Quantity,
                        line.Unit,
                        line.UnitPrice,
                        line.UnitCost,
                        line.TaxRate > 0m,
                        line.IsOptional))
                    .ToList();

                // The rates come from the organization again; the frozen version is not touched.
                var result = QuoteCalculator.Calculate(
                    drafts.Select(line => new QuoteLineInput(line.Taxable, line.IsOptional, line.Quantity, line.UnitPrice, line.UnitCost)).ToList(),
                    source.DiscountTotal,
                    organization.DefaultTaxRate,
                    organization.Currency);

                if (result.Value is not { } calculation)
                {
                    return Fail<QuoteDetail>(new QuoteOutcome<QuoteDetail>.Invalid(new Dictionary<string, string[]>(StringComparer.Ordinal)
                    {
                        [result.ErrorKey!] = [result.Error!],
                    }));
                }

                var now = timeProvider.GetUtcNow();
                var today = OrganizationTime.LocalDate(now, OrganizationTime.FindZone(organization.Timezone));
                var validUntil = source.ValidUntil is { } previous
                    && previous >= today.AddDays(1)
                    && previous <= today.AddDays(QuoteMessages.MaxValidDays)
                        ? previous
                        : today.AddDays(QuoteMessages.DefaultValidDays);

                var revision = source.CreateRevision(
                    source.VersionNo + 1,
                    actor.UserId,
                    validUntil,
                    organization.Currency,
                    calculation.Subtotal,
                    calculation.TaxTotal,
                    calculation.Total);
                dbContext.QuoteVersions.Add(revision);
                AddLines(actor.OrganizationId, revision.Id, drafts, calculation);

                dbContext.Entry(quote).Property(candidate => candidate.UpdatedAt).OriginalValue = updatedAt;
                quote.Touch(now);
                Audit(actor, quote, "quote.revised", null, null, new { versionNo = revision.VersionNo });

                return Step<QuoteDetail>.Done(() => DetailAsync(actor, quoteId, cancellationToken));
            },
            cancellationToken);

    public Task<QuoteOutcome<QuoteDiscarded>> DiscardDraftAsync(
        QuoteActor actor, Guid quoteId, DateTimeOffset updatedAt, CancellationToken cancellationToken) =>
        MutateAsync<QuoteDiscarded>(
            actor,
            quoteId,
            async locked =>
            {
                var quote = locked.Quote;
                var version = await dbContext.QuoteVersions.AsNoTracking().SingleOrDefaultAsync(
                    candidate => candidate.OrganizationId == actor.OrganizationId
                        && candidate.QuoteId == quote.Id
                        && !candidate.IsImmutable,
                    cancellationToken);

                if (version is null)
                {
                    return Fail<QuoteDiscarded>(Conflict<QuoteDiscarded>(QuoteMessages.NoDraftCode));
                }

                if (IsStale(quote, updatedAt))
                {
                    return Fail<QuoteDiscarded>(Conflict<QuoteDiscarded>(QuoteMessages.QuoteChangedCode));
                }

                dbContext.Entry(quote).Property(candidate => candidate.UpdatedAt).OriginalValue = updatedAt;

                if (quote.CurrentVersionNo == 0)
                {
                    // Never sent: the quote is cancelled and its number is never reused; the draft rows remain (BR-29).
                    quote.Cancel();
                    Audit(
                        actor,
                        quote,
                        "quote.cancelled",
                        new Dictionary<string, object?> { ["status"] = "draft" },
                        new Dictionary<string, object?> { ["status"] = "cancelled" },
                        new { reason = "discarded" });
                }
                else
                {
                    await dbContext.QuoteLines
                        .Where(line => line.OrganizationId == actor.OrganizationId
                            && line.QuoteVersionId == version.Id
                            && dbContext.QuoteVersions.Any(candidate => candidate.Id == line.QuoteVersionId && !candidate.IsImmutable))
                        .ExecuteDeleteAsync(cancellationToken);
                    await dbContext.QuoteVersions
                        .Where(candidate => candidate.Id == version.Id && candidate.OrganizationId == actor.OrganizationId && !candidate.IsImmutable)
                        .ExecuteDeleteAsync(cancellationToken);
                    Audit(actor, quote, "quote.draft_discarded", null, null, new { versionNo = version.VersionNo });
                }

                quote.Touch(timeProvider.GetUtcNow());
                var result = new QuoteDiscarded(quote.Id, QuoteStatusCodes.Code(quote.Status), quote.RequestId);

                return Step<QuoteDiscarded>.Done(() => Task.FromResult(result));
            },
            cancellationToken);

    private static bool IsStale(Quote quote, DateTimeOffset updatedAt) =>
        quote.Status == QuoteStatus.Cancelled || quote.UpdatedAt != updatedAt;

    private static QuoteOutcome<T> Conflict<T>(string code) => new QuoteOutcome<T>.Conflict(code);

    private static Step<T> Fail<T>(QuoteOutcome<T> failure) => Step<T>.Fail(failure);

    private async Task<QuoteDetail> DetailAsync(QuoteActor actor, Guid quoteId, CancellationToken cancellationToken) =>
        await BuildDetailAsync(actor.OrganizationId, actor.Scope, quoteId, true, true, cancellationToken)
            ?? throw new InvalidOperationException("The quote is no longer available.");

    private Task<QuoteVersion?> LoadMutableVersionAsync(Quote quote, CancellationToken cancellationToken) =>
        dbContext.QuoteVersions.SingleOrDefaultAsync(
            version => version.OrganizationId == quote.OrganizationId && version.QuoteId == quote.Id && !version.IsImmutable,
            cancellationToken);

    /// <summary>
    /// Catalog ownership (BR-09, one query for the distinct ids; inactive items are fine) and the calculation
    /// with the current organization settings. A calculation is null exactly when the errors are not empty.
    /// </summary>
    private async Task<(QuoteCalculation? Calculation, Dictionary<string, string[]> Errors, OrganizationInfo Organization)> PrepareAsync(
        Guid organizationId, QuoteDraft draft, CancellationToken cancellationToken)
    {
        var organization = await ReadOrganizationAsync(organizationId, cancellationToken);
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var ids = draft.Lines
            .Where(line => line.CatalogItemId is not null)
            .Select(line => line.CatalogItemId!.Value)
            .Distinct()
            .ToArray();

        if (ids.Length > 0)
        {
            var owned = (await dbContext.CatalogItems.AsNoTracking()
                .Where(item => item.OrganizationId == organizationId && ids.Contains(item.Id))
                .Select(item => item.Id)
                .ToListAsync(cancellationToken)).ToHashSet();

            for (var index = 0; index < draft.Lines.Count; index++)
            {
                if (draft.Lines[index].CatalogItemId is { } id && !owned.Contains(id))
                {
                    errors[$"lines[{index}].catalogItemId"] = [QuoteMessages.CatalogItemMessage];
                }
            }
        }

        var result = QuoteCalculator.Calculate(
            draft.Lines.Select(line => new QuoteLineInput(line.Taxable, line.IsOptional, line.Quantity, line.UnitPrice, line.UnitCost)).ToList(),
            draft.DiscountTotal,
            organization.DefaultTaxRate,
            organization.Currency);

        if (!result.IsValid)
        {
            errors[result.ErrorKey!] = [result.Error!];
        }

        return (errors.Count > 0 ? null : result.Value, errors, organization);
    }

    /// <summary>Replaces the whole mutable version: texts, totals and lines (BR-22). Runs before the version is frozen.</summary>
    private async Task ApplyDraftAsync(
        Locked locked,
        QuoteVersion version,
        QuoteDraft draft,
        QuoteCalculation calculation,
        OrganizationInfo organization,
        CancellationToken cancellationToken)
    {
        version.ReplaceDraft(
            draft.CustomerMessage,
            draft.InternalNote,
            draft.TermsText,
            draft.ValidUntil,
            calculation.DiscountTotal,
            calculation.Subtotal,
            calculation.TaxTotal,
            calculation.Total,
            organization.Currency);

        await dbContext.QuoteLines
            .Where(line => line.OrganizationId == locked.Actor.OrganizationId
                && line.QuoteVersionId == version.Id
                && dbContext.QuoteVersions.Any(candidate => candidate.Id == line.QuoteVersionId && !candidate.IsImmutable))
            .ExecuteDeleteAsync(cancellationToken);

        AddLines(locked.Actor.OrganizationId, version.Id, draft.Lines, calculation);
    }

    private void AddLines(Guid organizationId, Guid versionId, IReadOnlyList<QuoteLineDraft> lines, QuoteCalculation calculation)
    {
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var amounts = calculation.Lines[index];

            dbContext.QuoteLines.Add(QuoteLine.Create(
                organizationId,
                versionId,
                line.CatalogItemId,
                line.Type,
                line.Name,
                line.Description,
                line.Quantity,
                line.Unit,
                line.UnitCost,
                line.UnitPrice,
                amounts.TaxRate,
                amounts.LineSubtotal,
                amounts.LineTax,
                amounts.LineTotal,
                index,
                line.IsOptional));
        }
    }

    private void MoveRequestToQuoted(QuoteActor actor, ServiceRequest request, Guid quoteId)
    {
        if (!RequestTransitions.TryApply(request.Status, RequestAction.QuoteSent, out var to))
        {
            throw new InvalidOperationException("The request cannot move to quoted.");
        }

        var from = request.Status;
        request.ChangeStatus(to);
        dbContext.RequestStatusHistories.Add(
            RequestStatusHistory.Create(actor.OrganizationId, request.Id, from, to, actor.UserId));
        dbContext.AuditLogs.Add(AuditLog.Create(
            actor.OrganizationId,
            "service_request.quoted",
            "service_request",
            actor.UserId,
            request.Id,
            request.BranchId,
            actor.IpAddress,
            Serialize(new Dictionary<string, object?> { ["status"] = RequestTransitions.Code(from) }),
            Serialize(new Dictionary<string, object?> { ["status"] = RequestTransitions.Code(to) }),
            Serialize(new { quoteId })));
    }

    private async Task<QuoteEmailData> BuildEmailDataAsync(
        OrganizationInfo organization,
        ServiceRequest request,
        Quote quote,
        QuoteVersion version,
        string recipient,
        CancellationToken cancellationToken)
    {
        var categoryName = request.CategoryId is { } categoryId
            ? await dbContext.ServiceCategories.AsNoTracking()
                .Where(candidate => candidate.Id == categoryId && candidate.OrganizationId == quote.OrganizationId)
                .Select(candidate => candidate.Name)
                .SingleOrDefaultAsync(cancellationToken)
            : null;
        var serviceName = request.CatalogItemId is { } itemId
            ? await dbContext.CatalogItems.AsNoTracking()
                .Where(candidate => candidate.Id == itemId && candidate.OrganizationId == quote.OrganizationId)
                .Select(candidate => candidate.Name)
                .SingleOrDefaultAsync(cancellationToken)
            : null;

        return new QuoteEmailData(
            quote.Id,
            version.VersionNo,
            recipient,
            organization.Name,
            organization.Phone,
            RequestCardRules.DisplayNumber(organization.QuotePrefix, quote.QuoteNumber),
            RequestCardRules.Title(serviceName, categoryName, request.Description),
            version.Total,
            version.Currency,
            version.ValidUntil ?? DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime));
    }

    // BR-33: ids, status codes, version numbers, totals and currency only; never texts, contact data or tokens.
    private void Audit(QuoteActor actor, Quote quote, string action, object? before, object? after, object? metadata) =>
        dbContext.AuditLogs.Add(AuditLog.Create(
            actor.OrganizationId,
            action,
            AuditEntityType,
            actor.UserId,
            quote.Id,
            quote.BranchId,
            actor.IpAddress,
            Serialize(before),
            Serialize(after),
            Serialize(metadata)));

    private async Task<QuoteOutcome<T>> MutateAsync<T>(
        QuoteActor actor,
        Guid quoteId,
        Func<Locked, Task<Step<T>>> apply,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ExecuteAsync(actor, quoteId, apply, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            dbContext.ChangeTracker.Clear();

            return Conflict<T>(QuoteMessages.QuoteChangedCode);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException postgres)
        {
            dbContext.ChangeTracker.Clear();

            if (IsConflictState(postgres.SqlState))
            {
                return Conflict<T>(QuoteMessages.QuoteChangedCode);
            }

            // The database detail can quote the failing row: only state and constraint travel.
            throw new InvalidOperationException(
                $"The quote change could not be saved (SqlState {postgres.SqlState}, constraint {postgres.ConstraintName}).");
        }
        catch (PostgresException postgres) when (IsConflictState(postgres.SqlState))
        {
            dbContext.ChangeTracker.Clear();

            return Conflict<T>(QuoteMessages.QuoteChangedCode);
        }
    }

    private static bool IsConflictState(string sqlState) => sqlState is SerializationFailure or DeadlockDetected;

    // Lock order: request row, quote row (creation adds the organization row). Tenant and branch scope are part
    // of the first statement, so a missing, foreign or out-of-scope quote is one identical "not found".
    private async Task<QuoteOutcome<T>> ExecuteAsync<T>(
        QuoteActor actor,
        Guid quoteId,
        Func<Locked, Task<Step<T>>> apply,
        CancellationToken cancellationToken)
    {
        var organizationId = actor.OrganizationId;
        var requestId = await VisibleQuotes(organizationId, actor.Scope)
            .Where(candidate => candidate.Id == quoteId)
            .Select(candidate => (Guid?)candidate.RequestId)
            .SingleOrDefaultAsync(cancellationToken);

        if (requestId is null)
        {
            return new QuoteOutcome<T>.NotFound();
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        if (await LockRequestAsync(actor, requestId.Value, cancellationToken) is null)
        {
            return new QuoteOutcome<T>.NotFound();
        }

        var lockedQuote = await dbContext.Database
            .SqlQuery<Guid>(
                $"""
                SELECT id AS "Value" FROM quotes
                WHERE id = {quoteId} AND organization_id = {organizationId}
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);

        if (lockedQuote.Count != 1)
        {
            return new QuoteOutcome<T>.NotFound();
        }

        var quote = await dbContext.Quotes.SingleAsync(
            candidate => candidate.Id == quoteId && candidate.OrganizationId == organizationId, cancellationToken);
        var request = await dbContext.ServiceRequests.SingleAsync(
            candidate => candidate.Id == requestId && candidate.OrganizationId == organizationId, cancellationToken);

        var step = await apply(new Locked(actor, quote, request));

        if (step.Failure is not null)
        {
            await transaction.RollbackAsync(cancellationToken);

            return step.Failure;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new QuoteOutcome<T>.Succeeded(await step.Build!());
    }

    /// <summary>Locks the request row inside the caller scope; null when it is missing, foreign or out of scope.</summary>
    private async Task<string?> LockRequestAsync(QuoteActor actor, Guid requestId, CancellationToken cancellationToken)
    {
        var organizationId = actor.OrganizationId;
        var scopeAll = actor.Scope.All;
        var scopeIds = actor.Scope.BranchIds.ToArray();
        var locked = await dbContext.Database
            .SqlQuery<string>(
                $"""
                SELECT status::text AS "Value" FROM service_requests
                WHERE id = {requestId} AND organization_id = {organizationId}
                  AND ({scopeAll} OR branch_id IS NULL OR branch_id = ANY({scopeIds}))
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);

        return locked.Count == 1 ? locked[0] : null;
    }

    private sealed record Locked(QuoteActor Actor, Quote Quote, ServiceRequest Request);

    private readonly record struct Step<T>(QuoteOutcome<T>? Failure, Func<Task<T>>? Build)
    {
        public static Step<T> Done(Func<Task<T>> build) => new(null, build);

        public static Step<T> Fail(QuoteOutcome<T> failure) => new(failure, null);
    }
}
