using Microsoft.Extensions.Options;

namespace FieldOps.Api.Configuration;

/// <summary>
/// Rejects CORS configuration that is missing, uses a wildcard or contains
/// anything other than an absolute http(s) origin.
/// </summary>
public sealed class CorsSettingsValidator : IValidateOptions<CorsSettings>
{
    public ValidateOptionsResult Validate(string? name, CorsSettings options)
    {
        if (options.AllowedOrigins.Length == 0)
        {
            return ValidateOptionsResult.Fail(
                $"'{CorsSettings.SectionName}:AllowedOrigins' must contain at least one origin.");
        }

        var failures = options.AllowedOrigins
            .Where(origin => !IsValidOrigin(origin))
            .Select(origin =>
                $"'{origin}' is not a valid CORS origin. Use an absolute http or https origin without a path or wildcard.")
            .ToList();

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static bool IsValidOrigin(string origin)
    {
        if (string.IsNullOrWhiteSpace(origin)
            || origin.Contains('*')
            || origin.EndsWith('/'))
        {
            return false;
        }

        return Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && uri.AbsolutePath == "/"
            && string.IsNullOrEmpty(uri.Query)
            && string.IsNullOrEmpty(uri.Fragment)
            && string.IsNullOrEmpty(uri.UserInfo);
    }
}
