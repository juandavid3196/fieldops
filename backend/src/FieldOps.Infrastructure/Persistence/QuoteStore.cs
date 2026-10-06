using System.Text.Json;
using FieldOps.Application.Features.Access;
using FieldOps.Application.Features.Quotes;
using FieldOps.Application.Features.ServiceRequests;
using FieldOps.Domain.Catalog;
using FieldOps.Domain.Quotes;
using FieldOps.Domain.Requests;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Persistence;

/// <summary>
/// Quote builder persistence. Reads are no-tracking and always filter by organization first, then by the
/// branch scope of the quote request (quote-builder BR-07). Mutations serialize on the request row, then the
/// quote row (see <c>QuoteStore.Mutations</c>).
/// </summary>
internal sealed partial class QuoteStore(FieldOpsDbContext dbContext, TimeProvider timeProvider) : IQuoteStore
{
    private const string AuditEntityType = "quote";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<QuoteContext?> GetContextAsync(
        Guid organizationId, BranchScope scope, Guid quoteId, CancellationToken cancellationToken)
    {
        if (!await VisibleQuotes(organizationId, scope).AnyAsync(quote => quote.Id == quoteId, cancellationToken))
        {
            return null;
        }

        var timezone = await dbContext.Organizations.AsNoTracking()
            .Where(organization => organization.Id == organizationId)
            .Select(organization => organization.Timezone)
            .SingleAsync(cancellationToken);

        return new QuoteContext(quoteId, timezone);
    }

    public Task<QuoteDetail?> GetAsync(
        Guid organizationId, BranchScope scope, Guid quoteId, bool canManage, CancellationToken cancellationToken) =>
        BuildDetailAsync(organizationId, scope, quoteId, canManage, cancellationToken);

    public async Task<QuoteVersionView?> GetVersionAsync(
        Guid organizationId, BranchScope scope, Guid quoteId, int versionNo, CancellationToken cancellationToken)
    {
        if (!await VisibleQuotes(organizationId, scope).AnyAsync(quote => quote.Id == quoteId, cancellationToken))
        {
            return null;
        }

        var version = await dbContext.QuoteVersions.AsNoTracking()
            .Where(candidate => candidate.OrganizationId == organizationId
                && candidate.QuoteId == quoteId
                && candidate.VersionNo == versionNo
                && candidate.IsImmutable
                && candidate.SentAt != null)
            .SingleOrDefaultAsync(cancellationToken);

        if (version is null)
        {
            return null;
        }

        var lines = await LoadLinesAsync(organizationId, version.Id, cancellationToken);
        var regular = lines.Where(line => !line.IsOptional).ToList();
        var cost = regular.Sum(line => QuoteCalculator.Round(line.Quantity * line.UnitCost));

        return new QuoteVersionView(
            version.VersionNo,
            version.SentAt!.Value,
            version.Scope,
            version.CustomerNotes,
            version.InternalNotes,
            version.Terms,
            version.ValidUntil,
            version.Currency,
            lines.Select(line => new QuoteVersionLine(
                    TypeCode(line.LineType),
                    line.Name,
                    line.Description,
                    line.Quantity,
                    line.Unit,
                    line.UnitPrice,
                    line.UnitCost,
                    line.TaxRate,
                    line.LineSubtotal,
                    line.LineTax,
                    line.LineTotal,
                    line.IsOptional))
                .ToList(),
            version.Subtotal,
            version.DiscountTotal,
            QuoteCalculator.TaxLabel(regular.Select(line => line.TaxRate)),
            version.TaxTotal,
            version.Total,
            QuoteCalculator.Margin(version.Subtotal, version.DiscountTotal, cost));
    }

    /// <summary>Quotes of the organization whose request is visible: the branch is the request branch or null (BR-07).</summary>
    private IQueryable<Quote> VisibleQuotes(Guid organizationId, BranchScope scope)
    {
        var query = dbContext.Quotes.AsNoTracking().Where(quote => quote.OrganizationId == organizationId);

        if (scope.All)
        {
            return query;
        }

        var ids = scope.BranchIds.ToArray();

        return query.Where(quote => dbContext.ServiceRequests.Any(request =>
            request.Id == quote.RequestId
            && request.OrganizationId == organizationId
            && (request.BranchId == null || ids.Contains(request.BranchId.Value))));
    }

    private Task<List<QuoteLine>> LoadLinesAsync(Guid organizationId, Guid versionId, CancellationToken cancellationToken) =>
        dbContext.QuoteLines.AsNoTracking()
            .Where(line => line.OrganizationId == organizationId && line.QuoteVersionId == versionId)
            .OrderBy(line => line.SortOrder)
            .ThenBy(line => line.Id)
            .ToListAsync(cancellationToken);

    private async Task<QuoteDetail?> BuildDetailAsync(
        Guid organizationId, BranchScope scope, Guid quoteId, bool canManage, CancellationToken cancellationToken)
    {
        var quote = await VisibleQuotes(organizationId, scope)
            .Where(candidate => candidate.Id == quoteId)
            .SingleOrDefaultAsync(cancellationToken);

        if (quote is null)
        {
            return null;
        }

        var organization = await ReadOrganizationAsync(organizationId, cancellationToken);
        var zone = OrganizationTime.FindZone(organization.Timezone);
        var request = await dbContext.ServiceRequests.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == quote.RequestId && candidate.OrganizationId == organizationId, cancellationToken);

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

        var customer = await ReadCustomerAsync(organizationId, request, cancellationToken);
        var (recipient, _) = await ResolveRecipientAsync(organizationId, request, cancellationToken);

        var versions = await dbContext.QuoteVersions.AsNoTracking()
            .Where(version => version.OrganizationId == organizationId && version.QuoteId == quote.Id)
            .OrderBy(version => version.VersionNo)
            .ToListAsync(cancellationToken);

        QuoteDraftView? draft = null;

        if (quote.Status != QuoteStatus.Cancelled && versions.FirstOrDefault(version => !version.IsImmutable) is { } mutable)
        {
            draft = await BuildDraftAsync(organizationId, mutable, organization, cancellationToken);
        }

        return new QuoteDetail(
            quote.Id,
            quote.QuoteNumber,
            RequestCardRules.DisplayNumber(organization.QuotePrefix, quote.QuoteNumber),
            QuoteStatusCodes.Code(quote.Status),
            quote.UpdatedAt,
            canManage,
            new QuoteRequestRef(
                request.Id,
                RequestCardRules.DisplayNumber(organization.RequestPrefix, request.RequestNumber),
                RequestCardRules.Title(serviceName, categoryName, request.Description),
                categoryName,
                RequestTransitions.Code(request.Status)),
            customer,
            new QuoteRecipient(recipient),
            await CompletedAssessmentReader.ReadAsync(dbContext, organizationId, request.Id, zone, cancellationToken),
            new QuoteOrganizationRef(organization.Name, organization.Currency, organization.DefaultTaxRate),
            draft,
            versions
                .Where(version => version.IsImmutable && version.SentAt != null)
                .Select(version => new QuoteSentVersion(
                    version.VersionNo, version.SentAt!.Value, version.Total, version.VersionNo == quote.CurrentVersionNo))
                .ToList());
    }

    // A draft reads the current organization rate and currency on every calculation (BR-13, AS-05).
    private async Task<QuoteDraftView> BuildDraftAsync(
        Guid organizationId, QuoteVersion version, OrganizationInfo organization, CancellationToken cancellationToken)
    {
        var lines = await LoadLinesAsync(organizationId, version.Id, cancellationToken);
        var result = QuoteCalculator.Calculate(
            lines.Select(ToInput).ToList(), version.DiscountTotal, organization.DefaultTaxRate, organization.Currency);
        var calculation = result.Value ?? StoredCalculation(version, lines, organization.Currency);
        var (preset, customText) = QuoteTerms.Detect(version.Terms);

        return new QuoteDraftView(
            version.VersionNo,
            lines.Select(line => new QuoteDraftLineView(
                    line.CatalogItemId,
                    TypeCode(line.LineType),
                    line.Name,
                    line.Description,
                    line.Quantity,
                    line.Unit,
                    line.UnitPrice,
                    line.UnitCost,
                    line.TaxRate > 0m,
                    line.IsOptional))
                .ToList(),
            version.DiscountTotal,
            version.CustomerNotes,
            version.InternalNotes,
            new QuoteTermsView(preset, customText),
            version.ValidUntil ?? DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime),
            calculation);
    }

    private static QuoteCalculation StoredCalculation(QuoteVersion version, List<QuoteLine> lines, string currency)
    {
        var cost = lines.Where(line => !line.IsOptional).Sum(line => QuoteCalculator.Round(line.Quantity * line.UnitCost));

        return new QuoteCalculation(
            lines.Select(line => new CalculatedLine(line.LineSubtotal, 0m, line.TaxRate, line.LineTax, line.LineTotal)).ToList(),
            version.Subtotal,
            version.DiscountTotal,
            QuoteCalculator.TaxLabel(lines.Where(line => !line.IsOptional).Select(line => line.TaxRate)),
            version.TaxTotal,
            version.Total,
            currency,
            QuoteCalculator.Margin(version.Subtotal, version.DiscountTotal, cost));
    }

    // The editor reloads a line as taxable when it carries a rate (the schema stores the rate, not the flag).
    private static QuoteLineInput ToInput(QuoteLine line) =>
        new(line.TaxRate > 0m, line.IsOptional, line.Quantity, line.UnitPrice, line.UnitCost);

    private static string TypeCode(CatalogItemType type) => type == CatalogItemType.Product ? "product" : "service";

    private async Task<OrganizationInfo> ReadOrganizationAsync(Guid organizationId, CancellationToken cancellationToken) =>
        await dbContext.Organizations.AsNoTracking()
            .Where(organization => organization.Id == organizationId)
            .Select(organization => new OrganizationInfo(
                organization.Name,
                organization.Phone,
                organization.Timezone,
                organization.Currency,
                organization.DefaultTaxRate,
                organization.QuotePrefix,
                organization.RequestPrefix))
            .SingleAsync(cancellationToken);

    /// <summary>The recipient of the quote email: the active linked contact email, else the guest email (requests-pipeline BR-13).</summary>
    private async Task<(string? Email, string? FirstName)> ResolveRecipientAsync(
        Guid organizationId, ServiceRequest request, CancellationToken cancellationToken)
    {
        var contact = request.ContactId is { } contactId
            ? await dbContext.CustomerContacts.AsNoTracking()
                .Where(candidate => candidate.Id == contactId && candidate.OrganizationId == organizationId)
                .Select(candidate => new { candidate.FirstName, candidate.Email, candidate.IsActive })
                .SingleOrDefaultAsync(cancellationToken)
            : null;

        var email = contact is { IsActive: true } && !string.IsNullOrWhiteSpace(contact.Email)
            ? contact.Email.Trim()
            : string.IsNullOrWhiteSpace(request.GuestEmail) ? null : request.GuestEmail.Trim();

        return (email, contact?.FirstName);
    }

    private async Task<QuoteCustomerRef> ReadCustomerAsync(
        Guid organizationId, ServiceRequest request, CancellationToken cancellationToken)
    {
        var customerName = request.CustomerId is { } customerId
            ? await dbContext.Customers.AsNoTracking()
                .Where(candidate => candidate.Id == customerId && candidate.OrganizationId == organizationId)
                .Select(candidate => candidate.DisplayName)
                .SingleOrDefaultAsync(cancellationToken)
            : null;

        var contact = request.ContactId is { } contactId
            ? await dbContext.CustomerContacts.AsNoTracking()
                .Where(candidate => candidate.Id == contactId && candidate.OrganizationId == organizationId && candidate.IsActive)
                .Select(candidate => new { candidate.Phone, candidate.Email })
                .SingleOrDefaultAsync(cancellationToken)
            : null;

        var address = request.PropertyId is { } propertyId
            ? await dbContext.Properties.AsNoTracking()
                .Where(candidate => candidate.Id == propertyId && candidate.OrganizationId == organizationId)
                .Select(candidate => new
                {
                    candidate.AddressLine1,
                    candidate.AddressLine2,
                    candidate.City,
                    candidate.StateRegion,
                    candidate.PostalCode,
                })
                .SingleOrDefaultAsync(cancellationToken)
            : null;

        var formatted = address is null
            ? FormatServiceAddress(request.ServiceAddress)
            : FormatAddress(address.AddressLine1, address.AddressLine2, address.City, address.StateRegion, address.PostalCode);

        return new QuoteCustomerRef(
            request.CustomerId,
            customerName ?? request.GuestName ?? string.Empty,
            contact is not null && !string.IsNullOrWhiteSpace(contact.Phone) ? contact.Phone : NullIfBlank(request.GuestPhone),
            contact is not null && !string.IsNullOrWhiteSpace(contact.Email) ? contact.Email : NullIfBlank(request.GuestEmail),
            formatted);
    }

    private static string? FormatServiceAddress(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            string? Read(string name) =>
                document.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                    ? value.GetString()
                    : null;

            return FormatAddress(Read("line1"), Read("line2"), Read("city"), Read("state"), Read("postalCode"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? FormatAddress(string? line1, string? line2, string? city, string? state, string? postalCode)
    {
        var region = string.Join(' ', new[] { state, postalCode }.Where(part => !string.IsNullOrWhiteSpace(part)));
        var parts = new[] { line1, line2, city, region }.Where(part => !string.IsNullOrWhiteSpace(part)).Select(part => part!.Trim());
        var text = string.Join(", ", parts);

        return text.Length == 0 ? null : text;
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string? Serialize(object? value) =>
        value is null ? null : JsonSerializer.Serialize(value, JsonOptions);

    private sealed record OrganizationInfo(
        string Name,
        string? Phone,
        string Timezone,
        string Currency,
        decimal DefaultTaxRate,
        string QuotePrefix,
        string RequestPrefix);
}
