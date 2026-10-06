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
    /// <summary>
    /// Creates the draft of a ready_for_quote request, idempotently (BR-05, BR-06). Lock order: request row,
    /// then the organization row for the number. The unique index is the backstop of a lost race.
    /// </summary>
    public async Task<QuoteOutcome<QuoteCreated>> CreateAsync(
        QuoteActor actor, Guid requestId, CancellationToken cancellationToken)
    {
        try
        {
            return await CreateCoreAsync(actor, requestId, cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException postgres)
        {
            dbContext.ChangeTracker.Clear();

            if (postgres.SqlState == UniqueViolation && postgres.ConstraintName == OpenQuoteIndex)
            {
                // The transaction is rolled back; the winner is read again and returned unchanged.
                var winner = await ExistingOpenQuoteAsync(actor.OrganizationId, requestId, cancellationToken);

                return winner is { } id
                    && await BuildDetailAsync(actor.OrganizationId, actor.Scope, id.Id, true, cancellationToken) is { } detail
                        ? new QuoteOutcome<QuoteCreated>.Succeeded(new QuoteCreated(detail, false))
                        : Conflict<QuoteCreated>(ServiceRequestMessages.RequestChangedCode);
            }

            if (IsConflictState(postgres.SqlState))
            {
                return Conflict<QuoteCreated>(ServiceRequestMessages.RequestChangedCode);
            }

            throw new InvalidOperationException(
                $"The quote could not be saved (SqlState {postgres.SqlState}, constraint {postgres.ConstraintName}).");
        }
        catch (PostgresException postgres) when (IsConflictState(postgres.SqlState))
        {
            dbContext.ChangeTracker.Clear();

            return Conflict<QuoteCreated>(ServiceRequestMessages.RequestChangedCode);
        }
    }

    private async Task<QuoteOutcome<QuoteCreated>> CreateCoreAsync(
        QuoteActor actor, Guid requestId, CancellationToken cancellationToken)
    {
        var organizationId = actor.OrganizationId;
        var scopeAll = actor.Scope.All;
        var scopeIds = actor.Scope.BranchIds.ToArray();

        // Visibility first, with no lock: a foreign or out-of-scope request is an identical not found.
        if (!await dbContext.ServiceRequests.AsNoTracking().AnyAsync(
            request => request.Id == requestId
                && request.OrganizationId == organizationId
                && (scopeAll || request.BranchId == null || scopeIds.Contains(request.BranchId.Value)),
            cancellationToken))
        {
            return new QuoteOutcome<QuoteCreated>.NotFound();
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        if (await LockRequestAsync(actor, requestId, cancellationToken) is not { } status)
        {
            return new QuoteOutcome<QuoteCreated>.NotFound();
        }

        if (status != RequestTransitions.Code(RequestStatus.ReadyForQuote))
        {
            return Conflict<QuoteCreated>(ServiceRequestMessages.RequestChangedCode);
        }

        if (await ExistingOpenQuoteAsync(organizationId, requestId, cancellationToken) is { } existing)
        {
            if (existing.Status != QuoteStatus.Draft)
            {
                return Conflict<QuoteCreated>(ServiceRequestMessages.RequestChangedCode);
            }

            // An unsent quote is returned unchanged and consumes no number.
            await transaction.CommitAsync(cancellationToken);

            return new QuoteOutcome<QuoteCreated>.Succeeded(new QuoteCreated(await DetailAsync(actor, existing.Id, cancellationToken), false));
        }

        var request = await dbContext.ServiceRequests.AsNoTracking().SingleAsync(
            candidate => candidate.Id == requestId && candidate.OrganizationId == organizationId, cancellationToken);

        var locked = await dbContext.Database
            .SqlQuery<long>(
                $"""
                SELECT next_quote_number AS "Value" FROM organizations
                WHERE id = {organizationId} AND is_active = true
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);

        if (locked.Count != 1)
        {
            throw new InvalidOperationException("The organization is no longer available.");
        }

        var number = locked[0];
        var organization = await ReadOrganizationAsync(organizationId, cancellationToken);
        var categoryName = request.CategoryId is { } categoryId
            ? await dbContext.ServiceCategories.AsNoTracking()
                .Where(candidate => candidate.Id == categoryId && candidate.OrganizationId == organizationId)
                .Select(candidate => candidate.Name)
                .SingleOrDefaultAsync(cancellationToken)
            : null;
        var serviceName = request.CatalogItemId is { } itemId
            ? await dbContext.CatalogItems.AsNoTracking()
                .Where(candidate => candidate.Id == itemId && candidate.OrganizationId == organizationId)
                .Select(candidate => candidate.Name)
                .SingleOrDefaultAsync(cancellationToken)
            : null;

        var title = RequestCardRules.Title(serviceName, categoryName, request.Description);
        var today = OrganizationTime.LocalDate(timeProvider.GetUtcNow(), OrganizationTime.FindZone(organization.Timezone));

        var quote = Quote.Create(
            organizationId, request.BranchId, request.Id, request.CustomerId, request.PropertyId, number, actor.UserId);
        var version = QuoteVersion.Create(
            organizationId,
            quote.Id,
            1,
            string.IsNullOrWhiteSpace(title) ? "Service request" : title,
            0m,
            0m,
            0m,
            organization.Currency,
            actor.UserId,
            0m,
            QuoteTerms.PresetText(QuoteTerms.DueOnCompletion),
            today.AddDays(QuoteMessages.DefaultValidDays));

        dbContext.Quotes.Add(quote);
        dbContext.QuoteVersions.Add(version);

        // Quote-builder BR-33: the display number, status and version only.
        dbContext.AuditLogs.Add(AuditLog.Create(
            organizationId,
            "quote.created",
            AuditEntityType,
            actor.UserId,
            quote.Id,
            quote.BranchId,
            actor.IpAddress,
            afterData: Serialize(new Dictionary<string, object?>
            {
                ["quoteNumber"] = RequestCardRules.DisplayNumber(organization.QuotePrefix, number),
                ["status"] = "draft",
                ["versionNo"] = 1,
            })));

        await dbContext.Organizations
            .Where(candidate => candidate.Id == organizationId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(candidate => candidate.NextQuoteNumber, number + 1),
                cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new QuoteOutcome<QuoteCreated>.Succeeded(new QuoteCreated(await DetailAsync(actor, quote.Id, cancellationToken), true));
    }

    private Task<ExistingQuote?> ExistingOpenQuoteAsync(Guid organizationId, Guid requestId, CancellationToken cancellationToken) =>
        dbContext.Quotes.AsNoTracking()
            .Where(quote => quote.OrganizationId == organizationId
                && quote.RequestId == requestId
                && quote.Status != QuoteStatus.Cancelled)
            .Select(quote => new ExistingQuote(quote.Id, quote.Status))
            .SingleOrDefaultAsync(cancellationToken);

    private sealed record ExistingQuote(Guid Id, QuoteStatus Status);
}
