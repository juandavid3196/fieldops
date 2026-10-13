using FieldOps.Application.Features.PortalAccess;
using FieldOps.Application.Features.Quotes;

namespace FieldOps.Application.Features.QuoteLinks;

public static class QuoteLinkMessages
{
    public const int TextMaxLength = 1000;

    public const string UnavailableCode = "quote_link_unavailable";

    public const string UnavailableTitle = "This link isn't available.";

    public const string SupersededCode = "quote_superseded";

    public const string SupersededTitle = "A newer version of this quote is available.";

    public const string AlreadyAnsweredCode = "quote_already_answered";

    public const string AlreadyAnsweredTitle = "This quote has already been answered.";

    /// <summary>Portal only (customer portal BR-30): the valid-until date passed.</summary>
    public const string ExpiredCode = "quote_expired";

    public const string ExpiredTitle = "This quote has expired.";

    public const string SelectionKey = "selectedOptionalLineIds";

    public const string SelectionMessage = "One or more optional items aren't available.";

    public const string AcceptTermsMessage = "Confirm that you approve the scope of work and agree to the terms.";

    public const string ReasonRequiredMessage = "Tell us why you're declining.";

    public const string ReasonTooLongMessage = "Reason must be 1,000 characters or fewer.";

    public const string QuestionRequiredMessage = "Enter your question.";

    public const string QuestionTooLongMessage = "Question must be 1,000 characters or fewer.";

    public const string PdfContentType = "application/pdf";
}

/// <summary>Shared first steps of the public handlers: the token shape (BR-03) and the selection shape (BR-10).</summary>
internal static class QuoteLinkHandlerSupport
{
    public static QuoteLinkOutcome<T> Unavailable<T>() => new QuoteLinkOutcome<T>.Unavailable();

    public static QuoteLinkOutcome<T> Invalid<T>(string key, string message) =>
        new QuoteLinkOutcome<T>.Invalid(new Dictionary<string, string[]>(StringComparer.Ordinal) { [key] = [message] });

    /// <summary>Parses the selected ids; a value that is not an id, or a duplicate, is the same 400 as an unknown line.</summary>
    public static bool TryParseSelection(IReadOnlyList<string?>? selected, out List<Guid> ids)
    {
        ids = [];

        foreach (var value in selected ?? [])
        {
            if (!Guid.TryParse(value, out var id) || id == Guid.Empty || ids.Contains(id))
            {
                return false;
            }

            ids.Add(id);
        }

        return true;
    }
}

public sealed class ViewQuoteLinkHandler(IQuoteLinkStore store)
{
    public Task<QuoteLinkOutcome<PublicQuote>> HandleAsync(string? token, CancellationToken cancellationToken) =>
        QuoteLinkTokens.IsWellFormed(token)
            ? HandleAsync(ResourceAccess.FromToken(token!), cancellationToken)
            : Task.FromResult(QuoteLinkHandlerSupport.Unavailable<PublicQuote>());

    public Task<QuoteLinkOutcome<PublicQuote>> HandleAsync(ResourceAccess access, CancellationToken cancellationToken) =>
        store.ViewAsync(access, cancellationToken);
}

public sealed class CalculateQuoteLinkHandler(IQuoteLinkStore store)
{
    public Task<QuoteLinkOutcome<PublicTotals>> HandleAsync(
        string? token, IReadOnlyList<string?>? selectedOptionalLineIds, CancellationToken cancellationToken) =>
        QuoteLinkTokens.IsWellFormed(token)
            ? HandleAsync(ResourceAccess.FromToken(token!), selectedOptionalLineIds, cancellationToken)
            : Task.FromResult(QuoteLinkHandlerSupport.Unavailable<PublicTotals>());

    public Task<QuoteLinkOutcome<PublicTotals>> HandleAsync(
        ResourceAccess access, IReadOnlyList<string?>? selectedOptionalLineIds, CancellationToken cancellationToken) =>
        QuoteLinkHandlerSupport.TryParseSelection(selectedOptionalLineIds, out var ids)
            ? store.CalculateAsync(access, ids, cancellationToken)
            : Task.FromResult(QuoteLinkHandlerSupport.Invalid<PublicTotals>(
                QuoteLinkMessages.SelectionKey, QuoteLinkMessages.SelectionMessage));
}

public sealed class ApproveQuoteLinkHandler(IQuoteLinkStore store)
{
    public Task<QuoteLinkOutcome<PublicQuote>> HandleAsync(
        string? token,
        IReadOnlyList<string?>? selectedOptionalLineIds,
        bool? acceptTerms,
        QuoteLinkCaller caller,
        CancellationToken cancellationToken) =>
        QuoteLinkTokens.IsWellFormed(token)
            ? HandleAsync(ResourceAccess.FromToken(token!), selectedOptionalLineIds, acceptTerms, caller, cancellationToken)
            : Task.FromResult(QuoteLinkHandlerSupport.Unavailable<PublicQuote>());

    public Task<QuoteLinkOutcome<PublicQuote>> HandleAsync(
        ResourceAccess access,
        IReadOnlyList<string?>? selectedOptionalLineIds,
        bool? acceptTerms,
        QuoteLinkCaller caller,
        CancellationToken cancellationToken)
    {
        if (acceptTerms != true)
        {
            return Task.FromResult(QuoteLinkHandlerSupport.Invalid<PublicQuote>(
                "acceptTerms", QuoteLinkMessages.AcceptTermsMessage));
        }

        return QuoteLinkHandlerSupport.TryParseSelection(selectedOptionalLineIds, out var ids)
            ? store.ApproveAsync(access, ids, caller, cancellationToken)
            : Task.FromResult(QuoteLinkHandlerSupport.Invalid<PublicQuote>(
                QuoteLinkMessages.SelectionKey, QuoteLinkMessages.SelectionMessage));
    }
}

public sealed class DeclineQuoteLinkHandler(IQuoteLinkStore store)
{
    public Task<QuoteLinkOutcome<PublicQuote>> HandleAsync(
        string? token, string? reason, QuoteLinkCaller caller, CancellationToken cancellationToken) =>
        QuoteLinkTokens.IsWellFormed(token)
            ? HandleAsync(ResourceAccess.FromToken(token!), reason, caller, cancellationToken)
            : Task.FromResult(QuoteLinkHandlerSupport.Unavailable<PublicQuote>());

    public Task<QuoteLinkOutcome<PublicQuote>> HandleAsync(
        ResourceAccess access, string? reason, QuoteLinkCaller caller, CancellationToken cancellationToken)
    {
        var text = reason?.Trim() ?? string.Empty;

        if (text.Length == 0)
        {
            return Task.FromResult(QuoteLinkHandlerSupport.Invalid<PublicQuote>("reason", QuoteLinkMessages.ReasonRequiredMessage));
        }

        return text.Length > QuoteLinkMessages.TextMaxLength
            ? Task.FromResult(QuoteLinkHandlerSupport.Invalid<PublicQuote>("reason", QuoteLinkMessages.ReasonTooLongMessage))
            : store.DeclineAsync(access, text, caller, cancellationToken);
    }
}

public sealed class AskQuoteQuestionHandler(IQuoteLinkStore store)
{
    public Task<QuoteLinkOutcome<PublicQuote>> HandleAsync(
        string? token, string? message, QuoteLinkCaller caller, CancellationToken cancellationToken) =>
        QuoteLinkTokens.IsWellFormed(token)
            ? HandleAsync(ResourceAccess.FromToken(token!), message, caller, cancellationToken)
            : Task.FromResult(QuoteLinkHandlerSupport.Unavailable<PublicQuote>());

    public Task<QuoteLinkOutcome<PublicQuote>> HandleAsync(
        ResourceAccess access, string? message, QuoteLinkCaller caller, CancellationToken cancellationToken)
    {
        var text = message?.Trim() ?? string.Empty;

        if (text.Length == 0)
        {
            return Task.FromResult(QuoteLinkHandlerSupport.Invalid<PublicQuote>("message", QuoteLinkMessages.QuestionRequiredMessage));
        }

        return text.Length > QuoteLinkMessages.TextMaxLength
            ? Task.FromResult(QuoteLinkHandlerSupport.Invalid<PublicQuote>("message", QuoteLinkMessages.QuestionTooLongMessage))
            : store.AskAsync(access, text, caller, cancellationToken);
    }
}

public sealed class GetQuoteLinkPhotoHandler(IQuoteLinkStore store)
{
    public Task<QuoteLinkOutcome<PublicBinary>> HandleAsync(string? token, Guid photoId, CancellationToken cancellationToken) =>
        QuoteLinkTokens.IsWellFormed(token)
            ? HandleAsync(ResourceAccess.FromToken(token!), photoId, cancellationToken)
            : Task.FromResult(QuoteLinkHandlerSupport.Unavailable<PublicBinary>());

    public Task<QuoteLinkOutcome<PublicBinary>> HandleAsync(ResourceAccess access, Guid photoId, CancellationToken cancellationToken) =>
        store.GetPhotoAsync(access, photoId, cancellationToken);
}

public sealed class GetQuoteLinkLogoHandler(IQuoteLinkStore store)
{
    public Task<QuoteLinkOutcome<PublicBinary>> HandleAsync(string? token, CancellationToken cancellationToken) =>
        QuoteLinkTokens.IsWellFormed(token)
            ? store.GetLogoAsync(ResourceAccess.FromToken(token!), cancellationToken)
            : Task.FromResult(QuoteLinkHandlerSupport.Unavailable<PublicBinary>());
}

/// <summary>The PDF of the version and its response (BR-19), generated from stored data only.</summary>
public sealed class DownloadQuoteLinkPdfHandler(IQuoteLinkStore store, IQuotePdfRenderer renderer, TimeProvider timeProvider)
{
    public Task<QuoteLinkOutcome<PublicQuotePdf>> HandleAsync(string? token, CancellationToken cancellationToken) =>
        QuoteLinkTokens.IsWellFormed(token)
            ? HandleAsync(ResourceAccess.FromToken(token!), cancellationToken)
            : Task.FromResult(QuoteLinkHandlerSupport.Unavailable<PublicQuotePdf>());

    public async Task<QuoteLinkOutcome<PublicQuotePdf>> HandleAsync(ResourceAccess access, CancellationToken cancellationToken)
    {
        switch (await store.ViewAsync(access, cancellationToken))
        {
            case QuoteLinkOutcome<PublicQuote>.Succeeded succeeded:
                var document = QuotePdfDocumentComposer.Compose(succeeded.Value, timeProvider.GetUtcNow().Year);

                return new QuoteLinkOutcome<PublicQuotePdf>.Succeeded(
                    new PublicQuotePdf(QuotePdfDocumentComposer.FileName(succeeded.Value), renderer.Render(document)));
            case QuoteLinkOutcome<PublicQuote>.Superseded:
                return new QuoteLinkOutcome<PublicQuotePdf>.Superseded();
            default:
                return QuoteLinkHandlerSupport.Unavailable<PublicQuotePdf>();
        }
    }
}

public sealed record PublicQuotePdf(string FileName, byte[] Content);
