using System.Text.Json;

namespace FieldOps.Api.Contracts;

/// <summary>
/// Body of PUT /account/profile. It has no user, organization, email or role
/// field; unknown properties are ignored by the JSON serializer (BR-01).
/// </summary>
public sealed record UpdateAccountProfileRequest(string? FirstName, string? LastName, string? Phone);

/// <summary>Body of POST /account/password (BR-02); the confirmation is client-only.</summary>
public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);

/// <summary>
/// Body of PUT /account/language and PUT /portal/account/language. The value is
/// bound as raw JSON so a number or null reaches validation as a keyed 400.
/// </summary>
public sealed record UpdateLanguageRequest(JsonElement? Language)
{
    public string? LanguageText => LanguageInput.AsString(Language);
}

/// <summary>Response of the language endpoints.</summary>
public sealed record UpdateLanguageView(string PreferredLanguage);

/// <summary>
/// Body of PUT /account/theme and PUT /portal/account/theme. Values are bound as raw JSON so a number or null
/// reaches validation as a keyed 400.
/// </summary>
public sealed record UpdateThemeRequest(JsonElement? Theme, JsonElement? Source)
{
    public string? ThemeText => LanguageInput.AsString(Theme);

    public string? SourceText => LanguageInput.AsString(Source);
}

/// <summary>Response of the theme endpoints.</summary>
public sealed record UpdateThemeView(string PreferredTheme);

/// <summary>Reads a raw JSON value as a string; any other JSON type is null.</summary>
public static class LanguageInput
{
    public static string? AsString(JsonElement? value) =>
        value is { ValueKind: JsonValueKind.String } element ? element.GetString() : null;
}
