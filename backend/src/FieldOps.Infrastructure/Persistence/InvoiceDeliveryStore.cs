using System.Text.Json;
using FieldOps.Application.Features.Access;
using FieldOps.Application.Features.BillingReview;
using FieldOps.Application.Features.InvoiceDelivery;
using FieldOps.Domain.Invoices;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Persistence;

/// <summary>
/// Invoice draft delivery persistence (invoice-draft-delivery). Every call starts from the session organization and the
/// branch scope of <c>invoices.branch_id</c> (BR-02); a missing, foreign, out-of-scope or void invoice is one identical
/// "not found". Reads are no-tracking. Writes live in <c>InvoiceDeliveryStore.Mutations</c>.
/// </summary>
internal sealed partial class InvoiceDeliveryStore(FieldOpsDbContext dbContext, TimeProvider timeProvider) : IInvoiceDeliveryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private const string AuditEntityType = "invoice";

    public async Task<InvoiceDetail?> GetDetailAsync(BillingActor actor, Guid invoiceId, bool canAct, CancellationToken cancellationToken)
    {
        var invoice = await VisibleInvoices(actor.OrganizationId, actor.Scope)
            .SingleOrDefaultAsync(candidate => candidate.Id == invoiceId, cancellationToken);

        return invoice is null ? null : await BuildDetailAsync(invoice, canAct, cancellationToken);
    }

    public async Task<InvoicePdfSource?> GetPdfSourceAsync(BillingActor actor, Guid invoiceId, CancellationToken cancellationToken)
    {
        var invoice = await VisibleInvoices(actor.OrganizationId, actor.Scope)
            .SingleOrDefaultAsync(candidate => candidate.Id == invoiceId, cancellationToken);

        return invoice is null ? null : await new InvoicePreviewReader(dbContext).ReadPdfSourceAsync(invoice, cancellationToken);
    }

    /// <summary>Invoices of the organization inside the caller branch scope, never void (BR-02).</summary>
    private IQueryable<Invoice> VisibleInvoices(Guid organizationId, BranchScope scope)
    {
        var invoices = dbContext.Invoices.AsNoTracking()
            .Where(invoice => invoice.OrganizationId == organizationId && invoice.Status != InvoiceStatus.Void);

        if (!scope.All)
        {
            var ids = scope.BranchIds.ToArray();
            invoices = invoices.Where(invoice => ids.Contains(invoice.BranchId));
        }

        return invoices;
    }

    private async Task<InvoiceDetail> BuildDetailAsync(Invoice invoice, bool canAct, CancellationToken cancellationToken)
    {
        var reader = new InvoicePreviewReader(dbContext);
        var loaded = await reader.ReadAsync(invoice, cancellationToken);

        return await ToDetailAsync(reader, invoice, loaded, canAct, cancellationToken);
    }

    private static async Task<InvoiceDetail> ToDetailAsync(
        InvoicePreviewReader reader, Invoice invoice, InvoicePreviewReader.Loaded loaded, bool canAct, CancellationToken cancellationToken)
    {
        var preview = loaded.Preview;
        var recipient = invoice.RecipientEmail ?? loaded.DefaultRecipient ?? string.Empty;
        var message = invoice.DeliveryMessage
            ?? InvoiceDeliveryRules.DefaultMessage(
                loaded.ContactFirstName, preview.BillTo.Name, preview.Number, loaded.WorkOrderTitle, loaded.OrganizationName);

        return new InvoiceDetail(
            preview.Number,
            preview.IssueDate,
            preview.DueDate,
            preview.PaymentTerms,
            preview.Currency,
            preview.Timezone,
            preview.Organization,
            preview.BillTo,
            preview.ServiceAddress,
            preview.WorkOrderNumber,
            preview.Lines,
            preview.Totals,
            preview.CompletionNote,
            invoice.Id,
            InvoiceStatusCode(invoice.Status),
            loaded.WorkOrderId,
            preview.BillTo.Name,
            new InvoiceDelivery(recipient, message, invoice.DeliveryMessage is not null),
            InvoiceDeliveryRules.Checks(recipient, preview.Totals.TaxLabel),
            invoice.CreatedAt,
            await reader.ReadUserNameAsync(invoice.CreatedByUserId, cancellationToken),
            invoice.SentAt,
            canAct,
            invoice.UpdatedAt);
    }

    internal static string InvoiceStatusCode(InvoiceStatus status) => status switch
    {
        InvoiceStatus.Draft => "draft",
        InvoiceStatus.Sent => "sent",
        InvoiceStatus.PartiallyPaid => "partially_paid",
        InvoiceStatus.Paid => "paid",
        InvoiceStatus.Overdue => "overdue",
        _ => "void",
    };

    private static string? Serialize(object? value) => value is null ? null : JsonSerializer.Serialize(value, JsonOptions);
}
