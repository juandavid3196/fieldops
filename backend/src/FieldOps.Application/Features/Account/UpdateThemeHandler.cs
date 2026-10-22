using System.Net;

namespace FieldOps.Application.Features.Account;

/// <summary>Raw input of <c>PUT /account/theme</c> and <c>PUT /portal/account/theme</c>; null when not a string.</summary>
public sealed record UpdateThemeCommand(string? Theme, string? Source);

/// <summary>Result of <see cref="UpdateThemeHandler"/>.</summary>
public abstract record UpdateThemeResult
{
    private UpdateThemeResult()
    {
    }

    public sealed record Succeeded(string PreferredTheme) : UpdateThemeResult;

    public sealed record Invalid(IReadOnlyDictionary<string, string[]> Errors) : UpdateThemeResult;

    public sealed record NotFound : UpdateThemeResult;
}

/// <summary>Validates and stores the session user's preferred appearance (app dark mode BR-06..BR-08).</summary>
public sealed class UpdateThemeHandler(IAccountStore store, TimeProvider timeProvider)
{
    public const string ThemeUpdatedAuditAction = "user.theme_updated";

    public const string ThemeKey = "theme";

    public const string SourceKey = "source";

    public const string UnsupportedThemeMessage = "Choose a supported appearance.";

    public const string UnsupportedSourceMessage = "Choose a supported source.";

    public async Task<UpdateThemeResult> HandleAsync(
        Guid userId,
        Guid organizationId,
        bool portal,
        IPAddress? clientIp,
        UpdateThemeCommand command,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (!SupportedThemes.IsSupported(command.Theme))
        {
            errors[ThemeKey] = [UnsupportedThemeMessage];
        }

        if (!SupportedThemes.IsSupportedSource(command.Source, portal))
        {
            errors[SourceKey] = [UnsupportedSourceMessage];
        }

        if (errors.Count > 0)
        {
            return new UpdateThemeResult.Invalid(errors);
        }

        var stored = await store.UpdateThemeAsync(
            userId, organizationId, command.Theme!, command.Source!, clientIp, timeProvider.GetUtcNow(), cancellationToken);

        return stored is null
            ? new UpdateThemeResult.NotFound()
            : new UpdateThemeResult.Succeeded(stored);
    }
}
