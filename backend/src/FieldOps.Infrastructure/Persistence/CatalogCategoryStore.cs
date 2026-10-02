using System.Net;
using System.Text.Json;
using FieldOps.Application.Auditing;
using FieldOps.Application.Features.Catalog;
using FieldOps.Domain.Catalog;
using FieldOps.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FieldOps.Infrastructure.Persistence;

internal sealed class CatalogCategoryStore(FieldOpsDbContext dbContext) : ICatalogCategoryStore
{
    public async Task<IReadOnlyList<CatalogCategoryView>> ListAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var rows = await dbContext.ServiceCategories.AsNoTracking()
            .Where(category => category.OrganizationId == organizationId)
            .Select(category => new
            {
                category.Id,
                category.Name,
                category.IsActive,
                ItemCount = dbContext.CatalogItems.Count(item =>
                    item.OrganizationId == organizationId && item.CategoryId == category.Id),
                ActiveServiceCount = dbContext.CatalogItems.Count(item =>
                    item.OrganizationId == organizationId
                    && item.CategoryId == category.Id
                    && item.IsActive
                    && item.Type == CatalogItemType.Service),
            })
            .ToListAsync(cancellationToken);

        return
        [
            .. rows
                .OrderBy(row => row.Name.ToLowerInvariant(), StringComparer.Ordinal)
                .ThenBy(row => row.Id)
                .Select(row => new CatalogCategoryView(row.Id, row.Name, row.IsActive, row.ItemCount, row.ActiveServiceCount)),
        ];
    }

    public Task<bool> NameExistsAsync(
        Guid organizationId, string name, Guid? excludeId, CancellationToken cancellationToken)
    {
        var lowered = name.ToLower();

        return dbContext.ServiceCategories.AsNoTracking().AnyAsync(
            category => category.OrganizationId == organizationId
                && category.Name.ToLower() == lowered
                && (excludeId == null || category.Id != excludeId),
            cancellationToken);
    }

    public async Task<CatalogCategoryView> CreateAsync(
        Guid organizationId, string name, Guid actorUserId, IPAddress? clientIp, CancellationToken cancellationToken)
    {
        var category = ServiceCategory.Create(organizationId, name);
        var (_, after) = AuditFieldDiff.ForCreate(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["name"] = category.Name,
            ["isActive"] = category.IsActive,
        });

        dbContext.ServiceCategories.Add(category);
        dbContext.AuditLogs.Add(Audit(
            organizationId, CatalogCategoryAuditActions.Created, category.Id, actorUserId, clientIp, null, after));

        await SaveAsync(cancellationToken);

        return new CatalogCategoryView(category.Id, category.Name, category.IsActive, 0, 0);
    }

    public async Task<CatalogCategoryView?> RenameAsync(
        Guid organizationId, Guid id, string name, Guid actorUserId, IPAddress? clientIp, CancellationToken cancellationToken)
    {
        var category = await dbContext.ServiceCategories.SingleOrDefaultAsync(
            candidate => candidate.OrganizationId == organizationId && candidate.Id == id, cancellationToken);

        if (category is null)
        {
            return null;
        }

        var previous = category.Name;

        if (category.Rename(name))
        {
            dbContext.AuditLogs.Add(Audit(
                organizationId,
                CatalogCategoryAuditActions.Renamed,
                category.Id,
                actorUserId,
                clientIp,
                NameJson(previous),
                NameJson(category.Name)));

            await SaveAsync(cancellationToken);
        }

        return (await ListAsync(organizationId, cancellationToken)).Single(view => view.Id == id);
    }

    public async Task<CategoryWriteOutcome> SetActiveAsync(
        Guid organizationId, Guid id, bool isActive, Guid actorUserId, IPAddress? clientIp, CancellationToken cancellationToken)
    {
        var category = await dbContext.ServiceCategories.SingleOrDefaultAsync(
            candidate => candidate.OrganizationId == organizationId && candidate.Id == id, cancellationToken);

        if (category is null)
        {
            return CategoryWriteOutcome.NotFound;
        }

        if (!category.SetActive(isActive))
        {
            return CategoryWriteOutcome.NoChange;
        }

        var (before, after) = AuditFieldDiff.ForStateChange(!isActive, isActive);

        dbContext.AuditLogs.Add(Audit(
            organizationId,
            isActive ? CatalogCategoryAuditActions.Reactivated : CatalogCategoryAuditActions.Deactivated,
            category.Id,
            actorUserId,
            clientIp,
            before,
            after));

        await SaveAsync(cancellationToken);

        return CategoryWriteOutcome.Changed;
    }

    private static string NameJson(string name) =>
        JsonSerializer.Serialize(new Dictionary<string, object?>(StringComparer.Ordinal) { ["name"] = name });

    private static AuditLog Audit(
        Guid organizationId,
        string action,
        Guid categoryId,
        Guid actorUserId,
        IPAddress? clientIp,
        string? before,
        string? after) =>
        AuditLog.Create(
            organizationId,
            action,
            CatalogCategoryAuditActions.EntityType,
            actorUserId: actorUserId,
            entityId: categoryId,
            ipAddress: clientIp,
            beforeData: before,
            afterData: after);

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new DuplicateCatalogCategoryNameException(ex);
        }
    }
}
