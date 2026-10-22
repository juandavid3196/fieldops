namespace FieldOps.Application.Features.Account;

/// <summary>Supported UI languages (app i18n BR-01). Matching is ordinal, exact and untrimmed.</summary>
public static class SupportedLanguages
{
    public const string Default = "en";

    public static IReadOnlyList<string> Codes { get; } = ["en", "es"];

    public static bool IsSupported(string? code) =>
        code is not null && Codes.Contains(code, StringComparer.Ordinal);

    public static string Normalize(string? code) => IsSupported(code) ? code! : Default;
}
