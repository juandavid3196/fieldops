using System.Net;
using FieldOps.Domain.Catalog;

namespace FieldOps.Application.Features.Catalog;

/// <summary>Raw BR-03 query values (text, so bad input becomes a 400 key).</summary>
public sealed record CatalogListQuery(
    string? Type,
    string? Search,
    string? TaxStatus,
    string? Status,
    string? Sort,
    string? Page,
    string? PageSize);

public sealed record CatalogFile(string FileName, string ContentType, byte[] Content);

/// <summary>GET /catalog-items (FR-02, BR-03).</summary>
public sealed class ListCatalogItemsHandler(ICatalogStore store)
{
    public async Task<CatalogResult<CatalogListPage>> HandleAsync(
        Guid organizationId, CatalogListQuery query, CancellationToken cancellationToken)
    {
        var filter = CatalogQueryParser.Parse(
            query.Type, query.Search, query.TaxStatus, query.Status, query.Sort, out var errors);
        var merged = new Dictionary<string, string[]>(errors, StringComparer.Ordinal);

        if (!CatalogQueryParser.TryParsePage(query.Page, out var page))
        {
            merged["page"] = ["Page must be 1 or more."];
        }

        if (!CatalogQueryParser.IsValidPageSize(query.PageSize))
        {
            merged["pageSize"] = ["Page size must be 10."];
        }

        if (filter is null || merged.Count > 0)
        {
            return CatalogResult<CatalogListPage>.Invalid(merged);
        }

        return CatalogResult<CatalogListPage>.Ok(
            await store.ListAsync(organizationId, filter, page, CatalogQueryParser.PageSize, cancellationToken));
    }
}

/// <summary>GET /catalog-items/export (FR-12, BR-13): every matching item, no paging.</summary>
public sealed class ExportCatalogItemsHandler(ICatalogStore store, TimeProvider timeProvider)
{
    public async Task<CatalogResult<CatalogFile>> HandleAsync(
        Guid organizationId, CatalogListQuery query, CancellationToken cancellationToken)
    {
        var filter = CatalogQueryParser.Parse(
            query.Type, query.Search, query.TaxStatus, query.Status, query.Sort, out var errors);

        if (filter is null)
        {
            return CatalogResult<CatalogFile>.Invalid(errors);
        }

        var rows = await store.ListAllAsync(organizationId, filter, cancellationToken);
        var format = await store.GetOrganizationFormatAsync(organizationId, cancellationToken);

        return CatalogResult<CatalogFile>.Ok(new CatalogFile(
            CatalogCsv.ExportFileName(OrganizationDate(format?.Timezone, timeProvider.GetUtcNow())),
            "text/csv; charset=utf-8",
            CatalogCsv.Export(rows)));
    }

    private static DateOnly OrganizationDate(string? timezone, DateTimeOffset now)
    {
        try
        {
            var zone = timezone is null ? TimeZoneInfo.Utc : TimeZoneInfo.FindSystemTimeZoneById(timezone);

            return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return DateOnly.FromDateTime(now.UtcDateTime);
        }
    }
}

/// <summary>GET /catalog-items/summary (FR-03, BR-05).</summary>
public sealed class GetCatalogSummaryHandler(ICatalogStore store)
{
    public Task<CatalogSummary> HandleAsync(Guid organizationId, CancellationToken cancellationToken) =>
        store.GetSummaryAsync(organizationId, cancellationToken);
}

/// <summary>GET /catalog-items/{id} (FR-06).</summary>
public sealed class GetCatalogItemHandler(ICatalogStore store)
{
    public Task<CatalogItemDetail?> HandleAsync(Guid organizationId, Guid itemId, CancellationToken cancellationToken) =>
        store.GetDetailAsync(organizationId, itemId, cancellationToken);
}

/// <summary>POST /catalog-items (FR-05, BR-07, BR-08).</summary>
public sealed class CreateCatalogItemHandler(ICatalogStore store)
{
    public async Task<CatalogResult<CatalogItemDetail>> HandleAsync(
        Guid organizationId,
        Guid actorUserId,
        IPAddress? clientIp,
        CatalogItemInput input,
        CancellationToken cancellationToken)
    {
        var values = CatalogItemRules.Validate(input, out var fieldErrors);

        if (values is null)
        {
            return CatalogResult<CatalogItemDetail>.Invalid(ToErrors(fieldErrors));
        }

        if (values.CategoryId is { } categoryId
            && !await store.IsCategoryAssignableAsync(organizationId, categoryId, null, cancellationToken))
        {
            return CatalogResult<CatalogItemDetail>.Invalid(CatalogItemRules.CategoryKey, CatalogMessages.CategoryInvalid);
        }

        if (await store.NameExistsAsync(
                organizationId, values.Type, CatalogItem.NormalizeName(values.Name), null, cancellationToken))
        {
            return CatalogResult<CatalogItemDetail>.Conflict(CatalogItemRules.NameKey, CatalogMessages.DuplicateName);
        }

        try
        {
            return CatalogResult<CatalogItemDetail>.Ok(await store.CreateAsync(
                organizationId, values, actorUserId, clientIp, cancellationToken));
        }
        catch (DuplicateCatalogItemNameException)
        {
            return CatalogResult<CatalogItemDetail>.Conflict(CatalogItemRules.NameKey, CatalogMessages.DuplicateName);
        }
    }

    internal static Dictionary<string, string[]> ToErrors(IReadOnlyDictionary<string, string> errors) =>
        errors.ToDictionary(pair => pair.Key, pair => new[] { pair.Value }, StringComparer.Ordinal);
}

/// <summary>PUT /catalog-items/{id} (FR-07, BR-07, BR-08, BR-16).</summary>
public sealed class UpdateCatalogItemHandler(ICatalogStore store, TimeProvider timeProvider)
{
    public async Task<CatalogResult<CatalogItemDetail>> HandleAsync(
        Guid organizationId,
        Guid itemId,
        Guid actorUserId,
        IPAddress? clientIp,
        CatalogItemInput input,
        CancellationToken cancellationToken)
    {
        // The item is resolved first so another organization's id is 404 whatever the body is.
        if (!await store.ItemExistsAsync(organizationId, itemId, cancellationToken))
        {
            return CatalogResult<CatalogItemDetail>.NotFound();
        }

        var values = CatalogItemRules.Validate(input, out var fieldErrors);

        if (values is null)
        {
            return CatalogResult<CatalogItemDetail>.Invalid(CreateCatalogItemHandler.ToErrors(fieldErrors));
        }

        if (values.CategoryId is { } categoryId
            && !await store.IsCategoryAssignableAsync(organizationId, categoryId, itemId, cancellationToken))
        {
            return CatalogResult<CatalogItemDetail>.Invalid(CatalogItemRules.CategoryKey, CatalogMessages.CategoryInvalid);
        }

        if (await store.NameExistsAsync(
                organizationId, values.Type, CatalogItem.NormalizeName(values.Name), itemId, cancellationToken))
        {
            return CatalogResult<CatalogItemDetail>.Conflict(CatalogItemRules.NameKey, CatalogMessages.DuplicateName);
        }

        try
        {
            var detail = await store.UpdateAsync(
                organizationId, itemId, values, actorUserId, clientIp, timeProvider.GetUtcNow(), cancellationToken);

            return detail is null
                ? CatalogResult<CatalogItemDetail>.NotFound()
                : CatalogResult<CatalogItemDetail>.Ok(detail);
        }
        catch (DuplicateCatalogItemNameException)
        {
            return CatalogResult<CatalogItemDetail>.Conflict(CatalogItemRules.NameKey, CatalogMessages.DuplicateName);
        }
    }
}

/// <summary>POST /catalog-items/{id}/activate and /deactivate (FR-09, BR-12).</summary>
public sealed class SetCatalogItemActiveHandler(ICatalogStore store, TimeProvider timeProvider)
{
    public async Task<CatalogResult<NoValue>> HandleAsync(
        Guid organizationId,
        Guid itemId,
        bool isActive,
        Guid actorUserId,
        IPAddress? clientIp,
        CancellationToken cancellationToken)
    {
        var outcome = await store.SetActiveAsync(
            organizationId, itemId, isActive, actorUserId, clientIp, timeProvider.GetUtcNow(), cancellationToken);

        return outcome == CatalogStateOutcome.NotFound
            ? CatalogResult<NoValue>.NotFound()
            : CatalogResult<NoValue>.NoOp();
    }
}

public readonly record struct NoValue;

/// <summary>GET /catalog-items/{id}/image (FR-11).</summary>
public sealed class GetCatalogImageHandler(ICatalogStore store)
{
    public Task<CatalogImageContent?> HandleAsync(Guid organizationId, Guid itemId, CancellationToken cancellationToken) =>
        store.GetImageAsync(organizationId, itemId, cancellationToken);
}

/// <summary>PUT /catalog-items/{id}/image (FR-11, BR-11).</summary>
public sealed class UploadCatalogImageHandler(ICatalogStore store, TimeProvider timeProvider)
{
    public async Task<CatalogResult<CatalogImageInfo>> HandleAsync(
        Guid organizationId,
        Guid itemId,
        Guid actorUserId,
        IPAddress? clientIp,
        byte[] content,
        string? declaredContentType,
        CancellationToken cancellationToken)
    {
        // The item is resolved first so another organization's id is 404 whatever the file is.
        if (!await store.ItemExistsAsync(organizationId, itemId, cancellationToken))
        {
            return CatalogResult<CatalogImageInfo>.NotFound();
        }

        var check = CatalogImageContentValidator.Check(content, declaredContentType);

        if (!check.IsValid)
        {
            return CatalogResult<CatalogImageInfo>.Invalid("file", check.Error!);
        }

        var info = await store.UpsertImageAsync(
            organizationId,
            itemId,
            check.ContentType!,
            content,
            actorUserId,
            clientIp,
            timeProvider.GetUtcNow(),
            cancellationToken);

        return info is null ? CatalogResult<CatalogImageInfo>.NotFound() : CatalogResult<CatalogImageInfo>.Ok(info);
    }
}

/// <summary>DELETE /catalog-items/{id}/image: a no-op without audit when none exists (BR-11).</summary>
public sealed class RemoveCatalogImageHandler(ICatalogStore store)
{
    public async Task<CatalogResult<NoValue>> HandleAsync(
        Guid organizationId,
        Guid itemId,
        Guid actorUserId,
        IPAddress? clientIp,
        CancellationToken cancellationToken) =>
        await store.DeleteImageAsync(organizationId, itemId, actorUserId, clientIp, cancellationToken)
            == CatalogImageRemoval.ItemNotFound
            ? CatalogResult<NoValue>.NotFound()
            : CatalogResult<NoValue>.NoOp();
}

/// <summary>POST /catalog-items/import (FR-14, BR-13): all rows are created or none.</summary>
public sealed class ImportCatalogItemsHandler(ICatalogStore store)
{
    public async Task<CatalogResult<int>> HandleAsync(
        Guid organizationId,
        Guid actorUserId,
        IPAddress? clientIp,
        byte[] content,
        CancellationToken cancellationToken)
    {
        var parse = CatalogCsv.ParseImport(content);

        if (parse.FileError is not null)
        {
            return CatalogResult<int>.Invalid("file", parse.FileError);
        }

        var errors = new List<CatalogRowError>(parse.RowErrors);
        var existing = await store.FindExistingNamesAsync(
            organizationId,
            [.. parse.Items.Select(item => CatalogItem.NormalizeName(item.Name)).Distinct(StringComparer.Ordinal)],
            cancellationToken);

        for (var i = 0; i < parse.Items.Count; i++)
        {
            var item = parse.Items[i];

            if (existing.Contains((item.Type, CatalogItem.NormalizeName(item.Name))))
            {
                errors.Add(new CatalogRowError(parse.ItemRows[i], "name", CatalogMessages.DuplicateName));
            }
        }

        if (errors.Count > 0)
        {
            return CatalogResult<int>.InvalidRows(CatalogCsv.Finalize(errors));
        }

        try
        {
            return CatalogResult<int>.Ok(
                await store.ImportAsync(organizationId, parse.Items, actorUserId, clientIp, cancellationToken));
        }
        catch (DuplicateCatalogItemNameException)
        {
            return CatalogResult<int>.ConflictWithoutField(CatalogMessages.ImportConflict);
        }
    }
}
