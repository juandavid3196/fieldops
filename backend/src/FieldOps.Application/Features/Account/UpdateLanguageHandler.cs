using System.Net;

namespace FieldOps.Application.Features.Account;

/// <summary>Raw input of <c>PUT /account/language</c> and <c>PUT /portal/account/language</c>; null when not a string.</summary>
public sealed record UpdateLanguageCommand(string? Language);

/// <summary>Result of <see cref="UpdateLanguageHandler"/>.</summary>
public abstract record UpdateLanguageResult
{
    private UpdateLanguageResult()
    {
    }

    public sealed record Succeeded(string PreferredLanguage) : UpdateLanguageResult;

    public sealed record Invalid(IReadOnlyDictionary<string, string[]> Errors) : UpdateLanguageResult;

    public sealed record NotFound : UpdateLanguageResult;
}

/// <summary>Validates and stores the session user's preferred language (app i18n BR-05..BR-09).</summary>
public sealed class UpdateLanguageHandler(IAccountStore store, TimeProvider timeProvider)
{
    public const string LanguageUpdatedAuditAction = "user.language_updated";

    public const string LanguageKey = "language";

    public const string UnsupportedMessage = "Choose a supported language.";

    public async Task<UpdateLanguageResult> HandleAsync(
        Guid userId,
        Guid organizationId,
        IPAddress? clientIp,
        UpdateLanguageCommand command,
        CancellationToken cancellationToken)
    {
        if (!SupportedLanguages.IsSupported(command.Language))
        {
            return new UpdateLanguageResult.Invalid(
                new Dictionary<string, string[]>(StringComparer.Ordinal) { [LanguageKey] = [UnsupportedMessage] });
        }

        var stored = await store.UpdateLanguageAsync(
            userId, organizationId, command.Language!, clientIp, timeProvider.GetUtcNow(), cancellationToken);

        return stored is null
            ? new UpdateLanguageResult.NotFound()
            : new UpdateLanguageResult.Succeeded(stored);
    }
}
