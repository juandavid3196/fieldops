using System.Text.Json;
using FieldOps.Application.Features.BillingReview;
using FieldOps.Application.Features.InvoiceDelivery;
using FieldOps.Application.Features.Quotes;
using FieldOps.Application.Features.ServiceRequests;
using FieldOps.Domain.Invoices;
using FieldOps.Domain.WorkOrders;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Persistence;

/// <summary>
/// Reads the customer-facing content of an invoice (invoice-draft-delivery BR-04, BR-05) for the internal detail, the
/// PDFs and the public link. Amounts and lines always come from <c>invoices</c> and <c>invoice_lines</c>; Bill to, the
/// service address and the completion note are read live while the invoice is a draft and only from
/// <c>invoices.customer_snapshot</c> afterwards. Every query filters by the invoice organization and is no-tracking.
/// </summary>
internal sealed class InvoicePreviewReader(FieldOpsDbContext dbContext)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>The preview plus the facts the detail and the email need.</summary>
    public sealed record Loaded(
        InvoicePreview Preview,
        Guid WorkOrderId,
        string WorkOrderTitle,
        string OrganizationName,
        string? OrganizationPhone,
        string? ContactFirstName,
        string? DefaultRecipient);

    public async Task<Loaded> ReadAsync(Invoice invoice, CancellationToken cancellationToken)
    {
        var live = await ReadLiveAsync(invoice, cancellationToken);
        var snapshot = invoice.Status != InvoiceStatus.Draft && invoice.CustomerSnapshot is { } stored
            ? JsonSerializer.Deserialize<InvoiceCustomerSnapshot>(stored, JsonOptions)
            : null;

        return snapshot is null ? live.Loaded : live.Loaded with
        {
            Preview = live.Loaded.Preview with
            {
                BillTo = snapshot.BillTo,
                ServiceAddress = snapshot.ServiceAddress,
                CompletionNote = snapshot.CompletionNote,
            },
        };
    }

    /// <summary>The live customer-facing content; <c>Snapshot</c> is what a send freezes (SA-09).</summary>
    public async Task<(Loaded Loaded, InvoiceCustomerSnapshot Snapshot)> ReadLiveAsync(
        Invoice invoice, CancellationToken cancellationToken)
    {
        var organizationId = invoice.OrganizationId;
        var organization = await dbContext.Organizations.AsNoTracking()
            .Where(candidate => candidate.Id == organizationId)
            .Select(candidate => new
            {
                candidate.Name,
                candidate.Email,
                candidate.Phone,
                candidate.AddressLine1,
                candidate.City,
                candidate.StateRegion,
                candidate.PostalCode,
                candidate.Timezone,
                candidate.InvoicePrefix,
                candidate.WorkOrderPrefix,
            })
            .SingleAsync(cancellationToken);
        var hasLogo = await dbContext.OrganizationLogos.AsNoTracking()
            .AnyAsync(candidate => candidate.OrganizationId == organizationId, cancellationToken);
        var order = await dbContext.WorkOrders.AsNoTracking()
            .Where(candidate => candidate.OrganizationId == organizationId && candidate.Id == invoice.WorkOrderId)
            .Select(candidate => new { candidate.Id, candidate.WorkOrderNumber, candidate.Title, candidate.PropertyId })
            .SingleAsync(cancellationToken);
        var customer = await dbContext.Customers.AsNoTracking()
            .Where(candidate => candidate.OrganizationId == organizationId && candidate.Id == invoice.CustomerId)
            .Select(candidate => new { candidate.DisplayName, candidate.PrimaryEmail, candidate.PrimaryPhone, candidate.BillingAddress })
            .SingleAsync(cancellationToken);
        var contact = await dbContext.CustomerContacts.AsNoTracking()
            .Where(candidate => candidate.OrganizationId == organizationId
                && candidate.CustomerId == invoice.CustomerId
                && candidate.IsPrimary
                && candidate.IsActive)
            .OrderBy(candidate => candidate.CreatedAt)
            .ThenBy(candidate => candidate.Id)
            .Select(candidate => new { candidate.FirstName, candidate.Email, candidate.Phone })
            .FirstOrDefaultAsync(cancellationToken);
        var property = await dbContext.Properties.AsNoTracking()
            .Where(candidate => candidate.OrganizationId == organizationId && candidate.Id == order.PropertyId)
            .Select(candidate => new { candidate.AddressLine1, candidate.AddressLine2, candidate.City, candidate.StateRegion, candidate.PostalCode })
            .SingleOrDefaultAsync(cancellationToken);
        var summary = await dbContext.Visits.AsNoTracking()
            .Where(visit => visit.OrganizationId == organizationId
                && visit.WorkOrderId == invoice.WorkOrderId
                && visit.ActualCompletedAt != null
                && (visit.Status == VisitStatus.Completed || visit.Status == VisitStatus.Approved))
            .OrderByDescending(visit => visit.ActualCompletedAt)
            .Select(visit => visit.CompletionSummary)
            .FirstOrDefaultAsync(cancellationToken);

        var lines = await (
            from line in dbContext.InvoiceLines.AsNoTracking()
            where line.InvoiceId == invoice.Id
            join source in dbContext.QuoteLines.AsNoTracking().Where(candidate => candidate.OrganizationId == organizationId)
                on line.SourceQuoteLineId equals (Guid?)source.Id into sources
            from source in sources.DefaultIfEmpty()
            orderby line.SortOrder, line.Id
            select new
            {
                line.Description,
                Detail = source == null ? null : source.Description,
                line.Quantity,
                line.Unit,
                line.UnitPrice,
                line.TaxRate,
                line.LineSubtotal,
            })
            .ToListAsync(cancellationToken);

        var taxLabel = QuoteCalculator.TaxLabel(lines.Select(line => line.TaxRate));
        var contactEmail = Clean(contact?.Email) ?? Clean(customer.PrimaryEmail);
        var snapshot = new InvoiceCustomerSnapshot(
            new InvoiceBillTo(
                customer.DisplayName,
                contactEmail,
                Clean(contact?.Phone) ?? Clean(customer.PrimaryPhone),
                BillingAddressLines(customer.BillingAddress)),
            property is null
                ? null
                : QuoteStore.FormatAddress(property.AddressLine1, property.AddressLine2, property.City, property.StateRegion, property.PostalCode),
            Clean(summary));

        var preview = new InvoicePreview(
            DisplayNumber(organization.InvoicePrefix, invoice.InvoiceNumber),
            invoice.IssueDate,
            invoice.DueDate,
            invoice.PaymentTerms ?? PaymentTermsCodes.DueUponReceipt,
            invoice.Currency,
            organization.Timezone,
            new InvoiceOrganizationView(
                organization.Name,
                OrganizationAddressLines(organization.AddressLine1, organization.City, organization.StateRegion, organization.PostalCode),
                Clean(organization.Phone),
                Clean(organization.Email),
                hasLogo),
            snapshot.BillTo,
            snapshot.ServiceAddress,
            DisplayNumber(organization.WorkOrderPrefix, order.WorkOrderNumber),
            lines.Select(line => new InvoiceLineView(
                line.Description,
                Clean(line.Detail),
                InvoiceDeliveryRules.Quantity(line.Quantity),
                line.Unit,
                line.UnitPrice,
                line.TaxRate,
                line.LineSubtotal)).ToList(),
            new InvoiceTotalsView(invoice.Subtotal, invoice.DiscountTotal, taxLabel, invoice.TaxTotal, invoice.Total),
            snapshot.CompletionNote);

        return (
            new Loaded(preview, order.Id, order.Title, organization.Name, Clean(organization.Phone), Clean(contact?.FirstName), contactEmail),
            snapshot);
    }

    /// <summary>The PDF input (BR-12): the preview, whether it is still a draft and the PNG or JPEG logo when one exists.</summary>
    public async Task<InvoicePdfSource> ReadPdfSourceAsync(Invoice invoice, CancellationToken cancellationToken)
    {
        var loaded = await ReadAsync(invoice, cancellationToken);
        var logo = loaded.Preview.Organization.HasLogo
            ? await dbContext.OrganizationLogos.AsNoTracking()
                .Where(candidate => candidate.OrganizationId == invoice.OrganizationId)
                .Select(candidate => new { candidate.ContentType, candidate.Content })
                .SingleOrDefaultAsync(cancellationToken)
            : null;

        return new InvoicePdfSource(loaded.Preview, invoice.Status == InvoiceStatus.Draft, logo?.Content, logo?.ContentType);
    }

    public async Task<string> ReadUserNameAsync(Guid userId, CancellationToken cancellationToken)
    {
        var name = await dbContext.Users.AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new { user.FirstName, user.LastName })
            .SingleOrDefaultAsync(cancellationToken);

        return name is null
            ? string.Empty
            : string.Join(' ', new[] { name.FirstName, name.LastName }.Where(part => !string.IsNullOrWhiteSpace(part))).Trim();
    }

    public static string DisplayNumber(string prefix, long number) => RequestCardRules.DisplayNumber(prefix, number);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>"address_line1" and "&lt;city&gt;, &lt;state_region&gt; &lt;postal_code&gt;", each hidden when empty (BR-04).</summary>
    internal static IReadOnlyList<string> OrganizationAddressLines(string? line1, string? city, string? state, string? postalCode)
    {
        var lines = new List<string>();

        if (Clean(line1) is { } first)
        {
            lines.Add(first);
        }

        var region = string.Join(' ', new[] { Clean(state), Clean(postalCode) }.Where(part => part is not null));
        var second = string.Join(", ", new[] { Clean(city), region.Length == 0 ? null : region }.Where(part => part is not null));

        if (second.Length > 0)
        {
            lines.Add(second);
        }

        return lines;
    }

    /// <summary>
    /// The lines of <c>customers.billing_address</c> (JSON object with line1, line2, city, state and postalCode, like the
    /// other stored addresses); an empty, non-object or unreadable value has no lines.
    /// </summary>
    internal static IReadOnlyList<string> BillingAddressLines(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(json);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return [];
            }

            string? Read(params string[] names)
            {
                foreach (var name in names)
                {
                    if (document.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                    {
                        return Clean(value.GetString());
                    }
                }

                return null;
            }

            var lines = new List<string>();

            foreach (var line in new[] { Read("line1", "addressLine1"), Read("line2", "addressLine2") })
            {
                if (line is not null)
                {
                    lines.Add(line);
                }
            }

            var region = string.Join(' ', new[] { Read("state", "stateRegion"), Read("postalCode") }.Where(part => part is not null));
            var last = string.Join(", ", new[] { Read("city"), region.Length == 0 ? null : region }.Where(part => part is not null));

            if (last.Length > 0)
            {
                lines.Add(last);
            }

            return lines;
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
