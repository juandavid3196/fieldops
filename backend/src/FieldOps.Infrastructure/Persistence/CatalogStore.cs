using System.Net;
using System.Text.Json;
using FieldOps.Application.Auditing;
using FieldOps.Application.Features.Catalog;
using FieldOps.Domain.Catalog;
using FieldOps.Domain.Notifications;
using FieldOps.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FieldOps.Infrastructure.Persistence;

internal sealed class CatalogStore(FieldOpsDbContext dbContext) : ICatalogStore
{
    public async Task<CatalogOrganizationFormat?> GetOrganizationFormatAsync(
        Guid organizationId, CancellationToken cancellationToken)
    {
        var row = await dbContext.Organizations.AsNoTracking()
            .Where(organization => organization.Id == organizationId)
            .Select(organization => new { organization.Currency, organization.Timezone })
            .SingleOrDefaultAsync(cancellationToken);

        return row is null ? null : new CatalogOrganizationFormat(row.Currency, row.Timezone);
    }

    public async Task<CatalogListPage> ListAsync(
        Guid organizationId, CatalogListFilter filter, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = Filtered(organizationId, filter);
        var totalCount = await query.CountAsync(cancellationToken);

        var rows = await Project(Ordered(query, filter))
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new CatalogListPage([.. rows.Select(ToRow)], page, pageSize, totalCount);
    }

    public async Task<IReadOnlyList<CatalogItemRow>> ListAllAsync(
        Guid organizationId, CatalogListFilter filter, CancellationToken cancellationToken)
    {
        var rows = await Project(Ordered(Filtered(organizationId, filter), filter)).ToListAsync(cancellationToken);

        return [.. rows.Select(ToRow)];
    }

    public async Task<CatalogSummary> GetSummaryAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var groups = await dbContext.CatalogItems.AsNoTracking()
            .Where(item => item.OrganizationId == organizationId)
            .GroupBy(item => new { item.Type, item.IsActive })
            .Select(group => new { group.Key.Type, group.Key.IsActive, Count = group.Count() })
            .ToListAsync(cancellationToken);

        var currency = await dbContext.Organizations.AsNoTracking()
            .Where(organization => organization.Id == organizationId)
            .Select(organization => organization.Currency)
            .SingleAsync(cancellationToken);

        var anyActiveCategory = await dbContext.ServiceCategories.AsNoTracking()
            .AnyAsync(category => category.OrganizationId == organizationId && category.IsActive, cancellationToken);

        var readyCategory = anyActiveCategory
            && await dbContext.ServiceCategories.AsNoTracking().AnyAsync(
                category => category.OrganizationId == organizationId
                    && category.IsActive
                    && dbContext.CatalogItems.Any(item =>
                        item.OrganizationId == organizationId
                        && item.CategoryId == category.Id
                        && item.IsActive
                        && item.Type == CatalogItemType.Service),
                cancellationToken);

        var readiness = readyCategory ? "ready" : anyActiveCategory ? "no_active_services" : "no_active_categories";

        int Count(Func<CatalogItemType, bool, bool> predicate) =>
            groups.Where(group => predicate(group.Type, group.IsActive)).Sum(group => group.Count);

        return new CatalogSummary(
            ActiveItems: Count((_, active) => active),
            ActiveServices: Count((type, active) => active && type == CatalogItemType.Service),
            ActiveProducts: Count((type, active) => active && type == CatalogItemType.Product),
            InactiveItems: Count((_, active) => !active),
            AllItems: Count((_, _) => true),
            Services: Count((type, _) => type == CatalogItemType.Service),
            Products: Count((type, _) => type == CatalogItemType.Product),
            Currency: currency,
            PublicRequestReadiness: readiness);
    }

    public Task<CatalogItemDetail?> GetDetailAsync(Guid organizationId, Guid itemId, CancellationToken cancellationToken) =>
        BuildDetailAsync(organizationId, itemId, cancellationToken);

    public Task<bool> IsCategoryAssignableAsync(
        Guid organizationId, Guid categoryId, Guid? currentItemId, CancellationToken cancellationToken) =>
        dbContext.ServiceCategories.AsNoTracking().AnyAsync(
            category => category.OrganizationId == organizationId
                && category.Id == categoryId
                && (category.IsActive
                    || (currentItemId != null
                        && dbContext.CatalogItems.Any(item =>
                            item.OrganizationId == organizationId
                            && item.Id == currentItemId
                            && item.CategoryId == categoryId))),
            cancellationToken);

    public Task<bool> ItemExistsAsync(Guid organizationId, Guid itemId, CancellationToken cancellationToken) =>
        dbContext.CatalogItems.AsNoTracking()
            .AnyAsync(item => item.OrganizationId == organizationId && item.Id == itemId, cancellationToken);

    public Task<bool> NameExistsAsync(
        Guid organizationId,
        CatalogItemType type,
        string normalizedName,
        Guid? excludeItemId,
        CancellationToken cancellationToken) =>
        dbContext.CatalogItems.AsNoTracking().AnyAsync(
            item => item.OrganizationId == organizationId
                && item.Type == type
                && item.NormalizedName == normalizedName
                && (excludeItemId == null || item.Id != excludeItemId),
            cancellationToken);

    public async Task<HashSet<(CatalogItemType Type, string NormalizedName)>> FindExistingNamesAsync(
        Guid organizationId, IReadOnlyCollection<string> normalizedNames, CancellationToken cancellationToken)
    {
        var names = normalizedNames.ToArray();

        var rows = await dbContext.CatalogItems.AsNoTracking()
            .Where(item => item.OrganizationId == organizationId && names.Contains(item.NormalizedName))
            .Select(item => new { item.Type, item.NormalizedName })
            .ToListAsync(cancellationToken);

        return [.. rows.Select(row => (row.Type, row.NormalizedName))];
    }

    // One SaveChangesAsync: the item and its catalog_item.created audit row are written together or not at all.
    public async Task<CatalogItemDetail> CreateAsync(
        Guid organizationId,
        CatalogItemValues values,
        Guid actorUserId,
        IPAddress? clientIp,
        CancellationToken cancellationToken)
    {
        var item = CatalogItem.Create(
            organizationId,
            values.Type,
            values.Name,
            values.Description,
            values.UnitCost,
            values.UnitPrice,
            values.IsTaxable,
            values.IsActive,
            values.CategoryId);

        var createFields = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["type"] = CatalogItemRules.TypeText(item.Type),
            ["name"] = item.Name,
            ["unitCost"] = item.UnitCost,
            ["unitPrice"] = item.UnitPrice,
            ["isTaxable"] = item.IsTaxable,
            ["isActive"] = item.IsActive,
        };

        if (item.CategoryId is { } createdCategoryId)
        {
            createFields["categoryId"] = createdCategoryId;
        }

        var (_, after) = AuditFieldDiff.ForCreate(createFields);

        dbContext.CatalogItems.Add(item);
        dbContext.AuditLogs.Add(AuditLog.Create(
            organizationId,
            CatalogAuditActions.Created,
            CatalogAuditActions.EntityType,
            actorUserId: actorUserId,
            entityId: item.Id,
            ipAddress: clientIp,
            afterData: after));

        await SaveAsync(cancellationToken);

        return (await BuildDetailAsync(organizationId, item.Id, cancellationToken))!;
    }

    public async Task<CatalogItemDetail?> UpdateAsync(
        Guid organizationId,
        Guid itemId,
        CatalogItemValues values,
        Guid actorUserId,
        IPAddress? clientIp,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var item = await dbContext.CatalogItems.SingleOrDefaultAsync(
            candidate => candidate.OrganizationId == organizationId && candidate.Id == itemId, cancellationToken);

        if (item is null)
        {
            return null;
        }

        var before = Fields(item);

        var changed = item.Update(
            values.Type,
            values.Name,
            values.Description,
            values.UnitCost,
            values.UnitPrice,
            values.IsTaxable,
            values.IsActive,
            now);
        changed |= item.SetCategory(values.CategoryId, now);

        if (changed)
        {
            var after = Fields(item);
            var changedKeys = after.Keys.Where(key => !Equals(before[key], after[key])).ToList();
            string action;
            string? beforeData;
            string? afterData;

            if (changedKeys is ["isActive"])
            {
                (beforeData, afterData) = AuditFieldDiff.ForStateChange(!item.IsActive, item.IsActive);
                action = item.IsActive ? CatalogAuditActions.Activated : CatalogAuditActions.Deactivated;
            }
            else
            {
                (beforeData, afterData) = AuditFieldDiff.ForUpdate(before, after);
                action = CatalogAuditActions.Updated;
            }

            dbContext.AuditLogs.Add(AuditLog.Create(
                organizationId,
                action,
                CatalogAuditActions.EntityType,
                actorUserId: actorUserId,
                entityId: item.Id,
                ipAddress: clientIp,
                beforeData: beforeData,
                afterData: afterData));

            await SaveAsync(cancellationToken);
        }

        return await BuildDetailAsync(organizationId, itemId, cancellationToken);
    }

    public async Task<CatalogStateOutcome> SetActiveAsync(
        Guid organizationId,
        Guid itemId,
        bool isActive,
        Guid actorUserId,
        IPAddress? clientIp,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var item = await dbContext.CatalogItems.SingleOrDefaultAsync(
            candidate => candidate.OrganizationId == organizationId && candidate.Id == itemId, cancellationToken);

        if (item is null)
        {
            return CatalogStateOutcome.NotFound;
        }

        if (!item.SetActive(isActive, now))
        {
            return CatalogStateOutcome.NoChange;
        }

        var (before, after) = AuditFieldDiff.ForStateChange(!isActive, isActive);

        dbContext.AuditLogs.Add(AuditLog.Create(
            organizationId,
            isActive ? CatalogAuditActions.Activated : CatalogAuditActions.Deactivated,
            CatalogAuditActions.EntityType,
            actorUserId: actorUserId,
            entityId: item.Id,
            ipAddress: clientIp,
            beforeData: before,
            afterData: after));

        await SaveAsync(cancellationToken);

        return CatalogStateOutcome.Changed;
    }

    public Task<CatalogImageContent?> GetImageAsync(Guid organizationId, Guid itemId, CancellationToken cancellationToken) =>
        dbContext.CatalogItemImages.AsNoTracking()
            .Where(image => image.OrganizationId == organizationId && image.CatalogItemId == itemId)
            .Select(image => new CatalogImageContent(image.ContentType, image.Content, image.UpdatedAt))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<CatalogImageInfo?> UpsertImageAsync(
        Guid organizationId,
        Guid itemId,
        string contentType,
        byte[] content,
        Guid actorUserId,
        IPAddress? clientIp,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!await ItemExistsAsync(organizationId, itemId, cancellationToken))
        {
            return null;
        }

        for (var attempt = 0; ; attempt++)
        {
            var existing = await dbContext.CatalogItemImages.SingleOrDefaultAsync(
                image => image.OrganizationId == organizationId && image.CatalogItemId == itemId, cancellationToken);

            var before = existing is null ? null : Summarize(existing.ContentType, existing.SizeBytes);
            CatalogItemImage image;

            if (existing is null)
            {
                image = CatalogItemImage.Create(itemId, organizationId, contentType, content, now);
                dbContext.CatalogItemImages.Add(image);
            }
            else
            {
                existing.Replace(contentType, content, now);
                image = existing;
            }

            dbContext.AuditLogs.Add(AuditLog.Create(
                organizationId,
                CatalogAuditActions.ImageUpdated,
                CatalogAuditActions.EntityType,
                actorUserId: actorUserId,
                entityId: itemId,
                ipAddress: clientIp,
                beforeData: before,
                afterData: Summarize(image.ContentType, image.SizeBytes)));

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);

                return new CatalogImageInfo(image.ContentType, image.SizeBytes, image.UpdatedAt);
            }
            catch (Exception ex) when (attempt == 0 && IsRetryableImageRace(ex))
            {
                // Two first uploads collided on the primary key, or the row vanished between read and
                // update: start over once from a clean change tracker.
                dbContext.ChangeTracker.Clear();
            }
        }
    }

    public async Task<CatalogImageRemoval> DeleteImageAsync(
        Guid organizationId,
        Guid itemId,
        Guid actorUserId,
        IPAddress? clientIp,
        CancellationToken cancellationToken)
    {
        if (!await ItemExistsAsync(organizationId, itemId, cancellationToken))
        {
            return CatalogImageRemoval.ItemNotFound;
        }

        var existing = await dbContext.CatalogItemImages.SingleOrDefaultAsync(
            image => image.OrganizationId == organizationId && image.CatalogItemId == itemId, cancellationToken);

        if (existing is null)
        {
            return CatalogImageRemoval.NoImage;
        }

        dbContext.CatalogItemImages.Remove(existing);
        dbContext.AuditLogs.Add(AuditLog.Create(
            organizationId,
            CatalogAuditActions.ImageRemoved,
            CatalogAuditActions.EntityType,
            actorUserId: actorUserId,
            entityId: itemId,
            ipAddress: clientIp,
            beforeData: Summarize(existing.ContentType, existing.SizeBytes)));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);

            return CatalogImageRemoval.Removed;
        }
        catch (DbUpdateConcurrencyException)
        {
            // A concurrent delete removed the row first: nothing left to remove.
            dbContext.ChangeTracker.Clear();

            return CatalogImageRemoval.NoImage;
        }
    }

    // One SaveChangesAsync (implicit single transaction): every item plus the single imported audit row.
    public async Task<int> ImportAsync(
        Guid organizationId,
        IReadOnlyList<CatalogItemValues> items,
        Guid actorUserId,
        IPAddress? clientIp,
        CancellationToken cancellationToken)
    {
        foreach (var values in items)
        {
            dbContext.CatalogItems.Add(CatalogItem.Create(
                organizationId,
                values.Type,
                values.Name,
                values.Description,
                values.UnitCost,
                values.UnitPrice,
                values.IsTaxable,
                values.IsActive));
        }

        dbContext.AuditLogs.Add(AuditLog.Create(
            organizationId,
            CatalogAuditActions.Imported,
            CatalogAuditActions.EntityType,
            actorUserId: actorUserId,
            ipAddress: clientIp,
            metadata: JsonSerializer.Serialize(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["importedCount"] = items.Count,
            })));

        await SaveAsync(cancellationToken);

        return items.Count;
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsNameIndexViolation(ex))
        {
            throw new DuplicateCatalogItemNameException(ex);
        }
    }

    private IQueryable<CatalogItem> Filtered(Guid organizationId, CatalogListFilter filter)
    {
        var query = dbContext.CatalogItems.AsNoTracking().Where(item => item.OrganizationId == organizationId);

        if (filter.Type is { } type)
        {
            query = query.Where(item => item.Type == type);
        }

        if (filter.Search is { } search)
        {
            var pattern = $"%{search.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal)}%";

            query = query.Where(item =>
                EF.Functions.ILike(item.Name, pattern)
                || (item.Description != null && EF.Functions.ILike(item.Description, pattern)));
        }

        if (filter.Taxable is { } taxable)
        {
            query = query.Where(item => item.IsTaxable == taxable);
        }

        if (filter.Active is { } active)
        {
            query = query.Where(item => item.IsActive == active);
        }

        return query;
    }

    // Ascending follows the labels shown in the UI (Product < Service, Active < Inactive, No < Yes);
    // ties break by name, then id.
    private static IOrderedQueryable<CatalogItem> Ordered(IQueryable<CatalogItem> query, CatalogListFilter filter)
    {
        var desc = filter.Descending;

        var ordered = filter.Sort switch
        {
            CatalogSortField.Type => desc
                ? query.OrderByDescending(item => item.Type == CatalogItemType.Product ? 0 : 1)
                : query.OrderBy(item => item.Type == CatalogItemType.Product ? 0 : 1),
            CatalogSortField.UnitCost => desc
                ? query.OrderByDescending(item => item.UnitCost)
                : query.OrderBy(item => item.UnitCost),
            CatalogSortField.UnitPrice => desc
                ? query.OrderByDescending(item => item.UnitPrice)
                : query.OrderBy(item => item.UnitPrice),
            CatalogSortField.Taxable => desc
                ? query.OrderByDescending(item => item.IsTaxable)
                : query.OrderBy(item => item.IsTaxable),
            CatalogSortField.Status => desc
                ? query.OrderByDescending(item => item.IsActive ? 0 : 1)
                : query.OrderBy(item => item.IsActive ? 0 : 1),
            _ => desc
                ? query.OrderByDescending(item => item.NormalizedName)
                : query.OrderBy(item => item.NormalizedName),
        };

        return filter.Sort == CatalogSortField.Name
            ? ordered.ThenBy(item => item.Id)
            : ordered.ThenBy(item => item.NormalizedName).ThenBy(item => item.Id);
    }

    private IQueryable<RowProjection> Project(IQueryable<CatalogItem> query) =>
        query.Select(item => new RowProjection(
            item.Id,
            item.Type,
            item.Name,
            item.Description,
            item.UnitCost,
            item.UnitPrice,
            item.IsTaxable,
            item.IsActive,
            dbContext.CatalogItemImages.Any(image => image.CatalogItemId == item.Id),
            item.UpdatedAt));

    private static CatalogItemRow ToRow(RowProjection row) =>
        new(
            row.Id,
            CatalogItemRules.TypeText(row.Type),
            row.Name,
            row.Description,
            row.UnitCost,
            row.UnitPrice,
            CatalogItem.EstimatedMarginPercent(row.UnitCost, row.UnitPrice),
            row.IsTaxable,
            row.IsActive,
            row.HasImage,
            row.UpdatedAt);

    private async Task<CatalogItemDetail?> BuildDetailAsync(
        Guid organizationId, Guid itemId, CancellationToken cancellationToken)
    {
        var projection = await Project(
                dbContext.CatalogItems.AsNoTracking()
                    .Where(item => item.OrganizationId == organizationId && item.Id == itemId))
            .SingleOrDefaultAsync(cancellationToken);

        if (projection is null)
        {
            return null;
        }

        var row = ToRow(projection);

        var image = await dbContext.CatalogItemImages.AsNoTracking()
            .Where(candidate => candidate.OrganizationId == organizationId && candidate.CatalogItemId == itemId)
            .Select(candidate => new CatalogImageInfo(candidate.ContentType, candidate.SizeBytes, candidate.UpdatedAt))
            .SingleOrDefaultAsync(cancellationToken);

        var itemCategoryId = await dbContext.CatalogItems.AsNoTracking()
            .Where(item => item.OrganizationId == organizationId && item.Id == itemId)
            .Select(item => item.CategoryId)
            .SingleAsync(cancellationToken);

        var category = itemCategoryId is { } categoryId
            ? await dbContext.ServiceCategories.AsNoTracking()
                .Where(candidate => candidate.OrganizationId == organizationId && candidate.Id == categoryId)
                .Select(candidate => new { candidate.Id, candidate.Name, candidate.IsActive })
                .SingleOrDefaultAsync(cancellationToken)
            : null;

        return new CatalogItemDetail(
            row.Id,
            row.Type,
            row.Name,
            row.Description,
            row.UnitCost,
            row.UnitPrice,
            row.EstimatedMarginPercent,
            row.IsTaxable,
            row.IsActive,
            row.HasImage,
            row.UpdatedAt,
            image,
            await CountUsageAsync(organizationId, itemId, cancellationToken),
            category?.Id,
            category?.Name,
            category?.IsActive);
    }

    // BR-10: distinct records of the session organization; a record reached through two paths counts once.
    private async Task<CatalogUsage> CountUsageAsync(Guid organizationId, Guid itemId, CancellationToken cancellationToken)
    {
        var quotes = await (
            from line in dbContext.QuoteLines.AsNoTracking()
            join version in dbContext.QuoteVersions.AsNoTracking() on line.QuoteVersionId equals version.Id
            join quote in dbContext.Quotes.AsNoTracking() on version.QuoteId equals quote.Id
            where line.CatalogItemId == itemId && quote.OrganizationId == organizationId
            select quote.Id)
            .Distinct()
            .CountAsync(cancellationToken);

        var jobs = await dbContext.WorkOrders.AsNoTracking()
            .Where(order => order.OrganizationId == organizationId
                && (dbContext.QuoteLines.Any(line =>
                        line.QuoteVersionId == order.QuoteVersionId && line.CatalogItemId == itemId)
                    || dbContext.Visits.Any(visit =>
                        visit.WorkOrderId == order.Id
                        && dbContext.VisitMaterials.Any(material =>
                            material.VisitId == visit.Id && material.CatalogItemId == itemId))))
            .CountAsync(cancellationToken);

        var invoices = await dbContext.Invoices.AsNoTracking()
            .Where(invoice => invoice.OrganizationId == organizationId
                && dbContext.InvoiceLines.Any(invoiceLine =>
                    invoiceLine.InvoiceId == invoice.Id
                    && ((invoiceLine.SourceQuoteLineId != null
                            && dbContext.QuoteLines.Any(line =>
                                line.Id == invoiceLine.SourceQuoteLineId && line.CatalogItemId == itemId))
                        || (invoiceLine.SourceVisitMaterialId != null
                            && dbContext.VisitMaterials.Any(material =>
                                material.Id == invoiceLine.SourceVisitMaterialId && material.CatalogItemId == itemId)))))
            .CountAsync(cancellationToken);

        return new CatalogUsage(quotes, jobs, invoices);
    }

    private static Dictionary<string, object?> Fields(CatalogItem item) =>
        new(StringComparer.Ordinal)
        {
            ["type"] = CatalogItemRules.TypeText(item.Type),
            ["name"] = item.Name,
            ["description"] = item.Description,
            ["unitCost"] = item.UnitCost,
            ["unitPrice"] = item.UnitPrice,
            ["isTaxable"] = item.IsTaxable,
            ["isActive"] = item.IsActive,
            ["categoryId"] = item.CategoryId,
        };

    private static string Summarize(string contentType, int sizeBytes) =>
        JsonSerializer.Serialize(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["contentType"] = contentType,
            ["sizeBytes"] = sizeBytes,
        });

    private static bool IsNameIndexViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres
        && postgres.ConstraintName == CatalogItemConfiguration.OrgTypeNameIndexName;

    private static bool IsRetryableImageRace(Exception ex) =>
        ex is DbUpdateConcurrencyException
        || ex is DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } };

    private sealed record RowProjection(
        Guid Id,
        CatalogItemType Type,
        string Name,
        string? Description,
        decimal UnitCost,
        decimal UnitPrice,
        bool IsTaxable,
        bool IsActive,
        bool HasImage,
        DateTimeOffset UpdatedAt);
}
