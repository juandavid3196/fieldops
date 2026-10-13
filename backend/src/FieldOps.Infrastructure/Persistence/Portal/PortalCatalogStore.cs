using System.Text.Json;
using FieldOps.Application.Features.BillingReview;
using FieldOps.Application.Features.InvoicePayments;
using FieldOps.Application.Features.OnlinePayments;
using FieldOps.Application.Features.PortalAccess;
using FieldOps.Application.Features.PortalDashboard;
using FieldOps.Application.Features.QuoteLinks;
using FieldOps.Application.Features.Quotes;
using FieldOps.Application.Features.ServiceRequests;
using FieldOps.Domain.Invoices;
using FieldOps.Domain.Quotes;
using FieldOps.Domain.WorkOrders;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Persistence.Portal;

/// <summary>
/// The paged lists of requests, quotes and invoices, the request detail, the organization logo and the work order report of
/// the portal (customer portal BR-27, BR-30, BR-31). Every query starts from the customer of the session.
/// </summary>
internal sealed class PortalCatalogStore(FieldOpsDbContext dbContext) : IPortalCatalogStore
{
    private static readonly QuoteStatus[] ListedQuoteStatuses =
    [
        QuoteStatus.Sent,
        QuoteStatus.ClarificationRequested,
        QuoteStatus.Approved,
        QuoteStatus.Rejected,
        QuoteStatus.Expired,
    ];

    public async Task<PortalPage<PortalRequestRow>?> ListRequestsAsync(
        PortalScope scope, Guid? propertyId, int page, CancellationToken cancellationToken)
    {
        if (propertyId is { } selected && !await IsActivePropertyAsync(scope, selected, cancellationToken))
        {
            return null;
        }

        var org = await PortalOrg.LoadAsync(dbContext, scope, cancellationToken);
        var requests = dbContext.ServiceRequests.AsNoTracking().ForPortal(scope)
            .Where(request => propertyId == null || request.PropertyId == propertyId);
        var total = await requests.CountAsync(cancellationToken);
        var rows = await PortalReaders.RequestRowsAsync(
            dbContext,
            scope,
            org,
            requests
                .OrderByDescending(request => request.CreatedAt)
                .ThenBy(request => request.Id)
                .Skip((page - 1) * PortalPaging.PageSize)
                .Take(PortalPaging.PageSize),
            cancellationToken);

        return new PortalPage<PortalRequestRow>(rows.OrderByDescending(row => row.SubmittedOn).ToList(), page, PortalPaging.PageSize, total);
    }

    public async Task<PortalRequestDetail?> GetRequestAsync(PortalScope scope, Guid requestId, CancellationToken cancellationToken)
    {
        var request = await dbContext.ServiceRequests.AsNoTracking().ForPortal(scope)
            .SingleOrDefaultAsync(candidate => candidate.Id == requestId, cancellationToken);

        if (request is null)
        {
            return null;
        }

        var org = await PortalOrg.LoadAsync(dbContext, scope, cancellationToken);
        var row = (await PortalReaders.RequestRowsAsync(
            dbContext, scope, org, dbContext.ServiceRequests.AsNoTracking().ForPortal(scope).Where(candidate => candidate.Id == requestId), cancellationToken))
            .Single();

        var categoryId = request.CategoryId;
        var itemId = request.CatalogItemId;
        var propertyId = request.PropertyId;
        var categoryName = categoryId is null
            ? string.Empty
            : await dbContext.ServiceCategories.AsNoTracking()
                .Where(category => category.OrganizationId == scope.OrganizationId && category.Id == categoryId)
                .Select(category => category.Name)
                .SingleOrDefaultAsync(cancellationToken) ?? string.Empty;
        var serviceName = itemId is null
            ? null
            : await dbContext.CatalogItems.AsNoTracking()
                .Where(item => item.OrganizationId == scope.OrganizationId && item.Id == itemId)
                .Select(item => item.Name)
                .SingleOrDefaultAsync(cancellationToken);
        var property = propertyId is null
            ? null
            : await dbContext.Properties.AsNoTracking().ForPortal(scope)
                .Where(candidate => candidate.Id == propertyId)
                .Select(candidate => new PortalRequestProperty(
                    candidate.Id,
                    candidate.Name,
                    candidate.AddressLine1,
                    candidate.AddressLine2,
                    candidate.City,
                    candidate.StateRegion,
                    candidate.PostalCode,
                    candidate.CountryCode,
                    candidate.AccessInstructions,
                    candidate.IsPrimary))
                .SingleOrDefaultAsync(cancellationToken);

        var quote = await (
            from candidate in dbContext.Quotes.AsNoTracking().ForPortal(dbContext, scope)
            where candidate.RequestId == requestId && candidate.Status != QuoteStatus.Draft && candidate.Status != QuoteStatus.Cancelled
            orderby candidate.QuoteNumber descending
            select new { candidate.Id, candidate.QuoteNumber, candidate.Status })
            .FirstOrDefaultAsync(cancellationToken);

        return new PortalRequestDetail(
            row.Id,
            row.DisplayNumber,
            row.Title,
            row.PropertyName,
            row.Status,
            row.StatusLabel,
            row.SubmittedOn,
            categoryName,
            serviceName,
            request.Description,
            request.Urgency,
            request.HasActiveDamage,
            property,
            ParseAvailability(request.AvailabilityPreferences),
            quote is null
                ? null
                : new PortalLinkedQuote(quote.Id, RequestCardRules.DisplayNumber(org.QuotePrefix, quote.QuoteNumber), QuoteStatusCodes.Code(quote.Status)));
    }

    public async Task<PortalPage<PortalQuoteRow>?> ListQuotesAsync(
        PortalScope scope, Guid? propertyId, int page, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (propertyId is { } selected && !await IsActivePropertyAsync(scope, selected, cancellationToken))
        {
            return null;
        }

        var org = await PortalOrg.LoadAsync(dbContext, scope, cancellationToken);
        var today = org.Today(now);
        var query =
            from quote in dbContext.Quotes.AsNoTracking().ForPortal(dbContext, scope)
            where ListedQuoteStatuses.Contains(quote.Status) && (propertyId == null || quote.PropertyId == propertyId)
            join version in dbContext.QuoteVersions.AsNoTracking()
                on new { quote.OrganizationId, QuoteId = quote.Id } equals new { version.OrganizationId, version.QuoteId }
            where version.SentAt != null
                && ((quote.ApprovedVersionId != null && version.Id == quote.ApprovedVersionId)
                    || (quote.ApprovedVersionId == null && version.VersionNo == quote.CurrentVersionNo))
            select new { quote, version };

        var total = await query.CountAsync(cancellationToken);
        var rows = await (
            from row in query
            join property in dbContext.Properties.AsNoTracking().ForPortal(scope)
                on row.quote.PropertyId equals (Guid?)property.Id into properties
            from property in properties.DefaultIfEmpty()
            orderby row.version.SentAt descending, row.quote.Id
            select new
            {
                row.quote.Id,
                row.quote.QuoteNumber,
                row.quote.Status,
                row.version.Scope,
                row.version.Total,
                row.version.Currency,
                row.version.ValidUntil,
                PropertyName = property.Name,
            })
            .Skip((page - 1) * PortalPaging.PageSize)
            .Take(PortalPaging.PageSize)
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(row => new PortalQuoteRow(
                row.Id,
                RequestCardRules.DisplayNumber(org.QuotePrefix, row.QuoteNumber),
                row.Scope,
                row.PropertyName,
                row.Total,
                row.Currency,
                row.Status is QuoteStatus.Sent or QuoteStatus.ClarificationRequested && row.ValidUntil is { } validUntil && validUntil < today
                    ? "expired"
                    : QuoteStatusCodes.Code(row.Status),
                row.ValidUntil))
            .ToList();

        return new PortalPage<PortalQuoteRow>(items, page, PortalPaging.PageSize, total);
    }

    public async Task<PortalPage<PortalInvoiceRow>?> ListInvoicesAsync(
        PortalScope scope, Guid? propertyId, int page, CancellationToken cancellationToken)
    {
        if (propertyId is { } selected && !await IsActivePropertyAsync(scope, selected, cancellationToken))
        {
            return null;
        }

        var org = await PortalOrg.LoadAsync(dbContext, scope, cancellationToken);
        var query =
            from invoice in dbContext.Invoices.AsNoTracking().ForPortal(scope)
            where invoice.Status != InvoiceStatus.Draft && invoice.Status != InvoiceStatus.Void
            join order in dbContext.WorkOrders.AsNoTracking()
                on new { invoice.OrganizationId, Id = invoice.WorkOrderId } equals new { order.OrganizationId, order.Id }
            where propertyId == null || order.PropertyId == propertyId
            select new { invoice, order };

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(row => row.invoice.IssueDate)
            .ThenByDescending(row => row.invoice.InvoiceNumber)
            .Skip((page - 1) * PortalPaging.PageSize)
            .Take(PortalPaging.PageSize)
            .Select(row => new
            {
                row.invoice.Id,
                row.invoice.InvoiceNumber,
                row.order.Title,
                row.invoice.IssueDate,
                row.invoice.DueDate,
                row.invoice.Total,
                row.invoice.BalanceDue,
                row.invoice.Currency,
                row.invoice.Status,
            })
            .ToListAsync(cancellationToken);

        return new PortalPage<PortalInvoiceRow>(
            rows
                .Select(row => new PortalInvoiceRow(
                    row.Id,
                    RequestCardRules.DisplayNumber(org.InvoicePrefix, row.InvoiceNumber),
                    row.Title,
                    row.IssueDate,
                    row.DueDate,
                    row.Total,
                    row.BalanceDue,
                    row.Currency,
                    InvoiceHubRules.StoredStatus(row.Status)))
                .ToList(),
            page,
            PortalPaging.PageSize,
            total);
    }

    public async Task<PublicBinary?> GetOrganizationLogoAsync(PortalScope scope, CancellationToken cancellationToken)
    {
        var organizationId = scope.OrganizationId;
        var logo = await dbContext.OrganizationLogos.AsNoTracking()
            .Where(candidate => candidate.OrganizationId == organizationId)
            .Select(candidate => new { candidate.ContentType, candidate.Content })
            .SingleOrDefaultAsync(cancellationToken);

        return logo is null ? null : new PublicBinary(logo.ContentType, logo.Content);
    }

    // The public completion report data (customer portal BR-31) for a completed work order of the customer, with or without an invoice.
    public async Task<CompletionReportSource?> GetWorkOrderReportAsync(PortalScope scope, Guid workOrderId, CancellationToken cancellationToken)
    {
        var order = await dbContext.WorkOrders.AsNoTracking().ForPortal(scope)
            .Where(candidate => candidate.Id == workOrderId
                && (candidate.Status == WorkOrderStatus.Completed || candidate.Status == WorkOrderStatus.ApprovedForBilling))
            .Select(candidate => new { candidate.Id, candidate.OrganizationId, candidate.WorkOrderNumber, candidate.Title, candidate.PropertyId })
            .SingleOrDefaultAsync(cancellationToken);

        if (order is null)
        {
            return null;
        }

        var visit = await dbContext.Visits.AsNoTracking()
            .Where(candidate => candidate.OrganizationId == order.OrganizationId
                && candidate.WorkOrderId == order.Id
                && candidate.ActualCompletedAt != null
                && (candidate.Status == VisitStatus.Completed || candidate.Status == VisitStatus.Approved))
            .OrderByDescending(candidate => candidate.ActualCompletedAt)
            .ThenByDescending(candidate => candidate.VisitNumber)
            .Select(candidate => new { candidate.Id, candidate.ActualCompletedAt, candidate.CompletionSummary })
            .FirstOrDefaultAsync(cancellationToken);

        if (visit is null)
        {
            return null;
        }

        var organization = await dbContext.Organizations.AsNoTracking()
            .Where(candidate => candidate.Id == order.OrganizationId)
            .Select(candidate => new
            {
                candidate.Name,
                candidate.Phone,
                candidate.Email,
                candidate.AddressLine1,
                candidate.City,
                candidate.StateRegion,
                candidate.PostalCode,
                candidate.WorkOrderPrefix,
                candidate.Timezone,
            })
            .SingleAsync(cancellationToken);
        var zone = OrganizationTime.FindZone(organization.Timezone);
        var lines = new List<string>(InvoicePreviewReader.OrganizationAddressLines(
            organization.AddressLine1, organization.City, organization.StateRegion, organization.PostalCode));

        if (!string.IsNullOrWhiteSpace(organization.Phone))
        {
            lines.Add(organization.Phone.Trim());
        }

        if (!string.IsNullOrWhiteSpace(organization.Email))
        {
            lines.Add(organization.Email.Trim());
        }

        var logo = await dbContext.OrganizationLogos.AsNoTracking()
            .Where(candidate => candidate.OrganizationId == order.OrganizationId)
            .Select(candidate => new { candidate.ContentType, candidate.Content })
            .SingleOrDefaultAsync(cancellationToken);
        var property = await dbContext.Properties.AsNoTracking().ForPortal(scope)
            .Where(candidate => candidate.Id == order.PropertyId)
            .Select(candidate => new { candidate.AddressLine1, candidate.AddressLine2, candidate.City, candidate.StateRegion, candidate.PostalCode })
            .SingleOrDefaultAsync(cancellationToken);
        var technician = await (
            from assignment in dbContext.VisitAssignments.AsNoTracking()
            join profile in dbContext.TechnicianProfiles.AsNoTracking() on assignment.TechnicianId equals profile.Id
            where assignment.VisitId == visit.Id && assignment.IsPrimary && assignment.UnassignedAt == null
            select new { profile.FirstName, profile.LastName })
            .FirstOrDefaultAsync(cancellationToken);
        var checklist = await dbContext.VisitChecklistItems.AsNoTracking()
            .Where(item => item.VisitId == visit.Id)
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.Id)
            .Select(item => new CompletionChecklistItem(item.Label, item.IsCompleted))
            .ToListAsync(cancellationToken);
        var signoff = await dbContext.CustomerSignoffs.AsNoTracking()
            .Where(candidate => candidate.VisitId == visit.Id)
            .Select(candidate => new { candidate.AcknowledgementMethod, candidate.SignerName, candidate.SignedAt })
            .FirstOrDefaultAsync(cancellationToken);
        var technicianName = technician is null ? null : PortalReaders.FullName(technician.FirstName, technician.LastName);

        return new CompletionReportSource(
            organization.Name,
            lines,
            logo?.Content,
            logo?.ContentType,
            RequestCardRules.DisplayNumber(organization.WorkOrderPrefix, order.WorkOrderNumber),
            order.Title,
            property is null
                ? null
                : QuoteStore.FormatAddress(property.AddressLine1, property.AddressLine2, property.City, property.StateRegion, property.PostalCode),
            OrganizationTime.LocalDate(visit.ActualCompletedAt!.Value, zone),
            string.IsNullOrEmpty(technicianName) ? null : technicianName,
            visit.CompletionSummary,
            checklist,
            signoff is null ? null : CompletionVerifier.MethodLabel(signoff.AcknowledgementMethod),
            signoff?.SignerName,
            signoff is null ? null : OrganizationTime.LocalDate(signoff.SignedAt, zone));
    }

    private Task<bool> IsActivePropertyAsync(PortalScope scope, Guid propertyId, CancellationToken cancellationToken) =>
        dbContext.Properties.AsNoTracking().ForPortal(scope).AnyAsync(property => property.Id == propertyId && property.IsActive, cancellationToken);

    // customer portal API contracts: the stored public availability mapped exactly (mode = dateMode, one date only for "date").
    private static PortalRequestAvailability? ParseAvailability(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var mode = Text(root, "dateMode") ?? "asap";
            var window = Text(root, "timeWindow") ?? "any";
            var dates = new List<PortalAvailabilityDate>();

            if (mode == "date"
                && Text(root, "preferredDate") is { } preferred
                && DateOnly.TryParseExact(preferred, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var date))
            {
                dates.Add(new PortalAvailabilityDate(date, window));
            }

            return new PortalRequestAvailability(mode, dates, window, Text(root, "schedulingNotes"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;
}
