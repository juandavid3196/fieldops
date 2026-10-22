namespace FieldOps.Application.Features.Account;

/// <summary>Supported appearance values and per-endpoint audit sources (app dark mode BR-01, BR-06). Matching is ordinal, exact and untrimmed.</summary>
public static class SupportedThemes
{
    public const string Default = "system";

    public static IReadOnlyList<string> Values { get; } = ["light", "dark", "system"];

    public static IReadOnlyList<string> InternalSources { get; } = ["preferences_page", "user_menu", "technician_menu"];

    public static IReadOnlyList<string> PortalSources { get; } = ["portal_menu"];

    public static bool IsSupported(string? value) =>
        value is not null && Values.Contains(value, StringComparer.Ordinal);

    public static bool IsSupportedSource(string? source, bool portal) =>
        source is not null && (portal ? PortalSources : InternalSources).Contains(source, StringComparer.Ordinal);
}
