using FieldOps.Application.Features.Access;
using FieldOps.Application.Features.ServiceRequests;
using FieldOps.Application.Validation;

namespace FieldOps.Application.Features.Quotes;

/// <summary>Shared steps of the quote handlers: scope, visibility (404 first), shape validation and outcome mapping.</summary>
internal static class QuoteHandlerSupport
{
    public static async Task<QuoteActor> ActorAsync(
        IBranchScopeResolver scopes, MembershipCall call, CancellationToken cancellationToken) =>
        new(
            call.OrganizationId,
            call.UserId,
            await scopes.ResolveAsync(call.OrganizationId, call.MembershipId, cancellationToken),
            call.IpAddress);

    public static ServiceRequestResult<T> Map<T>(QuoteOutcome<T> outcome) =>
        outcome switch
        {
            QuoteOutcome<T>.Succeeded succeeded => ServiceRequestResult<T>.Ok(succeeded.Value),
            QuoteOutcome<T>.Conflict conflict => ServiceRequestResult<T>.Conflict(Title(conflict.Code), conflict.Code),
            QuoteOutcome<T>.Invalid invalid => ServiceRequestResult<T>.Invalid(invalid.Errors),
            _ => ServiceRequestResult<T>.NotFound(),
        };

    public static string Title(string code) => code switch
    {
        QuoteMessages.QuoteChangedCode => QuoteMessages.QuoteChangedTitle,
        QuoteMessages.NoDraftCode => QuoteMessages.NoDraftTitle,
        ServiceRequestMessages.QuoteDraftExistsCode => ServiceRequestMessages.QuoteDraftExistsTitle,
        _ => ServiceRequestMessages.ConflictTitle,
    };

    public static DateOnly Today(QuoteContext context, TimeProvider timeProvider) =>
        OrganizationTime.LocalDate(timeProvider.GetUtcNow(), OrganizationTime.FindZone(context.Timezone));

    public static DateTimeOffset? ParseUpdatedAt(string? value, Dictionary<string, string[]> errors)
    {
        if (UpdatedAtValidation.TryParse(value, out var parsed))
        {
            return parsed;
        }

        errors["updatedAt"] = [UpdatedAtValidation.InvalidMessage];

        return null;
    }
}

public sealed class CreateQuoteHandler(IQuoteStore store, IBranchScopeResolver scopes)
{
    public async Task<ServiceRequestResult<QuoteCreated>> HandleAsync(
        MembershipCall call, Guid? requestId, CancellationToken cancellationToken)
    {
        if (requestId is null || requestId == Guid.Empty)
        {
            return ServiceRequestResult<QuoteCreated>.Invalid("requestId", QuoteMessages.RequestIdMessage);
        }

        var actor = await QuoteHandlerSupport.ActorAsync(scopes, call, cancellationToken);

        return QuoteHandlerSupport.Map(await store.CreateAsync(actor, requestId.Value, cancellationToken));
    }
}

public sealed class GetQuoteHandler(IQuoteStore store, IBranchScopeResolver scopes)
{
    public async Task<QuoteDetail?> HandleAsync(
        Guid organizationId, Guid membershipId, Guid quoteId, bool canManage, bool canManageWorkOrders, CancellationToken cancellationToken)
    {
        var scope = await scopes.ResolveAsync(organizationId, membershipId, cancellationToken);

        return await store.GetAsync(organizationId, scope, quoteId, canManage, canManageWorkOrders, cancellationToken);
    }
}

public sealed class GetQuoteVersionHandler(IQuoteStore store, IBranchScopeResolver scopes)
{
    public async Task<QuoteVersionView?> HandleAsync(
        Guid organizationId, Guid membershipId, Guid quoteId, int versionNo, CancellationToken cancellationToken)
    {
        var scope = await scopes.ResolveAsync(organizationId, membershipId, cancellationToken);

        return await store.GetVersionAsync(organizationId, scope, quoteId, versionNo, cancellationToken);
    }
}

/// <summary>Validates the draft like a save and returns the backend calculation; persists nothing (BR-23).</summary>
public sealed class CalculateQuoteHandler(IQuoteStore store, IBranchScopeResolver scopes, TimeProvider timeProvider)
{
    public async Task<ServiceRequestResult<QuoteCalculation>> HandleAsync(
        MembershipCall call, Guid quoteId, QuoteDraftText body, CancellationToken cancellationToken)
    {
        var actor = await QuoteHandlerSupport.ActorAsync(scopes, call, cancellationToken);

        if (await store.GetContextAsync(call.OrganizationId, actor.Scope, quoteId, cancellationToken) is not { } context)
        {
            return ServiceRequestResult<QuoteCalculation>.NotFound();
        }

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var draft = QuoteDraftValidator.Validate(body, QuoteHandlerSupport.Today(context, timeProvider), errors);

        return draft is null
            ? ServiceRequestResult<QuoteCalculation>.Invalid(errors)
            : QuoteHandlerSupport.Map(await store.CalculateAsync(actor, quoteId, draft, cancellationToken));
    }
}

public sealed class SaveQuoteDraftHandler(IQuoteStore store, IBranchScopeResolver scopes, TimeProvider timeProvider)
{
    public async Task<ServiceRequestResult<QuoteDetail>> HandleAsync(
        MembershipCall call, Guid quoteId, string? updatedAt, QuoteDraftText body, CancellationToken cancellationToken)
    {
        var actor = await QuoteHandlerSupport.ActorAsync(scopes, call, cancellationToken);

        if (await store.GetContextAsync(call.OrganizationId, actor.Scope, quoteId, cancellationToken) is not { } context)
        {
            return ServiceRequestResult<QuoteDetail>.NotFound();
        }

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var token = QuoteHandlerSupport.ParseUpdatedAt(updatedAt, errors);
        var draft = QuoteDraftValidator.Validate(body, QuoteHandlerSupport.Today(context, timeProvider), errors);

        return draft is null || token is null
            ? ServiceRequestResult<QuoteDetail>.Invalid(errors)
            : QuoteHandlerSupport.Map(await store.SaveDraftAsync(actor, quoteId, token.Value, draft, cancellationToken));
    }
}

/// <summary>
/// Send (BR-24): validation here, then one store transaction. The raw token is generated before the store,
/// which only sees the hash; the email goes out after the commit and a failure never undoes the send (BR-26).
/// </summary>
public sealed class SendQuoteHandler(
    IQuoteStore store,
    IBranchScopeResolver scopes,
    IQuoteLinkBuilder links,
    IQuoteNotifier notifier,
    TimeProvider timeProvider)
{
    public async Task<ServiceRequestResult<QuoteSendResponse>> HandleAsync(
        MembershipCall call,
        Guid quoteId,
        string? updatedAt,
        string? emailMessage,
        QuoteDraftText body,
        CancellationToken cancellationToken)
    {
        var actor = await QuoteHandlerSupport.ActorAsync(scopes, call, cancellationToken);

        if (await store.GetContextAsync(call.OrganizationId, actor.Scope, quoteId, cancellationToken) is not { } context)
        {
            return ServiceRequestResult<QuoteSendResponse>.NotFound();
        }

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var token = QuoteHandlerSupport.ParseUpdatedAt(updatedAt, errors);
        var message = QuoteDraftValidator.ValidateEmailMessage(emailMessage, errors);
        var draft = QuoteDraftValidator.Validate(body, QuoteHandlerSupport.Today(context, timeProvider), errors);

        if (draft is not null && draft.Lines.All(line => line.IsOptional))
        {
            errors["lines"] = [QuoteMessages.NoNonOptionalLineMessage];
        }

        if (errors.Count > 0 || draft is null || token is null || message is null)
        {
            return ServiceRequestResult<QuoteSendResponse>.Invalid(errors);
        }

        var (raw, hash) = QuoteAccessTokens.Generate();
        var outcome = await store.SendAsync(actor, quoteId, token.Value, draft, hash, cancellationToken);

        if (outcome is not QuoteOutcome<QuoteSent>.Succeeded succeeded)
        {
            return QuoteHandlerSupport.Map<QuoteSendResponse>(outcome switch
            {
                QuoteOutcome<QuoteSent>.Conflict conflict => new QuoteOutcome<QuoteSendResponse>.Conflict(conflict.Code),
                QuoteOutcome<QuoteSent>.Invalid invalid => new QuoteOutcome<QuoteSendResponse>.Invalid(invalid.Errors),
                _ => new QuoteOutcome<QuoteSendResponse>.NotFound(),
            });
        }

        var status = await notifier.SendAsync(
            new QuoteEmail(succeeded.Value.Email, message, links.BuildLink(raw)), cancellationToken);

        return ServiceRequestResult<QuoteSendResponse>.Ok(new QuoteSendResponse(
            succeeded.Value.Detail, status == QuoteEmailStatus.Sent ? "sent" : "failed"));
    }
}

public sealed class ReviseQuoteHandler(IQuoteStore store, IBranchScopeResolver scopes)
{
    public async Task<ServiceRequestResult<QuoteDetail>> HandleAsync(
        MembershipCall call, Guid quoteId, string? updatedAt, CancellationToken cancellationToken)
    {
        var actor = await QuoteHandlerSupport.ActorAsync(scopes, call, cancellationToken);

        if (await store.GetContextAsync(call.OrganizationId, actor.Scope, quoteId, cancellationToken) is null)
        {
            return ServiceRequestResult<QuoteDetail>.NotFound();
        }

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        return QuoteHandlerSupport.ParseUpdatedAt(updatedAt, errors) is { } token
            ? QuoteHandlerSupport.Map(await store.ReviseAsync(actor, quoteId, token, cancellationToken))
            : ServiceRequestResult<QuoteDetail>.Invalid(errors);
    }
}

public sealed class DiscardQuoteDraftHandler(IQuoteStore store, IBranchScopeResolver scopes)
{
    public async Task<ServiceRequestResult<QuoteDiscarded>> HandleAsync(
        MembershipCall call, Guid quoteId, string? updatedAt, CancellationToken cancellationToken)
    {
        var actor = await QuoteHandlerSupport.ActorAsync(scopes, call, cancellationToken);

        if (await store.GetContextAsync(call.OrganizationId, actor.Scope, quoteId, cancellationToken) is null)
        {
            return ServiceRequestResult<QuoteDiscarded>.NotFound();
        }

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        return QuoteHandlerSupport.ParseUpdatedAt(updatedAt, errors) is { } token
            ? QuoteHandlerSupport.Map(await store.DiscardDraftAsync(actor, quoteId, token, cancellationToken))
            : ServiceRequestResult<QuoteDiscarded>.Invalid(errors);
    }
}

public sealed class ResendQuoteEmailHandler(
    IQuoteStore store,
    IBranchScopeResolver scopes,
    IQuoteLinkBuilder links,
    IQuoteNotifier notifier)
{
    public async Task<ServiceRequestResult<QuoteSendResponse>> HandleAsync(
        MembershipCall call, Guid quoteId, string? updatedAt, string? emailMessage, CancellationToken cancellationToken)
    {
        var actor = await QuoteHandlerSupport.ActorAsync(scopes, call, cancellationToken);

        if (await store.GetContextAsync(call.OrganizationId, actor.Scope, quoteId, cancellationToken) is null)
        {
            return ServiceRequestResult<QuoteSendResponse>.NotFound();
        }

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var token = QuoteHandlerSupport.ParseUpdatedAt(updatedAt, errors);
        var message = QuoteDraftValidator.ValidateEmailMessage(emailMessage, errors);

        if (token is null || message is null)
        {
            return ServiceRequestResult<QuoteSendResponse>.Invalid(errors);
        }

        var (raw, hash) = QuoteAccessTokens.Generate();
        var outcome = await store.ResendEmailAsync(actor, quoteId, token.Value, hash, cancellationToken);

        if (outcome is not QuoteOutcome<QuoteSent>.Succeeded succeeded)
        {
            return QuoteHandlerSupport.Map<QuoteSendResponse>(outcome switch
            {
                QuoteOutcome<QuoteSent>.Conflict conflict => new QuoteOutcome<QuoteSendResponse>.Conflict(conflict.Code),
                QuoteOutcome<QuoteSent>.Invalid invalid => new QuoteOutcome<QuoteSendResponse>.Invalid(invalid.Errors),
                _ => new QuoteOutcome<QuoteSendResponse>.NotFound(),
            });
        }

        var status = await notifier.SendAsync(
            new QuoteEmail(succeeded.Value.Email, message, links.BuildLink(raw)), cancellationToken);

        return ServiceRequestResult<QuoteSendResponse>.Ok(new QuoteSendResponse(
            succeeded.Value.Detail, status == QuoteEmailStatus.Sent ? "sent" : "failed"));
    }
}
