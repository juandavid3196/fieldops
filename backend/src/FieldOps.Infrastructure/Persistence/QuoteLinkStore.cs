using System.Text.Json;
using FieldOps.Application.Features.QuoteLinks;
using FieldOps.Application.Features.Quotes;
using FieldOps.Application.Features.ServiceRequests;
using FieldOps.Domain.Notifications;
using FieldOps.Domain.Quotes;
using FieldOps.Domain.Requests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace FieldOps.Infrastructure.Persistence;

/// <summary>
/// Persistence of the public quote link (customer-quote-approval). The organization, quote and version come only from
/// the token row, and every later query filters by that organization. Reads are no-tracking. A response locks the quote
/// row first, re-reads the state under the lock and applies BR-16; the partial unique indexes are the backstop (BR-17).
/// Logs carry the quote id and an outcome category only.
/// </summary>
internal sealed class QuoteLinkStore(
    FieldOpsDbContext dbContext,
    TimeProvider timeProvider,
    ILogger<QuoteLinkStore> logger) : IQuoteLinkStore
{
    private const string UniqueViolation = "23505";

    private const string FinalIndex = "ux_quote_responses_final";

    private const string ClarificationIndex = "ux_quote_responses_clarification";

    private const int MaxPhotos = 6;

    private const int MaxResponderNameLength = 180;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<QuoteLinkOutcome<PublicQuote>> ViewAsync(string token, CancellationToken cancellationToken)
    {
        var (link, failure) = await ResolveAsync(token, cancellationToken);

        return link is null
            ? Fail<PublicQuote>(failure)
            : new QuoteLinkOutcome<PublicQuote>.Succeeded(await BuildPublicQuoteAsync(link, cancellationToken));
    }

    public async Task<QuoteLinkOutcome<PublicTotals>> CalculateAsync(
        string token, IReadOnlyList<Guid> selectedOptionalLineIds, CancellationToken cancellationToken)
    {
        var (link, failure) = await ResolveAsync(token, cancellationToken);

        if (link is null)
        {
            return Fail<PublicTotals>(failure);
        }

        var lines = await LoadLinesAsync(link, cancellationToken);

        return TotalsFor(link.Version, lines, selectedOptionalLineIds) is { } totals
            ? new QuoteLinkOutcome<PublicTotals>.Succeeded(totals)
            : InvalidSelection<PublicTotals>();
    }

    public Task<QuoteLinkOutcome<PublicQuote>> ApproveAsync(
        string token, IReadOnlyList<Guid> selectedOptionalLineIds, QuoteLinkCaller caller, CancellationToken cancellationToken) =>
        RunAsync<PublicQuote>(
            token,
            async locked =>
            {
                var existing = await FinalResponseAsync(locked.Link, cancellationToken);

                if (existing is not null)
                {
                    return existing.Response == QuoteStatus.Approved ? Done(locked) : Refuse<PublicQuote>(new QuoteLinkOutcome<PublicQuote>.AlreadyAnswered());
                }

                var lines = await LoadLinesAsync(locked.Link, cancellationToken);

                if (TotalsFor(locked.Link.Version, lines, selectedOptionalLineIds) is not { } totals)
                {
                    return Refuse<PublicQuote>(InvalidSelection<PublicQuote>());
                }

                var now = timeProvider.GetUtcNow();
                var (name, contactId) = await ResponderAsync(locked.Quote, cancellationToken);
                var organizationId = locked.Quote.OrganizationId;
                var version = locked.Link.Version;
                var response = QuoteResponse.Approve(
                    organizationId,
                    version.Id,
                    name,
                    contactId,
                    caller.IpAddress,
                    now,
                    totals.Subtotal,
                    totals.DiscountTotal,
                    totals.TaxTotal,
                    totals.Total);

                dbContext.QuoteResponses.Add(response);

                foreach (var id in selectedOptionalLineIds)
                {
                    dbContext.QuoteResponseOptionalLines.Add(QuoteResponseOptionalLine.Create(organizationId, version.Id, response.Id, id));
                }

                var before = locked.Quote.Status;
                locked.Quote.Approve(version.Id, now);
                Audit(
                    locked.Quote,
                    "quote.approved",
                    caller,
                    before,
                    new
                    {
                        versionNo = version.VersionNo,
                        total = totals.Total,
                        currency = totals.Currency,
                        selectedOptionalLineIds,
                        termsAccepted = true,
                        userAgent = caller.UserAgent,
                    });

                return Done(locked, "approved");
            },
            cancellationToken);

    public Task<QuoteLinkOutcome<PublicQuote>> DeclineAsync(
        string token, string reason, QuoteLinkCaller caller, CancellationToken cancellationToken) =>
        RunAsync<PublicQuote>(
            token,
            async locked =>
            {
                var existing = await FinalResponseAsync(locked.Link, cancellationToken);

                if (existing is not null)
                {
                    return existing.Response == QuoteStatus.Rejected ? Done(locked) : Refuse<PublicQuote>(new QuoteLinkOutcome<PublicQuote>.AlreadyAnswered());
                }

                var now = timeProvider.GetUtcNow();
                var (name, contactId) = await ResponderAsync(locked.Quote, cancellationToken);
                var version = locked.Link.Version;

                dbContext.QuoteResponses.Add(QuoteResponse.Reject(
                    locked.Quote.OrganizationId, version.Id, name, contactId, caller.IpAddress, now, reason));

                var before = locked.Quote.Status;
                locked.Quote.Reject(now);
                Audit(locked.Quote, "quote.rejected", caller, before, new { versionNo = version.VersionNo, userAgent = caller.UserAgent });

                return Done(locked, "rejected");
            },
            cancellationToken);

    public Task<QuoteLinkOutcome<PublicQuote>> AskAsync(
        string token, string message, QuoteLinkCaller caller, CancellationToken cancellationToken) =>
        RunAsync<PublicQuote>(
            token,
            async locked =>
            {
                if (await FinalResponseAsync(locked.Link, cancellationToken) is not null)
                {
                    return Refuse<PublicQuote>(new QuoteLinkOutcome<PublicQuote>.AlreadyAnswered());
                }

                var version = locked.Link.Version;

                // One question per version: a later one returns the existing clarification and stores nothing (BR-15).
                if (await dbContext.QuoteResponses.AsNoTracking().AnyAsync(
                    response => response.OrganizationId == version.OrganizationId
                        && response.QuoteVersionId == version.Id
                        && response.Response == QuoteStatus.ClarificationRequested,
                    cancellationToken))
                {
                    return Done(locked);
                }

                var now = timeProvider.GetUtcNow();
                var (name, contactId) = await ResponderAsync(locked.Quote, cancellationToken);

                dbContext.QuoteResponses.Add(QuoteResponse.Ask(
                    locked.Quote.OrganizationId, version.Id, name, contactId, caller.IpAddress, now, message));

                var before = locked.Quote.Status;
                locked.Quote.RequestClarification(now);
                Audit(locked.Quote, "quote.clarification_requested", caller, before, new { versionNo = version.VersionNo, userAgent = caller.UserAgent });

                return Done(locked, "clarification requested");
            },
            cancellationToken);

    public async Task<QuoteLinkOutcome<PublicBinary>> GetPhotoAsync(string token, Guid photoId, CancellationToken cancellationToken)
    {
        var (link, failure) = await ResolveAsync(token, cancellationToken);

        if (link is null)
        {
            return Fail<PublicBinary>(failure);
        }

        // The photo must be one of the listed ones of this token's quote and organization (BR-07).
        var photoIds = (await ReadAssessmentAsync(link, cancellationToken)).PhotoIds;

        if (!photoIds.Contains(photoId))
        {
            return new QuoteLinkOutcome<PublicBinary>.Unavailable();
        }

        var photo = await dbContext.AssessmentAttachments.AsNoTracking()
            .Where(candidate => candidate.Id == photoId && candidate.OrganizationId == link.Token.OrganizationId && candidate.Content != null)
            .Select(candidate => new { candidate.MimeType, candidate.Content })
            .SingleOrDefaultAsync(cancellationToken);

        return photo is null
            ? new QuoteLinkOutcome<PublicBinary>.Unavailable()
            : new QuoteLinkOutcome<PublicBinary>.Succeeded(new PublicBinary(photo.MimeType, photo.Content!));
    }

    public async Task<QuoteLinkOutcome<PublicBinary>> GetLogoAsync(string token, CancellationToken cancellationToken)
    {
        var (link, failure) = await ResolveAsync(token, cancellationToken);

        if (link is null)
        {
            return Fail<PublicBinary>(failure);
        }

        var logo = await dbContext.OrganizationLogos.AsNoTracking()
            .Where(candidate => candidate.OrganizationId == link.Token.OrganizationId)
            .Select(candidate => new { candidate.ContentType, candidate.Content })
            .SingleOrDefaultAsync(cancellationToken);

        return logo is null
            ? new QuoteLinkOutcome<PublicBinary>.Unavailable()
            : new QuoteLinkOutcome<PublicBinary>.Succeeded(new PublicBinary(logo.ContentType, logo.Content));
    }

    private static QuoteLinkOutcome<T> Fail<T>(Failure failure) =>
        failure == Failure.Superseded ? new QuoteLinkOutcome<T>.Superseded() : new QuoteLinkOutcome<T>.Unavailable();

    private static QuoteLinkOutcome<T> InvalidSelection<T>() =>
        new QuoteLinkOutcome<T>.Invalid(new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [QuoteLinkMessages.SelectionKey] = [QuoteLinkMessages.SelectionMessage],
        });

    // Resolution (BR-03, BR-04, BR-18): superseded first, then revoked, cancelled or expired quotes, then the expiry date.
    private static Decision Decide(Link link, DateTimeOffset now)
    {
        if (link.Version.VersionNo < link.Quote.CurrentVersionNo)
        {
            return Decision.Superseded;
        }

        if (link.Token.RevokedAt is not null
            || link.Quote.Status is not (QuoteStatus.Sent or QuoteStatus.ClarificationRequested or QuoteStatus.Approved or QuoteStatus.Rejected))
        {
            return Decision.Unavailable;
        }

        if (link.Token.ExpiresAt <= now)
        {
            return link.Quote.Status is QuoteStatus.Sent or QuoteStatus.ClarificationRequested ? Decision.Expired : Decision.Unavailable;
        }

        return Decision.Valid;
    }

    private async Task<Link?> LoadLinkAsync(string hash, CancellationToken cancellationToken)
    {
        var row = await (
                from token in dbContext.QuoteAccessTokens.AsNoTracking()
                where token.TokenHash == hash
                join version in dbContext.QuoteVersions.AsNoTracking()
                    on new { token.OrganizationId, Id = token.QuoteVersionId } equals new { version.OrganizationId, version.Id }
                join quote in dbContext.Quotes.AsNoTracking()
                    on new { version.OrganizationId, Id = version.QuoteId } equals new { quote.OrganizationId, quote.Id }
                select new { token, version, quote })
            .SingleOrDefaultAsync(cancellationToken);

        return row is null ? null : new Link(row.token, row.version, row.quote);
    }

    /// <summary>The link of a valid token; a token that expired on a sent quote moves the quote to expired first (BR-18).</summary>
    private async Task<(Link? Link, Failure Failure)> ResolveAsync(string rawToken, CancellationToken cancellationToken)
    {
        if (!QuoteLinkTokens.IsWellFormed(rawToken))
        {
            return (null, Failure.Unavailable);
        }

        var hash = QuoteAccessTokens.Hash(rawToken);
        var link = await LoadLinkAsync(hash, cancellationToken);

        if (link is null)
        {
            return (null, Failure.Unavailable);
        }

        switch (Decide(link, timeProvider.GetUtcNow()))
        {
            case Decision.Valid:
                return (link, Failure.None);
            case Decision.Superseded:
                return (null, Failure.Superseded);
            case Decision.Expired:
                await ExpireAsync(hash, link, cancellationToken);

                return (null, Failure.Unavailable);
            default:
                return (null, Failure.Unavailable);
        }
    }

    // Lazy expiry in its own transaction, idempotent under the quote lock (BR-18); the caller then answers 404.
    private async Task ExpireAsync(string hash, Link link, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await LockQuoteAsync(link.Quote, cancellationToken);

        if (await LoadLinkAsync(hash, cancellationToken) is { } fresh && Decide(fresh, timeProvider.GetUtcNow()) == Decision.Expired)
        {
            await ExpireLockedAsync(link.Quote.OrganizationId, link.Quote.Id, fresh.Version.VersionNo, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private async Task ExpireLockedAsync(Guid organizationId, Guid quoteId, int versionNo, CancellationToken cancellationToken)
    {
        var quote = await dbContext.Quotes.SingleAsync(
            candidate => candidate.Id == quoteId && candidate.OrganizationId == organizationId, cancellationToken);
        var before = quote.Status;

        quote.Expire(timeProvider.GetUtcNow());
        Audit(quote, "quote.expired", null, before, new { versionNo }, QuoteStatus.Expired);
        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Quote {QuoteId} expired", quoteId);
    }

    private async Task LockQuoteAsync(Quote quote, CancellationToken cancellationToken)
    {
        var quoteId = quote.Id;
        var organizationId = quote.OrganizationId;

        await dbContext.Database
            .SqlQuery<Guid>(
                $"""
                SELECT id AS "Value" FROM quotes
                WHERE id = {quoteId} AND organization_id = {organizationId}
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);
    }

    // Runs one response under the quote lock; a unique violation of a partial index is resolved by repeating the read
    // and decision once in a new transaction (BR-17).
    private async Task<QuoteLinkOutcome<T>> RunAsync<T>(
        string rawToken, Func<Locked, Task<Step<T>>> apply, CancellationToken cancellationToken)
    {
        if (!QuoteLinkTokens.IsWellFormed(rawToken))
        {
            return new QuoteLinkOutcome<T>.Unavailable();
        }

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await RunOnceAsync(QuoteAccessTokens.Hash(rawToken), apply, cancellationToken);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: UniqueViolation } postgres
                && postgres.ConstraintName is FinalIndex or ClarificationIndex
                && attempt == 0)
            {
                dbContext.ChangeTracker.Clear();
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException postgres)
            {
                dbContext.ChangeTracker.Clear();

                // The database detail can quote the failing row: only state and constraint travel.
                throw new InvalidOperationException(
                    $"The quote response could not be saved (SqlState {postgres.SqlState}, constraint {postgres.ConstraintName}).");
            }
        }
    }

    private async Task<QuoteLinkOutcome<T>> RunOnceAsync<T>(
        string hash, Func<Locked, Task<Step<T>>> apply, CancellationToken cancellationToken)
    {
        var link = await LoadLinkAsync(hash, cancellationToken);

        if (link is null)
        {
            return new QuoteLinkOutcome<T>.Unavailable();
        }

        var first = Decide(link, timeProvider.GetUtcNow());

        if (first == Decision.Expired)
        {
            await ExpireAsync(hash, link, cancellationToken);

            return new QuoteLinkOutcome<T>.Unavailable();
        }

        if (first != Decision.Valid)
        {
            return Fail<T>(first == Decision.Superseded ? Failure.Superseded : Failure.Unavailable);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await LockQuoteAsync(link.Quote, cancellationToken);

        // The state, the token and the expiry are read again under the lock (BR-17, BR-18).
        var fresh = await LoadLinkAsync(hash, cancellationToken);
        var decision = fresh is null ? Decision.Unavailable : Decide(fresh, timeProvider.GetUtcNow());

        if (decision == Decision.Expired)
        {
            await ExpireLockedAsync(fresh!.Quote.OrganizationId, fresh.Quote.Id, fresh.Version.VersionNo, cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new QuoteLinkOutcome<T>.Unavailable();
        }

        if (decision != Decision.Valid)
        {
            return Fail<T>(decision == Decision.Superseded ? Failure.Superseded : Failure.Unavailable);
        }

        var quote = await dbContext.Quotes.SingleAsync(
            candidate => candidate.Id == fresh!.Quote.Id && candidate.OrganizationId == fresh.Quote.OrganizationId, cancellationToken);
        var step = await apply(new Locked(fresh!, quote));

        if (step.Failure is not null)
        {
            await transaction.RollbackAsync(cancellationToken);

            return step.Failure;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await step.Build!();
    }

    // The final outcome is built after the commit from the stored rows.
    private Step<PublicQuote> Done(Locked locked, string? answered = null) =>
        new(null, async () =>
        {
            if (answered is not null)
            {
                logger.LogInformation("Quote {QuoteId} answered: {Outcome}", locked.Quote.Id, answered);
            }

            var link = await LoadLinkAsync(locked.Link.Token.TokenHash, CancellationToken.None)
                ?? throw new InvalidOperationException("The quote link is no longer available.");

            return new QuoteLinkOutcome<PublicQuote>.Succeeded(await BuildPublicQuoteAsync(link, CancellationToken.None));
        });

    private static Step<T> Refuse<T>(QuoteLinkOutcome<T> failure) => new(failure, null);

    private Task<QuoteResponse?> FinalResponseAsync(Link link, CancellationToken cancellationToken) =>
        dbContext.QuoteResponses.AsNoTracking().SingleOrDefaultAsync(
            response => response.OrganizationId == link.Version.OrganizationId
                && response.QuoteVersionId == link.Version.Id
                && (response.Response == QuoteStatus.Approved || response.Response == QuoteStatus.Rejected),
            cancellationToken);

    // BR-13: the linked active contact, else the guest name, else the customer name; the contact id only for the first.
    private async Task<(string Name, Guid? ContactId)> ResponderAsync(Quote quote, CancellationToken cancellationToken)
    {
        var organizationId = quote.OrganizationId;
        var request = await dbContext.ServiceRequests.AsNoTracking()
            .Where(candidate => candidate.Id == quote.RequestId && candidate.OrganizationId == organizationId)
            .Select(candidate => new { candidate.ContactId, candidate.GuestName, candidate.CustomerId })
            .SingleAsync(cancellationToken);

        var contact = request.ContactId is { } contactId
            ? await dbContext.CustomerContacts.AsNoTracking()
                .Where(candidate => candidate.Id == contactId && candidate.OrganizationId == organizationId && candidate.IsActive)
                .Select(candidate => new { candidate.Id, candidate.FirstName, candidate.LastName })
                .SingleOrDefaultAsync(cancellationToken)
            : null;

        string? name = contact is null
            ? null
            : string.Join(' ', new[] { contact.FirstName, contact.LastName }.Where(part => !string.IsNullOrWhiteSpace(part))).Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            name = request.GuestName;
        }

        if (string.IsNullOrWhiteSpace(name) && request.CustomerId is { } customerId)
        {
            name = await dbContext.Customers.AsNoTracking()
                .Where(candidate => candidate.Id == customerId && candidate.OrganizationId == organizationId)
                .Select(candidate => candidate.DisplayName)
                .SingleOrDefaultAsync(cancellationToken);
        }

        name = string.IsNullOrWhiteSpace(name) ? "Customer" : name.Trim();

        return (name.Length > MaxResponderNameLength ? name[..MaxResponderNameLength] : name, contact?.Id);
    }

    private Task<List<QuoteLine>> LoadLinesAsync(Link link, CancellationToken cancellationToken) =>
        dbContext.QuoteLines.AsNoTracking()
            .Where(line => line.OrganizationId == link.Version.OrganizationId && line.QuoteVersionId == link.Version.Id)
            .OrderBy(line => line.SortOrder)
            .ThenBy(line => line.Id)
            .ToListAsync(cancellationToken);

    // Null when an id is a duplicate or not an optional line of the version (BR-10), or a figure does not fit (BR-11).
    private static PublicTotals? TotalsFor(QuoteVersion version, List<QuoteLine> lines, IReadOnlyList<Guid> selectedIds)
    {
        if (selectedIds.Distinct().Count() != selectedIds.Count)
        {
            return null;
        }

        var optional = lines.Where(line => line.IsOptional).ToDictionary(line => line.Id);
        var selected = new List<FrozenOptionalLine>();

        foreach (var id in selectedIds)
        {
            if (!optional.TryGetValue(id, out var line))
            {
                return null;
            }

            selected.Add(new FrozenOptionalLine(line.TaxRate, line.LineSubtotal, line.LineTax));
        }

        return QuoteLinkTotals.WithOptions(FrozenOf(version, lines), selected);
    }

    private static FrozenQuoteTotals FrozenOf(QuoteVersion version, List<QuoteLine> lines) =>
        new(
            version.Subtotal,
            version.DiscountTotal,
            version.TaxTotal,
            version.Currency,
            lines.Where(line => !line.IsOptional).Select(line => line.TaxRate).ToList());

    // The latest completed assessment of the request and its photos in creation order, up to six (BR-07).
    private async Task<(DateTimeOffset? CompletedAt, List<Guid> PhotoIds)> ReadAssessmentAsync(Link link, CancellationToken cancellationToken)
    {
        var organizationId = link.Quote.OrganizationId;
        var requestId = link.Quote.RequestId;
        var assessment = await dbContext.Assessments.AsNoTracking()
            .Where(candidate => candidate.OrganizationId == organizationId
                && candidate.RequestId == requestId
                && candidate.Status == AssessmentStatus.Completed
                && candidate.CompletedAt != null)
            .OrderByDescending(candidate => candidate.CompletedAt)
            .ThenByDescending(candidate => candidate.Id)
            .Select(candidate => new { candidate.Id, candidate.CompletedAt })
            .FirstOrDefaultAsync(cancellationToken);

        if (assessment is null)
        {
            return (null, []);
        }

        var photoIds = await dbContext.AssessmentAttachments.AsNoTracking()
            .Where(photo => photo.OrganizationId == organizationId && photo.AssessmentId == assessment.Id && photo.Content != null)
            .OrderBy(photo => photo.CreatedAt)
            .ThenBy(photo => photo.Id)
            .Select(photo => photo.Id)
            .Take(MaxPhotos)
            .ToListAsync(cancellationToken);

        return (assessment.CompletedAt, photoIds);
    }

    private async Task<PublicQuote> BuildPublicQuoteAsync(Link link, CancellationToken cancellationToken)
    {
        var organizationId = link.Quote.OrganizationId;
        var quote = link.Quote;
        var version = link.Version;

        var organization = await dbContext.Organizations.AsNoTracking()
            .Where(candidate => candidate.Id == organizationId)
            .Select(candidate => new { candidate.Name, candidate.Phone, candidate.Timezone, candidate.QuotePrefix })
            .SingleAsync(cancellationToken);
        var zone = OrganizationTime.FindZone(organization.Timezone);

        var branchPhone = quote.BranchId is { } branchId
            ? await dbContext.Branches.AsNoTracking()
                .Where(candidate => candidate.Id == branchId && candidate.OrganizationId == organizationId)
                .Select(candidate => candidate.Phone)
                .SingleOrDefaultAsync(cancellationToken)
            : null;
        var hasLogo = await dbContext.OrganizationLogos.AsNoTracking()
            .AnyAsync(candidate => candidate.OrganizationId == organizationId, cancellationToken);

        var request = await dbContext.ServiceRequests.AsNoTracking()
            .Where(candidate => candidate.Id == quote.RequestId && candidate.OrganizationId == organizationId)
            .Select(candidate => new
            {
                candidate.CreatedAt,
                candidate.CustomerId,
                candidate.PropertyId,
                candidate.GuestName,
                candidate.ServiceAddress,
            })
            .SingleAsync(cancellationToken);

        var customerName = request.CustomerId is { } customerId
            ? await dbContext.Customers.AsNoTracking()
                .Where(candidate => candidate.Id == customerId && candidate.OrganizationId == organizationId)
                .Select(candidate => candidate.DisplayName)
                .SingleOrDefaultAsync(cancellationToken)
            : null;

        var property = request.PropertyId is { } propertyId
            ? await dbContext.Properties.AsNoTracking()
                .Where(candidate => candidate.Id == propertyId && candidate.OrganizationId == organizationId)
                .Select(candidate => new { candidate.AddressLine1, candidate.AddressLine2, candidate.City, candidate.StateRegion, candidate.PostalCode })
                .SingleOrDefaultAsync(cancellationToken)
            : null;
        var address = property is null
            ? QuoteStore.FormatServiceAddress(request.ServiceAddress)
            : QuoteStore.FormatAddress(property.AddressLine1, property.AddressLine2, property.City, property.StateRegion, property.PostalCode);

        var lines = await LoadLinesAsync(link, cancellationToken);
        var (assessmentCompletedAt, photoIds) = await ReadAssessmentAsync(link, cancellationToken);

        var responses = await dbContext.QuoteResponses.AsNoTracking()
            .Where(response => response.OrganizationId == organizationId && response.QuoteVersionId == version.Id)
            .ToListAsync(cancellationToken);
        var final = responses.FirstOrDefault(response => response.Response is QuoteStatus.Approved or QuoteStatus.Rejected);
        var clarification = responses.FirstOrDefault(response => response.Response == QuoteStatus.ClarificationRequested);

        var frozen = FrozenOf(version, lines);
        var versionTotals = QuoteLinkTotals.WithOptions(frozen, [])
            ?? throw new InvalidOperationException("The version totals do not fit.");

        PublicResponse? publicResponse = null;

        if (final is not null)
        {
            var selectedIds = final.Response == QuoteStatus.Approved
                ? (await dbContext.QuoteResponseOptionalLines.AsNoTracking()
                    .Where(selection => selection.OrganizationId == organizationId && selection.QuoteResponseId == final.Id)
                    .Select(selection => selection.QuoteLineId)
                    .ToListAsync(cancellationToken)).ToHashSet()
                : [];
            var orderedIds = lines.Where(line => selectedIds.Contains(line.Id)).Select(line => line.Id).ToList();
            var approvedTotals = final.Response == QuoteStatus.Approved && final.Subtotal is { } subtotal
                ? new PublicTotals(
                    subtotal,
                    final.DiscountTotal ?? 0m,
                    QuoteCalculator.TaxLabel(
                        frozen.NonOptionalTaxRates.Concat(lines.Where(line => selectedIds.Contains(line.Id)).Select(line => line.TaxRate))),
                    final.TaxTotal ?? 0m,
                    final.Total ?? 0m,
                    version.Currency)
                : null;

            publicResponse = new PublicResponse(
                QuoteStatusCodes.Code(final.Response),
                OrganizationTime.LocalDate(final.RespondedAt, zone),
                orderedIds,
                approvedTotals);
        }

        var sentOn = OrganizationTime.LocalDate(version.SentAt ?? version.CreatedAt, zone);

        return new PublicQuote(
            new PublicOrganization(
                organization.Name,
                !string.IsNullOrWhiteSpace(branchPhone) ? branchPhone.Trim() : string.IsNullOrWhiteSpace(organization.Phone) ? null : organization.Phone.Trim(),
                hasLogo),
            new PublicQuoteInfo(
                RequestCardRules.DisplayNumber(organization.QuotePrefix, quote.QuoteNumber),
                version.VersionNo,
                QuoteStatusCodes.Code(quote.Status),
                sentOn,
                version.ValidUntil ?? sentOn,
                version.Scope,
                string.IsNullOrWhiteSpace(version.CustomerNotes) ? null : version.CustomerNotes,
                string.IsNullOrWhiteSpace(version.Terms) ? null : version.Terms),
            new PublicCustomer(customerName ?? request.GuestName ?? string.Empty, address),
            lines.Where(line => !line.IsOptional).Select(line => line.Name).ToList(),
            lines.Select(line => new PublicLine(
                    line.IsOptional ? line.Id : null,
                    line.Name,
                    line.Description,
                    line.Quantity,
                    line.Unit,
                    line.UnitPrice,
                    line.LineSubtotal,
                    line.IsOptional))
                .ToList(),
            versionTotals,
            photoIds.Select(id => new PublicPhoto(id)).ToList(),
            new PublicProgress(
                OrganizationTime.LocalDate(request.CreatedAt, zone),
                assessmentCompletedAt is { } completedAt ? OrganizationTime.LocalDate(completedAt, zone) : null),
            clarification is null ? null : new PublicClarification(OrganizationTime.LocalDate(clarification.RespondedAt, zone)),
            publicResponse);
    }

    // BR-22: the version, codes and totals only; never reasons, questions, names, contact data or tokens.
    private void Audit(Quote quote, string action, QuoteLinkCaller? caller, QuoteStatus before, object metadata, QuoteStatus? after = null) =>
        dbContext.AuditLogs.Add(AuditLog.Create(
            quote.OrganizationId,
            action,
            "quote",
            null,
            quote.Id,
            quote.BranchId,
            caller?.IpAddress,
            Serialize(new Dictionary<string, object?> { ["status"] = QuoteStatusCodes.Code(before) }),
            Serialize(new Dictionary<string, object?> { ["status"] = QuoteStatusCodes.Code(after ?? quote.Status) }),
            Serialize(metadata)));

    private static string Serialize(object value) => JsonSerializer.Serialize(value, JsonOptions);

    private enum Decision
    {
        Unavailable,
        Superseded,
        Expired,
        Valid,
    }

    private enum Failure
    {
        None,
        Unavailable,
        Superseded,
    }

    private sealed record Link(QuoteAccessToken Token, QuoteVersion Version, Quote Quote);

    private sealed record Locked(Link Link, Quote Quote);

    private readonly record struct Step<T>(QuoteLinkOutcome<T>? Failure, Func<Task<QuoteLinkOutcome<T>>>? Build);
}
