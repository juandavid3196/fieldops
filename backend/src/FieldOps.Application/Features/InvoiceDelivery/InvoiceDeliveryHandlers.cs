using FieldOps.Application.Features.Access;
using FieldOps.Application.Features.BillingReview;
using FieldOps.Application.Features.Quotes;
using FieldOps.Application.Features.QuoteLinks;
using FieldOps.Application.Features.ServiceRequests;

namespace FieldOps.Application.Features.InvoiceDelivery;

internal static class InvoiceHandlerSupport
{
    public static async Task<BillingActor> ActorAsync(IBranchScopeResolver scopes, MembershipCall call, CancellationToken cancellationToken) =>
        BillingHandlerSupport.Actor(call, await BillingHandlerSupport.ScopeAsync(scopes, call, cancellationToken));
}

/// <summary>GET detail (BR-02 to BR-07): null is the one identical "not found".</summary>
public sealed class GetInvoiceHandler(IInvoiceDeliveryStore store, IBranchScopeResolver scopes)
{
    public async Task<InvoiceDetail?> HandleAsync(MembershipCall call, Guid invoiceId, bool canAct, CancellationToken cancellationToken) =>
        await store.GetDetailAsync(await InvoiceHandlerSupport.ActorAsync(scopes, call, cancellationToken), invoiceId, canAct, cancellationToken);
}

/// <summary>GET pdf (BR-12): generated from stored data only, never from browser data.</summary>
public sealed class DownloadInvoicePdfHandler(IInvoiceDeliveryStore store, IBranchScopeResolver scopes, IInvoicePdfRenderer renderer)
{
    public async Task<InvoicePdfFile?> HandleAsync(MembershipCall call, Guid invoiceId, CancellationToken cancellationToken)
    {
        var source = await store.GetPdfSourceAsync(await InvoiceHandlerSupport.ActorAsync(scopes, call, cancellationToken), invoiceId, cancellationToken);

        return source is null
            ? null
            : new InvoicePdfFile(
                InvoiceDeliveryRules.SafeFileName(source.Preview.Number),
                renderer.Render(InvoicePdfDocumentComposer.Compose(source)));
    }
}

/// <summary>PUT draft (BR-09 to BR-11): the body is validated inside the locked transaction of the store.</summary>
public sealed class SaveInvoiceDraftHandler(IInvoiceDeliveryStore store, IBranchScopeResolver scopes)
{
    public async Task<BillingOutcome<InvoiceDetail>> HandleAsync(
        MembershipCall call, Guid invoiceId, DeliveryBodyText body, CancellationToken cancellationToken) =>
        await store.SaveDraftAsync(await InvoiceHandlerSupport.ActorAsync(scopes, call, cancellationToken), invoiceId, body, cancellationToken);
}

/// <summary>
/// POST send (BR-13 to BR-16). The raw token is generated before the store, which only sees the hash; the email goes out
/// after the commit and a failure never undoes the send. An invoice that was already sent returns no email data.
/// </summary>
public sealed class SendInvoiceHandler(
    IInvoiceDeliveryStore store,
    IBranchScopeResolver scopes,
    IInvoiceLinkBuilder links,
    IInvoiceNotifier notifier)
{
    public async Task<BillingOutcome<InvoiceSendResponse>> HandleAsync(
        MembershipCall call, Guid invoiceId, DeliveryBodyText body, CancellationToken cancellationToken)
    {
        var actor = await InvoiceHandlerSupport.ActorAsync(scopes, call, cancellationToken);
        var (raw, hash) = QuoteAccessTokens.Generate();

        switch (await store.SendAsync(actor, invoiceId, body, hash, cancellationToken))
        {
            case BillingOutcome<InvoiceSent>.Succeeded succeeded:
                var sent = succeeded.Value;

                if (!sent.Changed || sent.Email is null)
                {
                    return new BillingOutcome<InvoiceSendResponse>.Succeeded(
                        new InvoiceSendResponse(false, InvoiceDeliveryMessages.EmailNotSent, sent.Invoice));
                }

                var status = await notifier.SendAsync(new InvoiceEmail(sent.Email, links.BuildLink(raw)), cancellationToken);

                return new BillingOutcome<InvoiceSendResponse>.Succeeded(
                    new InvoiceSendResponse(true, EmailStatus(status), sent.Invoice));
            case BillingOutcome<InvoiceSent>.Invalid invalid:
                return new BillingOutcome<InvoiceSendResponse>.Invalid(invalid.Errors);
            case BillingOutcome<InvoiceSent>.Conflict conflict:
                return new BillingOutcome<InvoiceSendResponse>.Conflict(conflict.Code, conflict.Title);
            default:
                return new BillingOutcome<InvoiceSendResponse>.NotFound();
        }
    }

    internal static string EmailStatus(InvoiceEmailStatus status) =>
        status == InvoiceEmailStatus.Sent ? InvoiceDeliveryMessages.EmailSent : InvoiceDeliveryMessages.EmailFailed;
}

/// <summary>POST resend-email (BR-17): rotates the token inside the transaction, then emails the stored message.</summary>
public sealed class ResendInvoiceEmailHandler(
    IInvoiceDeliveryStore store,
    IBranchScopeResolver scopes,
    IInvoiceLinkBuilder links,
    IInvoiceNotifier notifier)
{
    public async Task<BillingOutcome<InvoiceResendResponse>> HandleAsync(
        MembershipCall call, Guid invoiceId, string? updatedAt, CancellationToken cancellationToken)
    {
        var actor = await InvoiceHandlerSupport.ActorAsync(scopes, call, cancellationToken);
        var (raw, hash) = QuoteAccessTokens.Generate();

        switch (await store.ResendAsync(actor, invoiceId, updatedAt, hash, cancellationToken))
        {
            case BillingOutcome<InvoiceResent>.Succeeded succeeded:
                var status = await notifier.SendAsync(new InvoiceEmail(succeeded.Value.Email, links.BuildLink(raw)), cancellationToken);

                return new BillingOutcome<InvoiceResendResponse>.Succeeded(
                    new InvoiceResendResponse(SendInvoiceHandler.EmailStatus(status), succeeded.Value.Invoice));
            case BillingOutcome<InvoiceResent>.Invalid invalid:
                return new BillingOutcome<InvoiceResendResponse>.Invalid(invalid.Errors);
            case BillingOutcome<InvoiceResent>.Conflict conflict:
                return new BillingOutcome<InvoiceResendResponse>.Conflict(conflict.Code, conflict.Title);
            default:
                return new BillingOutcome<InvoiceResendResponse>.NotFound();
        }
    }
}

/// <summary>POST /public/invoice-links/view (BR-19, BR-20): a malformed token never reaches the store.</summary>
public sealed class ViewInvoiceLinkHandler(IInvoiceLinkStore store)
{
    public Task<PublicInvoice?> HandleAsync(string? token, CancellationToken cancellationToken) =>
        QuoteLinkTokens.IsWellFormed(token) ? store.ViewAsync(token!, cancellationToken) : Task.FromResult<PublicInvoice?>(null);
}

/// <summary>POST /public/invoice-links/pdf (BR-20): the PDF of the frozen invoice without the DRAFT mark.</summary>
public sealed class DownloadInvoiceLinkPdfHandler(IInvoiceLinkStore store, IInvoicePdfRenderer renderer)
{
    public async Task<InvoicePdfFile?> HandleAsync(string? token, CancellationToken cancellationToken)
    {
        if (!QuoteLinkTokens.IsWellFormed(token))
        {
            return null;
        }

        var source = await store.GetPdfSourceAsync(token!, cancellationToken);

        return source is null
            ? null
            : new InvoicePdfFile(
                InvoiceDeliveryRules.SafeFileName(source.Preview.Number),
                renderer.Render(InvoicePdfDocumentComposer.Compose(source)));
    }
}

/// <summary>POST /public/invoice-links/logo (BR-20), scoped by the organization of the token.</summary>
public sealed class GetInvoiceLinkLogoHandler(IInvoiceLinkStore store)
{
    public Task<PublicBinary?> HandleAsync(string? token, CancellationToken cancellationToken) =>
        QuoteLinkTokens.IsWellFormed(token) ? store.GetLogoAsync(token!, cancellationToken) : Task.FromResult<PublicBinary?>(null);
}
